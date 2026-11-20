"""CPU inference and line timing from the pinned SOFA Mandarin singing model."""
import json
from pathlib import Path
import struct
import sys
import unicodedata

INPUT_RATE = 16000
MAX_SECONDS = 900
UPSTREAM = 'SOFA-1.0.3'
WEIGHTS = 'pretrained_mandarin_singing'


def configuration(model):
    import yaml

    path = model / WEIGHTS / 'hparams.yaml'
    config = yaml.safe_load(path.read_text(encoding='utf-8'))
    return config, yaml.safe_load(config['vocab_text'])


def encode_lines(lines, model):
    from pypinyin import Style, lazy_pinyin

    _, vocab = configuration(model)
    path = model / UPSTREAM / 'dictionary' / 'opencpop-extension.txt'
    lexicon = {}
    for row in path.read_text(encoding='utf-8').splitlines():
        if '\t' in row:
            key, phones = row.split('\t', 1)
            lexicon[key.strip()] = [vocab.get(phone) for phone in phones.split()]
    result = []
    for original in lines:
        text = unicodedata.normalize('NFKC', original)
        if any(c.isalnum() and not unicodedata.name(c, '').startswith(
                ('CJK UNIFIED IDEOGRAPH', 'CJK COMPATIBILITY IDEOGRAPH')) for c in text):
            result.append(None)
            continue
        # Retain punctuation as word context for the pronunciation dictionary.
        syllables = lazy_pinyin(text, style=Style.NORMAL, errors=list)
        encoded = []
        for syllable in syllables:
            if not any(c.isalnum() for c in syllable):
                continue
            phones = lexicon.get(syllable)
            if not phones or any(phone is None or phone == 0 for phone in phones):
                encoded = None
                break
            encoded.extend(phones)
            encoded.append(0)  # Optional silence between syllables.
        result.append(encoded or None)
    return result


def mel_filters(config):
    import numpy as np

    # SOFA uses the HTK mel scale with Slaney area normalization in librosa.
    low, high = [2595 * np.log10(1 + config[k] / 700) for k in ('fmin', 'fmax')]
    bands = 700 * (10 ** (np.linspace(low, high, config['n_mels'] + 2) / 2595) - 1)
    freqs = np.fft.rfftfreq(config['n_fft'], d=1 / config['sample_rate'])
    left = (freqs[None, :] - bands[:-2, None]) / (bands[1:-1] - bands[:-2])[:, None]
    right = (bands[2:, None] - freqs[None, :]) / (bands[2:] - bands[1:-1])[:, None]
    peaks = np.maximum(0, np.minimum(left, right))
    return (2 * peaks / (bands[2:] - bands[:-2])[:, None]).astype(np.float32)


