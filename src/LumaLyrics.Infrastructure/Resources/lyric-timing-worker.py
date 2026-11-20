"""Isolated W2V-BERT inference. The worker receives only a local PCM stream and original lines."""
import json
import os
import re
import struct
import sys
import unicodedata
from pathlib import Path

RATE = 16000
FRAME = 320
SECONDS = 900
LATIN_PUNCTUATION = str.maketrans({
    '’': "'", '‘': "'", 'ʼ': "'", '-': ' ', '‐': ' ', '‑': ' ', '–': ' ', '—': ' ',
})
ROMAJI_LONG_VOWELS = str.maketrans({
    'ā': 'aa', 'ī': 'ii', 'ū': 'uu', 'ē': 'ee', 'ō': 'ou',
    'â': 'aa', 'î': 'ii', 'û': 'uu', 'ê': 'ee', 'ô': 'ou',
})


def normalize(text, vocab, language=''):
    text = unicodedata.normalize('NFKC', text)
    if language in ('fr', 'de'):
        # Lowercasing preserves the German sharp s as a separate CTC label.
        text = text.lower().translate(LATIN_PUNCTUATION)
        if language == 'fr' and 'ÿ' not in vocab:
            text = text.replace('ÿ', 'y')
    else:
        text = text.casefold().replace('\u2019', "'")
    result = []
    for char in text:
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


def kana_to_romaji(term, readings):
    term = ''.join(chr(ord(char) + 96) if 'ぁ' <= char <= 'ゖ' else char for char in term)
    result, double, index = [], False, 0
    while index < len(term):
        char = term[index]
        pair = term[index:index + 2]
        index += 1
        if char == 'ッ':
            if double:
                return None
            double = True
            continue
        if pair in readings:
            sound = readings[pair]
            index += 1
        elif char in readings:
            sound = readings[char]
        elif char == 'ン':
            sound = 'n'
        elif char == 'ー' and result and result[-1][-1] in 'aiueo':
            sound = result[-1][-1]
        elif char.isascii() and (char.isalpha() or char == "'"):
            sound = char
        elif char.isalnum() or char == 'ー' or double:
            return None
        else:
            continue
        if double:
            if sound[0] in "aiueon'":
                return None
            sound = ('t' if sound.startswith('ch') else sound[0]) + sound
            double = False
        result.append(sound)
    return None if double else ''.join(result)


def japanese_line(text, reader, readings):
    text = unicodedata.normalize('NFKC', text).lower().translate(LATIN_PUNCTUATION).translate(ROMAJI_LONG_VOWELS)
    text = re.sub(r'([\u3400-\u9fff])々', r'\1\1', text)
    words = []
    for token in reader.tokenize(text):
        surface = token.surface()
        term = surface if surface.isascii() else token.reading_form()
        if token.part_of_speech()[0] == '助詞':
            term = {'は': 'ワ', 'へ': 'エ', 'を': 'オ'}.get(surface, term)
        roman = kana_to_romaji(term, readings)
        if roman is None:
            return None
        if roman:
            words.append(roman)
    romanized = ' '.join(words)
    return romanized if any(char.isalpha() for char in romanized) else None


def encode_lines(lines, vocab, language, readings):
    if language == 'ja':
        from sudachipy import dictionary, tokenizer

        reader = dictionary.Dictionary(dict='small').tokenizer(mode=tokenizer.Tokenizer.SplitMode.C)
        romanized = [japanese_line(line, reader, readings) for line in lines]
        return [normalize(line, vocab) if line else None for line in romanized]
    return [normalize(line, vocab, language) for line in lines]


