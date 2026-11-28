# -*- coding: utf-8 -*-
# 宣伝動画「スマホでの遊び方」(2026-11-28、マスター/エステルの構成)。縦 1080x1920・60fps・約35秒、日本語。
#  撮影: 撮影用ビルド(BuildStoreCapture)の台本 pf_launch / pf_combo / pf_land(QaSweep.PromoFlick.cs)。
#   縦は 608x1080 の窓を 2倍(1216x2160)で、横は 1920x1080 で。縦の3つの見え方は -pview side|fold|oblique で別々に撮る。
#  指の表示: 撮影の記録 raw/<shot>_touch.tsv(各コマの指の位置/押しているか)どおりに、白い丸(置いた所)と金の矢印(動いた向き)を重ねる。
#   記録の無い所には何も足さない。ゲームの映像は等速(スロー/早送りなし)。
#  映像の置き方: 縦 = 画面全体をそのまま 1080x1920 へ(縦横比そのまま、切らない)。横 = 幅 1080 に収めて中央、上下の余白はぼかした同じ映像 + 字幕。
#  音: 各場面のゲームの効果音 + BGM(低め)→ -14 LUFS、真のピーク -1.2 dBTP 以下(Tools/steam/finalize_audio.py)。
#   python edit.py <edl.json> <出力.mp4> <作業フォルダ>     (edl.json は make_edl.py が撮影の記録から作る。手で直してよい)
import io, json, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
FF = os.environ.get("FFMPEG", "ffmpeg")
FONT = os.environ.get("CAPTION_FONT", r"C:\Windows\Fonts\NotoSansJP-VF.ttf")
LOGO = os.path.join(ROOT, "Unity", "2ndAction", "Assets", "Art", "UI", "TitleLogoV2.png")
BGM = os.path.join(ROOT, "Unity", "2ndAction", "Assets", "Audio", "Placeholder", "Bgm", "bgm_wasteland_middle_n.wav")
OW, OH, FPS = 1080, 1920, 60
LAND_H = int(round(OW * 1080 / 1920))      # 横画面を幅 1080 に収めた高さ(608)
LAND_Y = (OH - LAND_H) // 2


def font(size, weight="Bold"):
    f = ImageFont.truetype(FONT, size)
    try: f.set_variation_by_name(weight)
    except Exception: pass
    return f


def caption(img, text, y, size=74):
    d = ImageDraw.Draw(img); f = font(size, "Black")
    tw = d.textlength(text, font=f)
    while tw > OW - 120 and size > 44:
        size -= 2; f = font(size, "Black"); tw = d.textlength(text, font=f)
    d.rounded_rectangle([(OW - tw) / 2 - 36, y - 20, (OW + tw) / 2 + 36, y + size + 34], radius=20, fill=(6, 8, 22, 200))
    d.text(((OW - tw) / 2, y), text, font=f, fill=(255, 255, 255, 255), stroke_width=5, stroke_fill=(8, 10, 22, 255))
    d.rectangle([OW / 2 - 64, y - 34, OW / 2 + 64, y - 27], fill=(232, 196, 102, 255))


def label(img, text, y, size=50):
    # 縦の見え方の名前(設定画面と同じ表記)。字幕の下に小さめの金の札
    d = ImageDraw.Draw(img); f = font(size)
    tw = d.textlength(text, font=f)
    d.rounded_rectangle([(OW - tw) / 2 - 30, y - 12, (OW + tw) / 2 + 30, y + size + 26], radius=16, fill=(232, 196, 102, 235))
    d.text(((OW - tw) / 2, y), text, font=f, fill=(18, 14, 8, 255))


def touches(path):
    rows = []
    if not os.path.exists(path): return rows
    for ln in io.open(path, encoding="utf-8").read().splitlines()[1:]:
        p = ln.split("\t")
        if len(p) >= 4: rows.append((int(p[0]), p[1] == "1", float(p[2]), float(p[3])))
    # 1回の指(置く → 離す)ごとにまとめる
    strokes, cur = [], None
    for f, down, x, y in rows:
        if down:
            if cur is None: cur = {"pts": []}
            cur["pts"].append((f, x, y))
        elif cur is not None:
            cur["up"] = f; strokes.append(cur); cur = None
    if cur is not None: cur["up"] = cur["pts"][-1][0] + 1; strokes.append(cur)
    return strokes


