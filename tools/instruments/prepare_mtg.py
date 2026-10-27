"""Prepare a private MTG Jamendo instrument experiment; never bundles weights.

Original feature extractor and head are MTG/UPF assets, CC BY-NC-SA 4.0.
Numerical parity does not establish musical accuracy or onset/offset timing.
"""
import argparse
import json
import os
import time
import urllib.request
from pathlib import Path

from prepare_yamnet import private_directory, sha256

BASE = "https://essentia.upf.edu/models/"
ASSETS = {
    "discogs-effnet-bsdynamic-1.onnx": ("feature-extractors/discogs-effnet/discogs-effnet-bsdynamic-1.onnx", "a280825b334797cf677939db8cd5762c0392aedd0ca6415dbc1cd083f045e43c"),
    "head.json": ("classification-heads/mtg_jamendo_instrument/mtg_jamendo_instrument-discogs-effnet-1.json", "7d02204c6451b5615e2968ec6364bbae3b915c886e608f05f00d3a38dc5177c4"),
    "discogs-effnet-bs1-1.pb": ("feature-extractors/discogs-effnet/discogs-effnet-bs1-1.pb", "58a577a603669b922589179bd85ae06031fe642ffcc0ee873d2d79fbfc0ff8e5"),
    "head.pb": ("classification-heads/mtg_jamendo_instrument/mtg_jamendo_instrument-discogs-effnet-1.pb", "2e8c3003c722e098da371b6a1f7ad0ce62fac0dcfc09c7c7997d430941196c2a"),
    "head.onnx": ("classification-heads/mtg_jamendo_instrument/mtg_jamendo_instrument-discogs-effnet-1.onnx", "9ae2d9e763d66bd8eed654d1ac3aa171e6539cb8a0e11f3dcd53df1428980802"),
}
WINDOW = 33024  # 128 complete 512-sample frames, hop 256, 16 kHz
LABELS = {15: "Guitar", 18: "Guitar", 2: "Guitar", 3: "Bass", 14: "Drums"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--validation-wav", help="Optional private PCM for extra parity; never copied into parity vectors")
    args = parser.parse_args()
    reference, output = private_directory(args.reference_dir), private_directory(args.output_dir)
    for name, (suffix, expected) in ASSETS.items():
        path = reference / name
        if path.exists() and sha256(path) == expected:
            continue
        partial = reference / (name + ".partial")
        try:
            with urllib.request.urlopen(BASE + suffix, timeout=60) as source, partial.open("wb") as target:
                copied = 0
                while chunk := source.read(65536):
                    copied += len(chunk)
                    if copied > 32 * 1024 * 1024:
                        raise ValueError("Reference size bound exceeded")
                    target.write(chunk)
            if sha256(partial) != expected:
                raise ValueError("Pinned reference checksum mismatch: " + name)
            partial.replace(path)
        finally:
            partial.unlink(missing_ok=True)

    os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")
    classes = json.loads((reference / "head.json").read_text(encoding="utf-8"))["classes"]
    if [classes[i] for i in LABELS] != ["electricguitar", "guitar", "acousticguitar", "bass", "drums"]:
        raise ValueError("Instrument class order changed")
    import numpy as np
    import onnx
    from onnx import helper as H, numpy_helper as N, TensorProto as T
    import onnxruntime as ort
    import tensorflow as tf
    tf.config.threading.set_intra_op_parallelism_threads(2)
    tf.config.threading.set_inter_op_parallelism_threads(1)

    def graph(name):
        result = tf.compat.v1.GraphDef()
        result.ParseFromString((reference / name).read_bytes())
        return result

    def function(definition, inp, out):
        wrapped = tf.compat.v1.wrap_function(lambda: tf.graph_util.import_graph_def(definition, name=""), [])
        return wrapped.prune(wrapped.graph.get_tensor_by_name(inp), wrapped.graph.get_tensor_by_name(out))

    embedding_def = graph("discogs-effnet-bs1-1.pb")
    embedding = function(embedding_def, "serving_default_melspectrogram:0", "PartitionedCall:1")
    head = function(graph("head.pb"), "model/Placeholder:0", "model/Sigmoid:0")

    # Slaney piecewise mel scale and unit-area triangular bands. A complete
    # interior patch intentionally has no artificial left/right padding.
    def hz_to_mel(hz):
        hz = np.asarray(hz, dtype=np.float64)
        return np.where(hz < 1000, hz * 3 / 200, 15 + np.log(np.maximum(hz, 1) / 1000) / (np.log(6.4) / 27))

    mel_edges = np.linspace(hz_to_mel(0), hz_to_mel(8000), 98)
    edges = np.where(mel_edges < 15, mel_edges * 200 / 3, 1000 * np.exp((mel_edges - 15) * np.log(6.4) / 27))
    frequencies = np.arange(257) * (16000 / 512)
    weights = np.maximum(0, np.minimum((frequencies[:, None] - edges[:-2]) / (edges[1:-1] - edges[:-2]),
                                      (edges[2:] - frequencies[:, None]) / (edges[2:] - edges[1:-1])))
    weights = (weights * (2 / (edges[2:] - edges[:-2]))).astype(np.float32)
    hann = np.hanning(512).astype(np.float32)
    indices = (np.arange(128)[:, None] * 256 + np.arange(512)).astype(np.int64)
    angles = 2 * np.pi * np.arange(512)[:, None] * np.arange(257)[None, :] / 512
    initializers = [N.from_array(value, name) for name, value in {
        "frames": indices, "hann": hann, "cos": np.cos(angles).astype(np.float32),
        "sin": (-np.sin(angles)).astype(np.float32), "mel": weights,
        "scale": np.array(10000, np.float32), "one": np.array(1, np.float32),
        "ln10": np.array(np.log(10), np.float32), "batch": np.array([0], np.int64)}.items()]
    nodes = [H.make_node("Gather", ["waveform", "frames"], ["chunks"], axis=0),
             H.make_node("Mul", ["chunks", "hann"], ["windowed"]),
             H.make_node("MatMul", ["windowed", "cos"], ["real"]), H.make_node("MatMul", ["windowed", "sin"], ["imag"]),
             H.make_node("Mul", ["real", "real"], ["r2"]), H.make_node("Mul", ["imag", "imag"], ["i2"]),
             H.make_node("Add", ["r2", "i2"], ["power"]), H.make_node("MatMul", ["power", "mel"], ["bands"]),
             H.make_node("Mul", ["bands", "scale"], ["scaled"]), H.make_node("Add", ["scaled", "one"], ["offset"]),
             H.make_node("Log", ["offset"], ["logged"]), H.make_node("Div", ["logged", "ln10"], ["log10"]),
             H.make_node("Unsqueeze", ["log10", "batch"], ["melspectrogram"])]
    frontend = H.make_model(H.make_graph(nodes, "interior-musicnn-patch", [H.make_tensor_value_info("waveform", T.FLOAT, [WINDOW])],
                                       [H.make_tensor_value_info("melspectrogram", T.FLOAT, [1, 128, 96])], initializers),
                            opset_imports=[H.make_opsetid("", 17)], ir_version=8)
    # Publisher's fixed ONNX avoids nondeterministic names from re-converting TF.
    # The untouched batch-one TF graph remains the independent numerical reference.
    enc = onnx.utils.Extractor(onnx.load(reference / "discogs-effnet-bsdynamic-1.onnx")).extract_model(["melspectrogram"], ["embeddings"])
    enc = onnx.version_converter.convert_version(enc, 17)
    enc = onnx.compose.add_prefix(enc, "encoder/")
    decoder = onnx.version_converter.convert_version(onnx.load(reference / "head.onnx"), 17)
    decoder.ir_version = enc.ir_version = frontend.ir_version
    decoder = onnx.compose.add_prefix(decoder, "head/")
    combined = onnx.compose.merge_models(frontend, enc, io_map=[("melspectrogram", "encoder/melspectrogram")])
    combined = onnx.compose.merge_models(combined, decoder, io_map=[("encoder/embeddings", "head/embeddings")])
    combined.graph.node.extend([H.make_node("Squeeze", ["head/activations", "batch"], ["scores"])])
    del combined.graph.output[:]
    combined.graph.output.append(H.make_tensor_value_info("scores", T.FLOAT, [40]))
    onnx.checker.check_model(combined)
    path = output / "mtg-jamendo-waveform.onnx"
    onnx.save(combined, path)
    options = ort.SessionOptions()
    options.intra_op_num_threads, options.inter_op_num_threads = 2, 1
    session = ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])

    def original(audio):
        fft = np.fft.rfft(audio[indices] * hann)
        mel = np.log10(1 + 10000 * (np.abs(fft) ** 2).dot(weights)).astype(np.float32)[None, :, :]
        return head(embedding(tf.constant(mel))).numpy()[0]

    rng = np.random.default_rng(4727)
    vectors = [np.zeros(WINDOW, np.float32)]
    vectors += [(.1 * np.sin(np.arange(WINDOW) * 2 * np.pi * f / 16000)).astype(np.float32) for f in [110, 220, 440, 880, 1760]]
    vectors += [rng.normal(0, .03, WINDOW).astype(np.float32) for _ in range(14)]
    expected, errors, timings = [], [], []
    for vector in vectors:
        raw = original(vector)
        start = time.perf_counter()
        actual = session.run(None, {"waveform": vector})[0]
        timings.append((time.perf_counter() - start) * 1000)
        expected.append(raw)
        errors.append(float(np.max(np.abs(raw - actual))))
    music_errors = []
    if args.validation_wav:
        import soundfile as sf
        from scipy.signal import resample_poly
        from math import gcd
        audio, rate = sf.read(args.validation_wav, dtype="float32", always_2d=True)
        audio = audio.mean(axis=1)
        audio = resample_poly(audio, 16000 // gcd(rate, 16000), rate // gcd(rate, 16000)).astype(np.float32)
        for start in range(0, len(audio) - WINDOW + 1, 8000):
            vector = audio[start:start + WINDOW]
            actual = session.run(None, {"waveform": vector})[0]
            music_errors.append(float(np.max(np.abs(original(vector) - actual))))
    np.stack(vectors).tofile(output / "parity-inputs.f32")
    np.stack(expected).tofile(output / "parity-scores.f32")
    silence_max = float(max(expected[0][i] for i in LABELS))
    report = dict(modelFile=path.name, modelSha256=sha256(path), modelBytes=path.stat().st_size,
                  sourceAssets={name: {"url": BASE + suffix, "sha256": digest} for name, (suffix, digest) in ASSETS.items()},
                  sourceLicenseReference="https://essentia.upf.edu/models.html", sourceLicense="CC BY-NC-SA 4.0",
                  inputName="waveform", outputName="scores", sampleRate=16000, windowSamples=WINDOW, hopSamples=8000,
                  outputClassCount=40, preprocessingVersion="musicnn-interior-slaney-power-log10-v1",
                  labelMap=LABELS, thresholdsCalibrated=False, paritySamples=20, parityVectorSamples=20,
                  parityVectorsContainUserAudio=False, maximumAbsoluteError=max(errors + music_errors),
                  musicParityWindows=len(music_errors), silenceMaximumTargetScore=silence_max,
                  numericParityPassed=max(errors + music_errors) <= 1e-4, silenceSanityPassed=silence_max < .5,
                  inferenceP95Ms=float(np.percentile(timings, 95)), accuracyValidated=False, productionReady=False)
    (output / "instrument-model-export.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report), flush=True)
    if not report["numericParityPassed"] or not report["silenceSanityPassed"]:
        raise SystemExit("Candidate rejected; do not install")


if __name__ == "__main__":
    main()
