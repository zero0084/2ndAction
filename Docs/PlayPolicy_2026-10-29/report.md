# Google Play ポリシー更新(2026-10-29 通知)の影響調査

調査日: 2026-10-31 / 対象: One More Mile(com.zero0084.action2nd)
**今回は調査のみ。コード・設定・サーバー・ビルドは変更していません。**

調べたビルド:
- APK 402(2026-10-30 2314、開発版 = Development Build、debuggable)
- クローズドテスト用の製品版 ct391(2026-10-27、`BuildAndroid.BuildClosedTest` の出力。debuggable なし・テスト広告・**デバッグ鍵の署名なので Play には上げられない**もの)

---

## 1. ターゲット API

| 項目 | 値 | 根拠 |
|---|---|---|
| Unity | 6000.5.6f1 | ProjectVersion.txt |
| Player Settings | Target API = Automatic(`AndroidTargetSdkVersion: 0`)、Minimum = 26 | ProjectSettings.asset |
| Unity に同梱の Android SDK | platforms: android-34 / android-36 / **android-37.0**、build-tools 36.0.0、NDK 27.2 | Unity の AndroidPlayer/SDK |
| **実際の targetSdkVersion** | **36**(APK 402・ct391 とも) | aapt2 dump badging |
| minSdkVersion | 26(Android 8.0) | 同上 |
| compileSdkVersion | 36(Android 16) | 同上 |
| ABI | arm64-v8a のみ | 同上 |
| 16 KB ページ | 全 .so の LOAD 境界 16384・zipalign -P 16 合格 | ELF ヘッダ・zipalign |

- **Google Play の現在の要件(2026-08-31 以降の新規アプリ・更新は API 36 以上)を満たしています。** 次の引き上げ(API 37)は例年どおりなら 2027年8月末の見込みです(ローカルネットワーク権限のポリシーの期限 2027-08-25 もそれに合わせた日付)。
- **API 36 と 37 のどちらか**: 今のビルドは **36**。
- **注意(リスク)**: Unity の「Automatic」は「インストール済みの最も新しい SDK」を使う仕様です。Unity 6000.5 は API 37 を知っていて(`AndroidApiLevel37`)、SDK フォルダにも android-37.0 が入っていますが、今は 36 が選ばれています(37.0 という名前の付き方のためと思われます)。**Unity の更新や SDK の追加で、知らないうちに 37 になる可能性があります。** 37 になると LOCAL PLAY が Android 17 の端末で動かなくなり(§2)、Play Console での申告も必要になります。→ §6 の変更案 A(36 に固定)を推奨します。

## 2. LOCAL PLAY と ACCESS_LOCAL_NETWORK

### 今の LOCAL PLAY の通信

| 処理 | 方式 | ファイル |
|---|---|---|
| 部屋の検索 | UDP ブロードキャスト(255.255.255.255 と各サブネット)、ポート 47777。探している間だけ MulticastLock | Net/LanDiscovery.cs・LanMulticastLock.cs |
| IP の直接入力 | 指定 IP へ UDP で部屋の案内を問い合わせ(HOST の証明書を受け取る) | LanDiscovery.RequestRoomInfo |
| 対戦の通信 | Unity Transport(UDP、既定ポート 7777)、HOST は 0.0.0.0 で待ち受け | Net/NetSession.cs |
| 暗号化 | DTLS(HOST が起動ごとに自己署名の証明書、JOIN は案内の証明書を固定) | Net/LanSecurity.cs |
| 権限 | INTERNET / ACCESS_NETWORK_STATE / ACCESS_WIFI_STATE / CHANGE_WIFI_MULTICAST_STATE(すべて通常の権限、許可の画面なし) | 実際の manifest |

### 結論

