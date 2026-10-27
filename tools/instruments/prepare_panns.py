"""Prepare the pinned, small PANNs Cnn6 reference in personal storage only.

Weights: CC BY 4.0, https://zenodo.org/records/3987831. Source provenance:
https://github.com/qiuqiangkong/audioset_tagging_cnn. No learned data is
committed by this tool. Synthetic parity proves conversion, not music accuracy.
"""
import argparse
import ast
import csv
import hashlib
import json
import time
import urllib.request

from prepare_yamnet import GROUPS, private_directory, sha256

REVISION = "d2f4b8c18eab44737fcc0de1248ae21eb43f6aa4"
ASSETS = {
    "LICENSE.MIT": (f"https://raw.githubusercontent.com/qiuqiangkong/audioset_tagging_cnn/{REVISION}/LICENSE.MIT", "sha256", "7e540655d851d9737fafaa9f0cbd064c3a29bdfe5e937ee07a09360f85161975"),
    "models.py": (f"https://raw.githubusercontent.com/qiuqiangkong/audioset_tagging_cnn/{REVISION}/pytorch/models.py", "sha256", "7f9af440395ace5160bbb51d654a0dc35fb887fbf5edecb12da61ff6efb306d9"),
    "class_labels_indices.csv": (f"https://raw.githubusercontent.com/qiuqiangkong/audioset_tagging_cnn/{REVISION}/metadata/class_labels_indices.csv", "sha256", "cdd1049833c4b86127c2773ac0d14a2754b6a6d0d1798002ed5c66e699708429"),
    "Cnn6.pth": ("https://zenodo.org/api/records/3987831/files/Cnn6_mAP%3D0.343.pth/content", "md5", "e25e26b84585b14c7754c91e48efc9be"),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()
    reference, output = private_directory(args.reference_dir), private_directory(args.output_dir)
    for name, (url, algorithm, expected) in ASSETS.items():
        target = reference / name
        if target.exists() and hashlib.new(algorithm, target.read_bytes()).hexdigest() == expected:
            continue
        partial = reference / (name + ".partial")
        try:
            with urllib.request.urlopen(url, timeout=60) as source, partial.open("wb") as sink:
                copied = 0
                while chunk := source.read(65536):
                    copied += len(chunk)
                    if copied > 32 * 1024 * 1024:
                        raise ValueError("Reference exceeds download bound")
                    sink.write(chunk)
            if hashlib.new(algorithm, partial.read_bytes()).hexdigest() != expected:
                raise ValueError("Pinned reference checksum mismatch: " + name)
            partial.replace(target)
        finally:
            partial.unlink(missing_ok=True)
    import numpy as np
    import torch
    import torch.nn as nn
    import torch.nn.functional as F
    from torchlibrosa.stft import Spectrogram, LogmelFilterBank
    from torchlibrosa.augmentation import SpecAugmentation
    import onnxruntime as ort
    torch.set_num_threads(2)
    torch.backends.mkldnn.enabled = False
    # Extract unchanged declarations from checksum-verified publisher source.
    # Unrelated architectures and training imports are not executed.
    tree = ast.parse((reference / "models.py").read_text(encoding="utf-8"))
    declarations = [node for node in tree.body if isinstance(node, (ast.FunctionDef, ast.ClassDef))
                    and node.name in {"init_layer", "init_bn", "ConvBlock5x5", "Cnn6"}]
    scope = dict(torch=torch, nn=nn, F=F, Spectrogram=Spectrogram, LogmelFilterBank=LogmelFilterBank,
                 SpecAugmentation=SpecAugmentation)
    exec(compile(ast.Module(body=declarations, type_ignores=[]), str(reference / "models.py"), "exec"), scope)
    model = scope["Cnn6"](32000, 1024, 320, 64, 50, 14000, 527)
    model.load_state_dict(torch.load(reference / "Cnn6.pth", map_location="cpu", weights_only=True)["model"])
    model.eval()

    class WaveformModel(nn.Module):
        def __init__(self):
            super().__init__()
            self.reference = model

        def forward(self, waveform):
            return self.reference(waveform.unsqueeze(0))["clipwise_output"][0]

    wrapper = WaveformModel().eval()
    path = output / "panns-cnn6-waveform.onnx"
    torch.onnx.export(wrapper, torch.zeros(32000), str(path), input_names=["waveform"],
                      output_names=["scores"], opset_version=17, dynamo=False)
    options = ort.SessionOptions()
    options.intra_op_num_threads, options.inter_op_num_threads = 2, 1
    options.log_severity_level = 3
    session = ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])
    with (reference / "class_labels_indices.csv").open(encoding="utf-8") as stream:
        indices = {row["display_name"]: int(row["index"]) for row in csv.DictReader(stream)}
    labels = {indices[name]: kind for kind, names in GROUPS.items() for name in names}
    rng = np.random.default_rng(4727)
    vectors = [np.zeros(32000, dtype=np.float32)]
    vectors += [(.1 * np.sin(np.arange(32000) * 2 * np.pi * f / 32000)).astype(np.float32) for f in [110, 220, 440, 880, 1760]]
    vectors += [rng.normal(0, .03, 32000).astype(np.float32) for _ in range(14)]
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
    report = dict(sourceRevision=REVISION, sourceWeightsSha256=sha256(reference / "Cnn6.pth"),
                  sourceLicenseReference="https://zenodo.org/records/3987831", modelFile=path.name,
                  modelSha256=sha256(path), modelBytes=path.stat().st_size,
                  inputName="waveform", outputName="scores", sampleRate=32000,
                  windowSamples=32000, hopSamples=16000, outputClassCount=527,
                  preprocessingVersion="official-panns-cnn6-waveform-1", labelMap=labels,
                  thresholdsCalibrated=False, paritySamples=20, parityVectorSamples=20,
                  parityVectorsContainUserAudio=False, maximumAbsoluteError=max(errors),
                  numericParityPassed=max(errors) <= 1e-4,
                  inferenceP50Ms=float(np.percentile(timings, 50)), inferenceP95Ms=float(np.percentile(timings, 95)),
                  accuracyValidated=False, productionReady=False)
    (output / "instrument-model-export.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: report[key] for key in ["numericParityPassed", "maximumAbsoluteError", "inferenceP95Ms"]}))
    if not report["numericParityPassed"]:
        raise SystemExit("Numeric parity failed")


if __name__ == "__main__":
    main()
