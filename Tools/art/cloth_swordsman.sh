#!/bin/bash
# 黒剣士の布の見直し(2026-10-25)の仮反映の切り替え。.meta(ピボット・PPU・圧縮の設定)は触らず、PNG だけを入れ替える。
#   bash Tools/art/cloth_swordsman.sh apply    … 試作を入れる(Docs/ClothRevision_2026-10-25/ingame/candidate/Art/ の PNG を Assets/Art/ の同じ場所へ)
#   bash Tools/art/cloth_swordsman.sh restore  … 元の絵へ戻す(ingame/original/Art/ から)
#   bash Tools/art/cloth_swordsman.sh status [-v] … 今どちらが入っているか(-v で1枚ずつ)
# 試作が無いコマは元のまま。入れ替えた後は Unity で読み込み直す(バッチの -executeMethod なら自動)。
#
# 2026-10-26 正式採用(マスターが APK 374 を実機で確認): 全12人の 589 枚はリポジトリの Assets に入った(通常のビルドに入る)。
#   ・ingame/candidate/Art = 採用した絵(Assets と同じ)、ingame/original/Art = 旧い絵の控え(消さない)
#   ・restore は旧い絵へ戻すので、うっかり戻さないよう `restore --old` と明示した時だけ動く
#   ・status は「採用」「旧」で数える。採用 589 / 旧 0 が正しい状態
cd "$(dirname "$0")/../.." || exit 1
A=Unity/2ndAction/Assets/Art
D=Docs/ClothRevision_2026-10-25/ingame
files() { (cd "$D/candidate/Art" && find . -mindepth 2 -name "*.png" | sed 's#^\./##' | sort); }
case "$1" in
  apply|restore)
    if [ "$1" = restore ] && [ "$2" != "--old" ]; then
      echo "restore は旧い絵へ戻します(全12人の布の絵は 2026-10-26 に正式採用済み)。本当に戻す時だけ: $0 restore --old"; exit 1
    fi
    src="$D/candidate/Art"; [ "$1" = restore ] && src="$D/original/Art"
    n=0; for f in $(files); do cp "$src/$f" "$A/$f"; n=$((n+1)); done
    echo "$1 done ($n files)"; bash "$0" status ;;
  status)
    c=0; o=0; u=0
    for f in $(files); do
      if cmp -s "$A/$f" "$D/candidate/Art/$f"; then c=$((c+1)); s=candidate; elif cmp -s "$A/$f" "$D/original/Art/$f"; then o=$((o+1)); s=original; else u=$((u+1)); s=UNKNOWN; fi
      [ "$2" = "-v" ] && echo "$s $f"
    done
    echo "candidate(採用) $c / original(旧の控え) $o / unknown $u" ;;
  *) echo "usage: $0 apply|restore --old|status [-v]"; exit 1 ;;
esac
