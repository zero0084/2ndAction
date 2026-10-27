"""新キャラ(ルカ / ペンタトゥットゥルー星人)の仮の素材を、基準絵の切り抜きから作る(2026-10-27)。
本番のアニメーションの絵ができたら、同じ名前のフォルダへ上書きする(フォルダ・ファイル名はほかのキャラと同じ決まり)。

  python placeholder.py <repo>

  Art/<Prefix>Run_v1/run_00.png ... Poses_v1/<pose>/<pose>_00.png など: 基準絵そのもの(同じ縮尺。PPU は CharacterDatabaseBuilder)
  Art/UI/Characters/<id>_portrait.png / <id>_main.png: キャラ選択の立ち絵(足元が下端)
"""
import os
import sys
from PIL import Image

REPO = sys.argv[1] if len(sys.argv) > 1 else r"C:\GameProject\2ndAction"
ART = os.path.join(REPO, "Unity", "2ndAction", "Assets", "Art")
BASE = os.path.join(REPO, "Docs", "NewCharacters_2026-10-26", "base_v1")

CHARS = [
    ("luka", "Luka", "luka_base_v1_cut.png", ["paw1", "paw2", "pounce", "tail", "leap", "dive", "scratch"]),
    ("pentatuttulu", "Penta", "penta_base_v1_cut.png", ["stretch1", "stretch2", "pokapoka", "bump", "upreach", "stomp", "kick"]),
]
FOLDERS = ["Run", "Jump", "Land", "Hurt", "Death", "Start", "FinishShort", "FinishMedium", "FinishLong", "FinishExtreme"]


def save(img, path, overwrite=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.exists(path) and not overwrite:
        print("keep", path)
        return
    img.save(path)
    print("wrote", path)


def main():
    for cid, prefix, src, poses in CHARS:
        im = Image.open(os.path.join(BASE, src)).convert("RGBA")
        for f in FOLDERS:
            d = os.path.join(ART, f"{prefix}{f}_v1")
            # 本番の絵が入ったフォルダは触らない
            if os.path.isdir(d) and any(n.endswith(".png") for n in os.listdir(d)):
                print("keep folder", d)
                continue
            save(im, os.path.join(d, f"{f.lower()}_00.png"))
        for p in poses:
            d = os.path.join(ART, f"{prefix}Poses_v1", p)
            if os.path.isdir(d) and any(n.endswith(".png") for n in os.listdir(d)):
                print("keep folder", d)
                continue
            save(im, os.path.join(d, f"{p}_00.png"))
        # キャラ選択の立ち絵: 700x1100 の中に足元を下端へ(ほかのキャラの _main と同じ大きさの枠)
        W, H = 700, 1100
        s = min((W - 40) / im.width, (H - 40) / im.height)
        fit = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.alpha_composite(fit, ((W - fit.width) // 2, H - fit.height - 20))
        ui = os.path.join(ART, "UI", "Characters")
        save(canvas, os.path.join(ui, f"{cid}_main.png"))
        save(canvas, os.path.join(ui, f"{cid}_portrait.png"))


if __name__ == "__main__":
    main()
