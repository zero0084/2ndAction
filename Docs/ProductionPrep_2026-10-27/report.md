# production への反映の準備(2026-10-27)

**production には何も変更していない。** 読んだだけ(読むだけの監査ツール `Editor/UgsProdAudit.cs`。環境の API は GET しか持たない)。
SOLO ランキングと公開の準備だけ。新しいランキングの種類(CO-OP / VERSUS / Season / 報酬 / Clan)は作っていない。

---

## 1. ランキング

| 経路 | 結果 |
|---|---|
| 通常 START → 脱出 | マスターが Android 実機で確認済み(APK 388) |
| 疾走出発 → 脱出 | マスターが Android 実機で確認済み(APK 388) |
| 途中で中断 → CONTINUE → 脱出 | **development で確認(Windows・Android の両方)**。下の表 |

### 途中セーブ → CONTINUE(実際の経路: START → 走る → 一時停止の「ホームへ戻る」= 中断の保存 → CONTINUE → 走る → 長押しの脱出 → RESULT → ホーム → ランキング)

| 試験 | 結果 |
|---|---|
| Windows・同じ起動の中で CONTINUE(180m で中断 → 421m で脱出) | 同じラン(台帳 `fed9f1fb`、`legacy=False`)のまま、`ranking=yes`、421m を送信 Accepted、「あなた: #2 421m」 |
| Windows・アプリを終了して起動し直し → CONTINUE(200m で中断 → 432m で脱出) | 同じラン(`9907afd5`)、`ranking=yes`、432m を送信 Accepted、「あなた: #2 432m」 |
| Android(エミュレーター、APK 389)・終了 → 起動し直し → CONTINUE(200m → 441m) | 同じラン(`a835505f`)、`ranking=yes`、送信中に画面を開いた → 送信 Accepted → 自動で読み直して「あなた: #2 441m」 |
| 起動し直した後の My Best / My Rank | Windows・Android とも表示される(クラウドから読める) |
| 自己ベスト未満(432m の後に CONTINUE → 268m で脱出) | 送らない(端末で自己ベストより短い)、表示は 432m のまま |

CONTINUE は中断の保存に入っている台帳(ランの ID・使った機能)をそのまま使うので、途中セーブしたことだけでは対象外にならない。
対象外になるのは、台帳の無い古い形式の中断データ(この仕組みより前の版で中断した物。使った機能を確かめられない)だけ。

### 最終的なランキングの対象 / 対象外(実装から書き出した)

**載る(すべて「正規の帰還」= 脱出の成功、またはラストダンジョンの終わり)**
- ソロ、通常 START → 脱出
- ソロ、疾走出発 → 脱出(出発の距離が、そのマップのサーバーに載っている自己ベスト以下であること ※)
- ソロ、正式な途中セーブ → CONTINUE → 脱出
- オート(自動操作補助)を使ったラン(載る。一覧に AUTO の印)
- 最高速度の設定(60〜120%)を変えたラン(載る)
- development では、開発版のテスト用データのラン
- そのマップの自己ベストを更新した時だけ送る(短い記録は送らない。サーバーでも書かない)

**載らない**
- GAME OVER(途中で倒れた)
- 無敵 ON(開発版)を一度でも使ったラン(途中で OFF にしても)
- 距離ワープ(開発版)を使ったラン
- Debug Run(ラスダンの終盤のデバッグワープ)/ 闘技場 / 操作の練習(チュートリアル)
- マルチプレイ(LOCAL / ONLINE)
- CPU サポート(仲間)を一度でも使ったラン
- production でのテスト用データ(テスト用データは開発版にしか無く、開発版は production へ送らない)
- 台帳の無い古い中断データからの再開
- 開発版の「全部選べる」で選んだ、まだ正式に解放していないマップ
- 開発版から production への送信(どの記録も送らない)/ 製品版から development への送信(送らない)
- サーバーの検査で弾かれる物(形式・距離の範囲・速すぎ・時刻のずれ・重複・ソロ以外・※の疾走の行き先)