- **今(target 36)は新しい権限は不要。追加もしません**(Google の案内でも「target 36 以下では ACCESS_LOCAL_NETWORK を宣言・要求しない」)。Android 17 の端末でも、target 36 のアプリは INTERNET 権限で今までどおりローカル通信できます。**クローズドテストへの影響はありません。**
- **target 37 にした時は必要**: 公式の表では、ローカルのアドレスへの TCP 接続・TCP 接続の受け入れ・UDP の送信(ユニキャスト/マルチキャスト/ブロードキャスト)・UDP の受信が**すべて**許可の対象です。LOCAL PLAY は「部屋の検索」「IP 直接入力」「HOST の待ち受け」「JOIN の通信」のすべてが当たります(ONLINE PLAY = Relay はインターネット側なので対象外)。
- **システムの機器選択(ピッカー)だけでは代わりになりません**: ピッカー(NsdManager の FLAG_SHOW_PICKER)は「mDNS で見つけた相手を 1 台選ぶと、その相手への接続だけ許可なしで使える」方式です。
  - 今の部屋の検索は独自の UDP ブロードキャストなので、ピッカーを使うには mDNS(NSD)での部屋の案内に作り替える必要があります(Java のプラグインが必要)。
  - HOST が複数の JOIN を受け入れる側(最大 8 人)は、ピッカーでは解決しません(相手を選ぶのは JOIN 側で、HOST の待ち受けは許可の対象)。
  - 結論として、**target 37 では ACCESS_LOCAL_NETWORK を実行時に求める方式**が現実的です(Google のポリシーでも「ピッカーで足りない中核機能」として申告する形)。
- **拒否された時**: UDP は EPERM(送信・受信の失敗)、TCP はタイムアウト。今のコードでは、部屋の検索は「見つからない」、接続は 1 秒×10〜15 回の試行の後に失敗、HOST は部屋を作っても誰も入れない、になります。**理由が画面に出ない**ので、target 37 対応の時は「LOCAL PLAY を開く時に許可を求める → 拒否なら説明と設定への案内、ONLINE PLAY は使える」の画面が必要です。
- **OS 別の互換**:

| 端末の OS | target 36(今) | target 37(将来) |
|---|---|---|
| Android 8〜15 | 今までどおり | 今までどおり(権限は存在しない) |
| Android 16 | 今までどおり(開発者の試験用の切り替えだけ) | 今までどおり |
| Android 17 以上 | 今までどおり | **許可が必要**(周辺機器 = NEARBY_DEVICES のグループ。Bluetooth 等を許可済みなら追加の確認なし) |

- **Play Console の申告(target 37 以降)**: 「LOCAL PLAY = 同じ Wi-Fi/テザリングにいる友だちと最大 8 人で遊ぶ中核機能。部屋の検索に UDP ブロードキャスト、対戦に端末どうしの直接通信(DTLS で暗号化)を使う。HOST は複数の参加者を受け入れる必要があるため、1 台を選ぶ機器選択では代われない。インターネットのオンライン対戦は別にあり、許可を拒否してもゲームは遊べる」という趣旨の説明になります(案)。
- 開発中の試験: Android 16 の端末で `adb shell am compat enable RESTRICT_LOCAL_NETWORK com.zero0084.action2nd` → 再起動で、target 37 の挙動を先に試せます(公式の手順)。
- DTLS 暗号化・最大 8 人は、どの方式でもそのまま維持できます(通信の中身は変わらない。変わるのは「通信してよいか」の許可だけ)。

## 3. メモリ・DEX(2027年2月からの基準)

### 基準(公式)
- メモリ: **Anonymous RSS + Swap の 90 パーセンタイル**(28 日間)を、端末の RAM の区分と状態ごとに比べる。**ゲームの上限**: 4GB 端末 前面 2.25GB / 6GB 2.75GB / 8GB 3.5GB / 12GB 4GB / 16GB 5GB(背面はその少し下)。ビットマップ(Java 側の画像)は背面 200MB / キャッシュ 400MB。
- DEX: **ゲームは DEX が 50MB を超える場合だけ**、R8 等で最適化・縮小・難読化を各 25% 以上。
- 超えると Play での表示や公開の機能に影響の可能性。確認は Play Console の Android vitals(メモリ)と App bundle explorer(DEX)。

### One More Mile の値

| 項目 | 値 | 判定 |
|---|---|---|
| DEX(製品版 ct391) | classes.dex 9.8MB + classes2.dex 8.0MB = **17.8MB** | **50MB 未満 → 対象外** |
| DEX(開発版 402) | 合計 21.0MB(5 ファイル) | 同上 |
| DEX の中身 | ほぼ広告 SDK(play-services-ads 25.4・UMP)・Billing・androidx・Unity の Java 部分 | ゲームのコードは IL2CPP(libil2cpp.so 99MB)で DEX ではない |
| R8 / 縮小 | **未使用**(Minify Release/Debug とも OFF、独自の proguard なし) | 今は不要。Play Console に「DEX の最適化が限られている」の注意が出る可能性はある |
| メモリ(エミュレーター) | §3.1 | |

