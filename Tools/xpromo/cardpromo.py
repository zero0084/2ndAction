# X 投稿用「カードシステム」紹介動画の組み立て(2026-11-09)。
#   python cardpromo.py <edl.json> <x45|x169> <out.mp4> [--preview 0.5] [--only i,j]
# 素材は QaSweep -storeCapture の cp_* の撮影(raw/<shot>.mkv 劣化なし + .wav その場面の SE、raw/taps.tsv 押した位置)。
#  4:5 版は 4:5 の窓(864x1080 を2倍 = 1728x2160)、16:9 版は 1920x1080 の窓で撮った物を使う(画面の並びはゲーム自身の縦/横の配置)。
# 1コマずつ Python で組み立てる: 場面ごとの寄せ(読ませたい所へ。ゆっくり寄る/動かすのも可)、上の帯の字幕、押した位置の指の印、締めのロゴ。
# ゲームに無い演出は足さない(足すのは 字幕・指の印・ロゴ・帯の背景のぼかし だけ)。
import json, math, os, subprocess, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FF = os.environ.get("FFMPEG", "ffmpeg")
FONT = os.environ.get("CAPTION_FONT", r"C:\Windows\Fonts\NotoSansJP-VF.ttf")
FPS = 60
GOLD = (240, 200, 104, 255)
INK = (8, 10, 22, 255)
LAYOUT = {
    # band = 上の字幕の帯の高さ。fg = ゲームの画面の置き場所(帯の下、全幅)
    "x45": dict(W=1080, H=1350, band=250),
    "x169": dict(W=1920, H=1080, band=150),
}


def font(size, weight="Black"):
    f = ImageFont.truetype(FONT, size)
    for w in (weight, "Bold"):
        try:
            f.set_variation_by_name(w)
            break
        except Exception:
            pass
    return f


def fit_font(d, text, size, maxw, weight="Black", stroke=6):
    f = font(size, weight)
    tw = max(d.textlength(ln, font=f) + stroke * 2 for ln in text.split("\n"))
    if tw > maxw:
        f = font(int(size * maxw / tw), weight)
    return f


def ease(u):
    u = max(0.0, min(1.0, u))
    return u * u * (3 - 2 * u)