ご認識との違い:
- **ご認識どおり**。補足は2点: 「疾走出発」は出発点がサーバー上の自己ベスト以下である必要がある(ランキングに参加する前の記録だけで疾走すると、送信が「疾走の行き先が確定した記録より先」で拒否されることがある)/ 「その他の DEBUG」の中身は上の一覧(無敵・ワープ・Debug Run)
- 「途中 GAME OVER」はランキングだけでなく、端末のマップ別 BEST も更新しない(仕様どおり)

---

## 2. development / production の差分

### サーバー(読むだけの監査の結果、production)

| 項目 | development | production |
|---|---|---|
| Cloud Code `omm_submit_run`(ランキングの送信・検査) | 配置済み・手元と同じ | **無い** |
| Cloud Code `omm_set_name`(コードネームの変更。キャラ/オート/疾走が消える不具合の修正入り) | 配置済み・修正版 | **無い** |
| Cloud Code `omm_delete_player`(データ削除) | 配置済み・本物で確認済み | **無い** |
| ランキングの表 `omm_best_{wasteland_road,natural_cave,sky_corridor,last_corridor}` | 4つ(高い順・Keep latest) | **無い** |
| Access Control `deny-player-leaderboard-writes`(プレイヤーが直接書けない) | あり | **無い** |
| Secret `OMM_ADMIN_BASIC` | 登録済み・読めることを確認 | 無い(これからマスターが登録) |

→ 今の製品版は、ランキングの送信も表示も production では動かない(表もスクリプトも無い)。データ削除も同じ(だから製品版はメールでの依頼を案内している)。

### アプリ(コード)

アプリのコードは development / production で共通(1つのソース)。次の製品版をビルドすると、ここまでの修正はすべて製品版にも入る。
環境は製品版が production に固定、開発版が development(起動引数で production は選べない)。

| 修正 | 製品版に入るか |
|---|---|
| ランキングの送信中に画面を開いた時の自動の読み直し(`RankingPanel`、443278b9) | 入る(次の製品版ビルドから) |
| 脱出の後の送信を最大30秒待たせない(`Leaderboard`、443278b9) | 入る |
| テスト用データのランを production でだけ除く(`Leaderboard.IsEligible`、52662b11) | 入る。製品版にはテスト用データが無いので、製品版では何も変わらない |
| コードネームの変更でキャラ/オート/疾走が消える | **サーバー側の修正**(`omm_set_name.js`)。production へ配置すると直る |
| データ削除の流れ(`AccountDeletion`) | 入っているが、製品版では `ProductionReady = false` で止めてある(メールの案内) |
| Privacy(ポリシーのリンク / 削除の画面 / Player ID の表示とコピー) | 製品版にある(削除のボタンだけ、production の確認まで出さない) |
| UGS 環境の切り替え | 製品版は production 固定(変えられない) |

---

## 3. production へ反映するもの / しないもの

### production へ反映する(マスターの「反映してよい」の後)
- サーバー: Cloud Code 3本(`Tools/ugs/cloudcode/omm_submit_run.js`、`omm_set_name.js`、`omm_delete_player.js`)/ ランキングの表4つ / Access Control(`Tools/ugs/access/omm_leaderboards.ac`)
- Secret `OMM_ADMIN_BASIC`(マスターが登録)
- アプリ: 次の製品版ビルド(上の修正はすべて入る)。データ削除は production での本物の試験の後に `ProductionReady = true`
- 必要なコミット: 443278b9(ランキングの画面・即時送信)、52662b11(テスト用データの扱い・診断)、ab96f189(コードネームの修正・削除の試験)、53a310ba(案内・表記)。ほかはすでに main に入っている

