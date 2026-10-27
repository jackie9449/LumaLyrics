"""Prepare a fixed EfficientAT waveform graph outside the software checkout.

Publisher code: Florian Schmid / EfficientAT (MIT), fixed Git revision below.
Weights are fetched only from that publisher's fixed v0.0.1 release. Conversion
parity is a numerical check, never evidence of instrument accuracy or timing.
"""
import argparse
import contextlib
import csv
import io
import json
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

from prepare_yamnet import private_directory, sha256

REVISION = "a425fdce92572e602a1d5634799bd9f1f2efa806"
WEIGHTS = "mn10_as_mAP_471.pt"
WEIGHTS_SHA256 = "0bd7dc2443af498c289a2e739f02ebb515d6aa3fd3ab9db539c86123ae368a4e"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()
    reference, output = private_directory(args.reference_dir), private_directory(args.output_dir)
    if not (reference / ".git").exists():
        subprocess.run(["git", "clone", "--no-checkout", "https://github.com/fschmid56/EfficientAT.git", str(reference)], check=True)
        subprocess.run(["git", "-C", str(reference), "checkout", REVISION], check=True)
    actual = subprocess.check_output(["git", "-C", str(reference), "rev-parse", "HEAD"], text=True).strip()
    if actual != REVISION:
        raise ValueError("Reference revision differs from the pinned source")
    if subprocess.check_output(["git", "-C", str(reference), "diff", "--name-only", "HEAD"], text=True).strip():
        raise ValueError("Publisher reference source has local modifications")
    weights = reference / WEIGHTS
    if not weights.exists() or sha256(weights) != WEIGHTS_SHA256:
        partial = reference / (WEIGHTS + ".partial")
        try:
            with urllib.request.urlopen("https://github.com/fschmid56/EfficientAT/releases/download/v0.0.1/" + WEIGHTS, timeout=60) as source, partial.open("wb") as sink:
                copied = 0
                while chunk := source.read(65536):
                    copied += len(chunk)
                    if copied > 32 * 1024 * 1024:
                        raise ValueError("Model exceeds download bound")
                    sink.write(chunk)
            if sha256(partial) != WEIGHTS_SHA256:
                raise ValueError("Publisher weight checksum differs")
            partial.replace(weights)
        finally:
            partial.unlink(missing_ok=True)
    import numpy as np
    import torch
    import onnxruntime as ort
    torch.set_num_threads(2)
    sys.path.insert(0, str(reference))
    previous = os.getcwd()
    try:
        os.chdir(reference)
        from models.mn.model import get_model
        from models.preprocess import AugmentMelSTFT
        with contextlib.redirect_stdout(io.StringIO()):
            model = get_model(num_classes=527, pretrained_name=None, width_mult=1).eval()
            mel = AugmentMelSTFT().eval()
        model.load_state_dict(torch.load(weights, map_location="cpu", weights_only=True), strict=True)
    finally:
        os.chdir(previous)

    class WaveformModel(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.mel, self.model = mel, model

        def forward(self, waveform):
            spectrum = self.mel(waveform.unsqueeze(0))
            # Some ORT sigmoid kernels round a saturated probability slightly
            # above one. Preserve the probability domain explicitly in the graph.
            return torch.clamp(torch.sigmoid(self.model(spectrum.unsqueeze(1))[0])[0], 0, 1)

    wrapper = WaveformModel().eval()
    path = output / "efficientat-mn10-waveform.onnx"
    with torch.inference_mode():
        torch.onnx.export(wrapper, torch.zeros(64000), str(path), input_names=["waveform"], output_names=["scores"], opset_version=17, dynamo=False)
    options = ort.SessionOptions()
    options.intra_op_num_threads, options.inter_op_num_threads = 2, 1
    options.log_severity_level = 3
    session = ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])
    with (reference / "metadata/class_labels_indices.csv").open(encoding="utf-8") as stream:
        indices = {row["display_name"]: int(row["index"]) for row in csv.DictReader(stream)}
    groups = {"Guitar": ["Electric guitar", "Acoustic guitar", "Steel guitar, slide guitar"],
              "Bass": ["Bass guitar"], "Drums": ["Drum kit", "Drum", "Snare drum", "Bass drum"]}
    label_map = {indices[name]: kind for kind, names in groups.items() for name in names}
    rng = np.random.default_rng(4727)
    vectors = [np.zeros(64000, dtype=np.float32)]
    vectors += [(.1 * np.sin(np.arange(64000) * 2 * np.pi * f / 32000)).astype(np.float32) for f in [110, 220, 440, 880, 1760]]
    vectors += [rng.normal(0, .03, 64000).astype(np.float32) for _ in range(14)]
    expected, errors, timings = [], [], []
    with torch.inference_mode():
        for vector in vectors:
            original = wrapper(torch.from_numpy(vector)).numpy()
            started = time.perf_counter()
            actual = session.run(None, {"waveform": vector})[0]
            timings.append((time.perf_counter() - started) * 1000)
            expected.append(original)
            errors.append(float(np.max(np.abs(original - actual))))
    np.stack(vectors).tofile(output / "parity-inputs.f32")
    np.stack(expected).tofile(output / "parity-scores.f32")
    silence_max = float(max(expected[0][i] for i in label_map))
    report = dict(sourceRevision=REVISION, sourceWeightsSha256=WEIGHTS_SHA256,
                  sourceLicenseReference="https://github.com/fschmid56/EfficientAT/blob/" + REVISION + "/LICENSE",
                  modelFile=path.name, modelSha256=sha256(path), modelBytes=path.stat().st_size,
                  inputName="waveform", outputName="scores", sampleRate=32000, windowSamples=64000,
                  hopSamples=16000, outputClassCount=527, preprocessingVersion="official-efficientat-mn10-waveform-1",
                  labelMap=label_map, thresholdsCalibrated=False, paritySamples=20, parityVectorSamples=20,
                  parityVectorsContainUserAudio=False, maximumAbsoluteError=max(errors), numericParityPassed=max(errors) <= 1e-4,
                  inferenceP50Ms=float(np.percentile(timings, 50)), inferenceP95Ms=float(np.percentile(timings, 95)),
                  silenceMaximumTargetScore=silence_max, silenceSanityPassed=silence_max < .5,
                  accuracyValidated=False, productionReady=False)
    (output / "instrument-model-export.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: report[key] for key in ["numericParityPassed", "maximumAbsoluteError", "inferenceP95Ms", "modelSha256", "modelBytes", "labelMap"]}))
    if not report["numericParityPassed"] or not report["silenceSanityPassed"]:
        raise SystemExit("Candidate rejected; numerical parity alone is insufficient. Do not install.")


if __name__ == "__main__":
    main()