def emissions(samples, model):
    import os
    import numpy as np
    import torch
    from torch import nn

    sys.path.insert(0, str(model / UPSTREAM))
    from modules.layer.backbone.unet import UNetBackbone
    from modules.layer.block.resnet_block import ResidualBasicBlock
    from modules.layer.scaling.stride_conv import DownSampling, UpSampling

    config, vocab = configuration(model)
    mel, architecture = config['melspec_config'], config['model_config']
    rate, hop, scale = mel['sample_rate'], mel['hop_length'], mel['scale_factor']
    torch.set_num_threads(min(8, max(1, (os.cpu_count() or 2) // 2)))
    torch.set_num_interop_threads(1)

    class AcousticModel(nn.Module):
        def __init__(self):
            super().__init__()
            width = architecture['hidden_dims']
            options = {key: value for key, value in architecture.items() if key != 'hidden_dims'}
            self.backbone = UNetBackbone(mel['n_mels'], width, width, ResidualBasicBlock,
                                         DownSampling, UpSampling, **options)
            self.head = nn.Linear(width, vocab['<vocab_size>'] + 2)

        def forward(self, features):
            return self.head(self.backbone(features))

    engine = AcousticModel().eval()
    checkpoint = torch.load(model / WEIGHTS / 'v1.0.0_mandarin_singing.ckpt',
                            map_location='cpu', weights_only=True)
    tensors = checkpoint['state_dict']
    engine.load_state_dict({name: tensors[name] for name in engine.state_dict()})
    del checkpoint, tensors

    filters = torch.from_numpy(mel_filters(mel))
    window = torch.hann_window(mel['win_length'])
    samples_count = len(samples) * rate // INPUT_RATE
    total_hops = (samples_count + hop - 1) // hop
    chunk_hops, context = 24 * rate // hop, 3 * rate // hop
    phones, edges = [], []
    with torch.inference_mode():
        for start in range(0, total_hops, chunk_hops):
            stop = min(total_hops, start + chunk_hops)
            lo, hi = max(0, start - context), min(total_hops, stop + context)
            samples_at_hops = np.arange(lo * hop, min(samples_count, hi * hop), dtype=np.float64)
            input_positions = samples_at_hops * (INPUT_RATE / rate)
            begin = int(input_positions[0])
            end = min(len(samples), int(input_positions[-1]) + 2)
            waveform = np.interp(input_positions, np.arange(begin, end), samples[begin:end]).astype(np.float32)
            spectrum = torch.stft(torch.from_numpy(waveform), mel['n_fft'], hop_length=hop,
                                  win_length=mel['win_length'], window=window, pad_mode='constant', return_complex=True)
            features = (filters @ spectrum.abs()).clamp_min(mel['clamp']).log()
            features = (features - features.mean()) / features.std().clamp_min(1e-6)
            features = features.transpose(0, 1).repeat_interleave(scale, dim=0).unsqueeze(0)
            output = engine(features)[0, (start - lo) * scale:(stop - lo) * scale]
            # Average over one mel hop. This keeps whole-song decoding bounded.
            weights = output[:, 2:].reshape(-1, scale, vocab['<vocab_size>']).log_softmax(-1)
            phones.append(weights.mean(1).numpy())
            edges.append(output[:, 0].reshape(-1, scale).sigmoid().amax(1).numpy())
    return np.concatenate(phones), np.concatenate(edges), 1000 * hop / rate


def align(logp, edge, step_ms, rows):
    import numpy as np

    labels, ranges = [0], []
    for row in rows:
        if row is None:
            ranges.append(None)
            continue
        ranges.append((len(labels), len(labels) + len(row) - 1))
        labels.extend(row)
    if len(labels) == 1 or len(logp) * len(labels) > 96_000_000:
        raise OverflowError('alignment matrix bound')
    labels = np.array(labels)
    # A phoneme may occupy multiple frames, advance to the next phoneme, or
    # skip the optional silence first. Start and end silence are optional.
    optional = labels[1:-1] == 0
    # EOS can dominate at quiet boundaries. The fifth-best phone sets a
    # background floor so an instrumental passage can stay outside the text.
    emissions = logp.copy()
    emissions[:, 0] = np.maximum(np.logaddexp(logp[:, 0], logp[:, 1]),
                                 np.partition(logp[:, 2:], -5, axis=1)[:, -5])
    edge = np.clip(edge, .001, .999)
    advance_bias, stay_bias = np.log(edge), np.log1p(-edge)
    steps = np.zeros((len(logp), len(labels)), dtype=np.uint8)
    score = np.full(len(labels), -np.inf, dtype=np.float32)
    score[:2] = emissions[0, labels[:2]]
    move, skip = np.full_like(score, -np.inf), np.full_like(score, -np.inf)
    for frame in range(1, len(logp)):
        move[1:] = score[:-1] + advance_bias[frame]
        skip[2:] = np.where(optional, score[:-2] + advance_bias[frame], -np.inf)
        score += stay_bias[frame]
        steps[frame] = move > score
        np.maximum(score, move, out=score)
        steps[frame, skip > score] = 2
        np.maximum(score, skip, out=score)
        score += emissions[frame, labels]

    state = len(labels) - 1 - int(score[-2] > score[-1])
    if not np.isfinite(score[state]):
        return [None] * len(rows)
    starts = np.full(len(labels), -1, dtype=np.int32)
    ends = starts.copy()
    for frame in range(len(logp) - 1, -1, -1):
        if ends[state] < 0:
            ends[state] = frame + 1
        starts[state] = frame
        state -= steps[frame, state]

    result = []
    for bounds in ranges:
        if bounds is None:
            result.append(None)
            continue
        first, stop = bounds
        states = [state for state in range(first, stop) if labels[state] != 0]
        onset, finish = starts[states[0]], ends[states[-1]]
        peaks = np.array([np.exp(logp[starts[state]:ends[state], labels[state]].max()) for state in states])
        longest_inner_gap = max((starts[right] - ends[left]) * step_ms
                                for left, right in zip(states, states[1:])) if len(states) > 1 else 0
        if not (0 <= onset < finish and (finish - onset) * step_ms <= 30000
                and longest_inner_gap <= 2500 and peaks[0] >= .045
                and np.median(peaks) >= .085 and np.mean(peaks >= .065) >= .60):
            result.append(None)
            continue
        result.append({'StartMs': round(onset * step_ms),
                       'EndMs': round(finish * step_ms) if peaks[-1] >= .045 else None})
    return result


def main():
    import numpy as np

    model = Path(sys.argv[1])
    source = sys.stdin.buffer
    size, = struct.unpack('<I', source.read(4))
    if not 0 < size <= 512 * 1024:
        raise ValueError('header limit')
    request = json.loads(source.read(size))
    lines, count = request['Lines'], request['Count']
    if not INPUT_RATE <= count <= MAX_SECONDS * INPUT_RATE or not 0 < len(lines) <= 1000:
        raise ValueError('input bounds')
    rows = encode_lines(lines, model)
    pcm = source.read(count * 2)
    if len(pcm) != count * 2 or source.read(1):
        raise ValueError('PCM length')
    if any(row is None for row in rows):
        return [None] * len(lines)
    samples = np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768
    return align(*emissions(samples, model), rows)


if __name__ == '__main__':
    try:
        print(json.dumps({'Spans': main()}, ensure_ascii=True, allow_nan=False))
    except OverflowError:
        print('{"Error":"size"}')
    except Exception as error:
        print('{"Error":"runtime"}')
        if __import__('os').environ.get('LUMA_TIMING_TEST_ERRORS') == '1':
            print(type(error).__name__, file=sys.stderr)
        sys.exit(1)
