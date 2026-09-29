# OneMoreMile 仮音源ジェネレータ(BGM/ジングル/環境音/SE)。
# 目的は「ゲーム全体で鳴る状態」を作ること。後でエステル(マスター)と差し替える前提の仮素材。
# 出力: Assets/Audio/Placeholder/{Bgm,Jingle,Ambience,SE}/*.wav (16bit PCM)
import os, math, wave
import numpy as np

SR = 44100
OUT = r'C:/GameProject/2ndAction/Unity/2ndAction/Assets/Audio/Placeholder'
rng = np.random.default_rng(20260929)

def midi(n): return 440.0 * 2 ** ((n - 69) / 12.0)

def write(path, x, stereo=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    x = np.asarray(x, dtype=np.float64)
    peak = np.max(np.abs(x)) + 1e-9
    x = x / peak * 0.89
    data = (np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(2 if stereo else 1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(data.tobytes())

def onepole_lp(x, cutoff):
    a = math.exp(-2 * math.pi * cutoff / SR)
    y = np.empty_like(x); s = 0.0
    for i in range(len(x)):
        s = (1 - a) * x[i] + a * s; y[i] = s
    return y

def lp_fast(x, cutoff):
    # 2回のone-poleを畳み込みで近似(速い): 指数窓の畳み込み
    n = int(SR / cutoff * 3) + 1
    k = np.exp(-np.arange(n) * 2 * math.pi * cutoff / SR); k /= k.sum()
    return np.convolve(x, k, mode='same')

def hp_fast(x, cutoff): return x - lp_fast(x, cutoff)

def env_adsr(n, a=0.005, d=0.1, s=0.6, r=0.1):
    t = np.arange(n) / SR; dur = n / SR
    e = np.ones(n) * s
    e[t < a] = t[t < a] / max(a, 1e-4)
    m = (t >= a) & (t < a + d); e[m] = 1 - (1 - s) * (t[m] - a) / max(d, 1e-4)
    rel = t > dur - r; e[rel] *= np.clip((dur - t[rel]) / max(r, 1e-4), 0, 1)
    return e

def osc(kind, f, n, phase=0.0):
    t = np.arange(n) / SR
    ph = (f * t + phase) % 1.0
    if kind == 'sin': return np.sin(2 * math.pi * ph)
    if kind == 'tri': return 2 * np.abs(2 * ph - 1) - 1
    if kind == 'saw': return 2 * ph - 1
    if kind == 'sq': return np.where(ph < 0.5, 1.0, -1.0)
    raise ValueError(kind)

def add_wrap(buf, sig, start):
    # ループ曲: 末尾からはみ出した分は先頭へ回す(つなぎ目が切れない)
    n = len(buf); start %= n
    end = start + len(sig)
    if end <= n: buf[start:end] += sig
    else:
        k = n - start; buf[start:] += sig[:k]
        rest = sig[k:]
        while len(rest) > 0:
            m = min(n, len(rest)); buf[:m] += rest[:m]; rest = rest[m:]

# ---------------- 楽器 ----------------
def pad_note(f, dur, bright=900, det=0.004):
    n = int(dur * SR)
    x = sum(osc('saw', f * (1 + d), n, rng.random()) for d in (-det, 0, det)) / 3
    x = lp_fast(x, bright)
    return x * env_adsr(n, a=min(0.4, dur * 0.3), d=0.2, s=0.8, r=min(0.5, dur * 0.3))

def pluck(f, dur, kind='tri', decay=0.35, bright=4000):
    n = int(dur * SR)
    x = osc(kind, f, n) * np.exp(-np.arange(n) / SR / decay)
    if bright < 6000: x = lp_fast(x, bright)
    x *= env_adsr(n, a=0.003, d=0.05, s=1.0, r=0.03)
    return x

def bell(f, dur):
    n = int(dur * SR); t = np.arange(n) / SR
    x = (np.sin(2 * math.pi * f * t) + 0.5 * np.sin(2 * math.pi * f * 2.76 * t) + 0.25 * np.sin(2 * math.pi * f * 5.4 * t)) * np.exp(-t / (dur * 0.35))
    return x

def bass_note(f, dur, kind='tri'):
    n = int(dur * SR)
    x = osc(kind, f, n) * 0.8 + osc('sin', f, n) * 0.5
    x = lp_fast(x, 600)
    return x * env_adsr(n, a=0.005, d=0.08, s=0.7, r=0.04)

def kick(level=1.0):
    n = int(0.28 * SR); t = np.arange(n) / SR
    f = 50 + 110 * np.exp(-t * 28)
    return np.sin(2 * math.pi * np.cumsum(f) / SR) * np.exp(-t * 11) * level

def snare(level=1.0):
    n = int(0.2 * SR); t = np.arange(n) / SR
    noise = hp_fast(rng.standard_normal(n), 1500) * np.exp(-t * 22)
    tone = np.sin(2 * math.pi * 190 * t) * np.exp(-t * 30)
    return (noise * 0.7 + tone * 0.5) * level

def hat(level=1.0, open_=False):
    n = int((0.18 if open_ else 0.05) * SR); t = np.arange(n) / SR
    return hp_fast(rng.standard_normal(n), 7000) * np.exp(-t * (18 if open_ else 70)) * level

def tom(f=110, level=1.0):
    n = int(0.3 * SR); t = np.arange(n) / SR
    ff = f * (1 + 0.5 * np.exp(-t * 20))
    return np.sin(2 * math.pi * np.cumsum(ff) / SR) * np.exp(-t * 9) * level

# ---------------- 曲 ----------------
SCALES = {
    'major': [0, 2, 4, 5, 7, 9, 11], 'minor': [0, 2, 3, 5, 7, 8, 10], 'dorian': [0, 2, 3, 5, 7, 9, 10],
    'lydian': [0, 2, 4, 6, 7, 9, 11], 'phrygian': [0, 1, 3, 5, 7, 8, 10], 'harmonic': [0, 2, 3, 5, 7, 8, 11],
}

def chord_tones(root, scale, degree):
    sc = SCALES[scale]
    return [root + sc[(degree + k) % 7] + 12 * ((degree + k) // 7) for k in (0, 2, 4)]

def song(name, bpm, root, scale, prog, bars=16, drums='mid', arp='8th', lead=True, pad_bright=900,
         bass_pat='drive', lead_kind='tri', bells=False, swing=0.0, arp_oct=12, stereo=True):
    beat = 60.0 / bpm; bar = beat * 4
    n = int(round(bars * bar * SR))
    L = np.zeros(n); R = np.zeros(n)
    def put(sig, t, pan=0.0, vol=1.0):
        s = int(round(t * SR))
        add_wrap(L, sig * vol * (1 - max(0, pan)), s); add_wrap(R, sig * vol * (1 + min(0, pan)), s)
    mel_rng = np.random.default_rng(abs(hash(name)) % (2 ** 32))
    for b in range(bars):
        deg = prog[b % len(prog)]
        tones = chord_tones(root, scale, deg)
        t0 = b * bar
        # パッド(和音)
        for i, m in enumerate(tones):
            put(pad_note(midi(m + 12), bar * 1.02, bright=pad_bright), t0, pan=(-0.3, 0, 0.3)[i], vol=0.10)
        # ベース
        bf = midi(tones[0] - 12)
        if bass_pat == 'drive':
            for k in range(8): put(bass_note(bf if k % 4 != 3 else midi(tones[2] - 12), beat * 0.45), t0 + k * beat / 2, vol=0.22)
        elif bass_pat == 'half':
            for k in range(2): put(bass_note(bf, beat * 1.9), t0 + k * beat * 2, vol=0.24)
        elif bass_pat == 'walk':
            for k in range(4): put(bass_note(midi(tones[k % 3] - 12), beat * 0.9), t0 + k * beat, vol=0.23)
        elif bass_pat == 'pulse':
            for k in range(16): put(bass_note(bf, beat * 0.2, 'sq'), t0 + k * beat / 4, vol=0.12)
        # アルペジオ
        if arp:
            step = {'8th': 2, '16th': 4, '4th': 1}[arp]
            seq = tones + [tones[1] + 12, tones[2]]
            for k in range(4 * step):
                m = seq[k % len(seq)] + arp_oct
                tt = t0 + k * beat / step + (swing * beat / step if k % 2 else 0)
                put(pluck(midi(m), beat / step * 1.6, 'tri', decay=0.18, bright=3500), tt, pan=0.25 * math.sin(k), vol=0.08)
        # メロディ(2小節ごとに短い動機)
        if lead and b % 2 == 0:
            sc = SCALES[scale]
            pos = 0.0; deg_m = 4
            while pos < 8:
                dur = mel_rng.choice([0.5, 0.5, 1, 1, 1.5, 2])
                deg_m = int(np.clip(deg_m + mel_rng.integers(-2, 3), 0, 9))
                m = root + 12 + sc[deg_m % 7] + 12 * (deg_m // 7)
                if mel_rng.random() > 0.15:
                    put(mix(pluck(midi(m), dur * beat * 0.95, lead_kind, decay=0.6, bright=2500), 0.3 * pluck(midi(m + 12), dur * beat * 0.9, 'sin', decay=0.4, bright=6000)), t0 + pos * beat, pan=-0.15, vol=0.13)
                pos += dur
        if bells and b % 4 == 0:
            for i, m in enumerate(tones):
                put(bell(midi(m + 24), bar * 2), t0 + i * beat * 0.5, pan=0.3, vol=0.07)
        # ドラム
        if drums:
            for k in range(4):
                tt = t0 + k * beat
                if drums == 'soft':
                    if k in (0, 2): put(kick(0.5), tt, vol=0.35)
                    put(hat(0.4), tt + beat / 2, pan=0.3, vol=0.25)
                elif drums == 'mid':
                    if k in (0, 2): put(kick(), tt, vol=0.5)
                    if k in (1, 3): put(snare(), tt, vol=0.3)
                    for h in range(2): put(hat(0.5), tt + h * beat / 2, pan=0.3, vol=0.22)
                elif drums == 'drive':
                    put(kick(), tt, vol=0.5)
                    if k in (1, 3): put(snare(1.1), tt, vol=0.33)
                    for h in range(4): put(hat(0.45), tt + h * beat / 4, pan=0.3, vol=0.2)
                    if k == 3 and b % 4 == 3: put(tom(120), tt + beat / 2, vol=0.3); put(tom(90), tt + beat * 0.75, vol=0.3)
                elif drums == 'tribal':
                    put(tom(80, 1.0), tt, vol=0.45)
                    if k % 2 == 1: put(tom(130, 0.8), tt + beat / 2, vol=0.3)
                    put(hat(0.3), tt + beat / 2, pan=0.3, vol=0.15)
                elif drums == 'heavy':
                    put(kick(1.2), tt, vol=0.55); put(kick(0.8), tt + beat / 2, vol=0.3)
                    if k in (1, 3): put(snare(1.2), tt, vol=0.38)
                    for h in range(4): put(hat(0.4), tt + h * beat / 4, pan=0.3, vol=0.18)
    stereo_out = np.stack([np.tanh(L * 1.4), np.tanh(R * 1.4)], axis=1)
    write(os.path.join(OUT, "Bgm", name + ".wav"), stereo_out.reshape(-1), stereo=True)
    print('bgm', name, round(n / SR, 1), 's')

def jingle(name, notes, bpm, final_chord, minor=False):
    beat = 60 / bpm
    total = len(notes) * beat / 2 + 3.0
    n = int(total * SR); L = np.zeros(n)
    for i, m in enumerate(notes):
        s = int(i * beat / 2 * SR); sig = pluck(midi(m), beat, 'tri', decay=0.3, bright=3500)
        L[s:s + len(sig)] += sig * 0.5
    s = int(len(notes) * beat / 2 * SR)
    for m in final_chord:
        sig = pad_note(midi(m), 2.6, bright=1500 if not minor else 700) + 0.6 * bell(midi(m + 12), 2.6)
        L[s:s + len(sig)] += sig * 0.3
    if not minor:
        k = kick(); L[s:s + len(k)] += k * 0.4
    L *= np.minimum(1, np.linspace(3, 0, n) * 1.5)  # 最後はフェード
    write(os.path.join(OUT, 'Jingle', name + '.wav'), np.tanh(L * 1.3))
    print('jingle', name, round(total, 1), 's')

# ---------------- 環境音 ----------------
def loop_noise(name, secs, lp, lfo=0.12, depth=0.6, rumble=0.0, hiss=0.0):
    n = int(secs * SR); extra = int(2 * SR)
    x = rng.standard_normal(n + extra)
    x = lp_fast(lp_fast(x, lp), lp * 1.5)
    t = np.arange(n + extra) / SR
    amp = 1 - depth / 2 + depth / 2 * np.sin(2 * math.pi * lfo * t + 1.3) * np.sin(2 * math.pi * lfo * 0.37 * t)
    x *= amp
    if rumble: x += lp_fast(rng.standard_normal(n + extra), 60) * rumble * 8
    if hiss: x += hp_fast(rng.standard_normal(n + extra), 5000) * hiss
    # ループ: 末尾2秒を先頭へクロスフェード
    head = x[:extra]; tail = x[n:n + extra]; w = np.linspace(0, 1, extra)
    y = x[:n].copy(); y[:extra] = head * w + tail * (1 - w)
    write(os.path.join(OUT, 'Ambience', name + '.wav'), y)
    print('amb', name)

def bird(variant):
    n = int(0.9 * SR); y = np.zeros(n)
    base = [2600, 3200, 2200][variant % 3]
    pos = 0
    for k in range([3, 5, 2][variant % 3]):
        d = int(0.08 * SR); t = np.arange(d) / SR
        f = base * (1 + 0.35 * np.sin(math.pi * t / 0.08)) * (1 + 0.1 * k)
        s = np.sin(2 * math.pi * np.cumsum(f) / SR) * np.sin(math.pi * t / 0.08)
        y[pos:pos + d] += s; pos += int(0.11 * SR)
    return y

def drip(variant):
    n = int(0.5 * SR); t = np.arange(n) / SR
    f = [1400, 1800][variant % 2] * (1 + 0.8 * np.exp(-t * 60))
    return np.sin(2 * math.pi * np.cumsum(f) / SR) * np.exp(-t * 14) + 0.3 * lp_fast(rng.standard_normal(n), 3000) * np.exp(-t * 40)

def rock():
    n = int(1.4 * SR); t = np.arange(n) / SR
    return lp_fast(rng.standard_normal(n), 180) * np.exp(-t * 3) * 6 + lp_fast(rng.standard_normal(n), 900) * np.exp(-t * 12) * 0.6

def rustle():
    n = int(0.8 * SR); t = np.arange(n) / SR
    x = hp_fast(lp_fast(rng.standard_normal(n), 4000), 800) * np.sin(math.pi * t / 0.8) ** 2
    return x * (0.6 + 0.4 * np.sin(2 * math.pi * 13 * t))

def whistle():
    n = int(2.2 * SR); t = np.arange(n) / SR
    f = 900 + 120 * np.sin(2 * math.pi * 0.7 * t) + 40 * np.sin(2 * math.pi * 5 * t)
    return np.sin(2 * math.pi * np.cumsum(f) / SR) * np.sin(math.pi * t / 2.2) ** 2 * 0.5 + lp_fast(rng.standard_normal(n), 1200) * np.sin(math.pi * t / 2.2) ** 2 * 0.8

def amb_shot(name, x): write(os.path.join(OUT, 'Ambience', name + '.wav'), x); print('amb shot', name)

# ---------------- SE ----------------
def se(name, x): write(os.path.join(OUT, 'SE', name + '.wav'), x); print('se', name)

def whoosh(dur=0.22, f0=800, f1=3000, level=1.0, low=False):
    n = int(dur * SR); t = np.arange(n) / SR
    x = rng.standard_normal(n)
    cut = np.linspace(f0, f1, n)
    y = np.zeros(n); s = 0.0
    for i in range(n):  # 周波数が動くローパス
        a = math.exp(-2 * math.pi * cut[i] / SR); s = (1 - a) * x[i] + a * s; y[i] = s
    y = hp_fast(y, 300 if not low else 80)
    return y * np.sin(math.pi * np.clip(t / dur, 0, 1)) ** 1.5 * level

def thump(f=70, dur=0.25, level=1.0):
    n = int(dur * SR); t = np.arange(n) / SR
    ff = f * (1 + 1.5 * np.exp(-t * 35))
    return np.sin(2 * math.pi * np.cumsum(ff) / SR) * np.exp(-t * 14) * level

def crack(dur=0.12, level=1.0):
    n = int(dur * SR); t = np.arange(n) / SR
    return hp_fast(rng.standard_normal(n), 1800) * np.exp(-t * 45) * level

def tone(freqs, note=0.07, decay=0.2, kind='sin', vol=1.0):
    n = int((note * len(freqs) + decay * 3) * SR); y = np.zeros(n)
    for i, f in enumerate(freqs):
        s = int(i * note * SR); k = int(decay * 3 * SR)
        t = np.arange(k) / SR
        y[s:s + k] += osc(kind, f, k) * np.exp(-t / decay) * np.clip(t * 300, 0, 1) * vol
    return y

def mix(*parts):
    n = max(len(p) for p in parts); y = np.zeros(n)
    for p in parts: y[:len(p)] += p
    return y

def pad_to(x, secs): return np.concatenate([x, np.zeros(max(0, int(secs * SR) - len(x)))])

def gen_all():
    # ===== BGM(ループ) =====
    # 天空回廊: 明るく浮遊感 → 速く力強く → 壮大で緊張
    song('bgm_sky_early', 118, 62, 'lydian', [0, 4, 5, 3], drums='soft', arp='8th', pad_bright=1400, bass_pat='half', lead_kind='tri')
    song('bgm_sky_middle', 132, 64, 'major', [0, 5, 3, 4], drums='mid', arp='16th', pad_bright=1600, bass_pat='drive')
    song('bgm_sky_late', 146, 62, 'dorian', [0, 6, 5, 4], drums='drive', arp='16th', pad_bright=1800, bass_pat='drive', bells=True)
    # 荒野街道: 序盤は既存のGameplayBgm。中盤=冒険の疾走 / 終盤=険しい
    song('bgm_wasteland_middle', 136, 57, 'dorian', [0, 3, 6, 4], drums='mid', arp='8th', pad_bright=1100, bass_pat='walk', swing=0.12)
    song('bgm_wasteland_late', 150, 57, 'minor', [0, 5, 3, 4], drums='drive', arp='16th', pad_bright=1300, bass_pat='drive')
    # 自然洞窟: 静かで湿った → 緊張 → 深部の不穏
    song('bgm_cave_early', 100, 52, 'minor', [0, 5, 0, 6], drums='tribal', arp='4th', pad_bright=600, bass_pat='half', lead_kind='sin', bells=True, arp_oct=24)
    song('bgm_cave_middle', 116, 52, 'dorian', [0, 6, 5, 6], drums='tribal', arp='8th', pad_bright=700, bass_pat='pulse', lead_kind='sin')
    song('bgm_cave_late', 132, 50, 'phrygian', [0, 1, 0, 6], drums='heavy', arp='16th', pad_bright=800, bass_pat='drive')
    # ボス(共通3系統)と死神
    song('bgm_boss_normal', 152, 57, 'minor', [0, 5, 6, 4], bars=16, drums='drive', arp='16th', pad_bright=1500, bass_pat='drive', lead_kind='sq')
    song('bgm_boss_strong', 162, 55, 'harmonic', [0, 5, 3, 4], bars=16, drums='heavy', arp='16th', pad_bright=1700, bass_pat='drive', lead_kind='sq')
    song('bgm_boss_special', 170, 53, 'phrygian', [0, 1, 5, 4], bars=16, drums='heavy', arp='16th', pad_bright=2000, bass_pat='pulse', lead_kind='saw', bells=True)
    song('bgm_boss_death', 84, 48, 'harmonic', [0, 5, 1, 4], bars=8, drums='tribal', arp='4th', pad_bright=500, bass_pat='half', lead_kind='sin', bells=True, arp_oct=24)
    song('bgm_bonus_zone', 150, 67, 'major', [0, 3, 4, 0], bars=8, drums='mid', arp='16th', pad_bright=2200, bass_pat='drive', lead_kind='sq')
    # ===== ジングル =====
    jingle('jingle_result', [60, 64, 67, 72, 76, 79], 170, [72, 76, 79, 84])
    jingle('jingle_game_over', [67, 66, 63, 60, 55], 110, [48, 51, 55], minor=True)
    # ===== 環境音 =====
    loop_noise('amb_sky_wind', 24, 700, lfo=0.09, depth=0.8, hiss=0.05)
    loop_noise('amb_wasteland_wind', 24, 450, lfo=0.07, depth=0.6)
    loop_noise('amb_cave_air', 24, 220, lfo=0.05, depth=0.3, rumble=0.25)
    loop_noise('amb_home_breeze', 24, 380, lfo=0.06, depth=0.5)
    for v in range(3): amb_shot(f'amb_bird_{v}', bird(v))
    for v in range(2): amb_shot(f'amb_drip_{v}', drip(v))
    amb_shot('amb_rock_far', rock())
    amb_shot('amb_grass_rustle', rustle())
    amb_shot('amb_wind_whistle', whistle())
    # ===== SE(プレイヤーの武器タイプ別: 通常/強) =====
    se('atk_dual_blade', mix(whoosh(0.12, 1500, 5000, 0.9), np.concatenate([np.zeros(int(0.06 * SR)), whoosh(0.12, 1800, 5500, 0.8)])))
    se('atk_dual_blade_strong', mix(whoosh(0.2, 900, 4000), np.concatenate([np.zeros(int(0.08 * SR)), whoosh(0.2, 1200, 4500)]), np.concatenate([np.zeros(int(0.1 * SR)), tone([2400], decay=0.15, vol=0.2)])))
    se('atk_gun', mix(crack(0.08, 1.4), thump(90, 0.18, 1.2), lp_fast(rng.standard_normal(int(0.25 * SR)), 1500) * np.exp(-np.arange(int(0.25 * SR)) / SR * 18) * 0.8))
    se('atk_gun_strong', mix(crack(0.1, 1.6), thump(60, 0.3, 1.5), lp_fast(rng.standard_normal(int(0.4 * SR)), 900) * np.exp(-np.arange(int(0.4 * SR)) / SR * 9)))
    twang = lambda f, d: mix(tone([f], decay=d, kind='tri', vol=0.8), tone([f * 2.01], decay=d * 0.6, vol=0.3))
    se('atk_bow', mix(twang(220, 0.12), np.concatenate([np.zeros(int(0.03 * SR)), whoosh(0.18, 2000, 6000, 0.7)])))
    se('atk_bow_strong', mix(twang(160, 0.2), np.concatenate([np.zeros(int(0.04 * SR)), whoosh(0.3, 1500, 7000, 0.9)]), tone([1800, 2400], note=0.05, decay=0.12, vol=0.2)))
    sparkle = lambda base: tone([base, base * 1.26, base * 1.5, base * 2], note=0.035, decay=0.15, vol=0.5)
    se('atk_magic', mix(sparkle(900), whoosh(0.25, 3000, 8000, 0.4)))
    se('atk_magic_strong', mix(sparkle(600), sparkle(1200) * 0.6, thump(120, 0.3, 0.6), whoosh(0.4, 2000, 8000, 0.5)))
    se('atk_strike', mix(whoosh(0.14, 500, 2000, 0.8, low=True), np.concatenate([np.zeros(int(0.06 * SR)), thump(110, 0.15, 0.8)])))
    se('atk_strike_strong', mix(whoosh(0.22, 300, 1800, 1.0, low=True), np.concatenate([np.zeros(int(0.08 * SR)), thump(70, 0.3, 1.3), crack(0.08, 0.5)])))
    se('atk_special', mix(whoosh(0.3, 600, 4500, 1.0), np.concatenate([np.zeros(int(0.12 * SR)), tone([1300, 1950], note=0.02, decay=0.25, vol=0.25)])))
    se('atk_special_strong', mix(whoosh(0.4, 400, 5000, 1.1, low=True), np.concatenate([np.zeros(int(0.15 * SR)), tone([900, 1350, 1800], note=0.02, decay=0.3, vol=0.3), thump(80, 0.3)])))
    se('atk_sword_strong', mix(whoosh(0.3, 500, 3500, 1.1, low=True), np.concatenate([np.zeros(int(0.12 * SR)), tone([1500, 2250], note=0.01, decay=0.2, vol=0.2)])))
    # 共通の動き別
    se('atk_up_launch', mix(whoosh(0.3, 400, 6000, 1.0), tone([300, 450, 600], note=0.05, decay=0.1, kind='tri', vol=0.25)))
    se('atk_air', whoosh(0.15, 1200, 4500, 0.8))
    se('atk_down_slam', mix(whoosh(0.25, 5000, 600, 1.0, low=True)))
    se('hit_strong', mix(thump(55, 0.35, 1.5), crack(0.1, 1.2), lp_fast(rng.standard_normal(int(0.3 * SR)), 800) * np.exp(-np.arange(int(0.3 * SR)) / SR * 15)))
    # 敵
    se('enemy_attack', mix(whoosh(0.2, 700, 2500, 0.8, low=True), tone([140, 120], note=0.08, decay=0.1, kind='saw', vol=0.15)))
    se('enemy_attack_big', mix(whoosh(0.45, 250, 1500, 1.2, low=True), np.concatenate([np.zeros(int(0.25 * SR)), thump(45, 0.45, 1.6)])))
    roar_n = int(1.1 * SR); rt = np.arange(roar_n) / SR
    roar = lp_fast(rng.standard_normal(roar_n) * (1 + 0.5 * np.sin(2 * math.pi * 23 * rt)), 700) * np.sin(math.pi * rt / 1.1) * 3 + osc('saw', 70, roar_n) * np.sin(math.pi * rt / 1.1) * 0.3
    se('boss_attack', lp_fast(roar, 1200))
    se('boss_hit', mix(thump(65, 0.3, 1.2), crack(0.08, 0.9), tone([520, 780], note=0.01, decay=0.12, vol=0.3)))
    boom_n = int(2.2 * SR); bt = np.arange(boom_n) / SR
    boom = mix(lp_fast(rng.standard_normal(boom_n), 250) * np.exp(-bt * 1.8) * 8, thump(40, 0.8, 2.0), crack(0.2, 1.0))
    se('boss_defeat', boom)
    se('boss_final_hit', mix(crack(0.15, 1.6), thump(50, 0.5, 1.6), tone([880, 1320, 1760], note=0.02, decay=0.5, vol=0.35)))
    se('boss_warning', tone([880, 660, 880, 660], note=0.25, decay=0.2, kind='sq', vol=0.25))
    se('boss_appear', mix(thump(35, 1.2, 2.0), lp_fast(rng.standard_normal(int(1.5 * SR)), 200) * np.exp(-np.arange(int(1.5 * SR)) / SR * 2.5) * 5))
    se('milestone', tone([988, 1319, 1568], note=0.08, decay=0.35, vol=0.5))
    se('milestone_clear', tone([784, 988, 1175, 1568, 1976], note=0.07, decay=0.4, vol=0.45))
    se('level_up', tone([523, 659, 784, 1047, 1319], note=0.06, decay=0.35, kind='tri', vol=0.5))
    # UI
    se('ui_decide', tone([1320, 1760], note=0.04, decay=0.06, vol=0.5))
    se('ui_cancel', tone([880, 660], note=0.05, decay=0.07, vol=0.45))
    se('ui_stage_select', mix(tone([660, 990], note=0.05, decay=0.1, kind='tri', vol=0.5), whoosh(0.15, 1500, 4000, 0.25)))
    se('ui_character_select', tone([784, 1175], note=0.05, decay=0.1, kind='tri', vol=0.5))
    se('ui_countdown', tone([880], decay=0.12, kind='sq', vol=0.25))
    se('ui_run_go', mix(tone([1047, 1319, 1568, 2093], note=0.035, decay=0.3, kind='sq', vol=0.22), whoosh(0.4, 800, 6000, 0.4)))
    se('ui_screen_whoosh', whoosh(0.35, 400, 3000, 0.6))
    # HOME
    knock = lambda: mix(thump(160, 0.1, 0.8), lp_fast(rng.standard_normal(int(0.08 * SR)), 1500) * np.exp(-np.arange(int(0.08 * SR)) / SR * 50) * 0.8)
    se('home_door', mix(knock(), np.concatenate([np.zeros(int(0.14 * SR)), knock()]), np.concatenate([np.zeros(int(0.3 * SR)), tone([220, 196], note=0.25, decay=0.3, kind='saw', vol=0.05)])))
    paper = lambda: hp_fast(lp_fast(rng.standard_normal(int(0.12 * SR)), 5000), 1500) * np.sin(math.pi * np.arange(int(0.12 * SR)) / int(0.12 * SR))
    se('home_deck_edit', mix(paper(), np.concatenate([np.zeros(int(0.08 * SR)), paper()])))
    rattle = np.concatenate([mix(crack(0.03, 0.6), tone([2600 + 200 * k], decay=0.02, vol=0.2)) for k in range(8)])
    se('home_gacha', mix(rattle, np.concatenate([np.zeros(len(rattle)), tone([1047, 1568], note=0.06, decay=0.3, vol=0.4)])))
    se('home_coin', tone([1976, 2637], note=0.05, decay=0.25, vol=0.5))

def gen_last_corridor():
    # LAST CORRIDOR(ラストダンジョン候補、2026-09-29): 荘厳で冷たい古代回廊 → 崩壊の緊迫 → 奈落の上の最後の道
    song('bgm_last_early', 108, 50, 'harmonic', [0, 5, 3, 4], drums='soft', arp='8th', pad_bright=700, bass_pat='half', lead_kind='sin', bells=True, arp_oct=24)
    song('bgm_last_middle', 128, 50, 'phrygian', [0, 1, 6, 4], drums='heavy', arp='16th', pad_bright=900, bass_pat='pulse', lead_kind='tri')
    song('bgm_last_late', 144, 50, 'harmonic', [0, 5, 1, 4], drums='drive', arp='16th', pad_bright=1300, bass_pat='drive', lead_kind='saw', bells=True)
    loop_noise('amb_last_hall', 24, 260, lfo=0.04, depth=0.45, rumble=0.18, hiss=0.02)

if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == 'last': gen_last_corridor()
    else: gen_all()
