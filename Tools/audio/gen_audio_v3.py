# OneMoreMile SE 改修 (2026-10-14): ボス遭遇の重低音 / ジャンプ / ボスの声。
#  すべてこのスクリプトで合成した音(外部の素材・サンプルは使っていない。権利はプロジェクトのもの)。
#  既存のファイルは上書きしない: 新しい名前(*_v3.wav)で書き出し、AudioLibrary の割り当てだけを替える(戻す時は割り当てを元の名前へ)。
#  大きさは gen_audio_v2.py と同じ段(SE_OFFSET 込み)にそろえる。
#   使い方: python gen_audio_v3.py            → Assets/Audio/Placeholder/SE/*_v3.wav
#           python gen_audio_v3.py --report   → 大きさ/ピーク/帯域の表
import os, sys, math
import numpy as np
from scipy import signal
sys.path.insert(0, os.path.dirname(__file__))
import gen_audio_v2 as v2   # 書き出し・大きさの段・既存の部品を使い回す
from gen_audio_v2 import SR, se, mix, at, z, env_exp, fade_tail, REPORT, L_MOVE, L_CUE, L_CUE_BIG, L_SLAM

rng = np.random.default_rng(20261014)

def noise(sec): return rng.standard_normal(int(sec * SR))
def sos_f(x, kind, f, order=2):
    nyq = SR / 2
    if kind == 'bp': sos = signal.butter(order, [f[0] / nyq, f[1] / nyq], 'bandpass', output='sos')
    else: sos = signal.butter(order, f / nyq, 'lowpass' if kind == 'lp' else 'highpass', output='sos')
    return signal.sosfilt(sos, x)
def lp(x, f, o=2): return sos_f(x, 'lp', f, o)
def hp(x, f, o=2): return sos_f(x, 'hp', f, o)
def bp(x, lo, hi, o=2): return sos_f(x, 'bp', (lo, hi), o)
def peak(x, f, q, gain=1.0):
    b, a = signal.iirpeak(f / (SR / 2), q)
    return signal.lfilter(b, a, x) * gain
def env_ad(n, a, d_tau):
    t = np.arange(n) / SR
    return np.clip(t / max(a, 1e-4), 0, 1) * np.exp(-np.maximum(t - a, 0) / d_tau)
def env_bell(n, peak_at, width):
    t = np.arange(n) / SR
    return np.exp(-((t - peak_at) / width) ** 2)
def tv_bp(x, f0, f1, bw_ratio=0.6, blocks=48):
    # 中心周波数が動く帯域通過(短い区間ごとに掛けてつなぐ)
    n = len(x); y = np.zeros(n); edges = np.linspace(0, n, blocks + 1).astype(int)
    for i in range(blocks):
        a, b = edges[i], edges[i + 1]
        fc = f0 + (f1 - f0) * (i + 0.5) / blocks
        lo, hi = fc * (1 - bw_ratio / 2), min(fc * (1 + bw_ratio / 2), SR / 2 - 100)
        seg = bp(x[max(0, a - 512):b], lo, hi)
        y[a:b] = seg[-(b - a):]
    return y
def soft(x, drive=1.5): return np.tanh(x * drive) / np.tanh(drive)
def reverb(x, sec=0.8, mixv=0.3, cut=2500):
    n = int(sec * SR); ir = rng.standard_normal(n) * env_exp(n, 6.0 / sec); ir = lp(ir, cut); ir /= np.sqrt((ir ** 2).sum()) + 1e-9
    wet = signal.fftconvolve(x, ir)
    return np.concatenate([x, np.zeros(len(wet) - len(x))]) + wet * mixv

# ---------- 声の素(のど)----------
def glottal(f0_curve, rough=0.0, sub=0.0, jitter=0.01):
    # f0_curve: 周波数の配列(サンプルごと)。のこぎり波 + 揺らぎ + 半分の高さの成分(うなりのざらつき)
    n = len(f0_curve)
    j = 1 + jitter * lp(rng.standard_normal(n), 30) * 8
    f = f0_curve * j
    ph = np.cumsum(f) / SR
    x = 2 * (ph % 1) - 1
    if sub > 0: x = x + sub * np.sign(np.sin(math.pi * ph))  # f0/2
    if rough > 0:
        am = 1 + rough * lp(rng.standard_normal(n), 60) * 4
        x = x * np.clip(am, 0, 3)
    return x
