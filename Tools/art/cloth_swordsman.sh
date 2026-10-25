#!/bin/bash
# 黒剣士の布の見直し(2026-10-25)の仮反映の切り替え。.meta(ピボット・PPU・圧縮の設定)は触らず、PNG だけを入れ替える。
#   bash Tools/art/cloth_swordsman.sh apply    … 試作を入れる(Docs/ClothRevision_2026-10-25/ingame/candidate/Art/ の PNG を Assets/Art/ の同じ場所へ)
#   bash Tools/art/cloth_swordsman.sh restore  … 元の絵へ戻す(ingame/original/Art/ から)
#   bash Tools/art/cloth_swordsman.sh status [-v] … 今どちらが入っているか(-v で1枚ずつ)
# 試作が無いコマは元のまま。入れ替えた後は Unity で読み込み直す(バッチの -executeMethod なら自動)。
# リポジトリへは「元」の状態でコミットする(途中の素材を配布ビルドに混ぜない)。
cd "$(dirname "$0")/../.." || exit 1
A=Unity/2ndAction/Assets/Art
D=Docs/ClothRevision_2026-10-25/ingame
files() { (cd "$D/candidate/Art" && find . -mindepth 2 -name "*.png" | sed 's#^\./##' | sort); }
case "$1" in
  apply|restore)
    src="$D/candidate/Art"; [ "$1" = restore ] && src="$D/original/Art"
    n=0; for f in $(files); do cp "$src/$f" "$A/$f"; n=$((n+1)); done
    echo "$1 done ($n files)"; bash "$0" status ;;
  status)
    c=0; o=0; u=0
    for f in $(files); do
      if cmp -s "$A/$f" "$D/candidate/Art/$f"; then c=$((c+1)); s=candidate; elif cmp -s "$A/$f" "$D/original/Art/$f"; then o=$((o+1)); s=original; else u=$((u+1)); s=UNKNOWN; fi
      [ "$2" = "-v" ] && echo "$s $f"
    done
    echo "candidate(試作) $c / original(元) $o / unknown $u" ;;
  *) echo "usage: $0 apply|restore|status [-v]"; exit 1 ;;
esac
