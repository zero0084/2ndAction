# furmatch.py <src.png> <ref.png> <out.png> : 狼の毛(茶〜橙の画素)だけ、平均と幅を ref に合わせる(生成ごとの毛色のぶれを直す。ポーズは変えない)
import sys; import os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reaperpose import key
import numpy as np
from PIL import Image
def furmask(a):
    r,g,b,al=a[...,0],a[...,1],a[...,2],a[...,3]
    return (al>200)&(r>g)&(g>b)&(r-b>25)&(r<250)&~((r>150)&(g<90))
ref=np.asarray(key(Image.open(sys.argv[2]))).astype(np.float32)
src=np.asarray(key(Image.open(sys.argv[1]))).astype(np.float32)
mr,ms=ref[...,:3][furmask(ref)].mean(0),ref[...,:3][furmask(ref)].std(0)
m=furmask(src); sr,ss=src[...,:3][m].mean(0),src[...,:3][m].std(0)
print('ref',mr.round(1),'src',sr.round(1))
out=src.copy(); adj=(src[...,:3]-sr)/ss*ms+mr; out[...,:3][m]=np.clip(adj[m],0,255)
im=Image.fromarray(out.astype(np.uint8),'RGBA'); c=Image.new('RGB',im.size,(255,0,255)); c.paste(im,(0,0),im); c.save(sys.argv[3])