def draw_touch(img, strokes, src_f, to_xy):
    # 指: 置いた所に白い丸、今の所まで金の線と矢印。離した後 0.3 秒で消える
    d = ImageDraw.Draw(img)
    for s in strokes:
        f0 = s["pts"][0][0]; up = s["up"]
        if src_f < f0 or src_f > up + 18: continue
        a = 1.0 if src_f <= up else max(0.0, 1.0 - (src_f - up) / 18.0)
        pts = [to_xy(x, y) for f, x, y in s["pts"] if f <= src_f] or [to_xy(s["pts"][0][1], s["pts"][0][2])]
        sx, sy = pts[0]; ex, ey = pts[-1]
        A = int(255 * a)
        r = 30
        d.ellipse([sx - r - 6, sy - r - 6, sx + r + 6, sy + r + 6], fill=(255, 255, 255, int(70 * a)))
        d.ellipse([sx - r, sy - r, sx + r, sy + r], outline=(255, 255, 255, A), width=6)
        if len(pts) >= 2 and (abs(ex - sx) + abs(ey - sy)) > 4:
            d.line([(sx, sy), (ex, ey)], fill=(240, 200, 90, A), width=12)
            import math
            ang = math.atan2(ey - sy, ex - sx); L = 34
            p1 = (ex + L * math.cos(ang), ey + L * math.sin(ang))
            p2 = (ex + L * 0.75 * math.cos(ang + 2.4), ey + L * 0.75 * math.sin(ang + 2.4))
            p3 = (ex + L * 0.75 * math.cos(ang - 2.4), ey + L * 0.75 * math.sin(ang - 2.4))
            d.polygon([p1, p2, p3], fill=(240, 200, 90, A))
            d.ellipse([ex - 12, ey - 12, ex + 12, ey + 12], fill=(255, 255, 255, A))