### 3.1 メモリの実測

エミュレーター(Android 15・RAM 約3.9GB = 「4GB」の区分、x86_64 で arm64 を変換して実行)に APK 402 を入れ、**約23分間続けて**実戦を回しながら 20 秒ごとに `/proc/<pid>/status` の RssAnon + VmSwap(Play の基準と同じ指標)を記録(`data/emulator_memory.tsv`)。内容: 炎・雷・混成の3ビルド × 闘技場の雑魚100体 / ボス / 90〜95km の雑魚ラッシュ / 95〜99km のボスラッシュ = 12 場面(場面ごとにシーンを読み直し)。

| 時点 | Anon RSS + Swap | PSS | その時の場面 |
|---|---|---|---|
| 起動直後(20秒) | 449MB | 621MB | ホーム → 闘技場 |
| 3分20秒 | 630MB | 848MB | 炎: 90〜95km ラッシュ |
| 6分20秒 | 667MB | 895MB | 炎: 95〜99km ボスラッシュ |
| 12分(最大) | **705MB** | 935MB | 雷: 90〜95km ラッシュ |
| 15分20秒 | 682MB | 914MB | 混成: 闘技場の雑魚100体 |
| 21分20秒 | 695MB | 927MB | 混成: 95〜99km ボスラッシュ |

- **最大 705MB、平均 666MB。ゲームの上限(4GB の端末で前面 2.25GB)の約 3 割**。最初の 10 分で増えて、その後は 680〜700MB で横ばい(**場面を繰り返しても増え続けない = 漏れの傾向なし**)。例外 0・異常終了なし。
- ネイティブのヒープは 45〜56MB。Java 側の画像(ビットマップ)はほぼ使っていない(Unity の画像は GPU 側)ので、ビットマップの基準(背面 200MB)にも当たらない見込み。
- Windows の試験版でも Unity が確保しているメモリは 189〜238MB(属性の比較の試験)。

**注意(この計測の限界)**:
- 開発版(Development Build)での値。製品版は少し小さくなる見込み。
- エミュレーターは arm64 の変換の分が上乗せされる一方、GPU のメモリ(テクスチャ)は数字に出ていない。**実機の値は Play Console の Android vitals(クローズドテスト開始後)で確認が必要**。
- 1 人プレイの 1 キャラ分。マルチで 8 人がそれぞれ別のキャラ(アニメの絵が 8 人分)を使う場合や、0→100km の 1 時間のランは今回は測っていない(次の実機確認の候補)。

## 4. その他の新しいポリシー

| 権限・機能 | 使用 | 判定 |
|---|---|---|
| MANAGE_CONTACTS(連絡先) | なし | **対象外** |
| ACCESS_FINE_LOCATION / 位置情報 | なし(Input.location も未使用) | **対象外** |
| MANAGE_EXTERNAL_STORAGE | なし(データ引き継ぎは SAF = システムのファイル選択で、権限不要) | **対象外** |
| READ_SMS / READ_CALL_LOG | なし | **対象外** |
| Accessibility API | なし | **対象外** |
| AppFunctions API | なし | **対象外** |
| USE_EXACT_ALARM | なし | 対象外 |

**実際の manifest の権限(ct391 = 製品版)**: INTERNET / ACCESS_NETWORK_STATE / ACCESS_WIFI_STATE / CHANGE_WIFI_MULTICAST_STATE(LOCAL PLAY)、com.android.vending.BILLING(広告なしパス)、AD_ID / ACCESS_ADSERVICES_AD_ID / ATTRIBUTION / TOPICS(広告 SDK)、WAKE_LOCK / FOREGROUND_SERVICE(広告 SDK が使う WorkManager)、DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION(androidx の内部用)。
- **意図しない危険な権限は無し**。FOREGROUND_SERVICE は広告 SDK の部品由来で、種類つきの権限(FOREGROUND_SERVICE_xxx)は無いので Play Console の「フォアグラウンド サービス」申告は通常不要です。
- AD_ID があるので、Play Console の「広告 ID」の申告(使用する: 広告)とデータ セーフティの記載が合っていることを確認してください(以前の準備と同じ内容)。
- 位置情報・連絡先・SMS の 2027-01-27 の期限、AppFunctions の 2027-04-14 は**関係しません**。

## 5. 決済・Trader

