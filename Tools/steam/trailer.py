# 紹介動画の組み立て(2026-10-23)。撮影素材(raw/<shot>.mkv + .wav = SE/環境音)を切り出してつなぎ、
# 字幕(文字の表 captions.tsv の ja/en)とゲームの BGM を重ね、最後に公式ロゴ。
#   python trailer.py <edl.json> <lang ja|en> <out.mp4>
# edl.json: {"raw": "<raw dir>", "bgm": [{"file": "...wav", "at": 0, "from": 0, "dur": 40, "gain": 0.7}],
#            "cuts": [{"shot": "run_wasteland", "in": 1.2, "dur": 3.0, "caption": "cap_run"}],
#            "logo": "...TitleLogoV2.png", "end": 4.5}
# 字幕は同じ id が続く間は1回だけ出す(切り替わり時にフェード)。文字を変えたら captions.tsv を直して再実行するだけ
import json, os, subprocess, sys, csv
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
FF = os.environ.get("FFMPEG", "ffmpeg")
FONT = os.environ.get("CAPTION_FONT", r"C:\Windows\Fonts\NotoSansJP-VF.ttf")
W, H, FPS = 1920, 1080, 60


def load_captions(path):
    with open(path, encoding="utf-8") as f:
        return {r["id"]: r for r in csv.DictReader(f, delimiter="\t")}


def font(size, weight="Bold"):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name(weight)
    except Exception:
        pass
    return f