def formants(x, fs, q=6.0):
    return sum(peak(x, f, q, g) for f, g in fs)
def curve(points, sec):
    # points: [(t0, f0), (t1, f1), ...] を滑らかにつなぐ(指数的)
    n = int(sec * SR); t = np.arange(n) / SR
    ts = [p[0] for p in points]; fs = [math.log(p[1]) for p in points]
    return np.exp(np.interp(t, ts, fs))
def breath(sec, lo, hi, level):
    return bp(noise(sec), lo, hi) * level

# ======================================================================
# 1. ボス遭遇: 重低音の「ドォーン」(チャイムの音程を無くす)
BOOM_MID = 2.0  # 携帯のスピーカー相当(180Hz より上)で約 -22dB(チャイム -20dB に近い)。ピーク -5.5dBFS
def boss_impact():
    sec = 2.4; n = int(sec * SR); t = np.arange(n) / SR
    # 低い芯: 58Hz → 34Hz へ沈む正弦。携帯のスピーカーでも聞こえるよう、ゆるく歪ませて倍音(100〜250Hz)を足す
    f = 34 + 24 * np.exp(-t / 0.18)
    ph = 2 * math.pi * np.cumsum(f) / SR
    sub = (np.sin(ph) + 0.45 * np.sin(2 * ph) + 0.22 * np.sin(3 * ph) + 0.1 * np.sin(4 * ph)) * env_ad(n, 0.004, 0.75)  # 倍音(68〜230Hz): 携帯のスピーカーでも重さが出る
    mid = bp(noise(sec), 110, 320, 2) * env_ad(n, 0.003, 0.22) * 1.6                   # 「ドォ」の胴(100〜300Hz)
    body = lp(noise(sec), 180, 4) * env_ad(n, 0.002, 0.28) * 2.2       # 胴: 低い所のざらつき(地鳴り)
    knock = v2.thump(95, 0.22, 0.9)                                      # 頭の「ド」
    click = lp(noise(0.02), 1400) * env_ad(int(0.02 * SR), 0.0005, 0.004) * 0.35  # 輪郭(高くしない)
    # 携帯のスピーカー(おおむね 180Hz より下は出ない)でも「ドォーン」と聞こえるよう、低い芯を強く歪ませた成分を 120〜500Hz だけ足す。
    # (測定: これが無いと 180Hz より上の大きさがチャイムより 14dB 小さかった)
    grit = bp(np.tanh(sub * 5.0) + np.tanh(lp(noise(sec), 90, 4) * env_ad(n, 0.002, 0.5) * 30) * 0.5, 120, 500, 2) * BOOM_MID
    x = mix(sub * 1.0, body, mid, grit, knock, click)
    x = soft(x * 1.1, 1.6)                                               # 倍音(重さが小さいスピーカーでも伝わる)
    x = reverb(x, 1.3, 0.32, cut=600)                                    # 短い余韻(低い所だけ)
    x = lp(hp(x, 28, 2), 2400, 2)                                        # 高いチャイム感は残さない
    return fade_tail(x[:int(2.6 * SR)], 0.4)

# 2. ジャンプ: 「サッ」(空気を軽く切る)/ 二段ジャンプ: 少し力強い「シュバッ」
def jump_light():
    sec = 0.14; n = int(sec * SR)
    air = tv_bp(noise(sec), 1800, 4200, 0.7) * env_ad(n, 0.006, 0.045)
    rustle = np.zeros(n)                                                 # 服のこすれ(小さな粒)
    for k, (st, dur) in enumerate([(0.0, 0.010), (0.012, 0.008), (0.026, 0.012)]):
        g = bp(noise(dur), 1200, 3200) * np.hanning(int(dur * SR)) * (0.6 - k * 0.15)
        s = int(st * SR); rustle[s:s + len(g)] += g[:n - s]
    x = mix(air, rustle * 0.7)
    return fade_tail(lp(hp(x, 700, 2), 9000, 2), 0.03)

