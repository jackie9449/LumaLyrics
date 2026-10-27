"""Private, recording-separated evaluation. Unreviewed audio never becomes ground truth.

Prepare a manifest without copying audio, review it outside the checkout, then run
the pinned YAMNet graph. This is an experiment, not a production model catalogue.
"""
import argparse
from bisect import bisect_right
import hashlib
import json
import math
from pathlib import Path
import time

KINDS = ("Drums", "Bass", "Guitar", "Piano", "Strings", "Brass", "Woodwind", "Synth")
SCENARIOS = {"dense", "piano", "strings", "electronic", "light", "negative"}
GRID_MS = 500


def outside_software(value):
    path = Path(value).resolve()
    repository = Path(__file__).resolve().parents[2]
    if path == repository or repository in path.parents or path in repository.parents:
        raise ValueError("Evaluation files must stay outside the software tree")
    return path


def write_report(path, report):
    path = outside_software(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    # Each run is an immutable review record, including unsuccessful evaluations.
    with path.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2, allow_nan=False)


def class_metrics(labels, predicted):
    if len(labels) != len(predicted):
        raise ValueError("Label and prediction time grids differ")
    tp = sum(a and b for a, b in zip(labels, predicted))
    fp = sum((not a) and b for a, b in zip(labels, predicted))
    fn = sum(a and (not b) for a, b in zip(labels, predicted))
    return {"precision": tp / (tp + fp) if tp + fp else None,
            "recall": tp / (tp + fn) if tp + fn else None,
            "tp": tp, "fp": fp, "fn": fn, "evaluatedBins": len(labels)}


def prepare_dataset(music_root, destination, count):
    import soundfile as sf
    root = Path(music_root).resolve(strict=True)
    destination = outside_software(destination)
    if destination.exists():
        raise FileExistsError("A dataset already exists; refusing to replace personal annotations")
    records = []
    for path in sorted(p for p in root.rglob("*") if p.suffix.lower() == ".wav"):
        with sf.SoundFile(str(path)) as audio:
            duration_ms = audio.frames * 1000 / audio.samplerate
        if duration_ms < 22000:
            continue
        # This opaque ID identifies a review entry, not a confirmed native recording.
        opaque = hashlib.sha256(str(path.relative_to(root)).encode("utf-8")).hexdigest()[:20]
        records.append({"recordingId": "review-" + opaque, "verifiedRecordingId": None,
                        "audioPath": str(path), "startMs": min(60000, duration_ms - 20000),
                        "durationMs": 20000, "split": "calibration" if len(records) % 2 == 0 else "holdout",
                        "reviewed": False, "reviewedKinds": [], "scenarioTags": [], "rightsNotes": "",
                        "events": [], "uncertain": []})
        if len(records) == count:
            break
    data = {"schemaVersion": 1, "timeConvention": "events/uncertain relative to the reviewed clip; half-open milliseconds",
            "instructions": "Listen to each clip. Annotate all eight families; Piano includes organ/harpsichord. "
                            "Empty events mean silence/absence only after review. Mark uncertain intervals. "
                            "Confirm independent recording identity, rights and split; repeated versions must share a split. "
                            "Whole-CD files are not automatically independent tracks. Never tune against holdout results.",
            "recordings": records}
    destination.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation also protects against another reviewer writing concurrently.
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)
    return {"status": "RequiresAnnotation", "reviewEntries": len(records), "accuracyValidated": False}


def checked_interval(value, duration, with_kind=False):
    start, end = value["startMs"], value["endMs"]
    if not all(isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x) for x in (start, end)):
        raise ValueError("Invalid annotation time")
    if not 0 <= start < end <= duration:
        raise ValueError("Annotation is outside its reviewed clip")
    if with_kind and value.get("kind") not in KINDS:
        raise ValueError("Unknown annotation family")


def validate_dataset(data):
    if data.get("schemaVersion") != 1 or not isinstance(data.get("recordings"), list):
        raise ValueError("Unsupported dataset schema")
    reviewed, seen, identities, files = [], set(), {}, {}
    for record in data["recordings"]:
        entry_id = record["recordingId"]
        if not isinstance(entry_id, str) or not entry_id or entry_id in seen:
            raise ValueError("Recording review entries must be unique")
        seen.add(entry_id)
        if record.get("reviewed") is not True:
            continue
        identity = record.get("verifiedRecordingId")
        if not isinstance(identity, str) or not identity.strip():
            raise ValueError("Reviewed entries require an independently confirmed recording identity")
        split = record["split"]
        if split not in ("calibration", "holdout") or identities.get(identity, split) != split:
            raise ValueError("The same recording cannot cross calibration and holdout")
        identities[identity] = split
        audio_file = str(Path(record["audioPath"]).resolve()).casefold()
        if files.get(audio_file, split) != split:
            raise ValueError("Clips from the same audio file cannot cross calibration and holdout")
        files[audio_file] = split
        if set(record.get("reviewedKinds", [])) != set(KINDS):
            raise ValueError("All candidate families must be reviewed before negative labels are valid")
        if not record.get("rightsNotes", "").strip():
            raise ValueError("Reviewed audio requires a private provenance/usage note")
        duration, start = record["durationMs"], record["startMs"]
        if not all(isinstance(x, (float, int)) and not isinstance(x, bool) and math.isfinite(x) for x in (duration, start)):
            raise ValueError("Invalid clip range")
        if not 1000 <= duration <= 300000 or start < 0:
            raise ValueError("Reviewed clips must be between one second and five minutes")
        for event in record["events"]:
            checked_interval(event, duration, True)
        for interval in record["uncertain"]:
            checked_interval(interval, duration)
        reviewed.append(record)
    return reviewed


