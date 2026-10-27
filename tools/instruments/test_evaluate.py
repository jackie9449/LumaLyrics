import copy
import unittest
from unittest.mock import patch

import evaluate


def reviewed_record():
    return {"recordingId": "authored-01", "verifiedRecordingId": "independent-01", "audioPath": "authored.wav",
            "startMs": 0, "durationMs": 2000, "split": "calibration", "reviewed": True,
            "reviewedKinds": list(evaluate.KINDS), "rightsNotes": "Self-authored numerical fixture; no actual audio file",
            "events": [{"kind": "Piano", "startMs": 0, "endMs": 1000}],
            "uncertain": [{"startMs": 500, "endMs": 750}]}


class EvaluationTests(unittest.TestCase):
    def test_known_confusion_matrix(self):
        metrics = evaluate.class_metrics([True, False, True], [True, True, False])
        self.assertEqual((metrics["precision"], metrics["recall"]), (.5, .5))
        self.assertEqual((metrics["tp"], metrics["fp"], metrics["fn"]), (1, 1, 1))

    def test_missing_positive_is_not_perfect_recall(self):
        metrics = evaluate.class_metrics([False, False], [False, False])
        self.assertIsNone(metrics["recall"])
        self.assertIsNone(metrics["precision"])
        with self.assertRaises(ValueError):
            evaluate.class_metrics([True], [])

    def test_uncertain_overlap_excludes_whole_bin(self):
        times, labels, excluded = evaluate.truth_grid(reviewed_record(), "Piano")
        self.assertEqual(times, [250, 1250, 1750])
        self.assertEqual(labels, [True, False, False])
        self.assertEqual(excluded, 1)

    def test_unreviewed_empty_annotations_never_become_negatives(self):
        record = reviewed_record()
        record["reviewed"] = False
        with patch("evaluate.infer", side_effect=AssertionError("Unreviewed audio reached inference")):
            report = evaluate.evaluate({"schemaVersion": 1, "recordings": [record]}, None)
        self.assertEqual(report["status"], "RequiresAnnotation")
        self.assertFalse(report["accuracyValidated"])

    def test_partial_family_review_cannot_claim_negative_labels(self):
        record = reviewed_record()
        record["reviewedKinds"] = ["Piano"]
        with self.assertRaises(ValueError):
            evaluate.validate_dataset({"schemaVersion": 1, "recordings": [record]})

    def test_same_recording_or_file_cannot_leak_across_split(self):
        first = reviewed_record()
        second = copy.deepcopy(first)
        second.update(recordingId="authored-02", split="holdout", audioPath="second.wav")
        with self.assertRaises(ValueError):
            evaluate.validate_dataset({"schemaVersion": 1, "recordings": [first, second]})
        second.update(verifiedRecordingId="independent-02", audioPath="authored.wav")
        with self.assertRaises(ValueError):
            evaluate.validate_dataset({"schemaVersion": 1, "recordings": [first, second]})

    def test_annotation_bounds_are_checked(self):
        for end in (2001, float("nan"), -1):
            record = reviewed_record()
            record["events"][0]["endMs"] = end
            with self.assertRaises(ValueError):
                evaluate.validate_dataset({"schemaVersion": 1, "recordings": [record]})
        record = reviewed_record()
        record["uncertain"][0]["kind"] = "unrecognised-family"
        with self.assertRaises(ValueError):
            evaluate.validate_dataset({"schemaVersion": 1, "recordings": [record]})

    def test_causal_attack_and_release(self):
        states = evaluate.activity_states([.9, .9, .3, .3, .3], [500, 1000, 1500, 2000, 2500],
                                          {"on": .8, "off": .4, "attackFrames": 2, "releaseMs": 750})
        self.assertEqual(states, [False, True, True, False, False])

    def test_family_uncertainty_does_not_hide_other_family_negatives(self):
        record = reviewed_record()
        record["uncertain"][0]["kind"] = "Piano"
        self.assertEqual(evaluate.truth_grid(record, "Strings")[2], 0)
        self.assertEqual(evaluate.truth_grid(record, "Piano")[2], 1)

    def test_composition_arrangements_stay_in_one_split(self):
        first = reviewed_record()
        first["compositionId"] = "authored-composition"
        second = dict(first, recordingId="other", verifiedRecordingId="other", audioPath="other.wav", split="holdout")
        with self.assertRaises(ValueError):
            evaluate.validate_dataset({"schemaVersion": 1, "recordings": [first, second]})

    def test_calibration_must_also_pass_before_enabling_a_candidate(self):
        self.assertFalse(evaluate.passes({"precision": .5, "recall": .57}))
        self.assertFalse(evaluate.passes({"precision": None, "recall": None}))
        self.assertTrue(evaluate.passes({"precision": .99, "recall": .89}))

    def test_unmatched_boundaries_are_not_omitted_from_report(self):
        record = reviewed_record()
        sample = {"Piano": [0, 0, 0], "centersMs": [250, 750, 1250], "endsMs": [500, 1000, 1500]}
        result = evaluate.boundary_metrics([record], {record["recordingId"]: sample}, "Piano",
                                            {"on": .5, "off": .3, "attackFrames": 1, "releaseMs": 0})
        self.assertEqual(result["offset"]["unmatchedReferenceEdges"], 1)
        self.assertIsNone(result["offset"]["medianAbsoluteErrorMs"])


if __name__ == "__main__":
    unittest.main()