def jump_double():
    sec = 0.21; n = int(sec * SR)
    swish = tv_bp(noise(sec), 900, 3800, 0.8) * env_bell(n, 0.055, 0.04)          # 「シュ」
    snap = tv_bp(noise(sec), 2600, 5200, 0.5) * env_bell(n, 0.11, 0.022) * 0.55    # 「バッ」の空気
    low = lp(noise(sec), 260) * env_bell(n, 0.10, 0.025) * 1.2                     # 軽い踏み込み(低い所を少しだけ)
    x = mix(swish, snap, low)
    return fade_tail(lp(hp(x, 120, 2), 10000, 2), 0.04)

# 3. ボスの声(種類ごとの素材。ボスごとの違いはゲーム側の Pitch/音量で出す)
#    roar = 登場の咆哮(1.3〜2.2秒)/ growl = 強い攻撃の予兆(0.5〜0.8秒)/ death = 撃破(1.0〜1.6秒)
def beast(kind):   # 巨大オオカミ等: 獣のうなり・咆哮
    if kind == 'roar':
        sec = 1.7; f = curve([(0, 110), (0.25, 210), (0.7, 260), (1.2, 170), (1.7, 95)], sec)
        g = glottal(f, rough=0.35, sub=0.45, jitter=0.02)
        v = formants(g, [(650, 1.0), (1150, 0.7), (2500, 0.35)], 4.0) + breath(sec, 800, 4500, 0.35)
        e = env_ad(len(f), 0.08, 0.9) * np.clip((sec - np.arange(len(f)) / SR) / 0.35, 0, 1)
    elif kind == 'growl':
        sec = 0.75; f = curve([(0, 85), (0.4, 105), (0.75, 80)], sec)
        g = glottal(f, rough=0.6, sub=0.6, jitter=0.03)
        v = formants(g, [(450, 1.0), (900, 0.6), (2200, 0.2)], 5.0) + breath(sec, 500, 2500, 0.25)
        e = env_ad(len(f), 0.06, 0.5) * np.clip((sec - np.arange(len(f)) / SR) / 0.2, 0, 1)
    else:
        sec = 1.3; f = curve([(0, 240), (0.2, 300), (0.6, 180), (1.3, 70)], sec)
        g = glottal(f, rough=0.3, sub=0.3, jitter=0.03)
        v = formants(g, [(700, 1.0), (1300, 0.6), (2700, 0.3)], 4.0) + breath(sec, 900, 5000, 0.4)
        e = env_ad(len(f), 0.03, 0.55)
    return reverb(soft(v * e, 1.4), 0.7, 0.22)

def dragon(kind):  # 竜: 腹に響く巨大な咆哮(低い芯 + 炎の息)
    if kind == 'roar':
        sec = 2.2; f = curve([(0, 70), (0.35, 150), (1.0, 170), (1.7, 110), (2.2, 55)], sec)
        g = glottal(f, rough=0.45, sub=0.8, jitter=0.025)
        v = formants(g, [(420, 1.0), (850, 0.8), (1900, 0.35)], 3.5) + breath(sec, 300, 3500, 0.55) + lp(noise(sec), 120) * 0.8
        e = env_ad(len(f), 0.12, 1.2) * np.clip((sec - np.arange(len(f)) / SR) / 0.5, 0, 1)
    elif kind == 'growl':
        sec = 0.8; f = curve([(0, 60), (0.5, 75), (0.8, 55)], sec)
        g = glottal(f, rough=0.7, sub=0.9, jitter=0.03)
        v = formants(g, [(350, 1.0), (700, 0.6)], 5.0) + breath(sec, 250, 2000, 0.4)
        e = env_ad(len(f), 0.08, 0.55) * np.clip((sec - np.arange(len(f)) / SR) / 0.25, 0, 1)
    else:
        sec = 1.6; f = curve([(0, 160), (0.3, 190), (0.9, 100), (1.6, 45)], sec)
        g = glottal(f, rough=0.4, sub=0.6, jitter=0.03)
        v = formants(g, [(480, 1.0), (950, 0.6), (2000, 0.3)], 4.0) + breath(sec, 300, 3000, 0.5)
        e = env_ad(len(f), 0.04, 0.7)
    return reverb(soft(v * e, 1.5), 0.9, 0.25, cut=1800)

