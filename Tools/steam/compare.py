# 修正前後の比較画像(2026-10-23): 2本の動画の同じ場面を時刻で指定して並べる。
#   python compare.py <before.mp4> <after.mp4> <out.png> <title> "<before times,...>" "<after times,...>" [width=640]
import subprocess, sys, os, tempfile
from PIL import Image, ImageDraw, ImageFont

FF = os.environ.get("FFMPEG", "ffmpeg")
FONT = r"C:\Windows\Fonts\NotoSansJP-VF.ttf"


def grab(path, t, w):
    fd, tmp = tempfile.mkstemp(suffix=".png"); os.close(fd)
    subprocess.run([FF, "-y", "-nostdin", "-loglevel", "error", "-ss", f"{t:.3f}", "-i", path, "-frames:v", "1", "-vf", f"scale={w}:-1:flags=area", tmp], check=True)
    im = Image.open(tmp).convert("RGB"); im.load(); os.remove(tmp)
    return im


def main():
    a, b, out, title, ta, tb = sys.argv[1:7]
    w = int(sys.argv[7]) if len(sys.argv) > 7 else 640
    ta = [float(x) for x in ta.split(",")]; tb = [float(x) for x in tb.split(",")]
    n = max(len(ta), len(tb))
    h = int(w * 9 / 16)
    f = ImageFont.truetype(FONT, 22); fs = ImageFont.truetype(FONT, 18)
    top = 44
    img = Image.new("RGB", (w * n + 8 * (n - 1) + 120, top + (h + 30) * 2), (16, 18, 28))
    d = ImageDraw.Draw(img)
    d.text((10, 8), title, font=f, fill=(240, 220, 150))
    for row, (path, ts, lab) in enumerate([(a, ta, "修正前"), (b, tb, "修正後")]):
        y = top + row * (h + 30)
        d.text((10, y + h // 2 - 12), lab, font=f, fill=(255, 255, 255))
        for i, t in enumerate(ts):
            x = 120 + i * (w + 8)
            img.paste(grab(path, t, w), (x, y))
            d.text((x + 4, y + h + 4), f"{t:.1f} 秒", font=fs, fill=(200, 200, 200))
    img.save(out)
    print(out, img.size)


if __name__ == "__main__":
    main()
