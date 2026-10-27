"""Download public URMP mixtures/notes into personal storage, with pinned mirror hashes.

Primary provenance: Li et al., URMP, Zenodo 5034983 (CC0). The pinned mirror
provides individually addressable files; it is not described as the publisher.
"""
import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import time
import urllib.parse
import urllib.request

from evaluate import KINDS, outside_software

REVISION = "58177a0f0f816e621b3d5d304c5fd1a03c035a86"
REPOSITORY = "Eredis02/URMP"
PRIMARY = "https://zenodo.org/records/5034983"
FAMILIES = {"vn": "Strings", "va": "Strings", "vc": "Strings", "db": "Bass",
            "fl": "Woodwind", "ob": "Woodwind", "cl": "Woodwind", "sax": "Woodwind", "bn": "Woodwind",
            "tpt": "Brass", "hn": "Brass", "tbn": "Brass", "tba": "Brass"}


def get_json(url):
    with urllib.request.urlopen(url, timeout=30) as response:
        return json.loads(response.read(4 * 1024 * 1024))


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download(entry, root):
    relative = Path(entry["path"])
    if relative.is_absolute() or ".." in relative.parts or len(relative.parts) != 2:
        raise ValueError("Unexpected dataset path")
    target = root / "files" / relative
    size = entry["size"]
    if not 0 < size < 100 * 1024 * 1024:
        raise ValueError("Dataset member exceeds size limit")

    def verified():
        if not target.is_file() or target.stat().st_size != size:
            return False
        if "lfs" in entry:
            return digest(target) == entry["lfs"]["oid"]
        raw = target.read_bytes()
        return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() == entry["oid"]

    if not verified():
        target.parent.mkdir(parents=True, exist_ok=True)
        partial = target.with_suffix(target.suffix + ".partial")
        url = f"https://huggingface.co/datasets/{REPOSITORY}/resolve/{REVISION}/" + urllib.parse.quote(entry["path"])
        for attempt in range(4):
            try:
                with urllib.request.urlopen(url, timeout=60) as response, partial.open("wb") as output:
                    copied = 0
                    while chunk := response.read(65536):
                        copied += len(chunk)
                        if copied > size:
                            raise ValueError("Unexpected dataset size")
                        output.write(chunk)
                if partial.stat().st_size != size:
                    raise ValueError("Truncated dataset member")
                if "lfs" in entry:
                    valid = digest(partial) == entry["lfs"]["oid"]
                else:
                    raw = partial.read_bytes()
                    valid = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() == entry["oid"]
                if not valid:
                    raise ValueError("Dataset checksum mismatch")
                partial.replace(target)
                break
            except Exception:
                partial.unlink(missing_ok=True)
                if attempt == 3:
                    raise
                time.sleep(attempt + 1)
    return {"path": entry["path"], "bytes": size, "sha256": digest(target)}