def insect(kind):  # 巨大昆虫/サソリ/ワーム: 甲殻の軋み・カチカチ・異質な鳴き声
    def clicks(sec, rate, lo, hi, level):
        n = int(sec * SR); y = np.zeros(n); t = 0.0
        while t < sec:
            s = int(t * SR); w = int(0.004 * SR)
            if s + w < n: y[s:s + w] += rng.standard_normal(w) * np.hanning(w)
            t += 1.0 / rate * rng.uniform(0.6, 1.4)
        return bp(y, lo, hi) * level
    if kind == 'roar':
        sec = 1.5; n = int(sec * SR); t = np.arange(n) / SR
        fm = 1400 + 500 * np.sin(2 * math.pi * 6.5 * t) + 300 * np.sin(2 * math.pi * 31 * t)
        screech = np.sin(2 * math.pi * np.cumsum(fm) / SR) * (1 + 0.6 * np.sin(2 * math.pi * 47 * t))
        v = bp(screech, 900, 3600) * 0.5 + clicks(sec, 55, 1800, 5200, 1.2) + bp(noise(sec), 2500, 7000) * 0.25
        e = env_ad(n, 0.05, 0.8) * np.clip((sec - t) / 0.3, 0, 1)
    elif kind == 'growl':
        sec = 0.55; n = int(sec * SR); t = np.arange(n) / SR
        v = clicks(sec, 90, 1500, 4800, 1.4) + bp(noise(sec), 600, 1800) * 0.35 * (1 + np.sin(2 * math.pi * 23 * t))
        e = env_ad(n, 0.02, 0.4)
    else:
        sec = 1.1; n = int(sec * SR); t = np.arange(n) / SR
        fm = 2200 * np.exp(-t / 0.5) + 300
        v = bp(np.sin(2 * math.pi * np.cumsum(fm) / SR), 300, 3500) * 0.5 + clicks(sec, 40, 1500, 4500, 1.0) * np.exp(-t / 0.4)
        e = env_ad(n, 0.01, 0.45)
    return reverb(soft(v * e, 1.3), 0.5, 0.18, cut=4000)

def golem(kind):   # ゴーレム/岩/甲冑: 岩や金属が軋む重い音
    def grind(sec, rate, level):
        n = int(sec * SR); base = lp(noise(sec), 400, 2)
        crackle = np.zeros(n); t = 0.0
        while t < sec:
            s = int(t * SR); w = int(rng.uniform(0.003, 0.012) * SR)
            if s + w < n: crackle[s:s + w] += rng.standard_normal(w) * np.hanning(w) * rng.uniform(0.3, 1.0)
            t += 1.0 / rate * rng.uniform(0.3, 1.7)
        return (base * 1.5 + bp(crackle, 300, 2500)) * level
    def creak(sec, f0, f1, level):
        f = curve([(0, f0), (sec, f1)], sec)
        g = glottal(f, rough=0.8, sub=0.3, jitter=0.06)
        return formants(g, [(300, 1.0), (1700, 0.4), (3100, 0.25)], 8.0) * level   # 金属の軋み(細い共鳴)
    if kind == 'roar':
        sec = 1.8; n = int(sec * SR)
        v = grind(sec, 70, 0.9) + creak(sec, 45, 70, 0.6) + lp(noise(sec), 90) * 0.9
        e = env_ad(n, 0.15, 1.0) * np.clip((sec - np.arange(n) / SR) / 0.4, 0, 1)
    elif kind == 'growl':
        sec = 0.7; n = int(sec * SR)
        v = grind(sec, 110, 1.0) + creak(sec, 60, 40, 0.5)
        e = env_ad(n, 0.05, 0.45)
    else:
        sec = 1.5; n = int(sec * SR)
        v = grind(sec, 140, 1.2) * np.exp(-np.arange(n) / SR / 0.6) + creak(sec, 70, 30, 0.5)
        v = mix(v, at(0.25, v2.thump(55, 0.6, 1.5)))   # 崩れ落ちる「ドン」
        e = np.ones(len(v)); e[:int(0.01 * SR)] = np.linspace(0, 1, int(0.01 * SR))
    return reverb(soft(v * e[:len(v)], 1.3), 0.8, 0.25, cut=1500)

