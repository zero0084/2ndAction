# X(旧 Twitter)投稿用の宣伝動画の組み立て(2026-11-08)。
#   python promo.py <edl.json> <x45|x169> <out.mp4>
#   x45  = 1080x1350(4:5、スマホのタイムライン向け。ゲームの横画面は切らずに中央へ、上下の帯に字幕)
#   x169 = 1920x1080(16:9、ゲームの画面そのまま。字幕は下の帯)
# 素材は QaSweep -storeCapture の撮影(raw/<shot>.mkv = 劣化なしの映像、.wav = その場面の SE/環境音)。
# BGM はゲーム内の自作曲を通しで重ねる(拍に合わせて場面を切り替える)。ゲームに無い演出は足さない
# (足すのは字幕・ロゴ・帯の背景のぼかしだけ)。
import json, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

FF = os.environ.get("FFMPEG", "ffmpeg")
FONT = os.environ.get("CAPTION_FONT", r"C:\Windows\Fonts\NotoSansJP-VF.ttf")
FPS = 60
GOLD = (240, 200, 104, 255)
INK = (8, 10, 22, 255)

LAYOUT = {
    # fg: ゲームの画面を置く位置と大きさ(16:9 のまま)
    # 4:5: ゲーム画面の中央 4:3(戦闘はほぼこの範囲)を 1080x810 で。上 330 = 字幕、下 210 = 小さいロゴ
    #  ("full": true の場面は 16:9 のまま縮めて置く = 左右も全部見せたい時)
    "x45": dict(W=1080, H=1350, fg=(0, 330, 1080, 810)),
    "x169": dict(W=1920, H=1080, fg=(0, 0, 1920, 1080)),
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


def fit_font(d, text, size, maxw, weight="Black"):
    f = font(size, weight)
    tw = max(d.textlength(ln, font=f) for ln in text.split("\n"))
    if tw > maxw:
        f = font(int(size * maxw / tw), weight)
    return f


def caption_png(L, cap, path):
    """字幕1つ分の透明な PNG(画面と同じ大きさ)。style: hero(大)/ main / sub(小)"""
    W, H = L["W"], L["H"]
    fx, fy, fw, fh = L["fg"]
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    text = cap["text"].replace("\\n", "\n")
    style = cap.get("style", "main")
    x45 = H > W
    if x45:
        size = {"hero": 104, "main": 96, "sub": 64}[style]
        f = fit_font(d, text, size, W - 96)
        bb = d.multiline_textbbox((0, 0), text, font=f, stroke_width=6, spacing=14, align="center")
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
        cy = cap.get("y", fy / 2 + 10)  # 上の帯の真ん中
        x, y = (W - tw) / 2 - bb[0], cy - th / 2 - bb[1]
        d.multiline_text((x, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=6, stroke_fill=INK, spacing=14, align="center")
        bar_w = min(tw, 220)
        d.rounded_rectangle([(W - bar_w) / 2, y + bb[3] + 26, (W + bar_w) / 2, y + bb[3] + 36], radius=5, fill=GOLD)
    else:
        size = {"hero": 112, "main": 100, "sub": 68}[style]
        band = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        bd = ImageDraw.Draw(band)
        for i in range(300):  # 下から上へ薄くなる帯(HUD は上の帯なので重ならない)
            a = int(175 * (1 - i / 300) ** 1.5)
            bd.line([(0, H - 1 - i), (W, H - 1 - i)], fill=(5, 8, 20, a))
        img.alpha_composite(band)
        d = ImageDraw.Draw(img)
        f = fit_font(d, text, size, W - 240)
        bb = d.multiline_textbbox((0, 0), text, font=f, stroke_width=6, spacing=14, align="center")
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
        x, y = (W - tw) / 2 - bb[0], H - 92 - th - bb[1]
        d.multiline_text((x, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=6, stroke_fill=INK, spacing=14, align="center")
        bar_w = min(tw, 260)
        d.rounded_rectangle([(W - bar_w) / 2, y + bb[1] - 30, (W + bar_w) / 2, y + bb[1] - 20], radius=5, fill=GOLD)
    img.save(path)


def badge_png(L, logo, path):
    """4:5 の下の帯に置く小さいロゴ(ゲームの画面の外。字幕の邪魔をしない)"""
    W, H = L["W"], L["H"]
    fx, fy, fw, fh = L["fg"]
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    lg = Image.open(logo).convert("RGBA")
    band_top, band_h = fy + fh, H - (fy + fh)
    s = min(460 / lg.width, (band_h * 0.62) / lg.height)
    lg = lg.resize((int(lg.width * s), int(lg.height * s)), Image.LANCZOS)
    img.alpha_composite(lg, ((W - lg.width) // 2, band_top + (band_h - lg.height) // 2))
    img.save(path)


def end_layers(L, logo, lines, work):
    """締め: ロゴ(大)と募集の2行。それぞれ別の PNG(順番に出す)"""
    W, H = L["W"], L["H"]
    x45 = H > W
    out = []
    lg = Image.open(logo).convert("RGBA")
    fx, fy, fw, fh = L["fg"]
    # 4:5: ロゴは上の帯、締めの言葉は下の帯(ゲームの画面に重ねない)。16:9: 画面の中央(暗くした場面の上)
    maxw, maxh = (980, fy - 40) if x45 else (1180, 470)
    s = min(maxw / lg.width, maxh / lg.height)
    lg = lg.resize((int(lg.width * s), int(lg.height * s)), Image.LANCZOS)
    ly = (fy - lg.height) // 2 + 6 if x45 else int(H * 0.40) - lg.height // 2
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    img.alpha_composite(lg, ((W - lg.width) // 2, ly))
    p = os.path.join(work, "end_logo.png"); img.save(p); out.append(p)
    y = (fy + fh + (H - fy - fh) // 2 - 40) if x45 else ly + lg.height + 24
    for i, ln in enumerate(lines):
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        size = ln.get("size", 80 if x45 else 76)
        f = fit_font(d, ln["text"], size, W - 100)
        bb = d.textbbox((0, 0), ln["text"], font=f, stroke_width=5)
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
        x = (W - tw) / 2 - bb[0]
        if ln.get("plate"):
            pad = 34
            d.rounded_rectangle([x + bb[0] - pad, y - 18, x + bb[2] + pad, y + th + 26], radius=20, fill=(196, 52, 40, 235), outline=GOLD, width=4)
        d.text((x, y - bb[1]), ln["text"], font=f, fill=ln.get("color_rgba", (255, 255, 255, 255)) if not ln.get("gold") else GOLD,
               stroke_width=5, stroke_fill=INK)
        p = os.path.join(work, f"end_line{i}.png"); img.save(p); out.append(p)
        y += th + (60 if x45 else 44)
    return out


def compose_cut(L, i, k, c, filters):
    """1つの場面の映像 → [v{i}](画面の大きさ)"""
    W, H = L["W"], L["H"]
    fx, fy, fw, fh = L["fg"]
    lay = H > W and "x45" or "x169"
    o = dict(c); o.update(c.get(lay, {}))  # レイアウト別の上書き(例: "x45": {"zoom": 1.1})
    z = o.get("zoom", 1.0)
    crop = ""
    if z > 1.001:
        cx, cy = o.get("cx", 0.5), o.get("cy", 0.5)
        crop = f"crop=w=iw/{z}:h=ih/{z}:x='max(0,min(iw-ow,iw*{cx}-ow/2))':y='max(0,min(ih-oh,ih*{cy}-oh/2))',"
    grade = (o["vf"] + ",") if o.get("vf") else ""
    if lay == "x169":
        filters.append(f"[{k}:v]{crop}{grade}scale={W}:{H}:flags=lanczos,format=yuv420p,setpts=PTS-STARTPTS,fps={FPS}[v{i}]")
    else:
        # 背景 = 同じ映像を大きくぼかして暗く(帯の中で動きが続く)、手前 = ゲームの画面
        cx, cy = o.get("cx", 0.5), o.get("cy", 0.5)
        if o.get("full"):
            fgf = f"{crop}scale={fw}:-2:flags=lanczos"
            oy = f"{fy}+({fh}-h)/2"
        else:
            a = fw / fh  # 4:3 の窓(zoom でさらに寄せる)
            fgf = (f"crop=w=ih*{a:.5f}/{z}:h=ih/{z}:x='max(0,min(iw-ow,iw*{cx}-ow/2))':y='max(0,min(ih-oh,ih*{cy}-oh/2))',"
                   f"scale={fw}:{fh}:flags=lanczos")
            oy = f"{fy}"
        filters.append(f"[{k}:v]{grade}split[p{i}a][p{i}b];"
                       f"[p{i}b]scale=-2:{H}:flags=bilinear,crop={W}:{H},boxblur=28:2,eq=brightness=-0.32:saturation=0.85[p{i}bg];"
                       f"[p{i}a]{fgf}[p{i}fg];"
                       f"[p{i}bg][p{i}fg]overlay=x={fx}:y={oy},format=yuv420p,setpts=PTS-STARTPTS,fps={FPS}[v{i}]")


def render(edl, layout, out, work, mode, gain_chain):
    L = LAYOUT[layout]
    W, H = L["W"], L["H"]
    raw = edl["raw"]
    video = mode == "video"
    inputs, filters, vl, al = [], [], [], []
    t = 0.0
    cuts = edl["cuts"]
    for i, c in enumerate(cuts):
        src = os.path.join(raw, c["shot"] + ".mkv")
        wav = os.path.join(raw, c["shot"] + ".wav")
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", src]
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", wav]
        k = i * 2
        if video:
            compose_cut(L, i, k, c, filters)
            vl.append(f"[v{i}]")
        g = c.get("se", 1.0)
        filters.append(f"[{k + 1}:a]aresample=48000,aformat=channel_layouts=stereo,volume={g},asetpts=PTS-STARTPTS,"
                       f"afade=t=in:st=0:d=0.012,afade=t=out:st={max(0, c['dur'] - 0.02):.3f}:d=0.02,apad=whole_dur={c['dur']:.3f}[a{i}]")
        al.append(f"[a{i}]")
        t += c["dur"]
    n = len(cuts)
    idx = n * 2
    # 締め(end): ゲームの場面を暗くしながら、ロゴ → 募集の2行
    end = edl["end"]
    ed = end["dur"]
    inputs += ["-ss", f'{end["in"]:.3f}', "-t", f"{ed:.3f}", "-i", os.path.join(raw, end["shot"] + ".mkv")]
    inputs += ["-ss", f'{end["in"]:.3f}', "-t", f"{ed:.3f}", "-i", os.path.join(raw, end["shot"] + ".wav")]
    if video:
        compose_cut(L, "e", idx, end, filters)
        dim, ramp = end.get("dim", 0.30), end.get("dim_ramp", 0.8)  # ロゴが読めるよう、少しずつ暗く(ゲームの動きは見せたまま)
        filters.append(f"[ve]eq=brightness='-{dim}*min(1,t/{ramp})':saturation='1-0.35*min(1,t/{ramp})':eval=frame,format=yuv420p[vend]")
        vl.append("[vend]")
    filters.append(f"[{idx + 1}:a]aresample=48000,aformat=channel_layouts=stereo,volume={end.get('se', 0.6)},asetpts=PTS-STARTPTS,"
                   f"afade=t=out:st={ed - 1.2:.3f}:d=1.2,apad=whole_dur={ed:.3f}[aend]")
    al.append("[aend]")
    idx += 2
    total = t + ed
    if video:
        filters.append("".join(vl) + f"concat=n={n + 1}:v=1:a=0[vcat]")
    filters.append("".join(al) + f"concat=n={n + 1}:v=0:a=1[se]")
    vcur = "[vcat]"

    def overlay_png(p, a, b, fi=0.18, fo=0.22, rise=18, tag="o"):
        nonlocal idx, vcur
        inputs.extend(["-loop", "1", "-t", f"{total:.3f}", "-framerate", str(FPS), "-i", p])
        if video:
            j = idx
            filters.append(f"[{j}:v]format=rgba,fade=t=in:st={a:.3f}:d={fi}:alpha=1,fade=t=out:st={b - fo:.3f}:d={fo}:alpha=1[c{j}]")
            # 入りは少し下から(字幕の動きだけ)
            filters.append(f"{vcur}[c{j}]overlay=x=0:y='{rise}*max(0,1-(t-{a:.3f})/{fi * 1.6:.3f})':enable='between(t,{a:.3f},{b:.3f})'[vo{j}]")
            vcur = f"[vo{j}]"
        idx += 1

    # 4:5 の下の帯の小さいロゴ(ゲームの場面の間だけ)
    if H > W and edl.get("badge", True):
        p = os.path.join(work, "badge.png"); badge_png(L, edl["logo"], p)
        overlay_png(p, 0.0, t + 0.25, fi=0.01, fo=0.25, rise=0)
    for j, cap in enumerate(edl["captions"]):
        p = os.path.join(work, f"cap_{layout}_{j}.png")
        caption_png(L, cap, p)
        overlay_png(p, cap["at"], cap["to"])
    lays = end_layers(L, edl["logo"], end["lines"], work)
    for j, p in enumerate(lays):
        a = t + (end["logo_at"] if j == 0 else end["lines"][j - 1]["at"])
        overlay_png(p, a, total + 1.0, fi=0.3 if j == 0 else 0.2, fo=0.01, rise=0 if j == 0 else 24)
    # BGM
    bl = []
    for j, b in enumerate(edl["bgm"]):
        loop = ["-stream_loop", "-1"] if b.get("loop") else []
        inputs += loop + ["-ss", f'{b.get("from", 0):.3f}', "-t", f'{b["dur"]:.3f}', "-i", b["file"]]
        fade_in, fade_out = b.get("fade_in", 0.05), b.get("fade_out", 1.6)
        filters.append(f"[{idx}:a]aresample=48000,aformat=channel_layouts=stereo,volume={b.get('gain', 0.5)},"
                       f"afade=t=in:st=0:d={fade_in},afade=t=out:st={b['dur'] - fade_out:.3f}:d={fade_out},"
                       f"adelay={int(b.get('at', 0) * 1000)}|{int(b.get('at', 0) * 1000)},apad=whole_dur={total:.3f}[b{j}]")
        bl.append(f"[b{j}]")
        idx += 1
    filters.append(f"[se]volume={edl.get('se_gain', 1.0)}[se2]")
    filters.append("[se2]" + "".join(bl) + f"amix=inputs={1 + len(bl)}:normalize=0:duration=first,alimiter=limit=0.93,atrim=0:{total:.3f}{gain_chain}[aout]")
    fcp = os.path.join(work, f"filter_{layout}_{mode}.txt")
    open(fcp, "w", encoding="utf-8").write(";\n".join(filters))
    if video:
        cmd = [FF, "-y", "-loglevel", "error", "-nostdin"] + inputs + ["-/filter_complex", fcp, "-map", vcur, "-map", "[aout]",
               "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", "-r", str(FPS), "-profile:v", "high",
               "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", out]
    else:
        cmd = [FF, "-y", "-loglevel", "error", "-nostdin"] + inputs + ["-/filter_complex", fcp, "-map", "[aout]", "-c:a", "pcm_s16le", "-ar", "48000", out]
    print(f"[{layout} {mode}] total {total:.2f}s, {n} cuts + end")
    r = subprocess.run(cmd)
    if r.returncode != 0:
        sys.exit(f"ffmpeg failed ({r.returncode})")
    return total


def measure(path):
    r = subprocess.run([FF, "-hide_banner", "-nostdin", "-i", path, "-af", "ebur128=framelog=quiet:peak=true", "-f", "null", "-"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    s = r.stderr[r.stderr.rindex("Summary:"):]
    return float(s.split("I:")[1].split("LUFS")[0]), float(s.split("Peak:")[1].split("dBFS")[0])


def main():
    edl_path, layout, out = sys.argv[1], sys.argv[2], sys.argv[3]
    edl = json.load(open(edl_path, encoding="utf-8"))
    work = os.path.join(os.path.dirname(os.path.abspath(out)), "_work_" + layout)
    os.makedirs(work, exist_ok=True)
    loud = edl.get("loudness", -14)
    lim = 10 ** (edl.get("tp", -1.5) / 20.0)
    chain = lambda g: f",volume={g:.2f}dB,aresample=192000,alimiter=limit={lim:.4f}:attack=1:release=60:level=false,aresample=48000"
    tmp = os.path.join(work, "measure.wav")
    render(edl, layout, tmp, work, "audio", "")
    i0, tp0 = measure(tmp)
    g = loud - i0
    for _ in range(2):
        render(edl, layout, tmp, work, "audio", chain(g))
        i1, tp1 = measure(tmp)
        g += loud - i1
    print(f"loudness {i0:.2f} LUFS / TP {tp0:.2f} -> gain {g:.2f} dB (check {i1:.2f} / TP {tp1:.2f})")
    render(edl, layout, out, work, "video", chain(g))


if __name__ == "__main__":
    main()
