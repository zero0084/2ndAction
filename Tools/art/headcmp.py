# headcmp.py (騎乗ボスのコマの縮尺の目視確認) <out.jpg> <a.png> <b.png>... : 出力の x=180〜600(鼻先の目標 266 に黄色の線)で狼の頭を並べる
import sys
from PIL import Image, ImageDraw
fs=sys.argv[2:]; out=Image.new('RGB',(len(fs)*420,340),(90,90,90))
for i,n in enumerate(fs):
    im=Image.open(n); H=im.height; c=im.crop((180,H-340,600,H)); out.paste(c,(i*420,0),c)
    d=ImageDraw.Draw(out); d.line((i*420+86,0,i*420+86,340),fill=(255,255,0))
    for yy in range(0,340,40): d.line((i*420,yy,i*420+419,yy),fill=(70,70,70))
out.save(sys.argv[1])
