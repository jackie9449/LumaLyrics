"""Private MATPAC++ / OpenMIC candidate, with independent numerical gates.

Encoder: Aurian Quelennec / MATPAC++ (Apache-2.0), pinned HF port below.
Head: vaclis / AMTFlow (CC BY 4.0). No weights or music enter the checkout.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import os
import time
import urllib.request

from prepare_yamnet import private_directory, sha256

ENCODER_REVISION = "a7e10eb8d14c764673f4364884fb06c9f9f88887"
HEAD_REVISION = "65ec90d739b4bd77aece2b47ec4fb5cc6440179c"
ENCODER = f"https://huggingface.co/auriankelen/matpac_music/resolve/{ENCODER_REVISION}/"
HEAD = f"https://huggingface.co/vaclis/amtflow/resolve/{HEAD_REVISION}/heads/openmic/matpac-music-mlp.safetensors"
ASSETS = {
    "config.json": (ENCODER + "config.json", "22081a30f8d1216fbaea5f95fc9518f368d606e578b6f2b58b79f27bf45c3bcc", 608),
    "configuration_matpac.py": (ENCODER + "configuration_matpac.py", "b6e5821c2fd7f35670ffa9e768d049387630c98ea8e5da0a40e08c5c6f7ac865", 2948),
    "modeling_matpac.py": (ENCODER + "modeling_matpac.py", "cd112e15fbded8056d13e9bc3097cb69edba77b367cfa4777b6106ed48c74a7a", 14533),
    "preprocessor_config.json": (ENCODER + "preprocessor_config.json", "11b3be6aa5979d76000d415a42e74e78fbc7de313e9d3d1a8ac41416b34b3ae9", 216),
    "model.safetensors": (ENCODER + "model.safetensors", "7492dbffe158b4e9c2b42739770d7ee944e6ebf401af7f7b49dfaf0d9de7f45d", 341685728),
    "openmic-head.safetensors": (HEAD, "45368f57df0b9786c9a5c3f7eeb5113a57d44feecea3167d6880ae653817a399", 7942696),
}
LABELS = {0: "Accordion", 1: "Banjo", 2: "Bass", 3: "Cello", 4: "Clarinet", 5: "Cymbals", 6: "Drums", 7: "Flute",
          8: "Guitar", 9: "MalletPercussion", 10: "Mandolin", 11: "Organ", 12: "Piano", 13: "Saxophone", 14: "Synth",
          15: "Trombone", 16: "Trumpet", 17: "Ukulele", 18: "Violin"}
WINDOW = 48880  # 304 complete 400-sample frames at 160 hop; 19 temporal patches.


def fetch_large(url, partial, size):
    # Bounded ranges avoid silently truncated large LFS responses. The final
    # whole-file SHA below remains authoritative, including when resuming parts.
    chunk_size = 4 * 1024 * 1024
    parts = partial.parent / (partial.name + "-parts")
    parts.mkdir(exist_ok=True)

    def fetch(index):
        start, end = index * chunk_size, min(size, (index + 1) * chunk_size) - 1
        part = parts / f"{index:04d}"
        if part.exists() and part.stat().st_size == end - start + 1:
            return part
        for attempt in range(3):
            try:
                request = urllib.request.Request(url + f"?download=true&part={index}", headers={"Range": f"bytes={start}-{end}"})
                with urllib.request.urlopen(request, timeout=60) as source:
                    if source.status != 206 or source.headers.get("Content-Range") != f"bytes {start}-{end}/{size}":
                        raise ValueError("Pinned asset range not honored")
                    data = source.read(end - start + 2)
                    if len(data) != end - start + 1:
                        raise ValueError("Pinned asset range length mismatch")
                part.write_bytes(data)
                return part
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(attempt + 1)

    with ThreadPoolExecutor(max_workers=8) as pool:
        ordered = list(pool.map(fetch, range((size + chunk_size - 1) // chunk_size)))
    with partial.open("wb") as out:
        for part in ordered:
            with part.open("rb") as source:
                while chunk := source.read(65536):
                    out.write(chunk)


def fetch_assets(reference):
    for name, (url, digest, size) in ASSETS.items():
        target = reference / name
        if target.exists() and target.stat().st_size == size and sha256(target) == digest:
            continue
        partial = reference / (name + ".partial")
        try:
            if size > 32 * 1024 * 1024:
                fetch_large(url, partial, size)
            else:
                with urllib.request.urlopen(url + "?download=true", timeout=60) as source, partial.open("wb") as out:
                    copied = 0
                    while chunk := source.read(65536):
                        copied += len(chunk)
                        if copied > size:
                            raise ValueError("Reference size exceeded: " + name)
                        out.write(chunk)
            if partial.stat().st_size != size or sha256(partial) != digest:
                raise ValueError("Pinned reference checksum/length mismatch: " + name)
            partial.replace(target)
        finally:
            partial.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--validation-wav", action="append", default=[])
    parser.add_argument("--response-gate", help="Private frozen calibration gate; requires its exact SHA-256.")
    parser.add_argument("--gate-sha256")
    args = parser.parse_args()
    reference, output = private_directory(args.reference_dir), private_directory(args.output_dir)
    fetch_assets(reference)
    os.environ["USE_TF"] = "0"
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["HF_MODULES_CACHE"] = str(reference / "hf-modules")
    import numpy as np
    import torch
    import onnxruntime as ort
    from safetensors.torch import load_file
    from transformers import AutoModel
    torch.set_num_threads(2)
    model = AutoModel.from_pretrained(str(reference), trust_remote_code=True, local_files_only=True).eval()
    head = load_file(reference / "openmic-head.safetensors", device="cpu")
    responsive = args.response_gate is not None
    patches = 9 if responsive else 19
    window_samples = patches * 16 * 160 + 240
    if responsive:
        if not args.gate_sha256 or sha256(args.response_gate) != args.gate_sha256:
            raise ValueError("Frozen response gate checksum mismatch")
        gate_data = np.load(args.response_gate)
        if not np.array_equal(gate_data["indices"], [3, 4, 7, 13, 15, 16, 18]) or gate_data["weight"].shape != (7, 3840) or gate_data["bias"].shape != (7,):
            raise ValueError("Unexpected response gate schema")
        if float(gate_data["floor"]) != .5 or not all(np.isfinite(gate_data[k]).all() for k in ["weight", "bias"]):
            raise ValueError("Invalid response gate values")
        gate_weight, gate_bias = torch.from_numpy(gate_data["weight"]), torch.from_numpy(gate_data["bias"])
        projection = torch.zeros(7, 20)
        projection[torch.arange(7), torch.from_numpy(gate_data["indices"])] = 1

    def correct(scores, emb):
        if not responsive:
            return scores
        attenuation = torch.clamp(torch.sigmoid(emb @ gate_weight.T + gate_bias) / .5, 0, 1)
        return scores * (1 + (attenuation - 1) @ projection)

    def original(vector):
        emb = model(vector.unsqueeze(0)).hidden_states[10]
        hidden = torch.relu(((emb - head["mu"]) / head["sd"]) @ head["w1"] + head["b1"])
        return correct(torch.sigmoid(hidden @ head["w2"] + head["b2"]), emb)[0]

    class Waveform(torch.nn.Module):
        def __init__(self):
            super().__init__()
            self.encoder = model
            angle = 2 * np.pi * np.arange(400)[:, None] * np.arange(201)[None, :] / 400
            self.register_buffer("cos", torch.from_numpy(np.cos(angle)))
            self.register_buffer("sin", torch.from_numpy(-np.sin(angle)))
            self.register_buffer("window", model.log_mel.MelSpectrogram.spectrogram.window)
            self.register_buffer("mel", model.log_mel.MelSpectrogram.mel_scale.fb)
            for name, value in head.items():
                self.register_buffer(name, value)
            if responsive:
                self.register_buffer("gate_weight", gate_weight)
                self.register_buffer("gate_bias", gate_bias)
                self.register_buffer("gate_projection", projection)

        def forward(self, waveform):
            frames = (waveform.unfold(0, 400, 160) * self.window).double()
            real, imag = frames @ self.cos, frames @ self.sin
            power = (real.square() + imag.square()).float()
            lms = torch.log(power @ self.mel + torch.finfo(torch.float32).eps)
            x = ((lms.T - self.encoder.config.lms_mean) / self.encoder.config.lms_std)[None, None, :, :]
            x = self.encoder.patch_embed(x)
            positions = self.encoder.pos_embed[:, 1:, :].reshape(1, 5, 38, 768)[:, :, :patches, :].reshape(1, 5 * patches, 768)
            x = x + positions
            x = torch.cat((self.encoder.cls_token + self.encoder.pos_embed[:, :1, :], x), dim=1)
            # Head consumes layer 10 (zero based), prior to the final layer norm.
            for block in self.encoder.student_encoder.blocks[:11]:
                x = block(x)
            emb = x[:, 1:, :].reshape(1, 5, patches, 768).permute(0, 2, 1, 3).reshape(1, patches, 3840).mean(1)
            hidden = torch.relu(((emb - self.mu) / self.sd) @ self.w1 + self.b1)
            scores = torch.sigmoid(hidden @ self.w2 + self.b2)
            if responsive:
                attenuation = torch.clamp(torch.sigmoid(emb @ self.gate_weight.T + self.gate_bias) / .5, 0, 1)
                scores = scores * (1 + (attenuation - 1) @ self.gate_projection)
            return torch.clamp(scores[0], 0, 1)

    wrapper = Waveform().eval()
    path = output / ("matpac-responsive-waveform.onnx" if responsive else "matpac-openmic-waveform.onnx")
    with torch.inference_mode():
        torch.onnx.export(wrapper, torch.zeros(window_samples), str(path), input_names=["waveform"], output_names=["scores"], opset_version=17, dynamo=False)
    options = ort.SessionOptions()
    options.intra_op_num_threads, options.inter_op_num_threads = 2, 1
    session = ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])
    rng = np.random.default_rng(4727)
    vectors = [np.zeros(window_samples, np.float32)]
    vectors += [(.1 * np.sin(np.arange(window_samples) * 2 * np.pi * f / 16000)).astype(np.float32) for f in [110, 220, 440, 880, 1760]]
    vectors += [rng.normal(0, .03, window_samples).astype(np.float32) for _ in range(14)]
    expected, errors, timings, rewrite_errors = [], [], [], []
    with torch.inference_mode():
        for vector in vectors:
            raw = original(torch.from_numpy(vector)).numpy()
            rewrite_errors.append(float(np.max(np.abs(raw - wrapper(torch.from_numpy(vector)).numpy()))))
            start = time.perf_counter()
            actual = session.run(None, {"waveform": vector})[0]
            timings.append((time.perf_counter() - start) * 1000)
            errors.append(float(np.max(np.abs(raw - actual))))
            expected.append(raw)
    music_errors = []
    with torch.inference_mode():
        for audio_path in args.validation_wav:
            import soundfile as sf
            from scipy.signal import resample_poly
            from math import gcd
            audio, rate = sf.read(audio_path, dtype="float32", always_2d=True)
            audio = resample_poly(audio.mean(1), 16000 // gcd(rate, 16000), rate // gcd(rate, 16000)).astype(np.float32)
            for start in range(0, min(len(audio) - window_samples + 1, 16000 * 30), 16000):
                vector = audio[start:start + window_samples]
                music_errors.append(float(np.max(np.abs(original(torch.from_numpy(vector)).numpy() - session.run(None, {"waveform": vector})[0]))))
    np.stack(vectors).tofile(output / "parity-inputs.f32")
    np.stack(expected).tofile(output / "parity-scores.f32")
    report = dict(modelFile=path.name, modelSha256=sha256(path), modelBytes=path.stat().st_size,
                  sourceAssets={name: dict(url=url, sha256=digest, bytes=size) for name, (url, digest, size) in ASSETS.items()},
                  encoderRevision=ENCODER_REVISION, headRevision=HEAD_REVISION, sourceLicenses=["Apache-2.0", "CC BY 4.0"],
                  inputName="waveform", outputName="scores", sampleRate=16000, windowSamples=window_samples, hopSamples=3200 if responsive else 8000, outputClassCount=20,
                  preprocessingVersion="matpac-144-slaney-power-ln-f64dft-layer10-gate-v2" if responsive else "matpac-304-slaney-power-ln-f64dft-layer10-v1", labelMap=LABELS,
                  responseGateSha256=args.gate_sha256 if responsive else None,
                  paritySamples=20, parityVectorSamples=20, parityVectorsContainUserAudio=False,
                  maximumRewriteError=max(rewrite_errors), maximumAbsoluteError=max(errors + music_errors),
                  numericParityPassed=max(errors + music_errors) <= 1e-4, musicParityWindows=len(music_errors),
                  inferenceP95Ms=float(np.percentile(timings, 95)), thresholdsCalibrated=False,
                  accuracyValidated=False, productionReady=False)
    (output / "instrument-model-export.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report), flush=True)
    if not report["numericParityPassed"]:
        raise SystemExit("Numerical gate failed; do not install")


if __name__ == "__main__":
    main()
