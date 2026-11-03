# 背景の旅の内部の絵(2026-11-03): 横に繰り返して継ぎ目が出ないようにする。
#  右端の o 画素を左端へ重ねて(なめらかに混ぜて)、右端を切る → 左右がつながる。幅と高さを4の倍数にそろえ、不透明のまま保存
#  python loop.py <in.png> <out.png> [--overlap 0.12] [--h 1024]
import sys, numpy as np
from PIL import Image
a = sys.argv[1:]; src, dst = a[0], a[1]
opt = {k: float(v) for k, v in zip(a[2::2], a[3::2])}
ov = opt.get('--overlap', 0.12); H = int(opt.get('--h', 1024))
im = Image.open(src).convert('RGB')
im = im.resize((round(im.width * H / im.height), H), Image.LANCZOS)
x = np.asarray(im).astype(np.float32); W = x.shape[1]; o = int(W * ov)
t = np.linspace(0, 1, o)[None, :, None]; t = t * t * (3 - 2 * t)
x[:, :o] = x[:, W - o:] * (1 - t) + x[:, :o] * t  # 左端は「右端の続き」から始まって元の左端へ
x = x[:, :W - o]
W2 = x.shape[1] // 4 * 4
Image.fromarray(np.clip(x[:, :W2], 0, 255).astype(np.uint8), 'RGB').save(dst)
print(dst, (W2, H))