def humanoid(kind):  # 魔人・人型: 低いうなり声、威圧的な声(母音「オ/ア」)
    if kind == 'roar':
        sec = 1.6; f = curve([(0, 70), (0.3, 105), (1.0, 95), (1.6, 62)], sec)
        g = glottal(f, rough=0.25, sub=0.5, jitter=0.012)
        v = formants(g, [(520, 1.0), (880, 0.8), (2400, 0.3), (3200, 0.15)], 6.0) + breath(sec, 400, 3000, 0.15)
        t = np.arange(len(f)) / SR
        v = v * (1 + 0.25 * np.sin(2 * math.pi * 5.0 * t))   # ゆっくりの震え(威圧)
        e = env_ad(len(f), 0.1, 0.9) * np.clip((sec - t) / 0.35, 0, 1)
    elif kind == 'growl':
        sec = 0.65; f = curve([(0, 72), (0.65, 60)], sec)
        g = glottal(f, rough=0.5, sub=0.6, jitter=0.02)
        v = formants(g, [(450, 1.0), (800, 0.6)], 7.0) + breath(sec, 300, 1800, 0.2)
        e = env_ad(len(f), 0.04, 0.45)
    else:
        sec = 1.3; f = curve([(0, 130), (0.3, 120), (1.3, 50)], sec)
        g = glottal(f, rough=0.3, sub=0.4, jitter=0.02)
        v = formants(g, [(700, 1.0), (1200, 0.7), (2600, 0.25)], 5.0) + breath(sec, 500, 3500, 0.25)
        e = env_ad(len(f), 0.03, 0.6)
    return reverb(soft(v * e, 1.4), 0.8, 0.25, cut=2200)

def reaper(kind):  # 死神: 不気味な共鳴・非人間的な声(ずれた音程の重なり + ささやき)
    sec = {'roar': 2.0, 'growl': 0.8, 'death': 1.6}[kind]
    n = int(sec * SR); t = np.arange(n) / SR
    base = {'roar': 196.0, 'growl': 174.6, 'death': 220.0}[kind]
    bend = np.exp(-t / 0.9) * (0.0 if kind != 'death' else -0.35) + 1.0
    v = np.zeros(n)
    for r, d, a in [(1.0, 0.0, 1.0), (1.059, 3.1, 0.7), (1.414, 1.7, 0.45), (0.5, 0.0, 0.6), (2.12, 4.3, 0.2)]:
        f = base * r * bend + d
        v += np.sin(2 * math.pi * np.cumsum(f) / SR + rng.uniform(0, 6)) * a
    v = v * (0.6 + 0.4 * np.sin(2 * math.pi * 2.3 * t))                 # ゆっくりのうねり
    whisper = bp(noise(sec), 2500, 7000) * (0.5 + 0.5 * np.sin(2 * math.pi * 7 * t + 1)) * 0.5
    swell = np.clip(t / (sec * 0.45), 0, 1) ** 1.5 if kind == 'roar' else env_ad(n, 0.05, sec * 0.5)
    tail = np.clip((sec - t) / 0.4, 0, 1)
    x = (v * 0.5 + whisper) * swell * tail
    return reverb(x, 1.4, 0.45, cut=5000)

