# 7コマの 元 / 修正 / 重ね(元=赤・修正=青緑) を横に並べる  usage: check.py <out.jpg>
import sys
from PIL import Image
A='Unity/2ndAction/Assets/Art/PlayerRun_v1/'; O='Docs/ClothRevision_2026-10-25/run/'
rows=[]
for k in range(7):
    bb=Image.open(A+f'run_{k:02d}.png').getbbox(); r=[]
    for d,bg in ((A,(110,120,130)),(O+'frame_fixed/',(110,120,130)),(O+'frame_dbg/',(0,0,0))):
        im=Image.open(d+f'run_{k:02d}.png').convert('RGBA'); b=Image.new('RGBA',im.size,bg+(255,)); b.alpha_composite(im)
        r.append(b.crop((0,bb[1]-12,im.width,bb[3]+8)).convert('RGB'))
    rows.append(r)
cw=max(t.width for r in rows for t in r); ch=max(t.height for r in rows for t in r)
S=Image.new('RGB',(7*(cw+6),3*(ch+6)),(30,30,30))
for k,r in enumerate(rows):
    for j,t in enumerate(r): S.paste(t,(k*(cw+6),j*(ch+6)+(ch-t.height)))
S.save(sys.argv[1],quality=90)
