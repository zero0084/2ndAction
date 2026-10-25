# 元 / 新しい絵 / 合成 / 判定 を並べる(4倍)  usage: check4.py <元.png> <新.png> <合成.png> <判定.png> <out.png>
import sys
from PIL import Image
o=Image.open(sys.argv[1]).convert('RGBA'); bb=o.getbbox(); bb=(0,max(0,bb[1]-6),o.width,min(o.height,bb[3]+6))
tiles=[]
for i,p in enumerate(sys.argv[2:5] if False else sys.argv[1:5]):
    im=Image.open(p).convert('RGBA').crop(bb)
    if i<3: b=Image.new('RGBA',im.size,(110,120,130,255)); b.alpha_composite(im); im=b
    tiles.append(im.convert('RGB').resize((im.width*4,im.height*4),Image.NEAREST))
S=Image.new('RGB',(sum(t.width+10 for t in tiles),tiles[0].height),(30,30,30)); x=0
for t in tiles: S.paste(t,(x,0)); x+=t.width+10
S.save(sys.argv[5])
