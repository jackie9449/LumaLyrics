"""Compare causal MATPAC context/pooling candidates on private URMP fixtures.

Uses the pinned publisher implementation. No model/recording is copied into Git.
Scores are compared to publisher note ranges at the result time, not retrospectively
shifted to the middle of the model window. This is an experiment, not qualification.
"""
import argparse
import json
import os
import re
import time
from math import gcd
from pathlib import Path

from prepare_matpac import fetch_assets, LABELS
from prepare_yamnet import private_directory, sha256

CODES = {"vn": "Violin", "vc": "Cello", "db": "Bass", "fl": "Flute", "cl": "Clarinet",
         "sax": "Saxophone", "tpt": "Trumpet", "tbn": "Trombone"}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--reference-dir", required=True)
    p.add_argument("--dataset", required=True)
    p.add_argument("--output-dir", required=True)
    p.add_argument("--split", choices=["calibration", "holdout", "all"], default="calibration")
    p.add_argument("--hop-seconds", type=float, default=2)
    p.add_argument("--limit", type=int)
    p.add_argument("--contexts", default="19,9", help="Comma separated temporal patch counts, at most 38.")
    p.add_argument("--save-embeddings", action="store_true", help="Private frozen features only; never raw PCM.")
    a = p.parse_args()
    contexts = [int(value) for value in a.contexts.split(",")]
    if not contexts or any(value < 4 or value > 38 for value in contexts) or not 0 < a.hop_seconds <= 30:
        raise ValueError("Invalid bounded context/hop")
    root, reference = private_directory(a.output_dir), private_directory(a.reference_dir)
    fetch_assets(reference)
    os.environ.update(USE_TF="0", HF_HUB_OFFLINE="1", HF_MODULES_CACHE=str(reference / "hf-modules"))
    import numpy as np
    import soundfile as sf
    import torch
    from scipy.signal import resample_poly
    from safetensors.torch import load_file
    from transformers import AutoModel
    torch.set_num_threads(2)
    model = AutoModel.from_pretrained(str(reference), trust_remote_code=True, local_files_only=True).eval()
    head = load_file(reference / "openmic-head.safetensors", device="cpu")

    def classify(emb):
        hidden = torch.relu(((emb - head["mu"]) / head["sd"]) @ head["w1"] + head["b1"])
        return torch.sigmoid(hidden @ head["w2"] + head["b2"])

    records = json.loads(Path(a.dataset).read_text(encoding="utf-8-sig"))["recordings"]
    if any(r.get("publishedAnnotationsVerified") is not True or r.get("split") not in ("calibration", "holdout") for r in records):
        raise ValueError("Comparison requires the prepared, publisher-annotated URMP manifest")
    records = [r for r in records if a.split == "all" or r["split"] == a.split]
    if a.limit:
        records = records[:a.limit]
    results, features = [], {}
    clock = time.perf_counter()
    for record in records:
        path = Path(record["audioPath"])
        audio, rate = sf.read(path, dtype="float32", always_2d=True)
        audio = resample_poly(audio.mean(1), 16000 // gcd(rate, 16000), rate // gcd(rate, 16000)).astype(np.float32)
        events = {}
        for note in path.parent.glob("Notes_*.txt"):
            code = re.match(r"Notes_\d+_([^_]+)_", note.name)[1]
            kind = CODES.get(code)
            if kind is None:
                continue
            for row in note.read_text().splitlines():
                fields = row.split()
                if len(fields) == 3:
                    start, _, duration = map(float, fields)
                    events.setdefault(kind, []).append((start, start + duration))
        scores = {name: [] for patches in contexts for name in [f"global{patches}", f"tail4of{patches}"] + (["tail8of19"] if patches == 19 else [])}
        ends, truths, costs, energies = [], [], [], []
        with torch.inference_mode():
            for end in range(max(48880, max(contexts) * 16 * 160 + 240), len(audio) + 1, round(a.hop_seconds * 16000)):
                ends.append(end / 16)
                truths.append([any(start - .15 <= end / 16000 < finish + .15 for start, finish in events.get(LABELS[i], [])) for i in range(19)])
                t = time.perf_counter()
                for patches in contexts:
                    window = patches * 16 * 160 + 240
                    vector = torch.from_numpy(audio[end-window:end]).unsqueeze(0)
                    emb = model(vector, pull_time_dimension=False).hidden_states[10]
                    pools = [emb.mean(1), emb[:, -4:, :].mean(1)]
                    names = [f"global{patches}", f"tail4of{patches}"]
                    if patches == 19:
                        pools.append(emb[:, -8:, :].mean(1)); names.append("tail8of19")
                    output = classify(torch.cat(pools)).numpy()
                    for name, values in zip(names, output):
                        scores[name].append(values.tolist())
                    if a.save_embeddings:
                        for name, pool in zip(names, pools):
                            features.setdefault(name, []).append(pool[0].numpy().copy())
                costs.append((time.perf_counter() - t) * 1000)
                energies.append(float(np.sqrt(np.mean(audio[end-48880:end].astype(np.float64) ** 2))))
        results.append(dict(recordingId=record["recordingId"], compositionId=record["compositionId"], split=record["split"], endsMs=ends,
                            truth=truths, present=list(events), scores=scores, rms=energies,
                            combinedInferenceP95Ms=float(np.percentile(costs, 95))))
        print(json.dumps(dict(recordingId=record["recordingId"], windows=len(ends), completed=len(results))), flush=True)
    report = dict(recordings=results, labels=LABELS, hopSeconds=a.hop_seconds, datasetSha256=sha256(a.dataset),
                  timeConvention="causal-window-end-with-publisher-note-150ms-tolerance",
                  elapsedSeconds=time.perf_counter() - clock, accuracyValidated=False)
    (root / "instrument-window-comparison.json").write_text(json.dumps(report), encoding="utf-8")
    if a.save_embeddings:
        np.savez_compressed(root / "instrument-features.npz", **{name: np.stack(values) for name, values in features.items()})
    y = np.array([t for r in results for t in r["truth"]], bool)
    for name in results[0]["scores"]:
        x = np.array([s[:19] for r in results for s in r["scores"][name]])
        for threshold in [.5, .7, .8, .9]:
            z = x >= threshold
            tp = (z & y).sum(); fp = (z & ~y).sum(); fn = (~z & y).sum()
            print(json.dumps(dict(candidate=name, threshold=threshold, precision=float(tp / max(1, tp+fp)),
                                  recall=float(tp / max(1, tp+fn)), f1=float(2*tp / max(1,2*tp+fp+fn)))), flush=True)


if __name__ == "__main__":
    main()
