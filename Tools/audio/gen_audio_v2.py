# OneMoreMile audio redesign (2026-10-06): placeholder audio, second generation.
#  - Adds new SE (FINISH / boss telegraphs / cards / COMBO / ULTIMATE / sprint / UI / arena / multiplayer),
#    high-speed wind, per-map ambient one-shots, final-boss and arena BGM.
#  - Normalizes every SE / BGM / ambient file to a loudness target per category
#    (fixes the old ladder where a normal hit and a strong hit were the same loudness).
# Loudness = loudest 50 ms RMS (dBFS) for SE, mean of the top 10% 400 ms windows for BGM / ambient.
# Output: Assets/Audio/Placeholder/{Bgm,Ambience,SE}/... (16-bit PCM)
# The old gen_audio.py functions (instruments, songs, SE parts) are reused.
import os, sys, math, wave, glob
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
import gen_audio as g
from gen_audio import SR, midi, whoosh, thump, crack, tone, mix, lp_fast, hp_fast, osc, song, loop_noise

ROOT = r'C:/GameProject/2ndAction/Unity/2ndAction/Assets/Audio'
OUT = ROOT + '/Placeholder'
rng = np.random.default_rng(20261006)
g.rng = rng

def z(sec): return np.zeros(int(sec * SR))
def at(sec, x): return np.concatenate([z(sec), x])
def noise(sec): return rng.standard_normal(int(sec * SR))
def env_exp(n, k): return np.exp(-np.arange(n) / SR * k)
def fade_tail(x, sec=0.02):
    k = min(len(x), int(sec * SR)); x = x.copy(); x[-k:] *= np.linspace(1, 0, k); return x

def sweep(f0, f1, sec, kind='sin', curve=1.0):
    n = int(sec * SR); t = np.linspace(0, 1, n) ** curve
    f = f0 + (f1 - f0) * t
    ph = np.cumsum(f) / SR
    if kind == 'sin': return np.sin(2 * math.pi * ph)
    if kind == 'tri': return 2 * np.abs(2 * (ph % 1) - 1) - 1
    if kind == 'saw': return 2 * (ph % 1) - 1
    return np.where((ph % 1) < 0.5, 1.0, -1.0)

def shimmer(base, n_notes=6, step=1.19, note=0.03, decay=0.22, vol=0.5):
    return tone([base * step ** k for k in range(n_notes)], note=note, decay=decay, vol=vol)

def reverb(x, sec=0.6, mixv=0.25):
    # Simple tail: decaying noise convolution (short ones only)
    n = int(sec * SR); ir = rng.standard_normal(n) * env_exp(n, 6.0 / sec); ir = lp_fast(ir, 3500); ir /= np.sqrt((ir ** 2).sum()) + 1e-9
    wet = np.convolve(x, ir)
    dry = np.concatenate([x, np.zeros(len(wet) - len(x))])
    return dry + wet * mixv

# ---------------- loudness ----------------
def short_loud(x, win=0.05):
    w = int(SR * win)
    if len(x) < w: return np.sqrt((x ** 2).mean() + 1e-12)
    c = np.convolve(x ** 2, np.ones(w) / w, mode='valid')
    return math.sqrt(c.max() + 1e-12)