def truth_grid(record, kind):
    times, labels, excluded = [], [], 0
    for start in range(0, math.floor(record["durationMs"] / GRID_MS) * GRID_MS, GRID_MS):
        end = start + GRID_MS
        if any(item["startMs"] < end and item["endMs"] > start for item in record["uncertain"]):
            excluded += 1
            continue
        center = (start + end) / 2
        times.append(center)
        labels.append(any(item["kind"] == kind and item["startMs"] <= center < item["endMs"] for item in record["events"]))
    return times, labels, excluded


def activity_states(scores, end_times, parameters):
    active, attack, last_supported = False, 0, 0
    states = []
    for score, stamp in zip(scores, end_times):
        if not active:
            attack = attack + 1 if score >= parameters["on"] else 0
            if attack >= parameters["attackFrames"]:
                active, last_supported = True, stamp
        elif score >= parameters["off"]:
            last_supported = stamp
        elif stamp - last_supported >= parameters["releaseMs"]:
            active, attack = False, 0
        states.append(active)
    return states


def aligned_predictions(sample, states, times):
    # A window score describes its centre; its availability is separately retained
    # at window end. Never treat inference completion as the music timestamp.
    centers = sample["centersMs"]
    result = []
    for stamp in times:
        index = bisect_right(centers, stamp)
        index = min(index, len(centers) - 1)
        if index and abs(centers[index - 1] - stamp) <= abs(centers[index] - stamp):
            index -= 1
        result.append(states[index])
    return result


def metrics_for(records, samples, kind, parameters):
    labels, predicted = [], []
    positives = negatives = excluded = 0
    for record in records:
        times, truth, uncertain = truth_grid(record, kind)
        states = activity_states(samples[record["recordingId"]][kind], samples[record["recordingId"]]["endsMs"], parameters)
        labels.extend(truth)
        predicted.extend(aligned_predictions(samples[record["recordingId"]], states, times))
        positives += int(any(truth))
        negatives += int(any(not value for value in truth))
        excluded += uncertain
    metrics = class_metrics(labels, predicted)
    metrics.update(positiveReviewEntries=positives, negativeReviewEntries=negatives, excludedBins=excluded)
    return metrics


def calibrate(records, samples, kind):
    best = None
    for on in (.1, .2, .3, .4, .5, .6, .7, .8):
        for attack in (1, 2):
            for release in (0, 500, 750):
                parameters = {"on": on, "off": on * .65, "attackFrames": attack, "releaseMs": release}
                metrics = metrics_for(records, samples, kind, parameters)
                precision, recall = metrics["precision"] or 0, metrics["recall"] or 0
                f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0
                rank = (precision >= .85 and recall >= .65, f1, precision, recall)
                if best is None or rank > best[0]:
                    best = (rank, parameters, metrics)
    return best[1], best[2]


