"""エステルに渡した並べた絵(入力)と、返ってきた絵(出力)を上下に並べて1枚に(確認用、2026-10-26)。
usage: clothio.py <入力.png> <出力.png> <out.jpg> [幅=1100]"""
import sys
from PIL import Image
a = Image.open(sys.argv[1]).convert('RGB'); b = Image.open(sys.argv[2]).convert('RGB'); w = int(sys.argv[4]) if len(sys.argv) > 4 else 1100
a = a.resize((w, int(a.height * w / a.width))); b = b.resize((w, int(b.height * w / b.width)))
M = Image.new('RGB', (w, a.height + b.height + 6), (40, 40, 40)); M.paste(a, (0, 0)); M.paste(b, (0, a.height + 6)); M.save(sys.argv[3], quality=88)