def union(intervals):
    result = []
    for start, end in sorted(intervals):
        if result and start <= result[-1][1]:
            result[-1][1] = max(result[-1][1], end)
        else:
            result.append([start, end])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()
    root = outside_software(args.output_dir)
    root.mkdir(parents=True, exist_ok=True)
    primary = get_json("https://zenodo.org/api/records/5034983")
    if primary["metadata"]["license"]["id"] != "cc-zero":
        raise ValueError("Publisher licence changed; inspect before downloading")
    (root / "publisher-record.json").write_text(json.dumps(primary, indent=2), encoding="utf-8")
    # Preserve the publisher's timing/format definition alongside mirror provenance.
    readme = root / "README_for_Dataset.tar.pdf"
    if not readme.exists() or hashlib.md5(readme.read_bytes()).hexdigest() != "6b29bc5744a7b013f79f3baf57b4913a":
        with urllib.request.urlopen("https://zenodo.org/api/records/5034983/files/README_for_Dataset.tar.pdf/content", timeout=60) as response:
            raw = response.read(200000)
        if len(raw) != 168924 or hashlib.md5(raw).hexdigest() != "6b29bc5744a7b013f79f3baf57b4913a":
            raise ValueError("Publisher annotation definition changed")
        readme.write_bytes(raw)
    tree = get_json(f"https://huggingface.co/api/datasets/{REPOSITORY}/tree/{REVISION}?recursive=true&limit=1000")
    entries = [entry for entry in tree if entry["type"] == "file" and
               ("/AuMix_" in entry["path"] and entry["path"].endswith(".wav") or
                "/Notes_" in entry["path"] and entry["path"].endswith(".txt"))]
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        hashes = list(pool.map(lambda entry: download(entry, root), entries))
    provenance = {"primary": PRIMARY, "license": "CC0-1.0", "mirror": REPOSITORY, "revision": REVISION, "files": hashes}
    (root / "download-provenance.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    import soundfile as sf
    recordings = []
    folders = sorted({Path(entry["path"]).parent.name for entry in entries})
    compositions = sorted({folder.split("_")[1] for folder in folders})
    # Split whole compositions, including alternate arrangements, before viewing model scores.
    heldout = {piece for index, piece in enumerate(compositions) if index % 3 == 0}
    for folder in folders:
        directory = root / "files" / folder
        audio = next(directory.glob("AuMix_*.wav"))
        info = sf.info(audio)
        duration = info.frames * 1000 / info.samplerate
        notes = {kind: [] for kind in KINDS}
        for path in sorted(directory.glob("Notes_*.txt")):
            kind = FAMILIES[path.name.split("_")[2]]
            for line in path.read_text(encoding="ascii").splitlines():
                start, pitch, length = map(float, line.split())
                if start < 0 or pitch <= 0 or length <= 0 or (start + length) * 1000 > duration + 50:
                    raise ValueError("Published note is outside the audio")
                notes[kind].append((start * 1000, min(duration, (start + length) * 1000)))
        events, uncertain = [], []
        for kind, intervals in notes.items():
            for start, end in union(intervals):
                events.append({"kind": kind, "startMs": start, "endMs": end})
                # Reference note edges are not exact acoustic/reverberation boundaries.
                for edge in (start, end):
                    uncertain.append({"kind": kind, "startMs": max(0, edge - 75), "endMs": min(duration, edge + 75)})
        recordings.append({"recordingId": "urmp-" + folder, "verifiedRecordingId": "urmp-" + folder,
                           "compositionId": folder.split("_")[1], "audioPath": str(audio), "startMs": 0, "durationMs": duration,
                           "split": "holdout" if folder.split("_")[1] in heldout else "calibration",
                           "reviewed": False, "publishedAnnotationsVerified": True,
                           "annotationProvenance": {"primary": PRIMARY, "method": "publisher-played-note-transcription", "mirrorRevision": REVISION},
                           "reviewedKinds": list(KINDS), "scenarioTags": ["strings", "light", "negative"],
                           "rightsNotes": "URMP / Li et al. / Zenodo 5034983 / CC0; individually hashed mirror copy",
                           "events": events, "uncertain": uncertain})
    dataset = {"schemaVersion": 1, "domain": "URMP classical chamber ensembles; no piano/drum/guitar/synth positive qualification",
               "timeConvention": "clip-relative half-open milliseconds; note onset plus note duration", "recordings": recordings}
    destination = root / "dataset.json"
    if destination.exists() and destination.read_text(encoding="utf-8") != json.dumps(dataset, indent=2):
        raise FileExistsError("A different dataset manifest already exists; choose another output folder")
    destination.write_text(json.dumps(dataset, indent=2), encoding="utf-8")
    print(json.dumps({"recordings": len(recordings), "files": len(hashes), "bytes": sum(entry["bytes"] for entry in hashes),
                      "publishedAnnotations": True, "manualReviewClaimed": False}))


if __name__ == "__main__":
    main()
