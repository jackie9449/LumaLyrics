"""Fit a conservative frozen-feature veto using calibration compositions only.

The gate can only attenuate the publisher's scores for seven URMP-covered
instruments. It cannot promote a new label or qualify the other twelve outputs.
All learned arrays and per-record reports stay in personal storage.
"""
import argparse
import hashlib
import json
from pathlib import Path

from prepare_yamnet import private_directory, sha256

INDICES = [3, 4, 7, 13, 15, 16, 18]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features-dir", required=True)
    parser.add_argument("--baseline-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    a = parser.parse_args()
    import numpy as np
    import torch
    torch.set_num_threads(2)
    root = private_directory(a.output_dir)
    source = Path(a.features_dir)
    data = json.loads((source / "instrument-window-comparison.json").read_text())
    arrays = np.load(source / "instrument-features.npz")
    records = data["recordings"]
    compositions = {}
    for record in records:
        compositions.setdefault(record["compositionId"], set()).add(record["split"])
    if any(len(splits) != 1 for splits in compositions.values()):
        raise ValueError("Calibration/holdout composition leakage")
    truth = np.array([row for r in records for row in r["truth"]], bool)
    calibrate = np.array([r["split"] == "calibration" for r in records for _ in r["truth"]])
    folds = np.array([int(hashlib.sha256(r["compositionId"].encode()).hexdigest()[:8], 16) % 3 for r in records for _ in r["truth"]])
    classes = np.array(INDICES)
    # Product's existing raw score thresholds, before hysteresis/roster admission.
    thresholds = np.full(19, .7); thresholds[[2, 5, 6, 8]] = .5

    def metric(scores, mask):
        pred, y = scores[mask] >= thresholds, truth[mask]
        tp, fp, fn = int((pred & y).sum()), int((pred & ~y).sum()), int((~pred & y).sum())
        return dict(tp=tp, fp=fp, fn=fn, precision=tp/max(1,tp+fp), recall=tp/max(1,tp+fn), f1=2*tp/max(1,2*tp+fp+fn))

    def fit(x, y, regularization):
        torch.manual_seed(42028)
        raw = torch.tensor(x)
        mu, sd = raw.mean(0), raw.std(0).clamp_min(.05)
        z = (raw-mu)/sd
        targets = torch.tensor(y.astype(np.float32))
        positive = targets.sum(0)
        balance = ((len(x)-positive)/positive.clamp_min(1)).sqrt().clamp(1, 5)
        linear = torch.nn.Linear(x.shape[1], len(INDICES))
        torch.nn.init.zeros_(linear.weight); torch.nn.init.zeros_(linear.bias)
        optimizer = torch.optim.Adam(linear.parameters(), lr=.02)
        for epoch in range(180):
            optimizer.zero_grad()
            loss = torch.nn.functional.binary_cross_entropy_with_logits(linear(z), targets, pos_weight=balance)
            loss = loss + regularization * linear.weight.square().sum()/2
            loss.backward(); optimizer.step()
        with torch.no_grad():
            w = linear.weight/sd
            b = linear.bias-w@mu
            # A fold with no adequate positive examples must not invent a veto.
            missing = positive < 10
            w[missing] = 0; b[missing] = 30
            return w.numpy(), b.numpy()

    def predict(x, w, b):
        return 1/(1+np.exp(-np.clip(x@w.T+b, -60, 60)))

    best, history = None, []
    for name in [key for key in arrays.files if key in ("global9", "tail4of9")]:
        x = arrays[name]
        original = np.array([row[:19] for r in records for row in r["scores"][name]], np.float32)
        for penalty in [.001, .01, .1]:
            oof = np.zeros((len(x), len(INDICES)), np.float32)
            for fold in range(3):
                train, valid = calibrate & (folds != fold), calibrate & (folds == fold)
                w,b = fit(x[train], truth[train][:, classes], penalty)
                oof[valid] = predict(x[valid], w,b)
            for floor in [.25, .5, .75]:
                corrected = original.copy()
                corrected[:, classes] *= np.minimum(1, oof/floor)
                value = metric(corrected, calibrate)
                candidate = dict(pool=name, l2=penalty, gateFloor=floor, calibrationGroupedOof=value)
                history.append(candidate)
                if best is None or value["f1"] > best["calibrationGroupedOof"]["f1"]:
                    best = candidate
    # Freeze all choices before looking at held-out compositions.
    (root / "instrument-gate-selection.json").write_text(json.dumps(dict(selected=best, candidates=history), indent=2))
    name = best["pool"]; x = arrays[name]
    w,b = fit(x[calibrate], truth[calibrate][:, classes], best["l2"])
    path = root / "matpac-gate.npz"
    np.savez(path, weight=w, bias=b, indices=classes, floor=np.float32(best["gateFloor"]))
    original = np.array([row[:19] for r in records for row in r["scores"][name]], np.float32)
    corrected = original.copy(); corrected[:, classes] *= np.minimum(1, predict(x,w,b)/best["gateFloor"])
    baseline_path = Path(a.baseline_dir)/"instrument-scores.json"
    old_format = baseline_path.exists()
    if not old_format:
        baseline_path = Path(a.baseline_dir)/"instrument-window-comparison.json"
    baseline = json.loads(baseline_path.read_text())
    by_id = {r["recordingId"]:r for r in baseline["recordings"]}
    for record in records:
        prior_record = by_id[record["recordingId"]]
        prior = np.asarray(prior_record["centersMs"]) + 1527.5 if old_format else np.asarray(prior_record["endsMs"])
        if prior.shape != (len(record["endsMs"]),) or not np.allclose(prior, record["endsMs"], atol=.001):
            raise ValueError("Baseline causal times do not align")
    raw_baseline = np.array([row[:19] for r in records for row in (by_id[r["recordingId"]]["matpac"] if old_format else by_id[r["recordingId"]]["scores"]["global19"])], np.float32)
    if raw_baseline.shape != original.shape:
        raise ValueError("Baseline windows do not align")
    report = dict(selected=best, gateSha256=sha256(path), indices=INDICES,
                  heldoutBaseline=metric(raw_baseline, ~calibrate), heldoutShort=metric(original, ~calibrate),
                  heldoutCorrected=metric(corrected, ~calibrate),
                  calibrationFullFit=metric(corrected, calibrate), featureSha256=sha256(source/"instrument-features.npz"),
                  accuracyValidated=False, productionReady=False)
    (root / "instrument-gate-evaluation.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
