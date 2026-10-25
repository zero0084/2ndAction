"""-qaClothCheck -ccOnly card の5枚(ホーム・キャラ選択 選択中/非選択・闘技場 選択中/非選択)を1枚に。usage: clothcardshots.py <qaフォルダ> <out.jpg>"""
import sys, glob, os
from PIL import Image
fs = sorted(glob.glob(os.path.join(sys.argv[1], '000*_card_*.png')))
t = [Image.open(f).convert('RGB').resize((800, 450), Image.LANCZOS) for f in fs]
S = Image.new('RGB', (1610, 450 * ((len(t) + 1) // 2) + 5 * ((len(t) + 1) // 2)), (0, 0, 0))
for i, im in enumerate(t): S.paste(im, ((i % 2) * 810, (i // 2) * 455))
S.save(sys.argv[2], quality=85); print(sys.argv[2], len(t))
