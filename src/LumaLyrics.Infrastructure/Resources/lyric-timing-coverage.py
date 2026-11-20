"""Check encoding with the installed worker, without PCM or an acoustic model."""
import json
from pathlib import Path
import runpy
import struct
import sys

worker = runpy.run_path(sys.argv[1])
model = Path(sys.argv[2])
source = sys.stdin.buffer
size, = struct.unpack('<I', source.read(4))
if not 0 < size <= 512 * 1024:
    raise ValueError('header limit')
lines = json.loads(source.read(size))['Lines']
if not 0 < len(lines) <= 1000 or not all(isinstance(line, str) and line for line in lines) or source.read(1):
    raise ValueError('coverage input')
if 'encode_lines' in worker:
    encoded = worker['encode_lines'](lines, model)
else:
    vocab = json.loads((model / 'vocab.json').read_text(encoding='utf-8'))
    encoded = [worker['normalize'](line, vocab) for line in lines]
print(json.dumps({'Coverage': [bool(tokens) for tokens in encoded]}))
