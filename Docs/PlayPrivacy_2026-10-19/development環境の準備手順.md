# Unity Dashboard: development 環境の準備手順(2026-10-19 版)

ランキングと Cloud Code(データの削除を含む)を、本番(production)に触れずに確かめるための環境です。

**この手順の作成・配置は、マスターが内容を確認してから行ってください。** アイリーンは、production への配置・変更・データの削除は行いません。
秘密の値(サービスアカウントのキーなど)を、チャットやファイルで渡す必要はありません。Dashboard の中だけで設定してください。

基本の手順は `Docs/UgsEnvironments_2026-10-14/開発版と製品版の環境の分離.md` の §5 です(環境・Leaderboards 4つ・Cloud Code 2本・Access Control・Multiplayer)。
ここでは、その手順に今回のデータの削除の分を足して、順番どおりにまとめます。

## 0. 前提
- プロジェクト: **One More Mile**(ID `8568059a-fddf-40c3-8e3f-53c041143997`)
- 開発版(Windows/Android の開発ビルド)は `development` だけにつながります。production へは切り替えません(`UgsEnvironment`)。
- 以下はすべて、Dashboard の上部の環境の切り替えを **`development`** にしてから行います。

## 1. 環境を作る
Project settings → Environments → Add environment → 名前 **`development`**

## 2. Authentication
- Player Authentication → Identity providers → **Anonymous** が有効になっていること(既定で有効)

## 3. Leaderboards(4つ)
前の手順書の §5-2 のとおり(`omm_best_wasteland_road` / `omm_best_natural_cave` / `omm_best_sky_corridor` / `omm_best_last_corridor`、降順、Keep latest score)。

## 4. Cloud Code(JavaScript のスクリプト 3本)
| 名前 | ファイル | パラメータ |
|---|---|---|
| `omm_submit_run` | `Tools/ugs/cloudcode/omm_submit_run.js` | 前の手順書 §5-3 の表 |
| `omm_set_name` | `Tools/ugs/cloudcode/omm_set_name.js` | `name`(String、必須) |
| **`omm_delete_player`**(新規) | `Tools/ugs/cloudcode/omm_delete_player.js` | **なし**(PlayerId を受け取らない。呼んだ本人だけを消す) |

作り方は前と同じです。Scripts → Create script(JavaScript)→ 中身を全部貼る → パラメータ → **Publish**。

## 5. データの削除に使う「サービスアカウント」と「Secret Manager」(新規)
ランキングの記録を消せるのは Leaderboards の管理用の API だけです(プレイヤーの権限や Cloud Code の通常の権限では消せません)。
そこで、権限を絞ったサービスアカウントを作り、その鍵を Secret Manager に入れて、`omm_delete_player` の中だけで読みます。
**鍵はアプリに入れません。**

1. **サービスアカウント**: Dashboard → Administration(組織の設定)→ Service accounts → Create
   - 名前の例: `omm-dev-data-deletion`(development 用。production 用は別に作るのが安全です)
   - 権限(Project role): このプロジェクトの **Leaderboards の管理者**(スコアの削除 `live_ops.leaderboards.scores.delete` を含む役割)。それ以外の権限は付けない
   - Create key → **Key ID** と **Secret key** が出ます(Secret key はこの時だけ表示)
2. **Secret Manager**: プロジェクト → Secret Manager(environment = development)→ Add secret
   - 名前: **`OMM_ADMIN_BASIC`**(正確に)
   - 値: `KeyID:SecretKey` を base64 にした文字列
     - 例: PowerShell なら `[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("KeyID:SecretKey"))`
   - Access / Permissions: **Cloud Code から読める**ようにする
   - 値はログ・チャット・ファイルに残さないでください
3. 値を変えた時: Cloud Code は最大5分ほど古い値を使います(キャッシュ)。

## 6. Access Control(前の手順書 §5-4)
プレイヤーがランキングへ直接書けないようにする設定(`Tools/ugs/access/omm_leaderboards.ac`)。

## 7. Multiplayer(ONLINE PLAY、前の手順書 §5-5)

## 8. 終わったら知らせてほしいこと
- 「development 環境の準備ができた」ことだけ(値や鍵は要りません)
- 環境の名前を `development` 以外にした場合は、その名前

## 9. 知らせてもらった後にアイリーンが確かめること(development だけ)
1. ランキングへの記録の登録と表示(Windows の開発版2つ、専用の保存ファイル)
2. データの削除: アプリから削除 → Cloud Save / Leaderboards から本人の記録が消えたこと(Dashboard の表示とアプリの順位の画面)→ アカウントが消えたこと → 次のオンラインの利用で新しい PlayerId になること
3. 他の人のデータを消せないこと: 2つ目のプレイヤーの記録が、1つ目の削除の後も残ること
4. 削除の後に古いランキングの記録が残らないこと(全部の表)
5. ONLINE PLAY の部屋の作成・参加・同期(LOCAL の暗号化の変更の後も、Relay の DTLS で動くこと)

## 10. production へ配置する時(マスターが判断)
- 同じスクリプト・同じ Access Control を production に配置します。
- production 用のサービスアカウントと Secret(`OMM_ADMIN_BASIC`)は、development とは別にしてください。
- 配置は、development での確認が終わってからにしてください。