def mech(kind):  # メカドラゴン: 機械の駆動音 + 合成の咆哮
    def servo(sec, f0, f1, level):
        n = int(sec * SR); t = np.arange(n) / SR
        steps = np.floor(t * 12) / 12                                    # 段のある駆動音
        f = f0 + (f1 - f0) * np.clip(steps / max(sec, 1e-3), 0, 1)
        return bp(signal.sawtooth(2 * math.pi * np.cumsum(f) / SR), 300, 3000) * level
    def comb(x, ms, fb):
        d = int(ms / 1000 * SR); y = x.copy()
        for i in range(d, len(y)): y[i] += fb * y[i - d]
        return y
    if kind == 'roar':
        sec = 1.9
        r = dragon('roar')[:int(sec * SR)]
        r = np.round(r * 24) / 24                                        # 少しだけ粗く(合成の声)
        v = mix(comb(r * 0.6, 7.0, 0.55), at(0.0, servo(0.6, 300, 900, 0.5)), bp(noise(sec), 3000, 8000) * env_ad(int(sec * SR), 0.3, 0.4) * 0.3)
    elif kind == 'growl':
        sec = 0.7
        v = mix(servo(sec, 500, 1400, 0.7), comb(dragon('growl')[:int(sec * SR)] * 0.5, 5.0, 0.5), bp(noise(sec), 2000, 6000) * env_ad(int(sec * SR), 0.01, 0.15) * 0.4)
    else:
        sec = 1.5; n = int(sec * SR); t = np.arange(n) / SR
        down = signal.sawtooth(2 * math.pi * np.cumsum(900 * np.exp(-t / 0.4) + 60) / SR)
        v = mix(bp(down, 150, 2500) * np.exp(-t / 0.6) * 0.7, comb(dragon('death')[:n] * 0.4, 6.0, 0.5), v2.crack(0.15, 0.6))
    return reverb(soft(v, 1.3), 0.6, 0.2, cut=3500)

def trim(x, sec, fade=0.35):
    n = min(len(x), int(sec * SR)); y = x[:n].copy(); k = int(fade * SR); y[-k:] *= np.linspace(1, 0, k) ** 2; return y
# 余韻込みの長さの上限(咆哮 2.6秒 / うなり 1.0秒 / 断末魔 2.0秒)。頻繁に聞こえても邪魔にならない長さ
MAXLEN = {'roar': 2.6, 'growl': 1.0, 'death': 2.0}

VOICES = {'beast': beast, 'dragon': dragon, 'insect': insect, 'golem': golem, 'humanoid': humanoid, 'reaper': reaper, 'mech': mech}
# 声の大きさの段: 咆哮はボスの重低音(L_CUE_BIG)より少し小さく、予兆のうなりは攻撃の予兆と同じ段、断末魔は撃破の音の下
L_ROAR, L_GROWL, L_DEATH = L_CUE_BIG - 2.0, L_CUE - 1.0, L_SLAM - 1.5

def gen():
    se('boss_impact_v3', boss_impact(), L_CUE_BIG)
    se('mv_jump_v3', jump_light(), L_MOVE - 1.0)          # 連続でもうるさくない(元の段より 1dB 下)
    se('mv_double_jump_v3', jump_double(), L_MOVE + 0.5)
    for name, fn in VOICES.items():
        for kind, db in [('roar', L_ROAR), ('growl', L_GROWL), ('death', L_DEATH)]:
            se(f'bv_{name}_{kind}_v3', trim(fn(kind), MAXLEN[kind]), db)

def report():
    import wave
    for n, db, s in REPORT:
        p = f'{v2.OUT}/SE/{n.split()[0]}.wav'
        w = wave.open(p); x = np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(float) / 32768
        X = np.abs(np.fft.rfft(x)); f = np.fft.rfftfreq(len(x), 1 / SR)
        flat = np.exp(np.mean(np.log(X + 1e-9))) / np.mean(X)
        bands = [20 * math.log10(np.sqrt((X[(f >= a) & (f < b)] ** 2).sum()) + 1e-9) for a, b in [(20, 100), (100, 300), (300, 1000), (1000, 3000), (3000, 10000)]]
        print(f'{n:34s} target {db:6.1f} dB  {s:4.2f}s  peak {20 * math.log10(abs(x).max() + 1e-9):5.1f} dBFS  flat {flat:.2f}  bands ' + ' '.join(f'{b:5.0f}' for b in bands))

if __name__ == '__main__':
    gen()
    report()