### production へ入れない(開発版だけ。製品版のビルドには入らない作り)
- ランキングの DIAG 表示(`RankingPanel.DrawDiag` は `#if UNITY_EDITOR || DEVELOPMENT_BUILD`)と `RankingDiag`(製品版では呼び出しごと消える `Conditional`)
- テスト用データ(開発版だけ。製品版は常に通常のデータ)。development でテスト用データを送れる特例は、製品版では働かない(テスト用データが無い + production では除く)
- 開発用のログの細かい部分(PlayerId は先頭6文字だけ、秘密は出さない)
- 開発版の環境の固定(development)と `-ugsEnv`
- 試験のコード(`QaSweep.*`、`NetAutoTest`、`AccountDeletion.DebugMock/DebugFailOnce`)はすべて `#if UNITY_EDITOR || DEVELOPMENT_BUILD`
- 配置・監査のツール(`Editor/UgsDevDeploy.cs`、`Editor/UgsProdAudit.cs`)はエディターだけ

### DIAG(開発版で維持)
開発版のランキングの画面で今のまま表示する(環境 / サインイン / Player ID の先頭 / ランキング ID / テスト用データ / 無敵 / 対象の判定 / 最後の送信の距離と結果 / My Rank・My Best)。
秘密・トークン・サービスアカウントの情報は持っていない。製品版には出ない(上の通りコンパイルで外れる)。

---

## 4. production のデータ削除の準備

### マスターの手順の確認(7つ)
**手順は正しい**です。補足:

1. production 用のサービスアカウントを作る — 正しい。サービスアカウントはプロジェクト単位(環境ごとではない)なので、development 用と別に作るのは鍵を分けるためで良い
2. One More Mile のプロジェクトに **Leaderboards Admin** だけ — 正しい(development で、この権限でスコアの削除が動くことを確認済み)。Cloud Save の削除は Cloud Code 自身の権限で行うので、Cloud Save の権限は要らない
3. production 用の Key を作る — 正しい。**Secret key は作った時の画面にしか出ないので、その場で控えて、チャット・リポジトリ・メモ帳のファイルには残さない**
4. `KeyID:SecretKey` を Base64 — 正しい。**末尾に改行を入れない**(例: `echo` は改行が付く。PowerShell なら `[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("<KeyID>:<SecretKey>"))` を画面で実行してコピー。履歴に残るのが気になる場合は実行後に PowerShell の履歴を消す)
5. production の環境の Secret として `OMM_ADMIN_BASIC` — 正しい。名前は大文字・アンダースコアまで完全一致。**environment の選択が production になっているかを登録の前に確認**
6. Access to Services = Cloud Code — 正しい
7. その後アイリーンが production へ配置 — 正しい。配置の前に読むだけの監査で Secret の名前が見えるかを確かめる(一覧が 404 の時は、配置の後の削除の試験で読めることを確かめる)

### production への配置の手順(アイリーン、「反映してよい」の後)
1. 読むだけの監査(今の状態の記録)
2. 配置の道具に production 用の入り口を足す(今は production を名前と ID の両方で拒否している。production 専用の確認の引数を2つ要る形にして、その時だけ書けるようにする)
3. 順番: ランキングの表4つ → Access Control(プレイヤーの直接書き込みの禁止)→ Cloud Code 3本(作成 → 公開)。
   Access Control を Cloud Code より先にする(スクリプトが動き始めた時に、直接書ける穴が開いていないように)
4. 読むだけの監査で、手元のファイルと同じかを確認
5. 下の最終試験

### ProductionReady を true にする時
production の Secret / Cloud Code の配置の後、**下の最終試験の削除(10〜14)が production の本物で通ってから**。
それまでは製品版はメールでの依頼の案内のまま(削除のボタンを出さない)。
試験のためには「ProductionReady を true にした、配らない確認用の製品版」(Windows の別名の試験用ビルド)を作って試し、通ったら true をコミットして本番の製品版を作る。

