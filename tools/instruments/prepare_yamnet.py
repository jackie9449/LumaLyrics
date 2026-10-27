"""Export the pinned official YAMNet reference; validate numerics, never musical accuracy.

Weights, source downloads, parity vectors and reports are private artifacts. This
tool does not publish a model or install an uncalibrated production model catalog.
"""
import argparse
import csv
import hashlib
import importlib
import json
import os
from pathlib import Path
import sys
import time
import urllib.request

REVISION = "c14bf9ad91962cf189f9f58db2132c06247fcd53"
ASSETS = {
    "params.py": "925bb1e62461016031f98aea09aeac28975dd516f5747513767de5d1b06b6145",
    "features.py": "e6cd53f81d072c7c43be4c7fff2b9dd0c5ccc7d64f2fbcfc85b44013d6d2ed5e",
    "yamnet.py": "7ef3df32b7ecb782490b5a04d7581a23cfcf701dbf476bc4d03deefe22cdb040",
    "yamnet_class_map.csv": "cdf24d193e196d9e95912a2667051ae203e92a2ba09449218ccb40ef787c6df2",
    "yamnet.h5": "13c3308955bbfaef262f175ac9c40e47b134573a93984f009220dd7cc12a1744",
}
GROUPS = {
    "Drums": ["Percussion", "Drum kit", "Drum machine", "Drum", "Snare drum", "Bass drum"],
    "Bass": ["Bass guitar", "Double bass"],
    "Guitar": ["Electric guitar", "Acoustic guitar", "Steel guitar, slide guitar"],
    "Piano": ["Piano", "Electric piano", "Organ", "Electronic organ", "Hammond organ", "Harpsichord"],
    "Strings": ["String section", "Violin, fiddle", "Cello"],
    "Brass": ["Brass instrument", "French horn", "Trumpet", "Trombone"],
    "Woodwind": ["Wind instrument, woodwind instrument", "Flute", "Saxophone", "Clarinet"],
    "Synth": ["Synthesizer"],
}