# ---------------------------------------------------------------- 字幕/ロゴ(帯の中)
def caption_img(L, cap):
    W, band = L["W"], L["band"]
    x45 = L["H"] > W
    img = Image.new("RGBA", (W, band), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    text = cap["text"].replace("\\n", "\n")
    style = cap.get("style", "main")
    size = (({"hero": 92, "main": 80, "sub": 58}) if x45 else ({"hero": 84, "main": 74, "sub": 54}))[style]
    note = cap.get("note")
    f = fit_font(d, text, size, W - (80 if x45 else 300))
    bb = d.multiline_textbbox((0, 0), text, font=f, stroke_width=6, spacing=10, align="center")
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    nf = nh = None
    if note:
        nf = fit_font(d, note, 34 if x45 else 30, W - 60, "Bold", 3)
        nb = d.multiline_textbbox((0, 0), note, font=nf, stroke_width=3, spacing=6, align="center")
        nh = nb[3] - nb[1]
    total = th + (22 + nh if note else 0)
    y = (band - total) / 2 - bb[1] + (6 if not note else 0)
    x = (W - tw) / 2 - bb[0]
    d.multiline_text((x, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=6, stroke_fill=INK, spacing=10, align="center")
    if not note:
        bar_w = min(tw, 200)
        d.rounded_rectangle([(W - bar_w) / 2, y + bb[3] + 14, (W + bar_w) / 2, y + bb[3] + 22], radius=4, fill=GOLD)
    else:
        ny = y + bb[3] + 22
        nb = d.multiline_textbbox((0, 0), note, font=nf, stroke_width=3, spacing=6, align="center")
        d.multiline_text(((W - (nb[2] - nb[0])) / 2 - nb[0], ny - nb[1]), note, font=nf, fill=(255, 226, 150, 255), stroke_width=3,
                         stroke_fill=INK, spacing=6, align="center")
    return img


def logo_img(logo, maxw, maxh):
    lg = Image.open(logo).convert("RGBA")
    s = min(maxw / lg.width, maxh / lg.height)
    return lg.resize((int(lg.width * s), int(lg.height * s)), Image.LANCZOS)


# ---------------------------------------------------------------- 指の印
def load_taps(path):
    taps = {}
    if not path or not os.path.exists(path):
        return taps
    for ln in open(path, encoding="utf-8"):
        p = ln.rstrip("\n").split("\t")
        if len(p) < 5:
            continue
        taps.setdefault(p[0], []).append((int(p[1]), p[2], float(p[3]), float(p[4])))
    return taps


def draw_taps(canvas, events, f_abs, to_out, scale, fix):
    """events: その撮影の (コマ, 種類, x, y)。f_abs = 素材のコマ番号。to_out(x, y) = 素材の割合 → 出力の画素"""
    d = ImageDraw.Draw(canvas, "RGBA")
    R = 34 * scale
    # ドラッグ: 始まり〜終わりの間は指の点が付いてくる
    drag = [e for e in events if e[1] in ("drag_start", "drag", "drag_end")]
    if drag and drag[0][0] - 6 <= f_abs <= drag[-1][0] + 4:
        cur = max((e for e in drag if e[0] <= f_abs), key=lambda e: e[0], default=drag[0])
        x, y = to_out(cur[2], cur[3])
        d.ellipse([x - R * 0.62, y - R * 0.62, x + R * 0.62, y + R * 0.62], fill=(255, 255, 255, 120), outline=(255, 255, 255, 235), width=max(2, int(4 * scale)))
    for (fr, kind, nx, ny) in events:
        if kind in ("drag",):
            continue
        if (cpkey := fix.get(kind)) is not None and nx == 0.0 and ny == 1.0:
            nx, ny = cpkey
        elif nx == 0.0 and ny == 1.0:
            continue
        start = fr - 4  # 指が触れるのは処理の少し前
        life = 26 if kind != "hold" else 40
        k = f_abs - start
        if k < 0 or k > life:
            continue
        u = k / life
        x, y = to_out(nx, ny)
        rr = R * (0.75 + 0.9 * ease(u))
        a = int(255 * (1 - u) ** 1.2)
        d.ellipse([x - rr, y - rr, x + rr, y + rr], outline=(255, 255, 255, a), width=max(3, int(6 * scale)))
        d.ellipse([x - rr - 2, y - rr - 2, x + rr + 2, y + rr + 2], outline=(240, 200, 104, a // 2), width=max(2, int(3 * scale)))
        if u < 0.55 or kind == "hold":
            ca = int(170 * (1 - u)) if kind != "hold" else 170
            r0 = R * 0.55
            d.ellipse([x - r0, y - r0, x + r0, y + r0], fill=(255, 255, 255, ca))


# ---------------------------------------------------------------- 映像
def view_at(c, lay, u):
    o = dict(c.get("view", {}))
    o.update(c.get(lay, {}))
    v0 = {"zoom": o.get("zoom", 1.0), "cx": o.get("cx", 0.5), "cy": o.get("cy", 0.5)}
    v1 = dict(v0)
    v1.update(o.get("to", {}))
    e = ease((u - o.get("move_from", 0.0)) / max(1e-6, o.get("move_to", 1.0) - o.get("move_from", 0.0)))
    return {k: v0[k] + (v1[k] - v0[k]) * e for k in v0}, o


def frames(src, t0, n, sw, sh):
    cmd = [FF, "-v", "error", "-nostdin", "-ss", f"{t0:.3f}", "-i", src, "-frames:v", str(n), "-f", "rawvideo", "-pix_fmt", "rgb24", "-r", str(FPS), "-"]
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=sw * sh * 3 * 2)
    n = sw * sh * 3
    while True:
        b = p.stdout.read(n)
        if len(b) < n:
            break
        yield Image.frombuffer("RGB", (sw, sh), b, "raw", "RGB", 0, 1)
    p.stdout.close()
    p.wait()


def probe(src):
    r = subprocess.run([FF.replace("ffmpeg", "ffprobe"), "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", src],
                       capture_output=True, text=True)
    w, h = r.stdout.strip().split(",")[:2]
    return int(w), int(h)


def enc_cmd(W2, H2, out_path, preview):
    return [FF, "-y", "-v", "error", "-nostdin", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W2}x{H2}", "-r", str(FPS), "-i", "-",
            "-c:v", "libx264", "-preset", "slow" if preview >= 1 else "veryfast", "-crf", "17", "-pix_fmt", "yuv420p", "-profile:v", "high",
            "-r", str(FPS), out_path]


def render_cut(job):
    """1つの場面 → 映像だけの mp4(全部の場面を同じ設定で書き、最後に -c copy でつなぐ)"""
    edl_path, lay, ci, t, out_path, preview = job
    edl = json.load(open(edl_path, encoding="utf-8"))
    L = LAYOUT[lay]
    W, H, band = L["W"], L["H"], L["band"]
    W2, H2 = int(W * preview) // 2 * 2, int(H * preview) // 2 * 2
    raw = edl["raw"][lay]
    taps = load_taps(os.path.join(raw, "taps.tsv"))
    fix = {k: v for k, v in edl.get("tapfix", {}).get(lay, {}).items()}
    masks = edl.get("masks", {}).get(lay, {})
    fw, fh = W, H - band
    c = dict(edl["cuts"][ci])
    c["in"] = c.get(lay, {}).get("in", c["in"])  # 4:5 と 16:9 で撮影の開始秒が違う場面(ガチャの抽選など)
    c["shot"] = c.get(lay, {}).get("shot", c["shot"])
    end = edl["end"]
    caps = [(cp, caption_img(L, cp)) for cp in edl["captions"] if cp["to"] > t and cp["at"] < t + c["dur"]]
    logo = logo_img(edl["logo"], W * (0.86 if H > W else 0.62), H * 0.30) if c.get("end") else None
    ep = subprocess.Popen(enc_cmd(W2, H2, out_path, preview), stdin=subprocess.PIPE)
    src = os.path.join(raw, c["shot"] + ".mkv")
    sw, sh = probe(src)
    n = int(round(c["dur"] * FPS))
    ev = taps.get(c["shot"], []) if c.get("taps", True) else []
    f0 = int(round(c["in"] * FPS))
    k = -1
    canvas = None
    for k, fr in enumerate(frames(src, c["in"], n, sw, sh)):
        u = k / max(1, n - 1)
        v, o = view_at(c, lay, u)
        for m in masks.get(c["shot"], []):
            if len(m) < 9 or c["in"] + k / FPS >= m[8]:  # 9番目 = 塗り始める素材の秒(その画面が出てから)
                fr = apply_mask(fr, sw, sh, m)
        canvas = compose(fr, sw, sh, W, H, band, fw, fh, v, o)
        if c.get("badge"):
            draw_badge(canvas, c["badge"], band, W / 1080.0, min(1.0, k / 9.0))
        if ev:
            cw, ch, x0, y0, ox, oy, s = canvas.info["map"]
            to_out = lambda nx, ny: (ox + (nx * sw - x0) * s, oy + (ny * sh - y0) * s)
            draw_taps(canvas, ev, f0 + k, to_out, W / 1080.0, fix)
        tt = t + k / FPS
        if c.get("end"):
            el = tt - t
            dim = end.get("dim", 0.45) * ease((el - end.get("dim_at", 0.0)) / end.get("dim_ramp", 0.8))
            if dim > 0:
                canvas = Image.blend(canvas, Image.new("RGB", canvas.size, (4, 6, 14)), dim)
            la = ease((el - end["logo_at"]) / 0.35)
            if la > 0:
                lg = logo.copy()
                if la < 1:
                    lg.putalpha(lg.getchannel("A").point(lambda p: int(p * la)))
                canvas.paste(lg, ((W - lg.width) // 2, int(band + fh * 0.42 - lg.height / 2 + 14 * (1 - la))), lg)
        for cap, im in caps:
            if cap["at"] <= tt <= cap["to"]:
                fi, fo = cap.get("fi", 0.16), cap.get("fo", 0.2)
                a = min(1.0, (tt - cap["at"]) / fi if fi > 0 else 1.0, (cap["to"] - tt) / fo if fo > 0 else 1.0)
                rise = int(14 * (1 - ease((tt - cap["at"]) / 0.25))) if cap.get("rise", True) else 0
                im2 = im
                if a < 1:
                    im2 = im.copy()
                    im2.putalpha(im.getchannel("A").point(lambda p: int(p * a)))
                canvas.paste(im2, (0, cap.get("y", 0) + rise), im2)
        if preview != 1.0:
            canvas = canvas.resize((W2, H2), Image.BILINEAR)
        ep.stdin.write(canvas.tobytes())
    for _ in range(n - (k + 1)):  # 素材が足りない時は最後のコマで埋める
        ep.stdin.write(canvas.tobytes())
    ep.stdin.close()
    ep.wait()
    print(f"  cut {ci:02d} {c['shot']} {c['in']:.2f}+{c['dur']:.2f}s at {t:.2f}s{'' if k + 1 >= n else f' (short {n - k - 1} frames)'}", flush=True)
    return out_path


def render_video(edl_path, edl, lay, out_path, audio_path, preview=1.0, only=None, work="."):
    from multiprocessing import Pool
    jobs, t = [], 0.0
    for ci, c in enumerate(edl["cuts"]):
        if only is None or ci in only:
            jobs.append((edl_path, lay, ci, t, os.path.join(work, f"cut_{ci:02d}.mp4"), preview))
        t += c["dur"]
    with Pool(int(os.environ.get("CP_JOBS", "3"))) as pool:
        parts = pool.map(render_cut, jobs)
    lst = os.path.join(work, "concat.txt")
    open(lst, "w", encoding="utf-8").write("".join("file '" + p.replace(os.sep, "/") + "'\n" for p in parts))
    cmd = [FF, "-y", "-v", "error", "-nostdin", "-f", "concat", "-safe", "0", "-i", lst]
    if audio_path:
        cmd += ["-i", audio_path, "-map", "0:v", "-map", "1:a", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest"]
    cmd += ["-c:v", "copy", "-movflags", "+faststart", out_path]
    if subprocess.run(cmd).returncode != 0:
        sys.exit("concat failed")


def apply_mask(fr, sw, sh, m):
    """ゲームの画面の一部(隠したい表示)を、すぐ下の背景の色で塗る。m = [x0,y0,x1,y1, 色を取る所 sx0,sy0,sx1,sy1](割合)"""
    x0, y0, x1, y1 = int(m[0] * sw), int(m[1] * sh), int(m[2] * sw), int(m[3] * sh)
    a = np.asarray(fr.crop((int(m[4] * sw), int(m[5] * sh), int(m[6] * sw), int(m[7] * sh))))
    col = tuple(int(x) for x in np.median(a.reshape(-1, 3), axis=0))
    fr = fr.copy()
    ImageDraw.Draw(fr).rectangle([x0, y0, x1, y1], fill=col)
    return fr


def draw_badge(canvas, badge, band, scale, a=1.0):
    """結果の場面に「成功例」「失敗例」の札(ゲームの画面の左上、編集で足した表示)"""
    text, kind = badge if isinstance(badge, list) else (badge, "ok")
    d = ImageDraw.Draw(canvas, "RGBA")
    f = font(int(42 * scale), "Black")
    bb = d.textbbox((0, 0), text, font=f, stroke_width=3)
    pw, ph = 26 * scale, 12 * scale
    x, y = 22 * scale, band + 20 * scale
    fill = (196, 140, 36, int(235 * a)) if kind == "ok" else (70, 82, 110, int(235 * a))
    d.rounded_rectangle([x, y, x + bb[2] - bb[0] + pw * 2, y + bb[3] - bb[1] + ph * 2], radius=int(14 * scale), fill=fill,
                        outline=(255, 236, 180, int(255 * a)), width=max(2, int(3 * scale)))
    d.text((x + pw - bb[0], y + ph - bb[1]), text, font=f, fill=(255, 255, 255, int(255 * a)), stroke_width=3, stroke_fill=(20, 16, 10, int(255 * a)))


def compose(fr, sw, sh, W, H, band, fw, fh, v, o):
    """1コマ: 背景 = 同じ画面をぼかして暗く(帯の中で動きが続く)、手前 = 寄せた画面"""
    small = fr.reduce(12).filter(ImageFilter.GaussianBlur(3)).point(lambda p: int(p * 0.42))
    bg = small.resize((W, H), Image.BILINEAR)
    a = fw / fh
    z = max(0.2, v["zoom"])
    if o.get("fit"):
        # 画面の全体を帯の下へ(左右/上下はぼかしの背景)。zoom > 1 で寄せる
        s0 = min(fw / sw, fh / sh)
        cw, ch = min(sw, fw / s0 / z), min(sh, fh / s0 / z)
    else:
        # 帯の下を埋める(はみ出す所は切る)。zoom で寄せる
        if sw / sh > a:
            ch = sh / z; cw = ch * a
        else:
            cw = sw / z; ch = cw / a
        cw, ch = min(cw, sw), min(ch, sh)
    x0 = min(max(0.0, v["cx"] * sw - cw / 2), sw - cw)
    y0 = min(max(0.0, v["cy"] * sh - ch / 2), sh - ch)
    s = min(fw / cw, fh / ch)
    ow, oh = int(round(cw * s)), int(round(ch * s))
    fg = fr.resize((ow, oh), Image.LANCZOS, box=(x0, y0, x0 + cw, y0 + ch), reducing_gap=3.0)
    ox, oy = (W - ow) // 2, band + (fh - oh) // 2
    bg.paste(fg, (ox, oy))
    bg.info["map"] = (cw, ch, x0, y0, ox, oy, s)
    return bg


# ---------------------------------------------------------------- 音
def render_audio(edl, lay, out, gain_chain):
    raw = edl["raw"][lay]
    inputs, filters, al = [], [], []
    t = 0.0
    for i, c in enumerate(edl["cuts"]):
        c = dict(c); c["in"] = c.get(lay, {}).get("in", c["in"]); c["shot"] = c.get(lay, {}).get("shot", c["shot"])
        wav = os.path.join(raw, c["shot"] + ".wav")
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", wav]
        g = c.get("se", 1.0)
        filters.append(f"[{i}:a]aresample=48000,aformat=channel_layouts=stereo,volume={g},asetpts=PTS-STARTPTS,"
                       f"afade=t=in:st=0:d=0.012,afade=t=out:st={max(0, c['dur'] - 0.02):.3f}:d=0.02,apad=whole_dur={c['dur']:.3f}[a{i}]")
        al.append(f"[a{i}]")
        t += c["dur"]
    n = len(edl["cuts"])
    filters.append("".join(al) + f"concat=n={n}:v=0:a=1[se]")
    idx = n
    bl = []
    for j, b in enumerate(edl["bgm"]):
        loop = ["-stream_loop", "-1"] if b.get("loop") else []
        inputs += loop + ["-ss", f'{b.get("from", 0):.3f}', "-t", f'{b["dur"]:.3f}', "-i", b["file"]]
        filters.append(f"[{idx}:a]aresample=48000,aformat=channel_layouts=stereo,volume={b.get('gain', 0.5)},"
                       f"afade=t=in:st=0:d={b.get('fade_in', 0.05)},afade=t=out:st={b['dur'] - b.get('fade_out', 1.6):.3f}:d={b.get('fade_out', 1.6)},"
                       f"adelay={int(b.get('at', 0) * 1000)}|{int(b.get('at', 0) * 1000)},apad=whole_dur={t:.3f}[b{j}]")
        bl.append(f"[b{j}]")
        idx += 1
    filters.append(f"[se]volume={edl.get('se_gain', 1.0)}[se2]")
    filters.append("[se2]" + "".join(bl) + f"amix=inputs={1 + len(bl)}:normalize=0:duration=first,alimiter=limit=0.93,atrim=0:{t:.3f}{gain_chain}[aout]")
    fcp = out + ".filter.txt"
    open(fcp, "w", encoding="utf-8").write(";\n".join(filters))
    cmd = [FF, "-y", "-loglevel", "error", "-nostdin"] + inputs + ["-/filter_complex", fcp, "-map", "[aout]", "-c:a", "pcm_s16le", "-ar", "48000", out]
    r = subprocess.run(cmd)
    if r.returncode != 0:
        sys.exit(f"ffmpeg audio failed ({r.returncode})")
    return t


def measure(path):
    r = subprocess.run([FF, "-hide_banner", "-nostdin", "-i", path, "-af", "ebur128=framelog=quiet:peak=true", "-f", "null", "-"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    s = r.stderr[r.stderr.rindex("Summary:"):]
    return float(s.split("I:")[1].split("LUFS")[0]), float(s.split("Peak:")[1].split("dBFS")[0])


def main():
    edl_path, lay, out = sys.argv[1], sys.argv[2], sys.argv[3]
    preview = float(sys.argv[sys.argv.index("--preview") + 1]) if "--preview" in sys.argv else 1.0
    only = [int(x) for x in sys.argv[sys.argv.index("--only") + 1].split(",")] if "--only" in sys.argv else None
    edl = json.load(open(edl_path, encoding="utf-8"))
    work = os.path.join(os.path.dirname(os.path.abspath(out)), "_work_" + lay)
    os.makedirs(work, exist_ok=True)
    apath = None
    if only is None:
        loud, lim = edl.get("loudness", -14), 10 ** (edl.get("tp", -2.0) / 20.0)
        chain = lambda g: f",volume={g:.2f}dB,aresample=192000,alimiter=limit={lim:.4f}:attack=1:release=60:level=false,aresample=48000"
        apath = os.path.join(work, "mix.wav")
        render_audio(edl, lay, apath, "")
        i0, tp0 = measure(apath)
        g = loud - i0
        for _ in range(2):
            render_audio(edl, lay, apath, chain(g))
            i1, tp1 = measure(apath)
            g += loud - i1
        print(f"loudness {i0:.2f} LUFS / TP {tp0:.2f} -> gain {g:.2f} dB (check {i1:.2f} / TP {tp1:.2f})")
    render_video(edl_path, edl, lay, out, apath, preview, only, work)


if __name__ == "__main__":
    main()
