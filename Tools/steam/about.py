# Steam の「このゲームについて」用のシステム紹介画像(2026-10-23)。
#   python about.py <layout.json> <lang ja|en> <outdir>
# 実際の撮影画像(stills)に、文字の表(about.tsv)の見出し/説明を重ねる。文字を差し替えたら再実行するだけ。
# layout.json: {"tsv": "...about.tsv", "width": 1232, "items": [
#   {"name": "omm_about_01_run", "title": "about_run_title", "body": "about_run_body",
#    "images": ["still.png"], "crop": [x, y, w, h], "arrows": true}]}
# images が複数なら横に並べる(コマ送り)。出力は <name>_<lang>.png(幅 1232)と _616.png(Steam の表示幅)
import csv, json, os, sys
from PIL import Image, ImageDraw, ImageFont

FONT = os.environ.get("CAPTION_FONT", r"C:\Windows\Fonts\NotoSansJP-VF.ttf")
NAVY = (12, 18, 40)
GOLD = (232, 196, 102)
TEXT = (238, 240, 248)


def font(size, weight="Bold"):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name(weight)
    except Exception:
        pass
    return f


def wrap(d, text, f, width, lang):
    # 日本語は文字ごと、英語は単語ごと
    lines, cur = [], ""
    units = list(text) if lang == "ja" else text.split(" ")
    sep = "" if lang == "ja" else " "
    for u in units:
        t = (cur + sep + u) if cur else u
        if d.textlength(t, font=f) <= width:
            cur = t
        else:
            lines.append(cur)
            cur = u
    if cur:
        lines.append(cur)
    # 行頭の句読点は前の行へ
    if lang == "ja":
        for i in range(1, len(lines)):
            while lines[i] and lines[i][0] in "、。」）!?！？…":
                lines[i - 1] += lines[i][0]
                lines[i] = lines[i][1:]
    return lines


def picture(item, W, base):
    paths = [os.path.join(base, p) for p in item["images"]]
    ims = [Image.open(p).convert("RGB") for p in paths]
    if "crop" in item:
        x, y, w, h = item["crop"]
        ims = [im.crop((x, y, x + w, y + h)) for im in ims]
    gap = 8
    n = len(ims)
    cw = (W - gap * (n - 1)) // n
    ch = int(round(cw * ims[0].height / ims[0].width))
    out = Image.new("RGB", (W, ch), NAVY)
    for i, im in enumerate(ims):
        out.paste(im.resize((cw, ch), Image.LANCZOS), (i * (cw + gap), 0))
        if n > 1 and item.get("step_numbers", True):
            d = ImageDraw.Draw(out)
            r = 26
            cx, cy = i * (cw + gap) + 18 + r, 18 + r
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=NAVY, outline=GOLD, width=3)
            f = font(30)
            s = str(i + 1)
            d.text((cx - d.textlength(s, font=f) / 2, cy - 20), s, font=f, fill=GOLD)
    return out


def compose(item, rows, lang, W, base):
    pic = picture(item, W, base)
    title = rows[item["title"]][lang]
    body = rows[item["body"]][lang]
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))
    tf, bf = font(46 if lang == "ja" else 44), font(28 if lang == "ja" else 27, "Regular")
    pad = 40
    tl = wrap(probe, title, tf, W - pad * 2, lang)
    bl = wrap(probe, body, bf, W - pad * 2, lang)
    th = len(tl) * 62
    bh = len(bl) * (46 if lang == "ja" else 42)
    H = pic.height + pad + 14 + th + 18 + bh + pad
    img = Image.new("RGB", (W, H), NAVY)
    img.paste(pic, (0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, pic.height, W, pic.height + 4], fill=GOLD)
    y = pic.height + pad
    d.rectangle([pad, y, pad + 90, y + 6], fill=GOLD)
    y += 14
    for line in tl:
        d.text((pad, y), line, font=tf, fill=GOLD)
        y += 62
    y += 18
    for line in bl:
        d.text((pad, y), line, font=bf, fill=TEXT)
        y += 46 if lang == "ja" else 42
    return img


def main():
    layout_path, lang, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    lay = json.load(open(layout_path, encoding="utf-8"))
    base = os.path.dirname(os.path.abspath(layout_path))
    with open(os.path.join(base, lay["tsv"]), encoding="utf-8") as f:
        rows = {r["id"]: r for r in csv.DictReader(f, delimiter="\t")}
    W = lay.get("width", 1232)
    os.makedirs(outdir, exist_ok=True)
    for item in lay["items"]:
        img = compose(item, rows, lang, W, os.path.join(base, lay.get("images_dir", ".")))
        p = os.path.join(outdir, f'{item["name"]}_{lang}.png')
        img.save(p)
        img.resize((616, int(round(img.height * 616 / W))), Image.LANCZOS).save(p.replace(".png", "_616.png"))
        print(p, img.size)


if __name__ == "__main__":
    main()
