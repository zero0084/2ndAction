# 元 / 合わせた新しい絵 / 合成 / 判定 を、コマごとに1行ずつ(3倍)  usage: sheet7.py <out.jpg> <folder/name> ...
import sys
from PIL import Image
A='Unity/2ndAction/Assets/Art/'; W='Docs/ClothRevision_2026-10-25/anim/'
rows=[]
for fn in sys.argv[2:]:
    d,n=fn.split('/')
    o=Image.open(A+fn+'.png').convert('RGBA'); bb=o.getbbox(); bb=(0,max(0,bb[1]-6),o.width,min(o.height,bb[3]+6))
    r=[]
    for i,p in enumerate([A+fn+'.png',W+f'aligned/{d}/{n}.png',W+f'local/{d}/{n}.png',W+f'dbg/{d}/{n}.png']):
        im=Image.open(p).convert('RGBA').crop(bb)
        if i<3: b=Image.new('RGBA',im.size,(110,120,130,255)); b.alpha_composite(im); im=b
        r.append(im.convert('RGB').resize((im.width*3,im.height*3),Image.LANCZOS))
    rows.append(r)
W_=max(sum(t.width+8 for t in r) for r in rows); H_=sum(r[0].height+8 for r in rows)
S=Image.new('RGB',(W_,H_),(30,30,30)); y=0
for r in rows:
    x=0
    for t in r: S.paste(t,(x,y)); x+=t.width+8
    y+=r[0].height+8
S.save(sys.argv[1],quality=90)