### production の最終試験の手順(専用の試験プレイヤー → 最後に正式な削除で全部消す)
前提: 製品版(production 接続)。開発版の自動試験は production へつながらない作りなので、ここは手で行う。
- Windows: 別名の試験用の製品版(製品名 "One More Mile Test" = 本物の保存と別のレジストリ → 新しい匿名プレイヤー)をアイリーンが操作
- Android: マスターの実機(製品版の確認用 APK)。本物の保存を変えないように、確認用の端末か、アプリのデータを控えてから

| # | 確認 | 見る所 |
|---|---|---|
| 1 | production の匿名サインイン | 画面(ランキングの参加)/ 読むだけの監査でアカウントがある |
| 2〜4 | 通常 START → 短く走る → 脱出 | RESULT |
| 5 | ランキングの送信 | 読むだけの監査で、その表に記録 |
| 6〜7 | My Best / My Rank、画面の即時表示 | ランキングの画面 |
| 8〜9 | コードネームの変更 → キャラ/オート/疾走が残る | ランキングの画面・監査の metadata |
| 10 | データ削除(設定 → データとプライバシー → データの削除) | 「サーバー上のデータを削除しました」 |
| 11〜13 | ランキング・コードネーム・送信の履歴・アカウントが消えた | 読むだけの監査(4つの表とも無し / Cloud Save 無し / アカウント無し) |
| 14 | 新しい Player ID | 起動しただけでは作らない → ランキングを開いた時に新しい ID |
| 15 | ランキングの再利用 | 新しいプレイヤーで送信・表示 |
| 16〜17 | ONLINE CREATE / FIND / JOIN、Relay の通信 | 2台(Windows の試験用の製品版 ×2、または Windows + Android) |
| 18 | Single Player | 普通に走る |
| 19 | LOCAL PLAY(DTLS) | 2台 |
| 20 | 端末の保存 | 削除の前後でカード・MILE・進行・設定が同じ |
| 後始末 | 15・16 で作った新しいプレイヤーも、もう一度正式な削除で消す → 監査で production に試験の記録が残っていないことを確認 |

---

## 5. 既存のデータ

### production(読むだけの監査、2026-10-27)
| もの | 結果 |
|---|---|
| ランキングの記録・コードネームの掲載 | **無い**(production にはランキングの表そのものが無い) |
| 送信の履歴(Cloud Save `omm_runs`) | この PC の前の production のプレイヤー(`PIQI…`、10/20 の控えから ID だけ読んだ)と、10/20 の試験の4人: どれも **無い** |
| 匿名アカウント | この5つは**ある**(匿名アカウントはプロジェクト全体で1つ。環境ごとではない) |

- 残っているもの: 匿名アカウント5つ(中身のデータは無い)
- 安全に消せるもの: 5つとも、Dashboard の Player Management から消せる(データが無いので、消しても誰の記録にも影響しない)。消さなくても公開には影響しない
- 本人の認証が無く、アプリから消せないもの: この5つ(サインインの記録が手元に無い)。アプリの削除の流れでは消せない
- この PC の前の production のサインインの記録が消えた理由は分からないまま(今回の試験は触っていない)。データが無いので実害は無い

### development の 10/20 の試験のアカウント
- 4つ(A・B・C・直接書き込みの試験の1つ)。ランキングは削除済み。A・B・直接書き込みの試験のプレイヤーには Cloud Save `omm_runs`(送ったランの ID の一覧)が残っている
- Dashboard の Player Management から消せる。公開には影響しないので、**消さなくて良い**(消す場合は、上の production の5つと同じ操作。4つは production の5つのうちの4つと同じアカウント)
- 今回の試験で作ったプレイヤー(Windows・Android とも)は、すべて正式な削除の流れで消し、管理側から無いことを確認した

---

## 6. Google Play の公開の準備との整合

