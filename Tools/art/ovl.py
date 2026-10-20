# ovl.py <a_src> <b_src> <out> : 2枚の元画像の輪郭を重ねる(赤=a、青=b、重なり=紫)。縮小して保存
import sys; sys.path.insert(0, r'C:/GameProject/2ndAction/Tools/art')
from reaperpose import key
from PIL import Image
a=key(Image.open(sys.argv[1])); b=key(Image.open(sys.argv[2]))
c=Image.new('RGBA',a.size,(40,40,40,255))
r=Image.new('RGBA',a.size,(255,60,60,255)); r.putalpha(a.split()[3].point(lambda v: 140 if v>0 else 0))
g=Image.new('RGBA',a.size,(60,140,255,255)); g.putalpha(b.split()[3].point(lambda v: 140 if v>0 else 0))
c.alpha_composite(r); c.alpha_composite(g); c.convert('RGB').resize((a.width//2,a.height//2)).save(sys.argv[3])
