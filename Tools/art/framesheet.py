# sheet.py <out.jpg> <a.png> <b.png> ... : 下端(地面)をそろえ、ピボット(中心)の線を引いて横に並べる。--gif <out.gif> <fps> でループ確認
import sys
from PIL import Image, ImageDraw
args=sys.argv[1:]
gif=None
if '--gif' in args:
    i=args.index('--gif'); gif=(args[i+1],float(args[i+2])); args=args[:i]+args[i+3:]
out=args[0]; ims=[Image.open(p).convert('RGBA') for p in args[1:]]
H=max(i.height for i in ims); W=max(i.width for i in ims)
s=Image.new('RGB',((W+10)*len(ims),H+10),(90,90,90)); d=ImageDraw.Draw(s)
for k,im in enumerate(ims):
    x=k*(W+10)+(W-im.width)//2; s.paste(im,(x,H-im.height),im); d.line((k*(W+10)+W//2,0,k*(W+10)+W//2,H),fill=(0,255,255))
d.line((0,H-1,s.width,H-1),fill=(255,255,0))
s.save(out,quality=85)
if gif:
    fr=[]
    for im in ims:
        c=Image.new('RGB',(W,H+10),(90,90,90)); c.paste(im,((W-im.width)//2,H-im.height),im); ImageDraw.Draw(c).line((0,H-1,W,H-1),fill=(255,255,0)); fr.append(c.resize((W//2,(H+10)//2)))
    fr[0].save(gif[0],save_all=True,append_images=fr[1:],duration=int(1000/gif[1]),loop=0)
