# 洞窟の背景の絵だけを持ち上げる(2026-11-05、エステル: 遠景にだけ掛かる暗闇の幕を弱めたのと同じ見え方に)。
#   python -I lift.py <入力png> <出力png> <ガンマ(1未満で暗い所ほど持ち上がる。0.85 ≒ 幕 85%)>
#  洞窟の暗闇の幕(CaveLighting)は画面全体に掛かる1枚の幕なので、背景だけ弱めることはできない → 背景の絵の暗い所を持ち上げる。
#  明るい所はほとんど変えない(結晶の光が白飛びしない)。
import sys
import numpy as np
from PIL import Image
src, dst, g = sys.argv[1], sys.argv[2], float(sys.argv[3])
a = np.asarray(Image.open(src).convert('RGB'), dtype=np.float64) / 255.0
out = np.power(a, g)
Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8)).save(dst)
