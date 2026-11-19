"""Isolated W2V-BERT inference. The worker receives only a local PCM stream and original lines."""
import json
import os
import struct
import sys
import unicodedata

RATE = 16000
FRAME = 320
SECONDS = 900


def normalize(text, vocab):
    result = []
    for char in unicodedata.normalize('NFKC', text).casefold().replace('\u2019', "'"):
        if '\uac00' <= char <= '\ud7a3':
            result.extend(unicodedata.normalize('NFD', char))
        elif char.isspace():
            if result and result[-1] != '|':
                result.append('|')
        elif char.isalnum() or unicodedata.category(char).startswith('M') or char == "'":
            result.append(char)
    while result and result[-1] == '|':
        result.pop()
    return [vocab[c] for c in result] if result and all(c in vocab for c in result) else None


def emissions(samples, model_dir):
    import numpy as np
    import torch
    from safetensors.torch import load_file
    from transformers import Wav2Vec2BertForCTC, Wav2Vec2BertProcessor

    torch.set_num_threads(min(8, max(1, (os.cpu_count() or 2) // 2)))
    torch.set_num_interop_threads(1)
    processor = Wav2Vec2BertProcessor.from_pretrained(model_dir, local_files_only=True)
    model = Wav2Vec2BertForCTC.from_pretrained(model_dir, local_files_only=True, use_safetensors=True).eval()
    heads = load_file(os.path.join(model_dir, 'aux_heads.safetensors'))
    logits, voiced = [], []
    with torch.inference_mode():
        for start in range(0, len(samples), 24 * RATE):
            lo, hi = max(0, start - 2 * RATE), min(len(samples), start + 26 * RATE)
            encoded = processor(samples[lo:hi], sampling_rate=RATE, return_tensors='pt')
            out = model(**encoded, output_hidden_states=True)
            selection = slice((start - lo) // FRAME, (start + 24 * RATE - lo) // FRAME)
            logits.append(out.logits[0, selection].log_softmax(-1).numpy())
            hidden = out.hidden_states[-1][0, selection]
            activity = torch.nn.functional.linear(hidden, heads['vad.weight'], heads['vad.bias']).sigmoid()
            voiced.append(activity.reshape(-1).numpy())
    return np.concatenate(logits), np.concatenate(voiced), processor.tokenizer.get_vocab(), processor.tokenizer.pad_token_id


def align(logp, voiced, vocab, blank, lines):
    import numpy as np

    filler = vocab['|']
    labels, text_rows = [filler], []
    for line in lines:
        chars = normalize(line, vocab)
        if chars:
            text_rows.append((len(labels), len(labels) + len(chars) - 1))
            labels.extend(chars)
            labels.append(filler)
        else:
            text_rows.append(None)
    if not any(r is not None for r in text_rows):
        return [None] * len(lines)
    states = 2 * len(labels) + 1
    if states * len(logp) > 96_000_000:
        raise OverflowError('alignment matrix bound')

    target = np.full(states, blank, dtype=np.int32)
    target[1::2] = labels
    allowed = (target != blank) & (target != np.r_[[-1, -1], target[:-2]])
    path = np.empty((len(logp), states), dtype=np.uint8)
    score = np.full(states, -np.inf, dtype=np.float32)
    score[0] = 0
    one, two = np.full_like(score, -np.inf), np.full_like(score, -np.inf)
    for index, frame in enumerate(logp):
        one[1:], two[2:] = score[:-1], score[:-2]
        two[~allowed] = -np.inf
        path[index] = one > score
        np.maximum(score, one, out=score)
        path[index, two > score] = 2
        np.maximum(score, two, out=score)
        score += frame[target]
    state = states - 1 - int(score[-2] > score[-1])
    if not np.isfinite(score[state]):
        return [None] * len(lines)

    start = np.full(states, -1, dtype=np.int32)
    end = start.copy()
    peak = np.full(states, -np.inf, dtype=np.float32)
    for frame in range(len(logp) - 1, -1, -1):
        if end[state] < 0:
            end[state] = frame + 1
        start[state] = frame
        peak[state] = max(peak[state], logp[frame, target[state]])
        state -= int(path[frame, state])

    spans = [None] * len(lines)
    for ix, row in enumerate(text_rows):
        if row is None:
            continue
        first, last = (2 * t + 1 for t in row)
        onset, finish = int(start[first]), int(end[last])
        acoustic = np.exp(peak[[t for t in range(first, last + 1, 2) if target[t] != filler]])
        if not (0 <= onset < finish <= len(voiced) and finish - onset <= 1500):
            continue
        if np.median(acoustic) < .20 or np.mean(acoustic >= .10) < .64 or np.max(voiced[onset:finish]) < .55:
            continue
        # A low-activity run can separate the lines; a CTC letter spike alone is not a line end.
        next_onset = len(voiced)
        for later in text_rows[ix + 1:]:
            if later is not None:
                next_onset = int(start[2 * later[0] + 1])
                break
        if next_onset < finish:
            continue
        quiet = 0
        boundary = None
        for frame in range(finish, min(next_onset, finish + 250)):
            quiet = quiet + 1 if voiced[frame] < .42 else 0
            if quiet >= 12:
                boundary = frame + 1 - quiet
                break
        if boundary is None:
            if next_onset - finish > 35 or next_onset == len(voiced):
                # Preserve the detected onset even when a trailing note or backing vocal obscures the end.
                spans[ix] = {'StartMs': onset * 20, 'EndMs': None}
                continue
            boundary = next_onset
        if boundary > onset:
            spans[ix] = {'StartMs': onset * 20, 'EndMs': boundary * 20}
    return spans


def main():
    import numpy as np

    source = sys.stdin.buffer
    raw = source.read(4)
    size = struct.unpack('<I', raw)[0]
    if not 0 < size <= 512 * 1024:
        raise ValueError('header limit')
    msg = json.loads(source.read(size))
    count, lines = msg['Count'], msg['Lines']
    if not (RATE <= count <= SECONDS * RATE and 0 < len(lines) <= 1000 and all(isinstance(l, str) and l for l in lines)):
        raise ValueError('input bounds')
    pcm = source.read(count * 2)
    if len(pcm) != count * 2 or source.read(1):
        raise ValueError('PCM length')
    samples = np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768
    logp, voiced, vocab, blank = emissions(samples, sys.argv[1])
    return align(logp, voiced, vocab, blank, lines)


if __name__ == '__main__':
    try:
        print(json.dumps({'Spans': main()}, ensure_ascii=True, allow_nan=False))
    except OverflowError:
        print('{"Error":"size"}')
    except Exception as error:
        # Never include lyric text, the user's paths, or raw model input in diagnostics.
        print('{"Error":"runtime"}')
        if os.environ.get('LUMA_TIMING_TEST_ERRORS') == '1':
            print(type(error).__name__, file=sys.stderr)
        sys.exit(1)
