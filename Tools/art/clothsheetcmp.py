"""並べた絵の 入力 / エステルの出力 を左右に(確認用)。usage: clothsheetcmp.py <入力.png> <出力.png> <out.jpg>"""
import sys
from PIL import Image
a = Image.open(sys.argv[1]).convert('RGB'); b = Image.open(sys.argv[2]).convert('RGB').resize(a.size)
M = Image.new('RGB', (a.width * 2 + 10, a.height)); M.paste(a, (0, 0)); M.paste(b, (a.width + 10, 0)); M.thumbnail((2000, 2000)); M.save(sys.argv[3], quality=85)