def end_card(a):
    img = Image.new("RGBA", (OW, OH), (5, 8, 20, int(215 * a)))
    logo = Image.open(LOGO).convert("RGBA")
    lw = int(OW * 0.86); lh = int(logo.height * lw / logo.width)
    logo = logo.resize((lw, lh), Image.LANCZOS)
    if a < 1: logo.putalpha(logo.getchannel("A").point(lambda v: int(v * a)))
    img.alpha_composite(logo, ((OW - lw) // 2, int(OH * 0.42) - lh // 2))
    return img


def main():
    edl_path, out, work = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(work, exist_ok=True)
    edl = json.load(io.open(edl_path, encoding="utf-8"))
    cap_dir = edl["capture_dir"]
    segs = edl["segments"]
    total = sum(s["end"] - s["start"] for s in segs)
    print(f"[promo] {len(segs)} segments, total {total:.2f}s")

    # 1) 映像: 区間ごとに等速で切り出し → 縦は全体を 1080x1920、横は中央 + ぼかした上下
    vf, inputs = [], []
    for i, s in enumerate(segs):
        inputs += ["-i", os.path.join(cap_dir, s["view"], "raw", s["shot"] + ".mkv")]
        t = f"trim=start={s['start']:.4f}:end={s['end']:.4f},setpts=PTS-STARTPTS,fps={FPS}"
        if s["layout"] == "land":
            vf.append(f"[{i}:v]{t},split[l{i}a][l{i}b]")
            vf.append(f"[l{i}a]scale={OW * 4}:{OH}:force_original_aspect_ratio=increase,crop={OW}:{OH},boxblur=24:2,eq=brightness=-0.22:saturation=0.8[bg{i}]")
            vf.append(f"[l{i}b]scale={OW}:{LAND_H}:flags=lanczos[fg{i}]")
            vf.append(f"[bg{i}][fg{i}]overlay=0:{LAND_Y},format=yuv420p,setsar=1[v{i}]")
        else:
            vf.append(f"[{i}:v]{t},scale={OW}:{OH}:flags=lanczos,format=yuv420p,setsar=1[v{i}]")
    vf.append("".join(f"[v{i}]" for i in range(len(segs))) + f"concat=n={len(segs)}:v=1:a=0[vout]")
    base = os.path.join(work, "base.mp4")
    subprocess.run([FF, "-y", "-loglevel", "error"] + inputs + ["-filter_complex", ";".join(vf), "-map", "[vout]", "-c:v", "libx264", "-crf", "12", "-preset", "medium", base], check=True)

    # 2) 重ねる絵(字幕・見え方の名前・指・最後のロゴ)
    strokes = {(s["view"], s["shot"]): touches(os.path.join(cap_dir, s["view"], "raw", s["shot"] + "_touch.tsv")) for s in segs}
    n = int(round(total * FPS))
    ov = os.path.join(work, "overlay.mov")
    p = subprocess.Popen([FF, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{OW}x{OH}", "-r", str(FPS), "-i", "-", "-c:v", "qtrle", ov], stdin=subprocess.PIPE)
    cache = {}
    seg_t = []
    t = 0.0
    for s in segs: seg_t.append((t, t + s["end"] - s["start"], s)); t += s["end"] - s["start"]
    end_from = total - edl.get("end_card_seconds", 3.0)
    for i in range(n):
        tt = i / FPS
        a0, a1, s = next(((a, b, s) for a, b, s in seg_t if a <= tt < b), seg_t[-1])
        src_f = int(round((s["start"] + (tt - a0)) * FPS))
        key = (s.get("caption", ""), s.get("label", ""), s["layout"], s.get("caption_y"), s.get("caption_size"))
        if key not in cache:
            img = Image.new("RGBA", (OW, OH), (0, 0, 0, 0))
            cy = s.get("caption_y", 250 if s["layout"] == "land" else 1600)  # 縦(横から見る): 道(画面の上から 78% = 1500px)より下の地面。上は HUD とコンボ数
            size = s.get("caption_size", 74)
            if s.get("caption"): caption(img, s["caption"], cy, size)
            if s.get("label"): label(img, s["label"], cy + size + 66)
            cache[key] = img
        img = cache[key].copy()
        if s["layout"] == "land": to_xy = lambda x, y: (x * OW, LAND_Y + (1 - y) * LAND_H)
        else: to_xy = lambda x, y: (x * OW, (1 - y) * OH)
        if s.get("touch", True): draw_touch(img, strokes.get((s["view"], s["shot"]), []), src_f, to_xy)
        if tt >= end_from:
            img = Image.alpha_composite(img, end_card(min(1.0, (tt - end_from) / 0.6)))
        p.stdin.write(img.tobytes())
    p.stdin.close(); p.wait()

    # 3) 音: 各区間のゲームの音(等速なのでそのまま)+ BGM
    af, ain = [], []
    for i, s in enumerate(segs):
        ain += ["-i", os.path.join(cap_dir, s["view"], "raw", s["shot"] + ".wav")]
        af.append(f"[{i}:a]atrim=start={s['start']:.4f}:end={s['end']:.4f},asetpts=PTS-STARTPTS,aformat=sample_rates=48000:channel_layouts=stereo,afade=t=in:d=0.03,afade=t=out:st={max(0.0, s['end'] - s['start'] - 0.03):.4f}:d=0.03[a{i}]")
    k = len(segs)
    af.append("".join(f"[a{i}]" for i in range(k)) + f"concat=n={k}:v=0:a=1[se]")
    bgm_vol = edl.get("bgm_volume", 0.30)
    af.append(f"[{k}:a]atrim=duration={total:.3f},volume={bgm_vol},afade=t=in:d=0.3,afade=t=out:st={total - 1.8:.3f}:d=1.8,aformat=sample_rates=48000:channel_layouts=stereo[bgm]")
    mix = os.path.join(work, "mix.wav")
    se_vol = edl.get("se_volume", 1.0)
    def render(gain_db):
        chain = af + [f"[se]volume={se_vol}[se2]", f"[se2][bgm]amix=inputs=2:normalize=0,volume={gain_db:.2f}dB,aresample=192000,alimiter=limit=0.79:attack=1:release=60:level=false,aresample=48000[aout]"]
        subprocess.run([FF, "-y", "-loglevel", "error"] + ain + ["-stream_loop", "-1", "-i", BGM, "-filter_complex", ";".join(chain), "-map", "[aout]", "-ar", "48000", mix], check=True)
        r = subprocess.run([FF, "-hide_banner", "-i", mix, "-af", "ebur128=framelog=quiet", "-f", "null", "-"], capture_output=True, text=True, encoding="utf-8", errors="replace")
        return float(r.stderr[r.stderr.rindex("Summary:"):].split("I:")[1].split("LUFS")[0])
    g = 0.0
    for _ in range(3): g += -14.0 - render(g)
    print(f"[promo] mix gain {g:.2f} dB -> {render(g):.2f} LUFS")

    # 4) 合わせる → 音の仕上げ
    tmp = os.path.join(work, "promo_tmp.mp4")
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", base, "-i", ov, "-i", mix, "-filter_complex", "[0:v][1:v]overlay=0:0:format=auto,format=yuv420p[v]",
                    "-map", "[v]", "-map", "2:a", "-r", str(FPS), "-c:v", "libx264", "-crf", "16", "-preset", "slow", "-profile:v", "high", "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", tmp], check=True)
    subprocess.run([sys.executable, "-I", os.path.join(ROOT, "Tools", "steam", "finalize_audio.py"), tmp, mix, out], check=True)
    print("[promo] wrote", out)


if __name__ == "__main__":
    main()
