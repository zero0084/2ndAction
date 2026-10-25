# 確認用: 元 / 修正 / 判定の色分け を7コマ並べる  usage: preview.py <fixed_dir> <debug_dir> <out.jpg>
import sys
from PIL import Image
A='Unity/2ndAction/Assets/Art/PlayerRun_v1/'
fixed, debug, out = sys.argv[1:4]
rows=[]
for d in (A, fixed+'/', debug+'/'):
    tiles=[]
    for k in range(7):
        im=Image.open(d+f'run_{k:02d}.png').convert('RGBA')
        b=Image.new('RGBA',im.size,(110,120,130,255)); b.alpha_composite(im)
        tiles.append(b.crop((0,120,im.width,600)).convert('RGB'))
    rows.append(tiles)
W=sum(t.width+8 for t in rows[0]); S=Image.new('RGB',(W,490*3))
for r,tiles in enumerate(rows):
    x=0
    for t in tiles: S.paste(t,(x,r*490)); x+=t.width+8
S=S.resize((int(S.width*1.3),int(S.height*1.3))); S.save(out,quality=90)
