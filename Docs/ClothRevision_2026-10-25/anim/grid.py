# 合成の結果を座標の格子付きで拡大(切れ端の範囲指定用)  usage: grid.py <png> <out> <x0,y0,x1,y1> [scale]
import sys
from PIL import Image, ImageDraw
p,out,box=sys.argv[1],sys.argv[2],list(map(int,sys.argv[3].split(','))); s=int(sys.argv[4]) if len(sys.argv)>4 else 8
im=Image.open(p).convert('RGBA'); b=Image.new('RGBA',im.size,(110,120,130,255)); b.alpha_composite(im)
x0,y0,x1,y1=box; c=b.crop(box).convert('RGB').resize(((x1-x0)*s,(y1-y0)*s),Image.NEAREST); d=ImageDraw.Draw(c)
for x in range(x0 - x0%10 + 10, x1, 10): d.line([((x-x0)*s,0),((x-x0)*s,c.height)],fill=(255,255,0)); d.text(((x-x0)*s+2,2),str(x),fill='yellow')
for y in range(y0 - y0%10 + 10, y1, 10): d.line([(0,(y-y0)*s),(c.width,(y-y0)*s)],fill=(0,255,255)); d.text((2,(y-y0)*s+2),str(y),fill='cyan')
c.save(out)