| 項目 | 今の製品版 | production 反映後 |
|---|---|---|
| アプリ内のデータ削除 | 出さない(メールで依頼。Player ID を表示・コピー) | 最終試験の後に `ProductionReady = true` で出す |
| Player ID の表示/コピー | ある | ある |
| プライバシーポリシーのリンク | ある(設定 → データとプライバシー) | ある |
| LOCAL PLAY の暗号化 | DTLS(実機の2台は未確認) | 同じ |
| ONLINE PLAY | Relay(DTLS)| 同じ |
| ランキングのデータ | production に表が無く動かない | 動く |
| コードネーム | 端末に保存、参加した時だけ公開 | 同じ(変更でキャラ等が消えない) |
| 匿名の認証 | ONLINE / ランキング / 削除の画面を使った時だけ | 同じ |

Play Console(データセーフティ)と合わせる点:
- 「データの削除をリクエストできる」: 今はメール(+プライバシーポリシーのページ)。アプリ内の削除を有効にした製品版を出す時に「アプリ内で削除できる」を足し、**プライバシーポリシーの削除の説明も「アプリ内(設定 → データとプライバシー)またはメール」に更新する**(今のポリシーは「メールで依頼」)
- 「転送中のデータの暗号化」: ONLINE(Relay DTLS / HTTPS)と LOCAL(DTLS)。LOCAL の2台の実機の確認がまだ
- ランキングを production で動かすと、コードネームと記録(マップ別の距離・キャラ・オート/疾走)が「公開される」。ポリシーには記載済み(2026-10-18 の版)

---

## 7. 製品版ビルドの前の確認(production の反映が終わった後)

| 確認 | 今の状態(コードの確認) |
|---|---|
| development 専用のコードが製品の画面に出ない | DIAG・試験・テスト用データ・DEBUG パネルは `#if UNITY_EDITOR || DEVELOPMENT_BUILD`。製品版のビルドで実際に画面を見て確かめる |
| DIAG が出ない | `DrawDiag` はコンパイルで外れる |
| development へスコアを送らない | 製品版は production 固定。`MaySubmit` が「製品版で production 以外」を止める |
| production へ正しく接続 | 製品版の起動のログ `UGS Environment = production (release build, fixed)` |
| DEBUG 用の試験プレイヤーを作らない | 製品版は起動・削除の画面だけではサインインしない(ONLINE/ランキングを使った時だけ) |
| Secret が APK に入らない | アプリのコードに `OMM_ADMIN_BASIC` の値もサービスアカウントも無い(サーバーの Secret Manager だけ)。製品版の APK の中身を文字列で検索して確かめる |
| サービスアカウントの Secret がログに出ない | アプリは持っていない。Cloud Code のスクリプトも値をログ・返り値に出さない |

---

## 8. まだ必要な作業 / 実機で確認していただくこと / 既知の問題

### Google Play の公開の前に必要な作業
1. production 用のサービスアカウント・Key・Secret(マスター)
2. production への配置(アイリーン、「反映してよい」の後)→ 最終試験 → `ProductionReady = true` → 製品版のビルド
3. プライバシーポリシーの削除の説明の更新、Play Console のデータセーフティの回答の更新
4. アップロード鍵(前回の報告のとおり、まだ無い)/ Play Console / AdMob とストアの連携 / app-ads.txt(前回の広告の報告)
5. ライセンスの判断(前回の報告の6点)

### Android 実機で確認していただくこと
- 途中で中断 → CONTINUE → 脱出 → ランキング(APK 389 でも、388 でも同じ)
- LOCAL PLAY(DTLS)の2台
- production 反映後の最終試験(上の表)

### 既知の問題
- 製品版は今、production にランキングの表もスクリプトも無いので、ランキングを開くと読み込みに失敗する(反映すれば直る)
- 配置の道具は Unity Hub が起動していないと止まる(エディターのサインインを使うため)
- エミュレーターの試験の落とし穴(同じフォルダの保存ファイルはバックアップを共有するので、空のファイルは前のデータへ「復旧」される)。試験の側の問題で、ゲームの動きとしては正しい

### ビルド
- APK 389(開発版、development): `Unity/2ndAction/Builds/Android/2ndAction.apk` — CONTINUE の試験の入口が増えただけで、ゲームの中身は 388 と同じ
