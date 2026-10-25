"""多角形の中だけ新しい絵、外は元の絵(縁はぼかす)。布と同じ色の体(竜人の翼・鱗)がある時の最後の仕上げ(2026-10-25)。
usage: clothpoly.py <元.png> <新しい絵.png> <出力.png> <x1,y1,x2,y2,...> [--feather 3]"""
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
a = sys.argv[1:]; f = float(a[a.index('--feather') + 1]) if '--feather' in a else 3.0
O = np.array(Image.open(a[0]).convert('RGBA')).astype(float); N = np.array(Image.open(a[1]).convert('RGBA')).astype(float)
v = list(map(float, a[3].split(','))); im = Image.new('L', (O.shape[1], O.shape[0]), 0); ImageDraw.Draw(im).polygon(list(zip(v[0::2], v[1::2])), fill=255)
m = ndimage.gaussian_filter((np.array(im) > 0).astype(float), f)[..., None]
out = O * (1 - m) + N * m
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(a[2]); print(a[2])