def sha256(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def private_directory(value):
    path = Path(value).resolve()
    repository = Path(__file__).resolve().parents[2]
    if path == repository or repository in path.parents or path in repository.parents:
        raise ValueError("Evaluation artifacts must stay outside the software tree")
    path.mkdir(parents=True, exist_ok=True)
    return path


def reference_assets(root):
    for name, expected in ASSETS.items():
        path = root / name
        if path.exists() and sha256(path) == expected:
            continue
        url = ("https://storage.googleapis.com/audioset/yamnet.h5" if name == "yamnet.h5"
               else f"https://raw.githubusercontent.com/tensorflow/models/{REVISION}/research/audioset/yamnet/{name}")
        partial = root / (name + ".partial")
        try:
            with urllib.request.urlopen(url, timeout=60) as source, partial.open("wb") as target:
                copied = 0
                while chunk := source.read(65536):
                    copied += len(chunk)
                    if copied > 32 * 1024 * 1024:
                        raise ValueError("Reference artifact exceeds size bound")
                    target.write(chunk)
            if sha256(partial) != expected:
                raise ValueError(f"Pinned reference checksum mismatch: {name}")
            partial.replace(path)
        finally:
            partial.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--music-root")
    transforms = parser.add_mutually_exclusive_group()
    transforms.add_argument("--native-fft", dest="native_fft", action="store_true", help="Use official FFT preprocessing (default)")
    transforms.add_argument("--tflite-dft", dest="native_fft", action="store_false", help="Explicitly compare the TFLite DFT variant")
    parser.set_defaults(native_fft=True)
    args = parser.parse_args()
    reference = private_directory(args.reference_dir)
    output = private_directory(args.output_dir)
    reference_assets(reference)
    os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
    os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")
    os.environ.setdefault("TF_USE_LEGACY_KERAS", "1")
    import numpy as np
    import tensorflow as tf
    import tf2onnx
    import onnxruntime as ort
    import onnx
    tf.config.threading.set_intra_op_parallelism_threads(2)
    tf.config.threading.set_inter_op_parallelism_threads(1)
    sys.path.insert(0, str(reference))
    params = importlib.import_module("params")
    yamnet = importlib.import_module("yamnet")
    original = yamnet.yamnet_frames_model(params.Params())
    original.load_weights(str(reference / "yamnet.h5"))
    convertible = yamnet.yamnet_frames_model(params.Params(tflite_compatible=True))
    convertible.load_weights(str(reference / "yamnet.h5"))
    signature = [tf.TensorSpec([15600], tf.float32, name="waveform")]

    @tf.function(input_signature=signature)
    def predict(waveform):
        return tf.identity((original if args.native_fft else convertible)(waveform, training=False)[0][0], name="scores")

    model_path = output / "yamnet-waveform.onnx"
    converted_graph, _ = tf2onnx.convert.from_function(predict, input_signature=signature, opset=17)
    # Keras' fixed DFT/mel tensors are eager captures rather than Variables.
    # Freeze those captures explicitly instead of exporting accidental inputs.
    concrete = predict.get_concrete_function()
    captured = {inside.name: outside.numpy() for outside, inside in concrete.graph.captures
                if outside.dtype != tf.resource}
    for model_input in list(converted_graph.graph.input):
        if model_input.name == "waveform":
            continue
        if model_input.name not in captured:
            raise ValueError(f"Unexpected non-waveform input: {model_input.name}")
        converted_graph.graph.initializer.append(onnx.numpy_helper.from_array(captured[model_input.name], model_input.name))
        converted_graph.graph.input.remove(model_input)
    onnx.checker.check_model(converted_graph)
    onnx.save(converted_graph, str(model_path))
    options = ort.SessionOptions()
    options.intra_op_num_threads = 2
    options.inter_op_num_threads = 1
    options.log_severity_level = 3
    session = ort.InferenceSession(str(model_path), options, providers=["CPUExecutionProvider"])
    with (reference / "yamnet_class_map.csv").open(encoding="utf-8") as stream:
        classes = list(csv.DictReader(stream))
    index = {row["display_name"]: int(row["index"]) for row in classes}
    label_map = {index[name]: kind for kind, names in GROUPS.items() for name in names}
    rng = np.random.default_rng(4727)
    waveforms = [np.zeros(15600, dtype=np.float32)]
    for frequency in [110, 220, 440, 880, 1760]:
        waveforms.append((.1 * np.sin(np.arange(15600) * (2 * np.pi * frequency / 16000))).astype(np.float32))
    waveforms.extend(rng.normal(0, .03, 15600).astype(np.float32) for _ in range(14))
    corpus = []
    if args.music_root:
        import soundfile as sf
        from scipy.signal import resample_poly
        from math import gcd
        for path in sorted(Path(args.music_root).resolve().rglob("*.wav"))[:30]:
            with sf.SoundFile(str(path)) as audio:
                audio.seek(min(audio.frames // 2, audio.samplerate * 60))
                chunk = audio.read(int(audio.samplerate * 1.2), dtype="float32", always_2d=True).mean(axis=1)
                divisor = gcd(audio.samplerate, 16000)
                converted = resample_poly(chunk, 16000 // divisor, audio.samplerate // divisor).astype(np.float32)
                if len(converted) < 15600:
                    continue
                waveform = np.clip(converted[:15600], -1, 1)
                waveforms.append(waveform)
                corpus.append({"path": str(path), "startMs": min(audio.frames // 2, audio.samplerate * 60) * 1000 / audio.samplerate,
                               "annotated": False})
    errors, timings, expected_scores, dft_errors, export_errors = [], [], [], [], []
    for waveform in waveforms:
        expected = original(waveform, training=False)[0][0].numpy()
        dft_expected = convertible(waveform, training=False)[0][0].numpy()
        start = time.perf_counter()
        actual = session.run(None, {session.get_inputs()[0].name: waveform})[0]
        timings.append((time.perf_counter() - start) * 1000)
        errors.append(float(np.max(np.abs(expected - actual))))
        dft_errors.append(float(np.max(np.abs(expected - dft_expected))))
        export_errors.append(float(np.max(np.abs((expected if args.native_fft else dft_expected) - actual))))
        expected_scores.append(expected)
    maximum_error = max(errors)
    # Persist only self-generated parity vectors. User WAV samples remain in memory.
    np.stack(waveforms[:20]).tofile(output / "parity-inputs.f32")
    np.stack(expected_scores[:20]).tofile(output / "parity-scores.f32")
    report = {
        "sourceRevision": REVISION, "sourceWeightsSha256": ASSETS["yamnet.h5"],
        "sourceLicenseReference": "https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1",
        "modelSha256": sha256(model_path), "modelBytes": model_path.stat().st_size,
        "inputName": session.get_inputs()[0].name, "outputName": session.get_outputs()[0].name,
        "sampleRate": 16000, "windowSamples": 15600, "hopSamples": 7680, "outputClassCount": 521,
        "preprocessingVersion": "official-yamnet-fft-waveform-1" if args.native_fft else "official-yamnet-tflite-dft-waveform-1",
        "labelMap": label_map, "thresholdsCalibrated": False,
        "paritySamples": len(waveforms), "maximumAbsoluteError": maximum_error,
        "parityVectorSamples": 20, "parityVectorsContainUserAudio": False,
        "tensorflowFftVsDftMaximumError": max(dft_errors), "onnxVsExportSourceMaximumError": max(export_errors),
        "numericParityPassed": maximum_error <= 1e-4,
        "inferenceP50Ms": float(np.percentile(timings, 50)),
        "inferenceP95Ms": float(np.percentile(timings, 95)),
        "corpusRecordings": len(corpus), "corpusAnnotated": False,
        "accuracyValidated": False, "productionReady": False,
    }
    (output / "instrument-model-export.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    (output / "instrument-corpus.json").write_text(json.dumps(corpus, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({key: report[key] for key in ["numericParityPassed", "maximumAbsoluteError", "inferenceP95Ms", "corpusRecordings", "accuracyValidated"]}))
    if not report["numericParityPassed"]:
        raise SystemExit("ONNX parity gate failed")


if __name__ == "__main__":
    main()
