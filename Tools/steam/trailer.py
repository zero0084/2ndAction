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


def caption_png(text, path, pos="bottom"):
    # 画面の下 1/4 の左寄り。暗い帯 + 白文字(縁取り)。HUD(上の帯/右下の一時停止)と重ならない位置
    # pos="top": 画面の下にゲームの板がある場面(カードの選択など)。上の HUD の下・中央に、暗い板を敷いて出す
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    if pos == "top":
        f = font(70)
        d = ImageDraw.Draw(img)
        tw = d.textlength(text, font=f)
        x, y = (W - tw) / 2, 150
        d.rounded_rectangle([x - 36, y - 30, x + tw + 36, y + 100], radius=14, fill=(5, 8, 20, 170))
        d.text((x, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=5, stroke_fill=(10, 12, 24, 255))
        d.rectangle([x, y - 24, x + 140, y - 17], fill=(232, 196, 102, 255))
        img.save(path)
        return
    band = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(band)
    for i in range(260):  # 下から上へ薄くなる帯
        a = int(160 * (1 - i / 260) ** 1.6)
        d.line([(0, H - 1 - i), (W, H - 1 - i)], fill=(5, 8, 20, a))
    img.alpha_composite(band)
    f = font(80)  # 2026-10-23 エステル: Steam の小さい再生画面(600px 前後)でも読める大きさに(64 → 80)
    d = ImageDraw.Draw(img)
    x, y = 96, H - 196
    d.text((x, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=5, stroke_fill=(10, 12, 24, 255))
    # 金の短い線(ゲームの UI の金色)
    d.rectangle([x, y - 24, x + 140, y - 17], fill=(232, 196, 102, 255))
    img.save(path)


def end_card(logo, path, tagline=None, scale=1.0):
    img = Image.new("RGBA", (W, H), (6, 9, 22, 255))
    lg = Image.open(logo).convert("RGBA")
    s = min(1300 / lg.width, 520 / lg.height) * scale  # scale: 2026-10-23 エステル(大きさの比較 1.0/1.2/1.4)。縦横比はそのまま
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
    mode = os.environ.get("TRAILER_MODE", "video")  # video / audio / stem_se / stem_bgm(音の大きさの確認用)
    loud = None if mode.startswith("stem") else edl.get("loudness")  # 目標の音の大きさ(LUFS)。2回: 測ってから合わせる
    ln = ""
    if loud is not None:
        # 2026-10-23: loudnorm は必要な上げ幅が大きいと動的な処理へ切り替わり、ピークが超えた。
        # 代わりに「全体の音量を上げる → 4倍の細かさで見るリミッター(真のピークを抑える)」を2回の測定で合わせる
        tmp = os.path.join(work, "measure.wav")

        def measure(chain):
            render(edl, lang, tmp, work, raw, caps, "audio", chain)
            r = subprocess.run([FF, "-hide_banner", "-i", tmp, "-af", "ebur128=framelog=quiet:peak=true", "-f", "null", "-"],
                               capture_output=True, text=True, encoding="utf-8", errors="replace")
            txt = r.stderr[r.stderr.rindex("Summary:"):]
            i = float(txt.split("I:")[1].split("LUFS")[0])
            tp = float(txt.split("Peak:")[1].split("dBFS")[0])
            return i, tp

        lim = 10 ** (edl.get("tp", -1.5) / 20.0)
        chain = lambda g: f",volume={g:.2f}dB,aresample=192000,alimiter=limit={lim:.4f}:attack=1:release=60:level=false,aresample=48000"
        i0, tp0 = measure("")
        g = loud - i0
        for _ in range(2):
            i1, tp1 = measure(chain(g))
            g += loud - i1
        ln = chain(g)
        print(f"loudness: before {i0:.2f} LUFS / TP {tp0:.2f} -> gain {g:.2f} dB (check {i1:.2f} LUFS / TP {tp1:.2f})")
    render(edl, lang, out, work, raw, caps, mode, ln)


def render(edl, lang, out, work, raw, caps, mode, ln):
    inputs, filters, vlabels, alabels = [], [], [], []
    video = mode == "video"
    t = 0.0
    cap_spans = []  # (id, start, end)
    for i, c in enumerate(edl["cuts"]):
        src = os.path.join(raw, c["shot"] + ".mkv")
        wav = os.path.join(raw, c["shot"] + ".wav")
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", src]
        inputs += ["-ss", f'{c["in"]:.3f}', "-t", f'{c["dur"]:.3f}', "-i", wav]
        k = i * 2
        if video:
            # zoom: 小さい再生画面でもキャラが分かるよう、実際の映像を少し寄せる(切り抜いて拡大。cx/cy は寄せる中心の割合)
            z = c.get("zoom", 1.0)
            crop = ""
            if z > 1.001:
                cx, cy = c.get("cx", 0.5), c.get("cy", 0.5)
                crop = (f"crop=w=iw/{z}:h=ih/{z}:x='max(0,min(iw-ow,iw*{cx}-ow/2))':y='max(0,min(ih-oh,ih*{cy}-oh/2))',"
                        f"scale={W}:{H}:flags=lanczos,")
            # vf: この場面だけの軽い調整(例: 暗い洞窟を少しだけ明るく "eq=gamma=1.08")
            grade = (c["vf"] + ",") if c.get("vf") else ""
            filters.append(f"[{k}:v]{crop}{grade}format=yuv420p,setpts=PTS-STARTPTS,fps={FPS}[v{i}]")
        filters.append(f"[{k + 1}:a]aresample=48000,asetpts=PTS-STARTPTS,apad=whole_dur={c['dur']:.3f}[a{i}]")
        vlabels.append(f"[v{i}]")
        alabels.append(f"[a{i}]")
        cid = c.get("caption")  # 無し: この場面は字幕を出さない(画面の説明の板と重なる時など)
        if cid:
            if cap_spans and cap_spans[-1][0] == cid and abs(cap_spans[-1][2] - t) < 1e-3:
                cap_spans[-1] = (cid, cap_spans[-1][1], t + c["dur"], cap_spans[-1][3], cap_spans[-1][4])
            else:
                cap_spans.append((cid, t, t + c["dur"], c.get("cap_pos", "bottom"), c.get("cap_max")))  # cap_max: この字幕だけの最長
        t += c["dur"]
    # 字幕は長い区間でも cap_max 秒まで(読み終えたら消す)
    cap_max = edl.get("cap_max")
    if cap_max:
        cap_spans = [(cid, a, min(b, a + (cm or cap_max)), ps, cm) for cid, a, b, ps, cm in cap_spans]
    n = len(edl["cuts"])
    base = n * 2  # 入力の番号: 切り出し(映像/音)の後
    # エンドカード
    endp = os.path.join(work, "end.png")
    end_card(edl["logo"], endp, edl.get("tagline", {}).get(lang), edl.get("logo_scale", 1.0))
    end_dur = edl.get("end", 4.5)
    inputs += ["-loop", "1", "-t", f"{end_dur:.3f}", "-framerate", str(FPS), "-i", endp]
    if video:
        filters.append(f"[{base}:v]format=yuv420p,fade=t=in:st=0:d=0.6[vend]")
    vlabels.append("[vend]")
    filters.append(f"anullsrc=r=48000:cl=stereo,atrim=0:{end_dur:.3f}[aend]")
    alabels.append("[aend]")
    total = t + end_dur
    if video:
        filters.append("".join(vlabels) + f"concat=n={n + 1}:v=1:a=0[vcat]")
    filters.append("".join(alabels) + f"concat=n={n + 1}:v=0:a=1[se]")
    idx = base + 1
    # 字幕
    vcur = "[vcat]"
    for j, (cid, a, b, ps, _) in enumerate(cap_spans):
        p = os.path.join(work, f"cap_{j}.png")
        caption_png(caps[cid][lang], p, ps)
        inputs += ["-loop", "1", "-t", f"{total:.3f}", "-framerate", str(FPS), "-i", p]
        fi, fo = 0.25, 0.3
        if video:
            filters.append(f"[{idx}:v]format=rgba,fade=t=in:st={a:.3f}:d={fi}:alpha=1,fade=t=out:st={b - fo:.3f}:d={fo}:alpha=1[c{j}]")
            filters.append(f"{vcur}[c{j}]overlay=0:0:enable='between(t,{a:.3f},{b:.3f})'[vc{j}]")
            vcur = f"[vc{j}]"
        idx += 1
    # ロゴの前後の短い一言(end_caption = 字幕の表の id)。ロゴのフェードインの後に出て、ロゴの余韻のうちに消える
    ec = edl.get("end_caption")
    if ec:
        p = os.path.join(work, "end_caption.png")
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        f = font(56, "Medium")
        txt = caps[ec][lang]
        tw = d.textlength(txt, font=f)
        d.text(((W - tw) / 2, H * 0.78), txt, font=f, fill=(238, 240, 248, 255))
        img.save(p)
        inputs += ["-loop", "1", "-t", f"{total:.3f}", "-framerate", str(FPS), "-i", p]
        a0 = total - end_dur + edl.get("end_caption_at", 0.7)
        a1 = a0 + edl.get("end_caption_dur", 2.4)
        if video:
            filters.append(f"[{idx}:v]format=rgba,fade=t=in:st={a0:.3f}:d=0.4:alpha=1,fade=t=out:st={a1 - 0.5:.3f}:d=0.5:alpha=1[ecap]")
            filters.append(f"{vcur}[ecap]overlay=0:0:enable='between(t,{a0:.3f},{a1:.3f})'[vec]")
            vcur = "[vec]"
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
    se_gain = 0.0 if mode == "stem_bgm" else edl.get("se_gain", 1.0)
    filters.append(f"[se]volume={se_gain}[se2]")
    if mode == "stem_se":
        filters.append(f"[se2]atrim=0:{total:.3f}[aout]")
        for bl in blabels:
            filters.append(f"{bl}anullsink")
    else:
        filters.append("[se2]" + "".join(blabels) + f"amix=inputs={1 + len(blabels)}:normalize=0:duration=first,alimiter=limit=0.93,atrim=0:{total:.3f}{ln}[aout]")
    fc = ";\n".join(filters)
    fcp = os.path.join(work, f"filter_{mode}.txt")
    open(fcp, "w", encoding="utf-8").write(fc)
    if video:
        cmd = [FF, "-y", "-loglevel", "error"] + inputs + ["-/filter_complex", fcp, "-map", vcur, "-map", "[aout]",
               "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p", "-r", str(FPS), "-profile:v", "high",
               "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", out]
    else:
        cmd = [FF, "-y", "-loglevel", "error"] + inputs + ["-/filter_complex", fcp, "-map", "[aout]", "-c:a", "pcm_s16le", "-ar", "48000", out]
    print(f"[{mode}] total {total:.2f}s, {n} cuts, {len(cap_spans)} captions: " + ", ".join(f"{cid} {a:.1f}-{b:.1f}" for cid, a, b, _, __ in cap_spans))
    r = subprocess.run(cmd)
    if r.returncode != 0:
        sys.exit(f"ffmpeg failed ({r.returncode})")

if __name__ == "__main__":
    main()
