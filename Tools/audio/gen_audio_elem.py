# OneMoreMile 属性の発動の SE (2026-10-30): 炎「ボッ」/ 氷「キン」/ 雷「バチッ」/ 風「シュッ」/ 血 低い吸収音。
#  すべてこのスクリプトで合成した音(外部の素材なし)。短く(0.12〜0.3秒)、大きさは命中の音より小さい段(L_MOVE 付近)。
#  ゲーム側でさらに間引く(同じ属性 0.1秒に1回・全属性で 0.25秒に4回まで、ElementVfx.Sfx)。
#   使い方: python gen_audio_elem.py   → Assets/Audio/Placeholder/SE/elem_*_v1.wav(既存は上書きしない名前)
import os, sys, math
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
import gen_audio_v2 as v2
from gen_audio_v2 import SR, se, mix, fade_tail, noise, L_MOVE, L_HIT
from gen_audio_v3 import lp, hp, bp, env_ad, env_bell, tv_bp, soft, reverb

rng = np.random.default_rng(20261030)
L_ELEM = L_MOVE + 1.0   # 移動の音より少し上、命中(L_HIT)より 5dB 下

def fire():   # 「ボッ」: 低めの空気の破裂 + 短いパチパチ
    sec = 0.26; n = int(sec * SR); t = np.arange(n) / SR
    whoomp = bp(noise(sec), 160, 900, 2) * env_ad(n, 0.004, 0.07) * 1.6
    body = np.sin(2 * math.pi * np.cumsum(140 - 60 * t / sec) / SR) * env_ad(n, 0.003, 0.05) * 0.7
    crackle = np.zeros(n)
    for k in range(6):
        st = int(rng.uniform(0.02, 0.18) * SR); d = int(0.004 * SR)
        crackle[st:st + d] += hp(noise(0.004), 2000)[:d] * np.hanning(d) * rng.uniform(0.3, 0.6)
    return fade_tail(lp(mix(whoomp, body, crackle * 0.8), 6000, 2), 0.04)

def ice():    # 「キン」: 高い澄んだ音(倍音が少しずれた鐘)+ 小さな結晶の粒
    sec = 0.3; n = int(sec * SR); t = np.arange(n) / SR
    tone = sum(np.sin(2 * math.pi * f * t) * a for f, a in [(2650, 1.0), (3960, 0.5), (5410, 0.3)]) * env_ad(n, 0.001, 0.08)
    tick = hp(noise(0.01), 4000) * env_ad(int(0.01 * SR), 0.0003, 0.002) * 0.6
    x = mix(tone * 0.5, tick)
    y = reverb(x, 0.3, 0.15); return fade_tail(y[:int(0.32 * SR)], 0.08)

def thunder():  # 「バチッ」: 鋭い放電のノイズの塊(2つ)+ 低い芯
    sec = 0.2; n = int(sec * SR)
    zap = np.zeros(n)
    for st, a in [(0.0, 1.0), (0.035, 0.7)]:
        d = int(0.05 * SR); s0 = int(st * SR)
        buzz = soft(bp(noise(0.05), 1500, 7000, 2) * 3, 2) * env_ad(d, 0.0005, 0.012) * a
        zap[s0:s0 + d] += buzz[:n - s0]
    hum = np.sign(np.sin(2 * math.pi * 120 * np.arange(n) / SR)) * env_ad(n, 0.001, 0.03) * 0.15
    return fade_tail(mix(zap, lp(hum, 1500)), 0.03)

def wind():   # 「シュッ」: 上がっていく風切り
    sec = 0.18; n = int(sec * SR)
    swish = tv_bp(noise(sec), 1200, 5200, 0.7) * env_bell(n, 0.06, 0.035)
    return fade_tail(lp(hp(swish, 500, 2), 10000, 2), 0.03)

def blood():  # 低い吸収音: 「ズゥ」と吸い込む低い音 + 小さなぬめり
    sec = 0.3; n = int(sec * SR); t = np.arange(n) / SR
    f = 90 + 70 * (t / sec)                      # 吸い込む(下から上へ)
    tone = np.sin(2 * math.pi * np.cumsum(f) / SR) * env_bell(n, 0.17, 0.07)
    slurp = bp(noise(sec), 300, 1100, 2) * env_bell(n, 0.12, 0.05) * 0.5
    return fade_tail(lp(soft(mix(tone, slurp) * 1.4, 1.5), 2500, 2), 0.05)

def gen():
    se('elem_fire_v1', fire(), L_ELEM)
    se('elem_ice_v1', ice(), L_ELEM - 1.5)       # 高い音は耳に立つので少し小さく
    se('elem_thunder_v1', thunder(), L_ELEM)
    se('elem_wind_v1', wind(), L_ELEM - 1.0)
    se('elem_blood_v1', blood(), L_ELEM)

if __name__ == '__main__':
    gen()
    for n_, db, s in v2.REPORT: print(f'{n_:20s} {db:6.1f} dB  {s:4.2f}s')
