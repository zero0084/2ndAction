# 紹介動画 v6 / 第2トレーラー「設定・カスタマイズ」(2026-10-24)

## 1. 第1トレーラー v6(RESULT の見出しだけ)

- **変えたところ**: 最後の2カット(長押しで脱出 → RESULT)だけを撮り直して差し替え。ほかのカット・字幕・BGM・ロゴ・締めの一言「決めるのはあなたです。」/ "The choice is yours." は v5 と同じ
- **v5 はそのまま残してある**(正式候補、上書きなし): `trailer/omm_trailer_draft5_{ja,en}_endline_1080p60.mp4`、アーカイブ `SteamCaptureArchive/2026-10-23/final`
- **見出し**: GAME CLEAR → 脱出成功！ / ESCAPED!(ID 10059、30言語)。成功/失敗の判定・MILE・記録・保存は変えていない(表示の文字だけ)
- **完成**: `trailer/omm_trailer_v6_ja_1080p60.mp4`(68.0秒)、`trailer/omm_trailer_v6_en_1080p60.mp4`(64.5秒)
- **音**: 日本語 -14.1 LUFS / 真のピーク -1.2 dBTP、英語 -14.1 LUFS / -1.1 dBTP(目標 -1 dBTP 以下)
- **編集データ**: `trailer/edl_v6_{ja,en}.json`(v5 endline の最後の2カットの素材と開始位置だけ変更)
- **撮り直し**: `-scShots result`(脱出だけを撮る。戦いは平らな所で、長引いたら通常の撃破の演出つきで倒す)。日本語 cap_r8_ja、英語 cap_r9_en
- **比較画像**: `compare_v5_v6/result_v5_vs_v6_{ja,en}.png`(左 v5、右 v6)
- **エステルの見直し**: 「v5 より v6 の方が適切。ゲーム全体をクリアしたという誤解がなくなる。採用推奨」(v5 はバックアップとして残す)。**採用の決定はマスターへ**

## 2. 第2トレーラー「設定・カスタマイズ」(第2稿、34.4秒、日英)

| 時間 | 場面 | 字幕(日 / 英) |
|---|---|---|
| 0.0〜1.4 | オート OFF で走る | 操作は、あなたに合わせて。/ Play your way. |
| 1.4〜5.0 | 設定でオートモード・自動攻撃・自動回避を ON | オートモード・自動攻撃・自動回避 / Auto Mode, Auto Attack, Auto Avoid |
| 5.0〜9.0 | 閉じると自動で穴を跳び、攻撃する | ジャンプも攻撃も、自動で / Jumps and attacks, automated. |
| 9.0〜14.4 | 表示: 画面揺れ OFF・警告を輪郭のみ・発光 40% → プレイ | 画面揺れ・警告・発光を好みに / Tune screen shake, warnings and glow |
| 14.4〜20.1 | 音量: BGM 100 → 25%(約1.3秒そのまま)→ 75%、SE 75%(この場面だけゲームの BGM をそのまま聞かせる) | BGMと効果音は別々に調整 / Set music and SFX separately |
| 20.1〜29.9 | 縦画面のラン表示: 横から → 上下2段 → 斜め上から(縦の撮影を中央に、左右はぼかし) | 縦画面は3つの見え方から / Three views for portrait play |
| 29.9〜34.4 | ロゴ | 自分のペースで、自分の走りを。/ Your pace. Your way. |

- **完成**: `trailer_settings/omm_settings_s2_{ja,en}_1080p60.mp4`。音: 日本語 -14.3 LUFS / -2.2 dBTP、英語 -14.1 LUFS / -1.4 dBTP
- **第1稿も残してある**: `omm_settings_s1_{ja,en}_1080p60.mp4`(エステルの見直しの前)
- **エステルの見直しで直したところ**(第1稿 → 第2稿):
  - 設定の画面の上に出る字幕は、位置は同じ(左下)のまま、後ろに濃い帯を敷いた
  - 締めの一言を変えた(「どこまでも」は無限に走れる印象のため)
  - BGM 25% の状態を1秒以上聞かせるようにした
  - 表示の場面を少し短くした
  - 自動のプレイの場面の英語を "Jumps and attacks, automated." に
- **自動回避の言い方**: ゲーム内の説明どおり「必ず避けられるわけではない」ので、字幕は「ジャンプも攻撃も、自動で」にとどめた(エステル: 誇張なし)
- **撮影**: `QaSweep.StoreCaptureSettings.cs`(`-scShots set_auto,set_display,set_volume,set_portrait`)。設定は設定の画面のボタンと同じ処理(HighSpeedAssist / AudioManager / GameSettings / PortraitRunView)で切り替える。set_portrait は 608x1080 のウィンドウで撮る
- **編集データ**: `trailer_settings/edl_s2_{ja,en}.json`、字幕 `text/captions_settings.tsv`。`trailer.py` に `pillar`(縦長の撮影を中央に)と `cap_pos: band` を追加

## 3. 撮影中に見つけた不具合(修正済み)

- 設定の画面の「×」が、画面の拡大が1倍でない時(1080p 以上・スマホ)に、ボタンから離れた所へ斜めの線1本として描かれていた。`GUIUtility.RotateAroundPivot` が GUI.matrix の拡大の前の座標で回していたのが原因。今の座標の中で回すようにした(`UiIcons.DrawClose`)ので、すべての画面の × が直る。Android エミュレーター(1080x2400)でも直ったことを確認

## 4. 保管

- `C:/GameProject/SteamCaptureArchive/2026-10-24/`
  - `masters/`: v6 と設定紹介の編集データが使う撮影素材すべて(H.264 4:4:4 crf10 + wav)
  - `edit/`: 編集データ・字幕・道具
  - `final/`: v6 と設定紹介の第1稿・第2稿
- 10-23 のアーカイブ(v4/v5 と素材)には手を加えていない
- ディスクの空き: 途中でCドライブの空きが無くなった(残り 6MB)。消したのは作業用フォルダの中の、10-21〜10-23 の古い試験用ビルドの写し81個と、使わなくなった撮影の試し4フォルダだけ。動画・編集データ・バックアップ・編集データが使う素材は消していない。今は約 300GB 空いている