| 期限 | 内容 | One More Mile での条件 | 確認場所 |
|---|---|---|---|
| **2026-12-01** | 日本に関わる販売の認証。期限までに済まないと**世界中の支払いが保留** | 広告なしパス(アプリ内購入)を販売すると対象になりうる。PSA(支払いの代理受領の同意)は「日本の開発者が**日本以外**の利用者へ売る」場合が対象(日本国内だけの販売は対象外)。ISA(事業者情報の開示)は「日本で売る開発者」が対象で、日本国内の開発者が含まれるかは公式ページに明記なし。**配信国を日本以外にも広げるなら PSA も対象** | Play Console・Google お支払いセンター(payments.google.com)の通知/招待メール、お支払いセンターの「確認(Verification)」 |
| 2026年11月〜(通知の本文。期限の日付は本文に無く、メールでは 2027-02-01) | Trader / non-trader の申告 | 広告・アプリ内購入で収益があるので、一般的には **Trader** にあたる(EU では氏名・住所・電話・メールが公開される)。非 Trader が選べるかはマスターの判断 | Play Console の通知、または 設定 → 開発者アカウント → アカウントの詳細(Play Console の表示を優先して確認) |

- アカウント・支払い情報は変更していません。手続きはマスターが行う前提です。
- 広告なしパスは今「準備中(レシートの検証なし)」で、**クローズドテストは実際の課金をしなければ**認証の期限の影響を受けません。ただし 12/1 の認証は「売上が出たら」ではなく「期限まで」に済ませる必要があるので、通知が来ていたら早めの対応を推奨します。

## 6. 必要になるコード・設定の変更案(未実施。マスターの判断待ち)

| 案 | 内容 | 時期 | 影響 |
|---|---|---|---|
| **A** | Target API を Automatic → **36 に固定**(PlayerSettings の1行) | **クローズドテスト用の製品版を作る前**(推奨) | 今と同じ 36 のまま、Unity/SDK の更新で勝手に 37 にならない。ゲームの動きは変わらない |
| B | LOCAL PLAY の入口で ACCESS_LOCAL_NETWORK を求める(Java の小さな部品 or Unity の Permission API)、拒否時の説明と設定への案内、manifest に権限を追加 | target 37 にする時(2027年夏まで) | LOCAL PLAY の画面と文言(30 言語)、Play Console の申告 |
| C | 部屋の検索・接続の失敗時に理由を出す(EPERM/タイムアウトを「ローカル通信が許可されていない」と区別) | B と同時 | LanDiscovery/NetSession |
| D | R8(Minify Release)を有効化 | 任意(2027年2月の基準では不要) | 広告/課金 SDK の keep 規則の確認が必要。今は**やらない方が安全** |
| E | メモリの継続監視(Play Console の Android vitals の「メモリ」) | クローズドテスト開始後 | 実機の値で上限との差を見る |

## 7. 12 人のクローズドテストへの影響

- **影響なし**(target 36・新しい権限なし・危険な権限なし・DEX 対象外・16KB 対応済み)。Android 17 の端末のテスターでも LOCAL PLAY は今までどおり動きます。
- クローズドテストの前に必要なのは、今回の通知とは別の、以前からの項目(アップロード鍵での署名、Play App Signing、データ セーフティ/広告 ID/コンテンツのレーティングの申告)です。
- 推奨: 案 A(36 に固定)を製品版を作る前に入れる。

## 8. 確認した公式資料

- Policy announcement: October 29, 2026 — https://support.google.com/googleplay/android-developer/answer/18100766
- Policy Deadlines — https://support.google.com/googleplay/android-developer/table/12921780
- Developer Program Policy — https://support.google.com/googleplay/android-developer/answer/17517561
- Target API level requirements — https://support.google.com/googleplay/android-developer/answer/11926878 / https://developer.android.com/google/play/requirements/target-sdk
- Behavior changes: Apps targeting Android 17 — https://developer.android.com/about/versions/17/behavior-changes-17
- Local network permission — https://developer.android.com/privacy-and-security/local-network-permission
- メモリ・DEX の基準(Play Console Help)— https://support.google.com/googleplay/android-developer/answer/17492799
- Android Developers Blog(2026-08)— https://android-developers.googleblog.com/2026/08/app-quality-memory-optimization-secure-onboarding.html
- Android vitals — https://developer.android.com/google/play/vitals
- 日本の販売の認証 — https://support.google.com/paymentscenter/answer/17595997
- Unity: Target API の設定 — https://docs.unity3d.com/Manual/android-setup-target-api.html
