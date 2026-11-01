# 添付の絵から黒剣士の影だけを消し、足もとの地面の線をつなぐ(2026-11-01)
#  ・人物 = 地面の線より上の暗い画素の、大きい塊(草の小さい塊は残す)。縁のにじみ(圧縮の明るい縁)も含めて 8px 広げる
#  ・空: 行ごとに、消す範囲の左右すぐ外の空の色をつなぎ、遠くの空の細かいむら(平均を引いた分)を足す
#  ・地面の線: 岩の右(x=258、草を避けて 669)〜剣先の右(x=478)を直線でつなぐ
import sys
from PIL import Image
src,out=sys.argv[1],sys.argv[2]
im=Image.open(src).convert('RGB'); W,H=im.size; px=im.load()
dark=lambda c: sum(c)<150
def edge(x, y0=600, y1=760):
    for y in range(y0,y1):
        if all(dark(px[x,yy]) for yy in range(y,min(H,y+6))): return y
X0,X1=258,478
e0,e1=669,edge(X1); print('edges',e0,e1)
line=lambda x: e0+(e1-e0)*(x-X0)/(X1-X0)
seen=set(); comps=[]
for x in range(X0,X1+1):
    for y in range(470,int(line(x))):
        if (x,y) in seen or not dark(px[x,y]): continue
        st=[(x,y)]; seen.add((x,y)); comp=[]
        while st:
            cx,cy=st.pop(); comp.append((cx,cy))
            for nx,ny in ((cx+1,cy),(cx-1,cy),(cx,cy+1),(cx,cy-1)):
                if X0<=nx<=X1 and 470<=ny<line(nx) and (nx,ny) not in seen and dark(px[nx,ny]): seen.add((nx,ny)); st.append((nx,ny))
        comps.append(comp)
comps.sort(key=len,reverse=True); print('components',[len(c) for c in comps[:6]])
fig=set(p for c in comps if len(c)>400 for p in c)
G=8
mask=set()
for (x,y) in fig:
    for dx in range(-G,G+1):
        for dy in range(-G,G+1):
            nx,ny=x+dx,y+dy
            if 0<=nx<W and 0<=ny<H and ny < line(min(max(nx,X0),X1)) + 1: mask.add((nx,ny))
rows={}
for (x,y) in mask: rows.setdefault(y,[]).append(x)
op_im=im.copy(); op=op_im.load()
def avg(cs): return tuple(sum(c[i] for c in cs)/len(cs) for i in range(3))
for y,xs in rows.items():
    xs.sort()
    # 行の中の連続した区間ごとに
    runs=[]; a=xs[0]; b=a
    for x in xs[1:]:
        if x==b+1: b=x
        else: runs.append((a,b)); a=b=x
    runs.append((a,b))
    for a,b in runs:
        L=avg([px[x,y] for x in range(max(0,a-6),a) if not dark(px[x,y])] or [px[x+700,y] for x in range(a,a+1)])
        R=avg([px[x,y] for x in range(b+1,min(W,b+7)) if not dark(px[x,y])] or [px[x+700,y] for x in range(b,b+1)])
        ref=[px[x+700,y] for x in range(a,b+1)]; m=avg(ref)
        for i,x in enumerate(range(a,b+1)):
            t=(x-a)/max(1,b-a); base=[L[k]*(1-t)+R[k]*t for k in range(3)]
            n=ref[i]; c=[base[k]+(n[k]-m[k]) for k in range(3)]
            ly=line(min(max(x,X0),X1))
            if y >= ly - 0.5:   # 地面の線の 1px をなじませる
                f=max(0.0,min(1.0,ly+0.5-y)); c=[v*f for v in c]
            op[x,y]=tuple(int(max(0,min(255,round(v)))) for v in c)
op_im.save(out); print('mask px',len(mask))
# 足もとの地面: 線より下は地面の黒で埋める(靴の底の段差/すき間を残さない)。線の 1px は空となじませる
op_im=Image.open(out).convert('RGB'); op=op_im.load()
for x in range(X0+40,X1+1):
    ly=line(x)
    for y in range(int(ly)-1,int(ly)+10):
        f=max(0.0,min(1.0,ly+0.5-y))  # 1 = 空、0 = 地面
        if f>=1: continue
        s=op[x,int(ly)-3]
        op[x,y]=tuple(int(s[k]*f) for k in range(3))
op_im.save(out); print('ground filled')
