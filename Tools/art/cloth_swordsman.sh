#!/bin/bash
# 黒剣士の布の見直し(2026-10-25)の仮反映の切り替え。.meta(ピボット・PPU・圧縮の設定)は触らず、PNG だけを入れ替える。
#   bash Tools/art/cloth_swordsman.sh apply    … 試作(立ち絵 v6 + 走り7コマ)を入れる
#   bash Tools/art/cloth_swordsman.sh restore  … 元の絵へ戻す
#   bash Tools/art/cloth_swordsman.sh status   … 今どちらが入っているか
# 入れ替えた後は Unity で読み込み直す(エディターを開く / バッチの -executeMethod なら自動)。
cd "$(dirname "$0")/../.."
A=Unity/2ndAction/Assets/Art
D=Docs/ClothRevision_2026-10-25/ingame
case "$1" in
  apply)   src=$D/candidate ;;
  restore) src=$D/original ;;
  status)
    if cmp -s $A/UI/Characters/swordsman_main.png $D/candidate/UI_Characters/swordsman_main.png; then echo "candidate (試作)"; \
    elif cmp -s $A/UI/Characters/swordsman_main.png $D/original/UI_Characters/swordsman_main.png; then echo "original (元)"; else echo "unknown"; fi
    exit 0 ;;
  *) echo "usage: $0 apply|restore|status"; exit 1 ;;
esac
for i in 00 01 02 03 04 05 06; do cp "$src/PlayerRun_v1/run_$i.png" "$A/PlayerRun_v1/run_$i.png"; done
cp "$src/UI_Characters/swordsman_main.png" "$A/UI/Characters/swordsman_main.png"
echo "$1 done: $(bash "$0" status)"
