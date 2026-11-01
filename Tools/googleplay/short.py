# -*- coding: utf-8 -*-
# Google Play の操作説明ショート(2026-11-01、縦 1080x1920・60fps・約22秒)。
#  撮影用ビルドの台本 gp_tutorial(QaSweep.StoreCapture)の映像 + [flick] の記録(何コマ目に、どの向き)から組み立てる。
#  ・映像: 縦の窓(608x1080)を 4倍で撮った 2432x4320 から、キャラの周りを切り出して 1080x1920 へ(ゲームのカメラは変えない。表示の拡大だけ)
#  ・打ち上げ/急降下は 0.6倍、空中コンボは 0.4倍のスロー(その間はゲームの音を消し、BGM だけ)
#  ・指とフリックの矢印(gesture.py の絵)、字幕(captions_short.tsv)、最後にロゴ(タイトルと同じ TitleLogoV2)は映像に後から重ねる。ゲームには描かない
#  ・冒頭 4 秒に「映像を拡大している」注記、最後に「技はキャラクターによって異なる」(2026-11-01 エステル/マスターのレビュー)
#  ・音: ゲームの効果音 + BGM(権利を確認済みの自作曲)→ -14 LUFS、真のピーク -1.2 dBTP 以下(Tools/steam/finalize_audio.py)
#   python short.py <撮影のフォルダ(out/raw と log.txt がある)> <ja|en> <出力.mp4> <作業フォルダ>
import csv, io, os, re, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FF = os.environ.get("FFMPEG", "ffmpeg")
FONT = os.environ.get("CAPTION_FONT", r"C:\Windows\Fonts\NotoSansJP-VF.ttf")
LOGO = os.path.join(HERE, "..", "..", "Unity", "2ndAction", "Assets", "Art", "UI", "TitleLogoV2.png")
BGM = os.path.join(HERE, "..", "..", "Unity", "2ndAction", "Assets", "Audio", "Placeholder", "Bgm", "bgm_wasteland_middle_n.wav")
GESTURE = os.environ.get("GESTURE_DIR", "")
OW, OH, FPS = 1080, 1920, 60
# 切り出し(元の映像に対する割合 (幅=高さの割合, 左, 上)。縦横比はそのまま)。縦画面のカメラではキャラが小さいので寄せる
#  ふつう: キャラの周り(約2.4倍)。空中コンボ: 上の HIT の数も入るように(約1.7倍)
NEAR = (0.42, 0.12, 0.40)
COMBO = (0.60, 0.14, 0.17)


def font(size):
    f = ImageFont.truetype(FONT, size)
    try: f.set_variation_by_name("Bold")
    except Exception: pass
    return f


def flicks(log):
    out = []
    for line in io.open(log, encoding="utf-8", errors="replace"):
        m = re.search(r"\[flick\] (\w+) frame (\d+)", line)
        if m: out.append((m.group(1).lower(), int(m.group(2))))
    return out


