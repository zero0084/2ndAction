import struct, sys
import numpy as np


def load(p):
    b = open(p, 'rb').read()
    assert b[:4] == b'RIFF' and b[8:12] == b'WAVE'
    i = 12
    fmt = None
    data = None
    chunks = []
    while i + 8 <= len(b):
        cid = b[i:i + 4]; sz = struct.unpack('<I', b[i + 4:i + 8])[0]
        chunks.append((cid.decode('latin1'), sz))
        body = b[i + 8:i + 8 + sz]
        if cid == b'fmt ':
            tag, ch, rate, _, _, bits = struct.unpack('<HHIIHH', body[:16])
            if tag == 0xFFFE:
                tag = struct.unpack('<H', body[24:26])[0]
            fmt = (tag, ch, rate, bits)
        elif cid == b'data':
            data = body
        i += 8 + sz + (sz & 1)
    tag, ch, rate, bits = fmt
    if tag == 3:
        a = np.frombuffer(data, dtype='<f4' if bits == 32 else '<f8')
    else:
        a = np.frombuffer(data, dtype='<i%d' % (bits // 8)).astype(np.float64) / (2 ** (bits - 1))
    return a.reshape(-1, ch).astype(np.float64), fmt, chunks


for a_p, b_p in [(sys.argv[1], sys.argv[2])]:
    a, fa, ca = load(a_p); b, fb, cb = load(b_p)
    print('fmt', fa, fb)
    print('chunks', ca, cb)
    n = min(len(a), len(b))
    a, b = a[:n], b[:n]
    diff = np.abs(a - b)
    print('samples', n, 'identical', np.array_equal(a, b), 'maxdiff', diff.max(), 'rms diff', np.sqrt((diff ** 2).mean()), 'rms a', np.sqrt((a ** 2).mean()))
    print('corr', np.corrcoef(a[:, 0], b[:, 0])[0, 1])
    # best lag in first 10s
    m = min(n, 44100 * 10)
    x = a[:m, 0]; y = b[:m, 0]
    best = max(range(-200, 201), key=lambda L: np.dot(x[max(0, L):m + min(0, L)], y[max(0, -L):m - max(0, L)]))
    print('best lag', best)
