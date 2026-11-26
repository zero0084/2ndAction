# OneMoreMile SE 改修 (2026-11-26 依頼②): 戦闘中にときどき鳴る「ポヨーン」を、力強く重みのある音へ。
#  対象(調査: 鳴る条件と音の形):
#   ・player_damage(敵の攻撃が自分に当たった時。PlayerController.ApplyDamageReaction)
#       旧 old_player_damage_n = 外部素材の 110→64Hz へ下がる澄んだ音 =「ボヨーン/ポヨーン」→ 体への重い打撃(低い芯+打撃の破裂+ざらつき)
#   ・enemy_telegraph(雑魚の攻撃の予兆。EnemySpecialBehavior の突進/近接/急降下/跳ね/ワーム、道中の大群の知らせ)
#       旧 = 500→1300Hz へ上がる三角波 =「ピュイーン」→ 力を溜める低いうなり + 構えの金属の擦れ(音程の上下しない音)
#  すべてこのスクリプトで合成した音(外部の素材・サンプルは使っていない)。numpy だけ(scipy 不要)。
#  既存のファイルは上書きしない: *_v4.wav で書き出し、AudioLibrary の割り当てだけを替える(戻す時は割り当てを元の名前へ)。
#  大きさは gen_audio_v2.py と同じ段(SE_OFFSET 込み)。旧と同じ段にそろえる(大きさで目立たせない。音色だけ替える)
#   使い方: python gen_audio_v4.py
import os, sys, math
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
import gen_audio_v2 as v2
from gen_audio_v2 import SR, se, mix, at, z, env_exp, REPORT, L_CUE, L_HIT
from gen_audio import lp_fast, hp_fast

rng = np.random.default_rng(20261126)
def noise(sec): return rng.standard_normal(int(sec * SR))
def tt(sec): return np.arange(int(sec * SR)) / SR

def body_thump(sec=0.3, f_hi=170.0, f_lo=52.0, drop=0.055, tau=0.09, level=1.0):
    # 打撃の芯: 一瞬だけ高く始まり、すぐ低い所へ落ちて止まる(音程が長く滑らない = 「ポヨーン」にしない)
    t = tt(sec)
    f = f_lo + (f_hi - f_lo) * np.exp(-t / drop * 3.0)
    ph = 2 * math.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t / tau) * level

def burst(sec, lo_cut, hi_cut, tau, level):
    x = noise(sec)
    if lo_cut > 0: x = hp_fast(x, lo_cut)
    if hi_cut > 0: x = lp_fast(x, hi_cut)
    return x * np.exp(-tt(sec) / tau) * level

def player_damage_v4():
    punch = burst(0.05, 0, 2200, 0.012, 1.0)              # 当たった瞬間の破裂
    crunch = burst(0.22, 350, 1500, 0.05, 0.45)            # 鈍いざらつき(骨/鎧に響く)
    core = body_thump(0.32, 170, 52, 0.05, 0.085, 1.1)     # 体に響く低い芯
    sub = np.sin(2 * math.pi * 46 * tt(0.28)) * np.exp(-tt(0.28) / 0.11) * 0.45
    x = mix(punch, crunch, core, sub)
    return np.tanh(x * 1.6) / np.tanh(1.6)                 # 軽く飽和させて「重さ」を出す

def enemy_telegraph_v4():
    n = int(0.26 * SR)
    swell = np.clip(np.arange(n) / (0.18 * SR), 0, 1) ** 1.6
    rumble = lp_fast(noise(0.26), 320) * swell * 1.3        # 力を溜める低いうなり(音程なし)
    t = tt(0.26)
    growl = np.sign(np.sin(2 * math.pi * 72 * t)) * 0.5     # 低い角ばった唸り(上下しない)
    growl = lp_fast(growl, 260) * swell * 0.6
    scrape = burst(0.08, 2500, 7000, 0.03, 0.22)            # 構えの擦れ(短く・小さく)
    brace = mix(burst(0.04, 0, 900, 0.01, 0.7), body_thump(0.12, 120, 60, 0.03, 0.04, 0.6))  # 構え切った「ドッ」
    x = mix(rumble, growl, at(0.1, scrape), at(0.17, brace))
    return np.tanh(x * 1.4) / np.tanh(1.4)

def gen():
    se('player_damage_v4', player_damage_v4(), L_CUE)        # 旧 old_player_damage_n と同じ段
    se('enemy_telegraph_v4', enemy_telegraph_v4(), L_HIT - 1)  # 旧 enemy_telegraph と同じ段

if __name__ == '__main__':
    gen()
    for n, db, s in REPORT: print(f'{n:28s} {db:6.1f} dB {s:5.2f}s')