def caption_layer(text, sub=None):
    img = Image.new("RGBA", (OW, OH), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    lines = text.split("\\n")
    size = 72
    while size > 40 and max(ImageDraw.Draw(img).textlength(ln, font=font(size)) for ln in lines) > OW - 140: size -= 2  # 入りきらない(英語)時は縮める
    f = font(size)
    y = 250
    for ln in lines:
        tw = d.textlength(ln, font=f)
        d.rounded_rectangle([(OW - tw) / 2 - 34, y - 22, (OW + tw) / 2 + 34, y + 100], radius=18, fill=(5, 8, 20, 185))
        d.text(((OW - tw) / 2, y), ln, font=f, fill=(255, 255, 255, 255), stroke_width=5, stroke_fill=(10, 12, 24, 255))
        y += 128
    d.rectangle([OW / 2 - 70, 222, OW / 2 + 70, 230], fill=(232, 196, 102, 255))
    return img


def note_layer(text):
    # 冒頭に小さく出す注記(映像を拡大していること)。画面の下、暗い帯の上
    img = Image.new("RGBA", (OW, OH), (0, 0, 0, 0)); d = ImageDraw.Draw(img); f = font(34)
    tw = d.textlength(text, font=f)
    d.rounded_rectangle([(OW - tw) / 2 - 22, OH - 186, (OW + tw) / 2 + 22, OH - 120], radius=12, fill=(5, 8, 20, 160))
    d.text(((OW - tw) / 2, OH - 178), text, font=f, fill=(235, 235, 240, 255))
    return img


def end_layer(text, a, sub=None):
    img = Image.new("RGBA", (OW, OH), (5, 8, 20, int(200 * a)))
    logo = Image.open(LOGO).convert("RGBA")
    lw = int(OW * 0.86); lh = int(logo.height * lw / logo.width)
    logo = logo.resize((lw, lh), Image.LANCZOS)
    if a < 1: logo.putalpha(logo.getchannel("A").point(lambda v: int(v * a)))
    img.alpha_composite(logo, ((OW - lw) // 2, int(OH * 0.36) - lh // 2))
    d = ImageDraw.Draw(img); f = font(64)
    tw = d.textlength(text, font=f)
    d.text(((OW - tw) / 2, int(OH * 0.36) + lh // 2 + 70), text, font=f, fill=(255, 255, 255, int(255 * a)), stroke_width=4, stroke_fill=(10, 12, 24, int(255 * a)))
    if sub:  # 2026-11-01: 技はキャラクターによって異なる、を短く小さく
        fs = font(38); sw = d.textlength(sub, font=fs)
        d.text(((OW - sw) / 2, int(OH * 0.36) + lh // 2 + 190), sub, font=fs, fill=(225, 225, 232, int(235 * a)))
    return img


def main():
    shot, lang, out, work = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    os.makedirs(work, exist_ok=True)
    caps = {r["id"]: r for r in csv.DictReader(io.open(os.path.join(HERE, "captions_short.tsv"), encoding="utf-8"), delimiter="\t")}
    T = lambda k: caps[k][lang]
    raw = os.path.join(shot, "out", "raw", "gp_tutorial")
    fl = flicks(os.path.join(shot, "log.txt"))
    fF = [f for d, f in fl if d == "forward"]; fB = [f for d, f in fl if d == "backward"][0]
    fU = [f for d, f in fl if d == "up"][0]; fD = [f for d, f in fl if d == "down"][0]
    f0 = fF[0]; air = [f for f in fF if f > fU]
    s = lambda fr: fr / FPS
    # 区間(元の秒, 終わり, 速さ, 字幕の id)
    segs = [
        (0.3, s(f0) - 0.9, 1.0, "run"),
        (s(f0) - 0.9, s(f0) + 1.9, 1.0, "fwd"),
        (s(fB) - 0.6, s(fB) + 2.0, 1.0, "back"),
        (s(fU) - 1.0, s(fU) + 0.35, 0.6, "up"),      # 打ち上げは 0.6倍
        (s(fU) + 0.35, s(fD) - 0.1, 0.4, "combo"),   # 空中コンボは 0.4倍
        (s(fD) - 0.1, s(fD) + 1.6, 0.6, "down"),     # 急降下は 0.6倍
        (s(fD) + 1.6, s(fD) + 5.1, 1.0, "end"),
    ]
    crops = {"combo": COMBO}
    # 出力の時間に置き換えた、フリックの時刻
    def out_t(src):
        t = 0.0
        for a, b, sp, _ in segs:
            if a <= src < b: return t + (src - a) / sp
            t += (b - a) / sp
        return None
    gestures = [(d, out_t(s(f))) for d, f in fl]
    total = sum((b - a) / sp for a, b, sp, _ in segs)
    print(f"[short] {lang} segments {[round((b - a) / sp, 2) for a, b, sp, _ in segs]} total {total:.2f}s flicks {[(d, round(t, 2)) for d, t in gestures]}")

    # 1) 映像: 区間を切り出して拡大・速さ → つなぐ
    vf = []
    for i, (a, b, sp, k) in enumerate(segs):
        z, cx, cy = crops.get(k, NEAR)
        vf.append(f"[0:v]trim=start={a:.4f}:end={b:.4f},setpts=(PTS-STARTPTS)/{sp},crop=iw*{z}:ih*{z}:iw*{cx}:ih*{cy},scale={OW}:{OH}:flags=lanczos,fps={FPS},format=yuv420p[v{i}]")
    vf.append("".join(f"[v{i}]" for i in range(len(segs))) + f"concat=n={len(segs)}:v=1:a=0[vout]")
    base = os.path.join(work, f"base_{lang}.mp4")
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", raw + ".mkv", "-filter_complex", ";".join(vf), "-map", "[vout]", "-c:v", "libx264", "-crf", "12", "-preset", "medium", base], check=True)

    # 2) 重ねる絵(字幕・指と矢印・最後のロゴ)を1コマずつ作って、透明つきの動画に
    n = int(round(total * FPS))
    starts, t = [], 0.0
    for a, b, sp, k in segs:
        starts.append((t, t + (b - a) / sp, k)); t += (b - a) / sp
    cap_cache = {}
    gfr = {}
    if GESTURE:
        for d in ("forward", "backward", "up", "down"):
            gfr[d] = [Image.open(os.path.join(GESTURE, f"flick_{d}_{k:02d}.png")).convert("RGBA") for k in range(18)]
    ov = os.path.join(work, f"overlay_{lang}.mov")
    p = subprocess.Popen([FF, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{OW}x{OH}", "-r", str(FPS), "-i", "-", "-c:v", "qtrle", ov], stdin=subprocess.PIPE)
    for i in range(n):
        tt = i / FPS
        img = Image.new("RGBA", (OW, OH), (0, 0, 0, 0))
        key = next((k for a, b, k in starts if a <= tt < b), "end")
        if key == "end":
            a0 = next(a for a, b, k in starts if k == "end")
            img.alpha_composite(end_layer(T("cap_end"), min(1.0, (tt - a0) / 0.5), T("cap_moves")))
        else:
            ck = {"run": "cap_run", "fwd": "cap_fwd", "back": "cap_back", "up": "cap_up", "combo": "cap_combo", "down": "cap_down"}[key]
            if ck not in cap_cache: cap_cache[ck] = caption_layer(T(ck))
            img.alpha_composite(cap_cache[ck])
            if tt < 4.0:  # 冒頭の 4 秒: 映像を拡大している旨
                if "note" not in cap_cache: cap_cache["note"] = note_layer(T("cap_zoom"))
                nl = cap_cache["note"]
                if tt > 3.5: nl = nl.copy(); nl.putalpha(nl.getchannel("A").point(lambda v, f=(4.0 - tt) / 0.5: int(v * f)))
                img.alpha_composite(nl)
            for d, gt in gestures:
                if gt is None: continue
                k = int((tt - gt) * FPS)
                if 0 <= k < 30 and d in gfr:  # 0.3 秒の動き + 0.2 秒そのまま
                    g = gfr[d][min(k, 17)]
                    if k >= 18: g = g.copy(); g.putalpha(g.getchannel("A").point(lambda v, f=1 - (k - 17) / 12: int(v * f)))
                    img.alpha_composite(g, ((OW - g.width) // 2, int(OH * 0.70) - g.height // 2))
        p.stdin.write(img.tobytes())
    p.stdin.close(); p.wait()

    # 3) 音: 区間ごとのゲームの音(スローの区間は無音)+ BGM
    af = []
    for i, (a, b, sp, _) in enumerate(segs):
        if sp == 1.0: af.append(f"[1:a]atrim=start={a:.4f}:end={b:.4f},asetpts=PTS-STARTPTS[a{i}]")
        else: af.append(f"anullsrc=r=48000:cl=stereo,atrim=duration={(b - a) / sp:.4f}[a{i}]")
    af.append("".join(f"[a{i}]" for i in range(len(segs))) + f"concat=n={len(segs)}:v=0:a=1,aformat=sample_rates=48000:channel_layouts=stereo[se]")
    af.append(f"[2:a]atrim=duration={total:.3f},volume=0.36,afade=t=in:d=0.2,afade=t=out:st={total - 1.6:.3f}:d=1.6,aformat=sample_rates=48000:channel_layouts=stereo[bgm]")
    mix = os.path.join(work, f"mix_{lang}.wav")
    # 音の大きさ: 紹介動画(trailer.py)と同じく、測ってから全体の音量を上げ下げ → 4倍の細かさで見るリミッター(-14 LUFS に合わせる)
    def render_mix(gain_db):
        chain = af + [f"[se][bgm]amix=inputs=2:normalize=0,volume={gain_db:.2f}dB,aresample=192000,alimiter=limit=0.79:attack=1:release=60:level=false,aresample=48000[aout]"]
        subprocess.run([FF, "-y", "-loglevel", "error", "-i", base, "-i", raw + ".wav", "-stream_loop", "-1", "-i", BGM, "-filter_complex", ";".join(chain), "-map", "[aout]", "-ar", "48000", mix], check=True)
        r = subprocess.run([FF, "-hide_banner", "-i", mix, "-af", "ebur128=framelog=quiet", "-f", "null", "-"], capture_output=True, text=True, encoding="utf-8", errors="replace")
        return float(r.stderr[r.stderr.rindex("Summary:"):].split("I:")[1].split("LUFS")[0])
    g = 0.0
    for _ in range(3):
        g += -14.0 - render_mix(g)
    print(f"[short] mix gain {g:.2f} dB -> {render_mix(g):.2f} LUFS")

    # 4) 合わせる
    tmp = os.path.join(work, f"short_{lang}_tmp.mp4")
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", base, "-i", ov, "-i", mix, "-filter_complex", "[0:v][1:v]overlay=0:0:format=auto,format=yuv420p[v]",
                    "-map", "[v]", "-map", "2:a", "-c:v", "libx264", "-crf", "16", "-preset", "slow", "-profile:v", "high", "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", tmp], check=True)
    subprocess.run([sys.executable, "-I", os.path.join(HERE, "..", "steam", "finalize_audio.py"), tmp, mix, out], check=True)
    print("[short] wrote", out)


if __name__ == "__main__":
    main()