def emissions(samples, model_dir, language=''):
    import numpy as np
    import torch

    torch.set_num_threads(min(8, max(1, (os.cpu_count() or 2) // 2)))
    torch.set_num_interop_threads(1)
    if language == 'ja':
        from transformers import Wav2Vec2FeatureExtractor, Wav2Vec2ForCTC

        config = json.loads((model_dir / 'processor_config.json').read_text(encoding='utf-8'))
        processor = Wav2Vec2FeatureExtractor(**config['feature_extractor'])
        model = Wav2Vec2ForCTC.from_pretrained(model_dir, local_files_only=True, use_safetensors=True).eval()
        heads, padding = None, RATE // 2
    else:
        from safetensors.torch import load_file
        from transformers import Wav2Vec2BertForCTC, Wav2Vec2BertProcessor

        processor = Wav2Vec2BertProcessor.from_pretrained(model_dir, local_files_only=True)
        model = Wav2Vec2BertForCTC.from_pretrained(model_dir, local_files_only=True, use_safetensors=True).eval()
        heads, padding = load_file(str(model_dir / 'aux_heads.safetensors')), 0
    logits, voiced = [], []
    with torch.inference_mode():
        for start in range(0, len(samples), 24 * RATE):
            lo, hi = max(0, start - 2 * RATE), min(len(samples), start + 26 * RATE)
            window = np.pad(samples[lo:hi], padding) if padding else samples[lo:hi]
            encoded = processor(window, sampling_rate=RATE, return_tensors='pt')
            out = model(**encoded, output_hidden_states=heads is not None)
            stop = start + 24 * RATE
            if padding:
                stop = min(stop, len(samples))
            selection = slice((start - lo + padding) // FRAME, (stop - lo + padding) // FRAME)
            logits.append(out.logits[0, selection].log_softmax(-1).numpy())
            if heads is not None:
                hidden = out.hidden_states[-1][0, selection]
                activity = torch.nn.functional.linear(hidden, heads['vad.weight'], heads['vad.bias']).sigmoid()
                voiced.append(activity.reshape(-1).numpy())
    logp = np.concatenate(logits)
    if heads is not None:
        return logp, np.concatenate(voiced), model.config.pad_token_id
    # The Japanese model has no vocal activity head. Only a stable quiet
    # stretch in the recording is evidence of a gap or a trailing boundary.
    frames = samples[:len(logp) * FRAME].reshape(-1, FRAME)
    activity = np.sqrt(np.mean(frames ** 2, axis=1)) >= .003
    return logp, activity, model.config.pad_token_id


def quiet_start(activity, start, stop, required):
    count = 0
    for frame in range(start, stop):
        count = count + 1 if activity[frame] < .42 else 0
        if count >= required:
            return frame + 1 - count
    return None


def align(logp, voiced, filler, blank, encoded_lines, language):
    import numpy as np

    labels, text_rows = [filler], []
    for chars in encoded_lines:
        if chars:
            text_rows.append((len(labels), len(labels) + len(chars) - 1))
            labels.extend(chars)
            labels.append(filler)
        else:
            text_rows.append(None)
    if not any(r is not None for r in text_rows):
        return [None] * len(encoded_lines)
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
        return [None] * len(encoded_lines)

    start = np.full(states, -1, dtype=np.int32)
    end = start.copy()
    peak = np.full(states, -np.inf, dtype=np.float32)
    for frame in range(len(logp) - 1, -1, -1):
        if end[state] < 0:
            end[state] = frame + 1
        start[state] = frame
        peak[state] = max(peak[state], logp[frame, target[state]])
        state -= int(path[frame, state])

    spans = [None] * len(encoded_lines)
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
        if language == 'ja' and quiet_start(voiced, onset, finish, 125) is not None:
            continue
        # A low-activity run can separate the lines; a CTC letter spike alone is not a line end.
        next_onset = len(voiced)
        for later in text_rows[ix + 1:]:
            if later is not None:
                next_onset = int(start[2 * later[0] + 1])
                break
        if next_onset < finish:
            continue
        # With a mixed recording, level alone cannot identify a far-away vocal cutoff.
        search_frames = 50 if language == 'ja' else 250
        boundary = quiet_start(voiced, finish, min(next_onset, finish + search_frames), 12)
        if boundary is None:
            if next_onset - finish > (20 if language == 'ja' else 35) or next_onset == len(voiced):
                # Preserve the detected onset even when a trailing note or backing vocal obscures the end.
                spans[ix] = {'StartMs': onset * 20, 'EndMs': None}
                continue
            boundary = next_onset
        if boundary > onset:
            spans[ix] = {'StartMs': onset * 20, 'EndMs': boundary * 20}
    return spans


def main():
    source = sys.stdin.buffer
    raw = source.read(4)
    size = struct.unpack('<I', raw)[0]
    if not 0 < size <= 512 * 1024:
        raise ValueError('header limit')
    msg = json.loads(source.read(size))
    count, lines = msg['Count'], msg['Lines']
    language = sys.argv[2] if len(sys.argv) > 2 else ''
    model_dir = Path(sys.argv[1])
    if not (language in ('', 'fr', 'de', 'ja') and 0 < len(lines) <= 1000 and all(isinstance(line, str) and line for line in lines)):
        raise ValueError('input bounds')
    vocab = json.loads((model_dir / 'vocab.json').read_text(encoding='utf-8'))
    encoded_lines = encode_lines(lines, vocab, language, msg.get('KanaReadings'))
    if count == 0:
        if source.read(1):
            raise ValueError('unexpected audio')
        return {'Coverage': [line is not None for line in encoded_lines]}
    if not RATE <= count <= SECONDS * RATE:
        raise ValueError('PCM bounds')
    import numpy as np

    pcm = source.read(count * 2)
    if len(pcm) != count * 2 or source.read(1):
        raise ValueError('PCM length')
    if any(line is None for line in encoded_lines):
        return {'Spans': [None] * len(lines)}
    samples = np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768
    logp, voiced, blank = emissions(samples, model_dir, language)
    return {'Spans': align(logp, voiced, vocab['|'], blank, encoded_lines, language)}


if __name__ == '__main__':
    try:
        print(json.dumps(main(), ensure_ascii=True, allow_nan=False))
    except OverflowError:
        print('{"Error":"size"}')
    except Exception as error:
        # Never include lyric text, the user's paths, or raw model input in diagnostics.
        print('{"Error":"runtime"}')
        if os.environ.get('LUMA_TIMING_TEST_ERRORS') == '1':
            print(type(error).__name__, file=sys.stderr)
        sys.exit(1)