def infer(records, model_directory):
    import numpy as np
    import onnxruntime as ort
    import soundfile as sf
    from scipy.signal import resample_poly
    root = outside_software(model_directory)
    metadata = json.loads((root / "instrument-model-export.json").read_text(encoding="utf-8"))
    model = root / "yamnet-waveform.onnx"
    with model.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if digest != metadata["modelSha256"] or not metadata["numericParityPassed"]:
        raise ValueError("The graph must pass the pinned numerical export gate")
    if (metadata["sampleRate"], metadata["windowSamples"], metadata["hopSamples"], metadata["outputClassCount"]) != (16000, 15600, 7680, 521):
        raise ValueError("Unsupported waveform/label schema")
    labels = metadata["labelMap"]
    if set(labels.values()) != set(KINDS) or any(not 0 <= int(index) < 521 for index in labels):
        raise ValueError("Invalid candidate family mapping")
    options = ort.SessionOptions()
    options.intra_op_num_threads, options.inter_op_num_threads = 2, 1
    options.log_severity_level = 3
    session = ort.InferenceSession(str(model), options, providers=["CPUExecutionProvider"])
    if len(session.get_inputs()) != 1 or session.get_inputs()[0].shape != [15600] or len(session.get_outputs()) != 1 or session.get_outputs()[0].shape != [521]:
        raise ValueError("Unexpected graph tensor shape")
    samples, timings = {}, []
    for record in records:
        with sf.SoundFile(record["audioPath"]) as audio:
            first = round(record["startMs"] * audio.samplerate / 1000)
            count = round(record["durationMs"] * audio.samplerate / 1000)
            if not 1 <= audio.channels <= 8 or first + count > audio.frames:
                raise ValueError("Reviewed clip is outside the source audio")
            audio.seek(first)
            chunk = audio.read(count, dtype="float32", always_2d=True).mean(axis=1)
            divisor = math.gcd(audio.samplerate, 16000)
            mono = resample_poly(chunk, 16000 // divisor, audio.samplerate // divisor).astype(np.float32)
        if not np.all(np.isfinite(mono)) or np.max(np.abs(mono)) > 1.001:
            raise ValueError("Invalid normalized source audio")
        mono = np.clip(mono, -1, 1)
        sample = {kind: [] for kind in KINDS}
        sample.update(endsMs=[], centersMs=[])
        for start in range(0, len(mono) - 15600 + 1, 7680):
            began = time.perf_counter()
            output = session.run([metadata["outputName"]], {metadata["inputName"]: mono[start:start + 15600]})[0]
            timings.append((time.perf_counter() - began) * 1000)
            if not np.all(np.isfinite(output)) or np.min(output) < 0 or np.max(output) > 1:
                raise ValueError("Invalid model score")
            for kind in KINDS:
                sample[kind].append(float(max(output[int(index)] for index, mapped in labels.items() if mapped == kind)))
            sample["endsMs"].append((start + 15600) / 16)
            sample["centersMs"].append((start + 7800) / 16)
        samples[record["recordingId"]] = sample
    return samples, {"modelSha256": digest, "mappingSha256": hashlib.sha256(json.dumps(labels, sort_keys=True).encode()).hexdigest(),
                     "inferenceP50Ms": float(np.percentile(timings, 50)), "inferenceP95Ms": float(np.percentile(timings, 95)),
                     "endToEndLatencyValidated": False}


def evaluate(data, model_directory):
    records = validate_dataset(data)
    if not records:
        return {"status": "RequiresAnnotation", "reviewEntries": len(data["recordings"]), "reviewedEntries": 0,
                "accuracyValidated": False, "productionReady": False}
    calibration = [record for record in records if record["split"] == "calibration"]
    holdout = [record for record in records if record["split"] == "holdout"]
    if not calibration or not holdout:
        raise ValueError("Reviewed calibration and holdout recordings are both required")
    samples, performance = infer(records, model_directory)
    results = {}
    for kind in KINDS:
        parameters, calibration_metrics = calibrate(calibration, samples, kind)
        heldout_metrics = metrics_for(holdout, samples, kind, parameters)
        results[kind] = {"parameters": parameters, "calibration": calibration_metrics, "holdout": heldout_metrics,
                         "candidatePassed": (calibration_metrics["recall"] is not None and
                                             heldout_metrics["precision"] is not None and heldout_metrics["precision"] >= .85 and
                                             heldout_metrics["recall"] is not None and heldout_metrics["recall"] >= .65)}
    independent = len({record["verifiedRecordingId"] for record in records})
    scenarios = set(tag for record in records for tag in record.get("scenarioTags", []))
    adequate = independent >= 30 and SCENARIOS <= scenarios
    return {"status": "Evaluated" if adequate else "InsufficientCorpus", "independentRecordings": independent,
            "reviewedEntries": len(records), "calibrationEntries": len(calibration), "holdoutEntries": len(holdout),
            "missingScenarios": sorted(SCENARIOS - scenarios), "classes": results, "performance": performance,
            "accuracyValidated": adequate and any(value["candidatePassed"] for value in results.values()),
            "productionReady": False,
            "remainingGates": ["manual onset/offset comparison", "C# streaming resource/latency test", "live capture matrix", "UI, cache and distribution validation"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--model", choices=["yamnet", "panns"], default="yamnet")
    parser.add_argument("--model-dir")
    parser.add_argument("--output")
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--music-root")
    parser.add_argument("--count", type=int, default=30)
    args = parser.parse_args()
    dataset = outside_software(args.dataset)
    if args.prepare:
        if not args.music_root or not 30 <= args.count <= 200:
            parser.error("Preparation needs --music-root and --count between 30 and 200")
        report = prepare_dataset(args.music_root, dataset, args.count)
    else:
        if not args.output:
            parser.error("Evaluation needs a private --output")
        data = json.loads(dataset.read_text(encoding="utf-8-sig"))
        if args.model == "panns":
            report = {"status": "CandidateNotImplemented", "accuracyValidated": False, "productionReady": False}
        elif not validate_dataset(data):
            report = evaluate(data, None)
        else:
            if not args.model_dir:
                parser.error("Reviewed data requires a pinned --model-dir")
            report = evaluate(data, args.model_dir)
        write_report(args.output, report)
    print(json.dumps({key: report[key] for key in ("status", "accuracyValidated")}))


if __name__ == "__main__":
    main()