def long_loud(x):
    w = int(SR * 0.4); k = len(x) // w
    if k < 2: return short_loud(x)
    r = np.sqrt((x[:k * w].reshape(k, w) ** 2).mean(axis=1)); r = np.sort(r)[::-1]
    return r[:max(1, k // 10)].mean()

def normalize(x, target_db, long=False):
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 1: m = x
    else: m = x.mean(axis=1)
    cur = long_loud(m) if long else short_loud(m)
    x = x * (10 ** (target_db / 20) / (cur + 1e-12))
    pk = np.abs(x).max()
    if pk > 0.97:  # soft limit instead of hard clip
        x = np.tanh(x / pk * 1.6) / np.tanh(1.6) * 0.97
    return x

def write(path, x, stereo=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    x = np.asarray(x, dtype=np.float64)
    data = (np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(2 if stereo else 1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(data.reshape(-1).tobytes())

def load(p):
    with wave.open(p) as w:
        n = w.getnframes(); ch = w.getnchannels(); sw = w.getsampwidth(); sr = w.getframerate(); b = w.readframes(n)
    x = np.frombuffer(b, '<i2').astype(np.float64) / 32768 if sw == 2 else (np.frombuffer(b, np.uint8).astype(np.float64) - 128) / 128
    x = x.reshape(-1, ch)
    if sr != SR:  # resample (linear) to 44.1k
        t = np.arange(int(len(x) * SR / sr)) * sr / SR
        x = np.stack([np.interp(t, np.arange(len(x)), x[:, c]) for c in range(ch)], axis=1)
    return x

REPORT = []
# SE は全体を 5.2dB 下げて書き出す(ゲーム側の SE の基本音量 0.55 → 1.0 で戻す)。
# 大きい音(FINISH/BOSS FINISH)にピークの余裕を残し、段の上のほうが頭打ちにならないようにするため。
SE_OFFSET = -5.2
def se(name, x, db):
    x = np.asarray(x, dtype=np.float64)
    pre = np.abs(x).max() * 10 ** ((db + SE_OFFSET) / 20) / (short_loud(x) + 1e-12)
    x = fade_tail(normalize(x, db + SE_OFFSET)); write(f'{OUT}/SE/{name}.wav', x)
    REPORT.append((name + (f'  (limited, peak would be {20 * math.log10(pre):+.1f}dB)' if pre > 0.97 else ''), db, len(x) / SR))

def amb(name, x, db, long=False):
    x = normalize(x, db, long=long); write(f'{OUT}/Ambience/{name}.wav', x)
    REPORT.append((name, db, len(x) / SR))

def pitch_copy(x, ratio):
    t = np.arange(int(len(x) / ratio)) * ratio
    return np.interp(t, np.arange(len(x)), x)

# Loudness ladder (dBFS, loudest 50 ms). The final in-game level also gets the per-entry volume in AudioLibrary.
L_MOVE, L_SWING, L_HIT, L_HIT_STRONG, L_SLAM, L_FINISH, L_BOSS_FINISH = -21, -18, -15, -11.5, -10, -8, -6.5
L_UI, L_UI_SOFT, L_CUE, L_CUE_BIG = -16, -19, -12, -9

def gen_se():
    # ===== Player: movement =====
    se('mv_jump', mix(whoosh(0.16, 600, 2600, 0.7), tone([520, 780], note=0.03, decay=0.05, kind='tri', vol=0.25)), L_MOVE)
    se('mv_double_jump', mix(whoosh(0.22, 900, 4200, 0.8), tone([700, 1050, 1400], note=0.03, decay=0.06, kind='tri', vol=0.22)), L_MOVE + 1)
    se('mv_land', mix(thump(95, 0.12, 0.8), lp_fast(noise(0.1), 1200) * env_exp(int(0.1 * SR), 40) * 0.6), L_MOVE - 1)
    se('mv_land_heavy', mix(thump(55, 0.35, 1.4), lp_fast(noise(0.35), 500) * env_exp(int(0.35 * SR), 9) * 2.5, crack(0.06, 0.4)), L_SLAM)
    # ===== Player: swings (variations per weapon) =====
    for i, (f0, f1) in enumerate([(700, 3600), (900, 4200)]):
        se(f'atk_sword_{i}', mix(whoosh(0.18, f0, f1, 1.0), at(0.05, tone([1700 + 200 * i], decay=0.06, vol=0.12))), L_SWING)
    se('atk_sword_finisher', mix(whoosh(0.3, 450, 3800, 1.1, low=True), at(0.1, tone([1500, 2250], note=0.01, decay=0.2, vol=0.22))), L_SWING + 2)
    for wpn in ['dual_blade', 'gun', 'bow', 'magic', 'strike', 'special']:
        p = f'{OUT}/SE/atk_{wpn}.wav'
        if os.path.exists(p):
            x = load(p)[:, 0]
            se(f'atk_{wpn}_1', pitch_copy(x, 1.07), L_SWING + (-2 if wpn == 'gun' else 0))
            se(f'atk_{wpn}_0', x, L_SWING + (-2 if wpn == 'gun' else 0))
        ps = f'{OUT}/SE/atk_{wpn}_strong.wav'
        if os.path.exists(ps): se(f'atk_{wpn}_strong_n', load(ps)[:, 0], L_SWING + 2)
    # ---- 武器の系統を増やす(2026-10-06): ランス/忍者/爪(竜人)/吸血鬼/巫女(霊術) ----
    for i, (f0, f1) in enumerate([(1200, 5200), (1400, 6000)]):   # ランス: 鋭い突き + 金属の響き
        se(f'atk_lance_{i}', mix(whoosh(0.14, f0, f1, 1.0), at(0.06, tone([2100 + 150 * i, 3150 + 150 * i], note=0.0, decay=0.08, vol=0.18))), L_SWING)
    se('atk_lance_strong', mix(whoosh(0.32, 500, 6000, 1.2, low=True), at(0.14, mix(thump(70, 0.25, 1.0), tone([1600, 2400], note=0.0, decay=0.2, vol=0.2)))), L_SWING + 2)
    for i in range(2):   # 忍者: 軽く速い、高い
        se(f'atk_ninja_{i}', mix(whoosh(0.09, 2500 + 300 * i, 8000, 0.9), at(0.04, whoosh(0.08, 3000, 8500, 0.6))), L_SWING - 1)
    se('atk_ninja_strong', mix(whoosh(0.12, 2000, 8000, 0.9), at(0.06, whoosh(0.12, 2400, 8500, 0.8)), at(0.12, whoosh(0.14, 2800, 9000, 0.8)), at(0.16, tone([2600], decay=0.06, vol=0.15))), L_SWING + 1)
    def claw(n_streaks, base):   # 爪: 引き裂く
        parts = []
        for k in range(n_streaks):
            d = 0.07; nn = int(d * SR)
            x = hp_fast(lp_fast(noise(d), base * 2), base) * np.sin(np.linspace(0, math.pi, nn)) * (0.7 + 0.3 * np.sin(2 * math.pi * 90 * np.arange(nn) / SR))
            parts.append(at(k * 0.035, x))
        return mix(*parts)
    se('atk_claw_0', mix(claw(3, 900), whoosh(0.16, 600, 3000, 0.5)), L_SWING)
    se('atk_claw_1', mix(claw(3, 1100), whoosh(0.16, 700, 3400, 0.5)), L_SWING)
    se('atk_claw_strong', mix(claw(4, 700), whoosh(0.3, 300, 2600, 0.9, low=True), at(0.12, thump(60, 0.3, 1.2))), L_SWING + 2)
    flap = lambda: lp_fast(noise(0.06), 1500) * np.hanning(int(0.06 * SR))   # 吸血鬼: 暗い振り + 低いうねり + 羽ばたき
    se('atk_blood_0', mix(whoosh(0.2, 400, 2500, 0.9, low=True), sweep(90, 60, 0.25, 'saw') * env_exp(int(0.25 * SR), 8) * 0.15, at(0.05, flap())), L_SWING)
    se('atk_blood_1', mix(whoosh(0.2, 450, 2800, 0.9, low=True), sweep(100, 65, 0.25, 'saw') * env_exp(int(0.25 * SR), 8) * 0.15, at(0.07, flap())), L_SWING)
    se('atk_blood_strong', mix(whoosh(0.4, 250, 3000, 1.1, low=True), sweep(70, 40, 0.6, 'saw') * env_exp(int(0.6 * SR), 4) * 0.25, at(0.1, flap()), at(0.2, flap()), at(0.25, tone([311, 466], note=0.0, decay=0.3, kind='tri', vol=0.2))), L_SWING + 2)
    for i, b in enumerate([1568, 1760]):   # 巫女: 鈴 + 札の振り
        se(f'atk_spirit_{i}', mix(whoosh(0.16, 1500, 6000, 0.5), at(0.03, g.bell(b, 0.35) * 0.4)), L_SWING)
    se('atk_spirit_strong', mix(whoosh(0.3, 1000, 7000, 0.7), at(0.05, mix(g.bell(1175, 0.6) * 0.4, g.bell(1760, 0.6) * 0.3)), at(0.15, shimmer(2000, 5, 1.12, 0.03, 0.2, 0.3))), L_SWING + 2)
    se('atk_up_n', load(f'{OUT}/SE/atk_up_launch.wav')[:, 0], L_SWING + 1)
    se('atk_air_n', load(f'{OUT}/SE/atk_air.wav')[:, 0], L_SWING)
    se('atk_down_n', load(f'{OUT}/SE/atk_down_slam.wav')[:, 0], L_SWING + 1)
    # ===== Hits: normal < strong < slam < FINISH < BOSS FINISH =====
    for i in range(3):
        f = [180, 210, 160][i]
        se(f'hit_{i}', mix(crack(0.05, 1.0), thump(f, 0.1, 0.9), tone([f * 6 + 90 * i], decay=0.03, vol=0.15)), L_HIT)
    se('hit_strong_n', mix(thump(60, 0.3, 1.5), crack(0.09, 1.2), lp_fast(noise(0.3), 800) * env_exp(int(0.3 * SR), 14)), L_HIT_STRONG)
    se('hit_launch', mix(crack(0.06, 1.0), thump(120, 0.15, 1.0), at(0.02, whoosh(0.28, 500, 5200, 0.8))), L_HIT_STRONG + 1)
    se('hit_projectile_0', mix(crack(0.04, 0.9), thump(240, 0.07, 0.6)), L_HIT - 1.5)
    se('hit_projectile_1', mix(crack(0.04, 0.9), thump(300, 0.06, 0.6), tone([1800], decay=0.03, vol=0.1)), L_HIT - 1.5)
    se('hit_slam', mix(thump(48, 0.45, 1.7), crack(0.08, 0.8), lp_fast(noise(0.5), 400) * env_exp(int(0.5 * SR), 7) * 3), L_SLAM)
    fin = mix(thump(45, 0.6, 1.8), crack(0.12, 1.4), tone([660, 990, 1320], note=0.0, decay=0.35, vol=0.28), lp_fast(noise(0.5), 1400) * env_exp(int(0.5 * SR), 10) * 1.4)
    se('finish_hit', reverb(fin, 0.6, 0.22), L_FINISH)
    se('finish_burst', mix(shimmer(1400, 7, 1.12, 0.022, 0.18, 0.5), hp_fast(noise(0.35), 3000) * env_exp(int(0.35 * SR), 12) * 0.5), L_HIT_STRONG + 1)
    bfin = mix(thump(36, 0.9, 2.2), thump(70, 0.4, 1.0), crack(0.16, 1.6), tone([440, 660, 880, 1320], note=0.0, decay=0.6, vol=0.3), lp_fast(noise(0.9), 600) * env_exp(int(0.9 * SR), 5) * 3)
    se('boss_finish_hit', reverb(bfin, 1.0, 0.28), L_BOSS_FINISH)
    se('boss_collapse', mix(lp_fast(noise(1.2), 220) * env_exp(int(1.2 * SR), 3.2) * 6, thump(40, 0.6, 1.4), at(0.25, thump(55, 0.4, 1.0))), L_SLAM)
    se('boss_dissolve', mix(sweep(500, 1600, 1.2, 'sin') * np.sin(np.linspace(0, math.pi, int(1.2 * SR))) * 0.4, shimmer(900, 9, 1.1, 0.08, 0.35, 0.35)), L_CUE - 2)
    # ===== Enemies =====
    se('enemy_telegraph', mix(sweep(500, 1300, 0.22, 'tri') * env_exp(int(0.22 * SR), 4) * 0.5, whoosh(0.18, 2000, 5000, 0.3)), L_HIT - 1)
    se('enemy_shot', mix(sweep(1400, 500, 0.14, 'sq') * env_exp(int(0.14 * SR), 18) * 0.35, crack(0.03, 0.4)), L_SWING)
    se('enemy_defeat_0', mix(thump(140, 0.15, 0.8), shimmer(900, 4, 1.25, 0.025, 0.1, 0.4), hp_fast(noise(0.15), 2000) * env_exp(int(0.15 * SR), 25) * 0.5), L_HIT)
    se('enemy_defeat_1', mix(thump(120, 0.15, 0.8), shimmer(1100, 4, 1.22, 0.025, 0.1, 0.4), hp_fast(noise(0.15), 2000) * env_exp(int(0.15 * SR), 25) * 0.5), L_HIT)
    # ===== Bosses: telegraphs must be heard =====
    se('boss_telegraph', mix(sweep(140, 420, 0.5, 'saw', 0.7) * np.linspace(0.2, 1, int(0.5 * SR)) * 0.25, at(0.38, tone([1900, 2850], note=0.0, decay=0.12, vol=0.45)), lp_fast(noise(0.5), 900) * np.linspace(0, 1, int(0.5 * SR)) * 0.6), L_CUE)
    se('boss_telegraph_heavy', mix(sweep(70, 260, 0.75, 'saw', 0.6) * np.linspace(0.2, 1, int(0.75 * SR)) * 0.3, at(0.6, mix(thump(60, 0.25, 1.2), tone([1500, 2250], decay=0.15, vol=0.4))), lp_fast(noise(0.75), 600) * np.linspace(0, 1, int(0.75 * SR))), L_CUE_BIG)
    se('boss_charge', mix(sweep(200, 1200, 0.9, 'saw', 1.4) * np.linspace(0.1, 1, int(0.9 * SR)) * 0.2, sweep(203, 1215, 0.9, 'saw', 1.4) * np.linspace(0.1, 1, int(0.9 * SR)) * 0.2), L_CUE)
    se('boss_break', mix(crack(0.2, 1.5), shimmer(2200, 6, 0.9, 0.02, 0.2, 0.5), thump(80, 0.3, 1.0)), L_CUE_BIG)
    se('boss_phase', mix(lp_fast(load(f'{OUT}/SE/boss_attack.wav')[:, 0], 1000), at(0.15, thump(40, 0.7, 1.8))), L_CUE_BIG)
    se('boss_ultimate', mix(tone([440, 466, 440, 466], note=0.12, decay=0.1, kind='saw', vol=0.15), sweep(80, 300, 1.2, 'saw', 0.5) * 0.25, at(0.9, thump(45, 0.5, 1.6))), L_CUE_BIG)
    se('boss_warning_n', tone([880, 660, 880, 660], note=0.25, decay=0.18, kind='tri', vol=0.3), L_CUE)
    # ===== Cards / COMBO / ULTIMATE / fusion =====
    se('card_appear', mix(whoosh(0.3, 1500, 7000, 0.5), shimmer(1200, 5, 1.15, 0.04, 0.2, 0.35)), L_UI)
    se('card_flip', mix(hp_fast(noise(0.07), 2000) * env_exp(int(0.07 * SR), 50), tone([1600], decay=0.03, vol=0.15)), L_UI_SOFT)
    se('card_hover', tone([1760], decay=0.03, kind='tri', vol=0.4), L_UI_SOFT - 2)
    se('card_get', mix(tone([784, 988, 1175, 1568], note=0.05, decay=0.3, vol=0.45), shimmer(2000, 5, 1.12, 0.03, 0.2, 0.25)), L_CUE)
    se('card_rare', mix(tone([659, 831, 988, 1319, 1661, 1976], note=0.05, decay=0.45, kind='tri', vol=0.4), whoosh(0.5, 1000, 8000, 0.4)), L_CUE)
    se('fusion_charge', mix(sweep(200, 1400, 1.1, 'sin', 1.5) * np.linspace(0.2, 1, int(1.1 * SR)) * 0.4, hp_fast(noise(1.1), 3000) * np.linspace(0, 0.5, int(1.1 * SR))), L_CUE - 2)
    se('fusion_success', reverb(mix(thump(70, 0.3, 1.2), tone([523, 659, 784, 1047, 1319, 1568], note=0.04, decay=0.5, vol=0.4), shimmer(1800, 8, 1.1, 0.03, 0.3, 0.3)), 0.6, 0.2), L_CUE_BIG)
    se('fusion_fail', tone([392, 370, 311], note=0.12, decay=0.25, kind='tri', vol=0.4), L_CUE - 2)
    se('mastery_up', mix(tone([587, 740, 880, 1175], note=0.06, decay=0.4, kind='tri', vol=0.4), shimmer(1500, 6, 1.12, 0.035, 0.25, 0.25)), L_CUE)
    se('max_level', reverb(mix(tone([523, 784, 1047, 1568, 2093], note=0.07, decay=0.6, vol=0.4), thump(60, 0.3, 1.0)), 0.7, 0.25), L_CUE_BIG)
    se('combo_formed', mix(tone([659, 988], note=0.0, decay=0.3, kind='saw', vol=0.15), tone([1319, 1976], note=0.06, decay=0.25, vol=0.4), crack(0.05, 0.3)), L_CUE)
    se('fe_ready', mix(sweep(300, 900, 0.6, 'tri') * np.linspace(0.3, 1, int(0.6 * SR)) * 0.3, at(0.5, tone([1175, 1760], note=0.05, decay=0.4, vol=0.4))), L_CUE)
    se('fe_activate', reverb(mix(thump(45, 0.6, 1.8), sweep(200, 2000, 0.6, 'saw', 0.5) * env_exp(int(0.6 * SR), 3) * 0.2, tone([523, 784, 1047, 1568], note=0.03, decay=0.6, vol=0.35)), 0.8, 0.25), L_CUE_BIG)
    se('ult_ready', mix(tone([1047, 1568, 2093], note=0.05, decay=0.3, kind='tri', vol=0.4), hp_fast(noise(0.3), 4000) * env_exp(int(0.3 * SR), 10) * 0.3), L_CUE - 1)
    se('ult_activate', reverb(mix(sweep(120, 900, 0.7, 'saw', 0.6) * np.linspace(0.3, 1, int(0.7 * SR)) * 0.25, at(0.6, mix(thump(40, 0.7, 2.0), crack(0.12, 1.0)))), 0.9, 0.25), L_FINISH)
    se('ult_impact', mix(thump(42, 0.6, 2.0), crack(0.14, 1.4), lp_fast(noise(0.6), 900) * env_exp(int(0.6 * SR), 6) * 2), L_FINISH + 0.5)
    # ===== Sprint ring / MILE / speed =====
    se('ring_pass', mix(tone([1568, 2349], note=0.03, decay=0.18, vol=0.45), whoosh(0.15, 2000, 6000, 0.3)), L_UI)
    se('ring_burst', mix(tone([1047, 1319, 1568, 2093], note=0.03, decay=0.3, vol=0.4), hp_fast(noise(0.25), 3000) * env_exp(int(0.25 * SR), 14) * 0.4), L_CUE)
    se('mile_get', tone([1976, 2637, 3136], note=0.045, decay=0.2, vol=0.45), L_UI)
    se('sonic_boom', mix(thump(50, 0.4, 1.6), lp_fast(noise(0.6), 700) * env_exp(int(0.6 * SR), 6) * 2.5, crack(0.08, 0.6)), L_HIT_STRONG)
    # ===== UI =====
    se('ui_tap', tone([2200], decay=0.025, kind='tri', vol=0.4), L_UI_SOFT - 1)
    se('ui_decide_n', tone([1320, 1760], note=0.04, decay=0.06, vol=0.5), L_UI)
    se('ui_cancel_n', tone([880, 660], note=0.05, decay=0.07, vol=0.45), L_UI - 1)
    se('ui_open', mix(whoosh(0.2, 800, 4000, 0.5), tone([990, 1320], note=0.04, decay=0.06, vol=0.2)), L_UI_SOFT)
    se('ui_close', mix(whoosh(0.18, 3500, 700, 0.5), tone([1320, 990], note=0.04, decay=0.06, vol=0.2)), L_UI_SOFT - 1)
    se('ui_tab', tone([1480, 1760], note=0.025, decay=0.04, kind='tri', vol=0.4), L_UI_SOFT)
    se('ui_toggle', tone([1100, 1650], note=0.02, decay=0.03, vol=0.4), L_UI_SOFT)
    se('ui_deny', tone([220, 208], note=0.07, decay=0.08, kind='sq', vol=0.25), L_UI)
    se('ui_slider', tone([2600], decay=0.012, vol=0.4), L_UI_SOFT - 5)
    se('ui_pause', mix(tone([1047, 784], note=0.05, decay=0.1, kind='tri', vol=0.4), whoosh(0.2, 3000, 800, 0.2)), L_UI)
    se('ui_resume', mix(tone([784, 1047], note=0.05, decay=0.1, kind='tri', vol=0.4), whoosh(0.2, 800, 3000, 0.2)), L_UI)
    se('ui_countdown_n', tone([880], decay=0.1, kind='tri', vol=0.4), L_UI)
    # ===== Arena =====
    gong = lambda f: mix(*(np.sin(2 * math.pi * f * r * np.arange(int(2.0 * SR)) / SR) * env_exp(int(2.0 * SR), 1.8 + r) * a for r, a in [(1, 1), (2.4, 0.5), (3.9, 0.3), (5.6, 0.15)]))
    se('arena_start', mix(gong(110), thump(50, 0.4, 1.0)), L_CUE_BIG)
    se('arena_win', reverb(mix(tone([523, 659, 784, 1047], note=0.09, decay=0.4, kind='sq', vol=0.18), at(0.36, tone([1047, 1319, 1568], note=0.0, decay=0.7, kind='tri', vol=0.35))), 0.8, 0.2), L_CUE_BIG)
    se('arena_lose', tone([440, 415, 392, 330], note=0.16, decay=0.35, kind='tri', vol=0.4), L_CUE)
    # ===== Multiplayer =====
    se('net_join', tone([784, 1175], note=0.07, decay=0.2, kind='tri', vol=0.45), L_UI)
    se('net_leave', tone([1175, 784], note=0.07, decay=0.2, kind='tri', vol=0.4), L_UI - 1)
    se('net_down', mix(tone([523, 392, 330], note=0.1, decay=0.25, kind='saw', vol=0.15), thump(70, 0.3, 0.8)), L_CUE)
    se('net_revive', mix(tone([523, 659, 784, 1047], note=0.06, decay=0.35, vol=0.4), shimmer(1500, 5, 1.15, 0.04, 0.25, 0.3)), L_CUE)
    # ===== Old pack, normalized copies (kept as the base character of the game) =====
    old = ROOT + '/SE'
    se('old_jump_n', load(old + '/JumpSe.wav')[:, 0], L_MOVE)
    se('old_double_jump_n', load(old + '/DoubleJumpSe.wav')[:, 0], L_MOVE + 1)
    se('old_landing_n', load(old + '/05_landing.wav')[:, 0], L_MOVE - 1)
    se('old_attack_hit_n', load(old + '/01_attack_hit.wav')[:, 0], L_HIT)
    se('old_player_damage_n', load(old + '/02_player_damage.wav')[:, 0], L_CUE)
    se('old_enemy_defeat_n', load(old + '/03_enemy_defeat.wav')[:, 0], L_HIT)
    se('old_player_death_n', load(old + '/04_player_death.wav')[:, 0], L_CUE_BIG)
    for i, n in enumerate(['AttackSe1', 'AttackSe2', 'AttackSe3']): se(f'old_{n}_n', load(old + f'/{n}.wav')[:, 0], L_SWING + (1 if i == 2 else 0))
    for n, db in [('CardSelectSe', L_UI), ('CardConfirmSe', L_CUE), ('CardDeckAppearSe', L_UI), ('CardDrawSe', L_UI_SOFT), ('CardFlipSe', L_UI_SOFT)]:
        se(f'old_{n}_n', load(old + f'/{n}.wav')[:, 0], db)
    # ===== Existing placeholders that were too loud / too quiet =====
    for n, db in [('boss_hit', L_HIT_STRONG), ('boss_final_hit', L_FINISH), ('boss_defeat', L_SLAM), ('boss_attack', L_CUE), ('boss_appear', L_CUE_BIG),
                  ('enemy_attack', L_SWING), ('enemy_attack_big', L_HIT_STRONG), ('milestone', L_CUE), ('milestone_clear', L_CUE), ('level_up', L_CUE),
                  ('ui_stage_select', L_UI), ('ui_character_select', L_UI), ('ui_run_go', L_CUE), ('ui_screen_whoosh', L_UI_SOFT),
                  ('home_door', L_UI), ('home_deck_edit', L_UI_SOFT), ('home_gacha', L_UI), ('home_coin', L_UI)]:
        se(n + '_n', load(f'{OUT}/SE/{n}.wav')[:, 0], db)

def gen_ambient():
    # High-speed wind: broadband wind with a gust LFO, looped (volume/pitch follow the run speed in-game)
    n = int(12 * SR); extra = int(1.5 * SR)
    x = hp_fast(lp_fast(rng.standard_normal(n + extra), 2600), 250)
    t = np.arange(n + extra) / SR
    x *= 0.75 + 0.25 * np.sin(2 * math.pi * 0.31 * t) * np.sin(2 * math.pi * 0.11 * t + 1.0)
    head = x[:extra]; tail = x[n:n + extra]; w = np.linspace(0, 1, extra)
    y = x[:n].copy(); y[:extra] = head * w + tail * (1 - w)
    amb('amb_speed_wind', y, -16, long=True)
    # Per-map one-shots
    hawk_n = int(1.3 * SR); ht = np.arange(hawk_n) / SR
    hawk = np.sin(2 * math.pi * np.cumsum(2400 + 900 * np.exp(-ht * 3) * np.sin(math.pi * ht / 1.3)) / SR) * np.sin(math.pi * ht / 1.3) ** 2 * (0.7 + 0.3 * np.sin(2 * math.pi * 40 * ht))
    amb('amb_hawk', reverb(lp_fast(hawk, 5000), 0.8, 0.35), -19)
    amb('amb_chime', reverb(tone([1568, 2093, 2637, 3136], note=0.18, decay=0.6, vol=0.4), 1.0, 0.3), -21)
    flut = np.concatenate([hp_fast(noise(0.03), 1500) * np.hanning(int(0.03 * SR)) for _ in range(9)])
    amb('amb_bat_flutter', flut * np.linspace(1, 0.4, len(flut)), -21)
    clank = mix(tone([620, 1310, 2290], note=0.0, decay=0.25, vol=0.5), crack(0.04, 0.6), at(0.22, mix(tone([590, 1250], decay=0.2, vol=0.35), crack(0.03, 0.4))))
    amb('amb_chain', reverb(clank, 1.0, 0.35), -21)
    swell_n = int(4 * SR)
    swell = mix(np.sin(2 * math.pi * 55 * np.arange(swell_n) / SR), 0.5 * np.sin(2 * math.pi * 82.4 * np.arange(swell_n) / SR)) * np.sin(np.linspace(0, math.pi, swell_n)) ** 2
    amb('amb_drone_swell', swell + lp_fast(rng.standard_normal(swell_n), 200) * np.sin(np.linspace(0, math.pi, swell_n)) ** 2 * 2, -20)
    thunder_n = int(3 * SR)
    amb('amb_far_thunder', lp_fast(rng.standard_normal(thunder_n), 150) * env_exp(thunder_n, 1.5) * (1 + 0.6 * np.sin(2 * math.pi * 3 * np.arange(thunder_n) / SR)), -20)
    # Existing ambient: loops to a common level, one-shots slightly under the loops
    for nm in ['amb_sky_wind', 'amb_wasteland_wind', 'amb_cave_air', 'amb_home_breeze', 'amb_last_hall']:
        amb(nm + '_n', load(f'{OUT}/Ambience/{nm}.wav')[:, 0], -17, long=True)
    for nm in ['amb_bird_0', 'amb_bird_1', 'amb_bird_2', 'amb_drip_0', 'amb_drip_1', 'amb_rock_far', 'amb_grass_rustle', 'amb_wind_whistle']:
        amb(nm + '_n', load(f'{OUT}/Ambience/{nm}.wav')[:, 0], -20)

def tame_piercing(x, name):
    # 一つの音(高い楽器の同じ音程)だけが耳につく曲: 1〜8kHz で周りより極端に強い帯域(中央値の10倍超、全体の2.5%超)を
    # その帯域だけ下げる(-6dB、なだらかに)。曲の雰囲気はそのまま、突き刺さる1音だけを丸くする
    m = x.mean(axis=1)
    X = np.abs(np.fft.rfft(m)) ** 2; f = np.fft.rfftfreq(len(m), 1 / SR); tot = X.sum()
    edges = np.geomspace(1000, 8000, 19)
    bands = [X[(f >= edges[i]) & (f < edges[i + 1])].sum() for i in range(18)]
    med = np.median(bands); cuts = []
    for i, b in enumerate(bands):
        if b > med * 10 and b / tot > 0.025: cuts.append((edges[i], edges[i + 1]))
    if not cuts: return x
    g = np.ones(len(f))
    for a, b in cuts:
        c = math.sqrt(a * b); w = math.log(b / a)
        g *= 1 - (1 - 10 ** (-6 / 20)) * np.exp(-0.5 * (np.log(np.maximum(f, 1) / c) / (w * 0.8)) ** 2)
    print('tame', name, ['%d-%dHz' % (a, b) for a, b in cuts])
    return np.stack([np.fft.irfft(np.fft.rfft(x[:, c]) * g, n=len(x)) for c in range(x.shape[1])], axis=1)

def gen_bgm(songs=True):
    if songs: gen_new_songs()
    normalize_bgm()

def gen_new_songs():
    # Final boss (last-dungeon rush / the final): heavy, choir-like pad, harmonic minor
    song('bgm_boss_final', 158, 50, 'harmonic', [0, 5, 1, 4, 0, 3, 6, 4], bars=16, drums='heavy', arp='16th', pad_bright=1900, bass_pat='drive', lead_kind='saw', bells=True)
    # Arena: competitive, bright, punchy
    song('bgm_arena', 144, 60, 'dorian', [0, 6, 3, 4], bars=16, drums='drive', arp='16th', pad_bright=1700, bass_pat='drive', lead_kind='sq')

def normalize_bgm():
    # All BGM to a common loudness (gain written as a new file, original untouched)
    for p in sorted(glob.glob(f'{OUT}/Bgm/bgm_*.wav')):
        if p.endswith('_n.wav'): continue
        x = tame_piercing(load(p), os.path.basename(p))
        y = normalize(x, -16, long=True)
        write(p[:-4] + '_n.wav', y.reshape(-1), stereo=True)
        REPORT.append((os.path.basename(p)[:-4] + '_n', -16, len(y) / SR))

if __name__ == '__main__':
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if what in ('all', 'se'): gen_se()
    if what in ('all', 'amb'): gen_ambient()
    if what in ('all', 'bgm'): gen_bgm()
    if what == 'bgmnorm': gen_bgm(songs=False)   # 曲は作り直さず、大きさ/耳につく帯域だけやり直す
    for n, db, s in REPORT: print(f'{n:28s} {db:6.1f} dB {s:5.2f}s')
    print(len(REPORT), 'files')
