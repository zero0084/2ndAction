# 双剣士(2026-10-28): エステルが描き直した手(hands_v2)だけを元の絵へ移す(手以外の画素は元のまま)
import numpy as np
from PIL import Image
from scipy import ndimage
a = np.array(Image.open("db_pose_v2.png").convert("RGB")).astype(float)
b = np.array(Image.open("f28_dual_blade_hands_v2.png").convert("RGB")).astype(float)
H, W = a.shape[:2]; yy, xx = np.mgrid[:H, :W]
m = np.zeros((H, W))
for cx, cy, rx, ry in ((118, 398, 50, 46), (474, 680, 54, 38)):
    m = np.maximum(m, (((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 < 1).astype(float))
m[(xx > 486) & (yy > 700)] = 0            # 拳の下に出た親指のようなかたまりは使わない(元の帯/肌のまま)
m = ndimage.gaussian_filter(m, 3)
o = b * m[..., None] + a * (1 - m[..., None])
Image.fromarray(o.astype(np.uint8)).save("db_pose_v3.png")
r1 = (40, 300, 260, 500); r2 = (380, 580, 600, 760)
S = Image.new("RGB", (880, 800)); A = Image.fromarray(a.astype(np.uint8)); O = Image.fromarray(o.astype(np.uint8))
for j, im in enumerate([A, O]):
    S.paste(im.crop(r1).resize((440, 400)), (440 * j, 0)); S.paste(im.crop(r2).resize((440, 360)), (440 * j, 420))
S.save("hv3z.jpg", quality=92)