def caption_png(text, path):
    # 画面の下 1/4 の左寄り。暗い帯 + 白文字(縁取り)。HUD(上の帯/右下の一時停止)と重ならない位置
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    band = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(band)
    for i in range(220):  # 下から上へ薄くなる帯
        a = int(150 * (1 - i / 220) ** 1.6)
        d.line([(0, H - 1 - i), (W, H - 1 - i)], fill=(5, 8, 20, a))
    img.alpha_composite(band)
    f = font(64)
    d = ImageDraw.Draw(img)
    x, y = 96, H - 168
    d.text((x, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=4, stroke_fill=(10, 12, 24, 255))
    # 金の短い線(ゲームの UI の金色)
    d.rectangle([x, y - 22, x + 120, y - 16], fill=(232, 196, 102, 255))
    img.save(path)


def end_card(logo, path, tagline=None):
    img = Image.new("RGBA", (W, H), (6, 9, 22, 255))
    lg = Image.open(logo).convert("RGBA")
    s = min(1300 / lg.width, 520 / lg.height)
    lg = lg.resize((int(lg.width * s), int(lg.height * s)), Image.LANCZOS)
    img.alpha_composite(lg, ((W - lg.width) // 2, (H - lg.height) // 2 - 40))
    if tagline:
        d = ImageDraw.Draw(img)
        f = font(44, "Medium")
        tw = d.textlength(tagline, font=f)
        d.text(((W - tw) / 2, (H + lg.height) // 2 + 10), tagline, font=f, fill=(232, 214, 160, 255))
    img.convert("RGB").save(path)


def main():
    edl_path, lang, out = sys.argv[1], sys.argv[2], sys.argv[3]
    edl = json.load(open(edl_path, encoding="utf-8"))
    caps = load_captions(edl["captions"])
    work = os.path.join(os.path.dirname(out), "_work_" + lang)
    os.makedirs(work, exist_ok=True)
    raw = edl["raw"]
    inputs, filters, vlabels, alabels = [], [], [], []
    t = 0.0
    cap_spans = []  # (id, start, end)
    for i, c in enumerate(edl["cuts"]):
        src = os.path.join(raw, c["shot"] + ".mkv")
        wav = os.path.join(raw, c["shot"] + ".wav")
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", src]
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", wav]
        k = i * 2
        filters.append(f"[{k}:v]format=yuv420p,setpts=PTS-STARTPTS,fps={FPS}[v{i}]")
        filters.append(f"[{k + 1}:a]aresample=48000,asetpts=PTS-STARTPTS,apad=whole_dur={c['dur']:.3f}[a{i}]")
        vlabels.append(f"[v{i}]")
        alabels.append(f"[a{i}]")
        cid = c.get("caption")
        if cid:
            if cap_spans and cap_spans[-1][0] == cid and abs(cap_spans[-1][2] - t) < 1e-3:
                cap_spans[-1] = (cid, cap_spans[-1][1], t + c["dur"])
            else:
                cap_spans.append((cid, t, t + c["dur"]))
        t += c["dur"]
    n = len(edl["cuts"])
    base = n * 2  # 入力の番号: 切り出し(映像/音)の後
    # エンドカード
    endp = os.path.join(work, "end.png")
    end_card(edl["logo"], endp, edl.get("tagline", {}).get(lang))
    end_dur = edl.get("end", 4.5)
    inputs += ["-loop", "1", "-t", f"{end_dur:.3f}", "-framerate", str(FPS), "-i", endp]
    filters.append(f"[{base}:v]format=yuv420p,fade=t=in:st=0:d=0.6[vend]")
    vlabels.append("[vend]")
    filters.append(f"anullsrc=r=48000:cl=stereo,atrim=0:{end_dur:.3f}[aend]")
    alabels.append("[aend]")
    total = t + end_dur
    filters.append("".join(vlabels) + f"concat=n={n + 1}:v=1:a=0[vcat]")
    filters.append("".join(alabels) + f"concat=n={n + 1}:v=0:a=1[se]")
    idx = base + 1
    # 字幕
    vcur = "[vcat]"
    for j, (cid, a, b) in enumerate(cap_spans):
        p = os.path.join(work, f"cap_{j}.png")
        caption_png(caps[cid][lang], p)
        inputs += ["-loop", "1", "-t", f"{total:.3f}", "-framerate", str(FPS), "-i", p]
        fi, fo = 0.25, 0.25
        filters.append(f"[{idx}:v]format=rgba,fade=t=in:st={a:.3f}:d={fi}:alpha=1,fade=t=out:st={b - fo:.3f}:d={fo}:alpha=1[c{j}]")
        filters.append(f"{vcur}[c{j}]overlay=0:0:enable='between(t,{a:.3f},{b:.3f})'[vc{j}]")
        vcur = f"[vc{j}]"
        idx += 1
    # BGM(ゲームの BGM を通しで)
    blabels = []
    for j, b in enumerate(edl["bgm"]):
        loop = ["-stream_loop", "-1"] if b.get("loop") else []  # ゲームの BGM はループ用に作ってある
        inputs += loop + ["-ss", f'{b.get("from", 0):.3f}', "-t", f'{b["dur"]:.3f}', "-i", b["file"]]
        fade_in = b.get("fade_in", 0.4)
        fade_out = b.get("fade_out", 1.5)
        filters.append(f"[{idx}:a]aresample=48000,aformat=channel_layouts=stereo,volume={b.get('gain', 0.7)},"
                       f"afade=t=in:st=0:d={fade_in},afade=t=out:st={b['dur'] - fade_out:.3f}:d={fade_out},"
                       f"adelay={int(b.get('at', 0) * 1000)}|{int(b.get('at', 0) * 1000)},apad=whole_dur={total:.3f}[b{j}]")
        blabels.append(f"[b{j}]")
        idx += 1
    filters.append(f"[se]volume={edl.get('se_gain', 1.0)}[se2]")
    filters.append("[se2]" + "".join(blabels) + f"amix=inputs={1 + len(blabels)}:normalize=0:duration=first,alimiter=limit=0.93,atrim=0:{total:.3f}[aout]")
    fc = ";\n".join(filters)
    fcp = os.path.join(work, "filter.txt")
    open(fcp, "w", encoding="utf-8").write(fc)
    cmd = [FF, "-y", "-loglevel", "error"] + inputs + ["-/filter_complex", fcp, "-map", vcur, "-map", "[aout]",
           "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p", "-r", str(FPS), "-profile:v", "high",
           "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", out]
    print(f"total {total:.2f}s, {n} cuts, {len(cap_spans)} captions")
    r = subprocess.run(cmd)
    if r.returncode != 0:
        sys.exit(f"ffmpeg failed ({r.returncode})")


if __name__ == "__main__":
    main()
