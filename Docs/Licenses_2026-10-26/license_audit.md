# One More Mile 第三者ライセンス監査(2026-10-26)

> 調査と記録のみ。`Unity/2ndAction/Assets` 以下のファイルは変更していない。コミットもしていない。
> 提案するライセンス文: `Docs/Licenses_2026-10-26/ThirdPartyNotices_proposed.txt`(英語。既存の MS Basic の部分は今のファイルのまま、その後ろに追加)
> 区分: **確認済み**(ファイルを読んで確かめた)/ **推測** / **未確認**

## 0. 製品に入るものの決め方(根拠)

| 記号 | 根拠 | 対象 |
|---|---|---|
| **APK** | `Unity/2ndAction/Builds/ClosedTest/OneMoreMile_ct380_debugsigned.apk`(2026-10-26 19:33、developmentBuild=false、IL2CPP、arm64-v8a)の中身。`assets/bin/Data/ScriptingAssemblies.json`(入っている C# の DLL の一覧)、`META-INF/*.version`、ルートの `*.properties`、`lib/arm64-v8a/*.so` | Android |
| **GR** | Unity が作った Gradle プロジェクト `Library/Bee/Android/Prj/IL2CPP/Gradle` で `:launcher:dependencies --configuration releaseRuntimeClasspath --offline` を実行して得た、実際に解決された依存の一覧(90件)。`launcher/build.gradle` は `minifyEnabled false`(R8 で削られない = 依存のクラスはすべて APK に入る) | Android |
| **POM** | `~/.gradle/caches/modules-2/files-2.1/<group>/<artifact>/<version>/*/*.pom` の `<licenses>` | Android |
| **TPL** | Google の AAR の中の `third_party_licenses.txt` / `third_party_licenses.json`(Google のライブラリに含まれるオープンソースの一覧と本文) | Android |
| **WIN** | `Unity/2ndAction/Builds/WindowsProduct`(2026-10-24、Mono)の `OneMoreMile_Data/Managed/*.dll`、`Plugins/x86_64`、ルートの DLL | Windows |
| **PKG** | `Unity/2ndAction/Library/PackageCache/<pkg>/LICENSE.md` と `Third Party Notices.md` | 両方 |

- `Packages/manifest.json` と `packages-lock.json` の最後の更新は 2026-10-10(コミット 54da7cac)。上の APK(10-26)と Windows(10-24)はどちらも今のパッケージ構成で作られている。
- Windows のスクリプトの方式は Mono(`MonoBleedingEdge` がある)、Android は IL2CPP(`libil2cpp.so`)。`ProjectSettings.asset` の `scriptingBackend: {}` = 既定値。
- `com.unity.microsoft.gdk.discovery` 1.3.0 は PackageCache にあるが `packages-lock.json` に無く、どちらのビルドにも DLL が無い → 対象外。

---

## 1. 製品に入るもの(表)

「表示が要る」= ライセンスが、配布する製品にライセンス文や著作権表示を付けることを求めているか。

### 1-A. 両方のビルド(Android / Windows)に入るもの

| 名前 | 版 | 入るビルドと判定 | ライセンス | 表示が要るか | ライセンス文の元ファイル |
|---|---|---|---|---|---|
| MS Basic SoundFont(BGM 2曲の書き出しに使用) | v0.2 | 両方(BGM の WAV。`Docs/AudioProvenance_2026-10-23/README.md` で 2曲とも確定) | MIT | **要る**(謝辞と著作権表示、MIT 本文)。**今のゲーム内表示に収録済み** | `Docs/AudioProvenance_2026-10-23/originals/bgm/MS Basic_License.md` |
| Newtonsoft.Json(Unity の com.unity.nuget.newtonsoft-json) | パッケージ 3.2.2 | 両方(APK の ScriptingAssemblies に `Newtonsoft.Json.dll`、WIN の Managed にある)。Authentication / Core / Multiplayer などが使う | MIT(包みのパッケージ自体は UCL) | **要る**(MIT: 著作権表示と許諾文を全ての複製に) | `PackageCache/com.unity.nuget.newtonsoft-json@*/Third Party Notices.md`(Newtonsoft.Json / Json.Net.Unity3D / Newtonsoft.Json-for-Unity / com.newtonsoft.json の4件) |
| websocket-sharp(com.unity.services.wire の中) | Wire 1.4.4 | 両方(APK に `websocket-sharp.dll` と `unity-websocket-sharp.dll`、WIN にも両方。元は `Plugins/websocket-sharp.dll` と `Runtime/websocket-sharp/`) | MIT | **要る** | `PackageCache/com.unity.services.wire@*/Third Party Notices.md` 4〜18行 |
| unity-websocket-webgl(Wire の `Runtime/WebSocket.cs`) | Wire 1.4.4 | 両方。`WebSocket.jslib` は WebGL だけだが、`WebSocket.cs` は WebGL 以外の分岐(websocket-sharp を包むクラス)も同じファイルにあり、`Unity.Services.Wire.Internal.dll` に入る。ファイル先頭に「unity-websocket-webgl / Jiri Hybek / Apache 2.0」 | Apache-2.0 | **要る**(Apache 4(a) ライセンス本文の写し、4(c) 著作権表示の保持。NOTICE ファイルは無い) | 同上 19〜36行。Multiplayer の `Third Party Notices.md` にも同じ記載 |
| Google Mobile Ads Unity Plugin(com.google.ads.mobile) | 11.5.0 | 両方。APK: `GoogleMobileAds*.dll`(Android 用2つを含む6つ)+ `googlemobileads-unity.aar` + `GoogleMobileAdsPlugin.androidlib`。WIN: `GoogleMobileAds.dll / .Common / .Core / .Ump` が Managed にある(Windows でも DLL は入っている) | Apache-2.0(`LICENSE.md` = `GoogleMobileAds/LICENSE`、「Copyright 2013 Google Inc.」) | **要る**(ライセンス本文。NOTICE ファイルは無い。パッケージ内を `*notice*` で検索して0件) | `PackageCache/com.google.ads.mobile@*/LICENSE.md` |
| Burst のランタイムと Burst で作られたネイティブコード | 1.8.29 | 両方(`Unity.Burst.dll`, `Unity.Burst.Unsafe.dll`, APK の `lib_burst_generated.so`, WIN の `Plugins/x86_64/lib_burst_generated.dll`)。`*_BurstDebugInformation_DoNotShip` は配らない | UCL(コード)/ Unity Package Distribution License | UCL の部分は 2章参照。第三者分は下の行 | `PackageCache/com.unity.burst@*/LICENSE.md` |
| └ Burst の第三者分: LLVM、Legacy LLVM(NCSA)、Mono.Cecil、Smash、xxHash、gRPC for .NET、Google.Protobuf、mimalloc | — | **エディターでのコンパイラー側**(推測: Burst のコンパイラーの部品で、製品のファイルには入らない) | 各種 | 要らない(推測)。LLVM は「コンパイルの結果としてオブジェクト形式に埋め込まれた部分は 4(a)(b)(d) に従わなくてよい」という例外付き | `Third Party Notices.md` |
| └ SLEEF | — | 数学関数として生成コードに入る(Burst の CHANGELOG に「SLEEF の三角関数を Burst に取り込む」等) | Boost 1.0 | 要らない(Boost は「言語処理系が生成した機械語のオブジェクトコードだけの形」なら表示不要と明記) | 同上 180〜205行 |
| └ musl | — | **未確認**(CHANGELOG 388行に「MUSL と SLEEF の帰属表示を追加」とあり、生成コードに入る可能性がある) | MIT | **念のため載せる**(提案ファイルに収録) | 同上 132〜178行 |
| Netcode for GameObjects | 2.13.0 | 両方(`Unity.Netcode.Runtime.dll`) | UCL | 2章参照(念のため載せる案) | `PackageCache/com.unity.netcode.gameobjects@*/LICENSE.md`(Third Party Notices なし) |
| Unity Transport | 6.5.0 | 両方(`Unity.Networking.Transport.dll`) | UCL | 同上 | `PackageCache/com.unity.transport@*/LICENSE.md` |
| Collections | 6.5.0 | 両方(`Unity.Collections.dll`, `.LowLevel.ILSupport.dll`) | UCL | 同上 | `PackageCache/com.unity.collections@*/LICENSE.md` |
| Mathematics | 1.4.0 | 両方 | UCL | 同上 | `PackageCache/com.unity.mathematics@*/LICENSE.md` |
| uGUI(TextMeshPro を含む) | 2.5.0 | 両方(`UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`) | UCL | 同上 | `PackageCache/com.unity.ugui@*/LICENSE.md` |
| Profiling Core API | 1.0.3 | 両方(`Unity.Profiling.Core.dll`) | UCL | 同上 | `PackageCache/com.unity.profiling.core@*/LICENSE.md` |
| Multiplayer Tools | 2.2.9 | 両方(`Unity.Multiplayer.Tools.*` 18個) | Unity Package Distribution License | 要らない(UPDL に表示の義務は無い。下の Pugrad も参照) | `PackageCache/com.unity.multiplayer.tools@*/LICENSE.md` |
| └ Pugrad の `MatplotlibColorMaps.cs` | — | 両方(`NetVis/Configuration` の asmdef = `Unity.Multiplayer.Tools.NetVis.Configuration.dll` がビルドに入る) | Unlicense(パブリックドメイン相当) | 要らない | `Third Party Notices.md` |
| Multiplayer Center(Common) | 1.0.1 | 両方(`Unity.Multiplayer.Center.Common.dll`) | UPDL(Third Party Notices は空) | 要らない | `LICENSE.md` |
| Leaderboards | 2.3.4 | 両方 | UPDL | 要らない | `LICENSE.md` |
| Cloud Code | 2.10.4 | 両方(`Unity.Services.CloudCode.dll`) | Unity Terms of Service | 要らない(規約の同意で使う。表示の義務は書かれていない) | `LICENSE.md` |
| └ rollup | — | 入らない(エディターの仕組み。製品の DLL 一覧に無い) | MIT | 要らない | `Third Party Notices.md` |
| Multiplayer Services / Authentication / Core / QoS / Wire | 2.2.4 / 3.7.3 / 1.18.0 / 1.4.1 / 1.4.4 | 両方 | Unity Terms of Service | 要らない(中の第三者分は上の Newtonsoft / websocket-sharp / unity-websocket-webgl) | 各 `LICENSE.md` |
| └ moq4(Authentication の Third Party Notices) | — | 入らない(`AssemblyInfo.cs` の試験用 InternalsVisibleTo だけ。moq 本体はどこにも無い) | BSD-3 | 要らない | `Third Party Notices.md` |
| Unity IAP(com.unity.purchasing) | 5.4.4 | 両方(`Unity.Purchasing.*`)。Android は加えて Google Play Billing(1-B) | IAP Service の規約(Operate Service) | 要らない(規約) | `LICENSE.md` |
| └ ASN.1-Editor | — | 両方(`Runtime/Security/Asn1Processor`) | Public domain | 要らない | `Third Party Notices.md` |
| └ Apple StoreKit v2 | — | 入らない(iOS/macOS の OS の機能。Android/Windows には Stub の DLL だけ) | — | 要らない | `Third Party Notices.md` |
| Unity エンジン本体(UnityPlayer.dll / libunity.so / IL2CPP / Mono のクラスライブラリ `mscorlib.dll`・`System*.dll`、組み込みフォント LegacyRuntime など) | 6000.5.6f1 | 両方 | Unity の規約 | **未確認**(Unity のインストール先 `Editor/Data` に製品用の第三者表記のファイルは見つからなかった。Unity の規約上、ゲーム側に表示義務があるかは確認できていない) | — |
| └ Windows の Mono ランタイム(`MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll`, `MonoPosixHelper.dll`) | — | Windows | 未確認(Unity が同梱する部品) | 未確認 | — |
| └ Windows の `D3D12/D3D12Core.dll`, `dstorage.dll`, `dstoragecore.dll` | — | Windows(Unity が同梱する Microsoft の再配布部品と推測) | 未確認(Microsoft の再配布条件) | 未確認 | — |

### 1-B. Android にだけ入るもの

| 名前 | 版 | 判定 | ライセンス(根拠) | 表示が要るか | 元ファイル |
|---|---|---|---|---|---|
| Google Mobile Ads SDK(play-services-ads, -ads-api) | 25.4.0 | APK の `play-services-ads.properties` 等、GR | **Android Software Development Kit License**(POM。URL `https://developer.android.com/studio/terms.html`)。Apache ではない | SDK 本体には表示の義務なし。ただし**中に含まれるオープンソースの表示は開発者の責任**と Google が明記(下の注) | TPL |
| User Messaging Platform(UMP) | 4.0.0 | APK `user-messaging-platform.properties`、GR | Android SDK License(POM) | 同上 | TPL |
| play-services-ads-identifier / appset / base / basement / location / measurement-base / measurement-sdk-api / places-placereport / tasks | 18.0.0 / 16.0.1 / 18.5.0 / 18.9.0 / 19.0.0 / 20.1.2 / 20.1.2 / 17.0.0 / 18.2.0 | APK の `*.properties`、GR(location と placereport は Billing 9.0.0 が引き込む) | Android SDK License(POM) | 同上 | TPL |
| Google Play Billing Library(Unity IAP が追加) | 9.0.0 | APK `billing.properties`、GR(`unityLibrary/build.gradle` 101行) | **Android SDK License(POM)**。Unity IAP の Third Party Notices は「Apache 2.0」と書きつつ本文は MIT の文面・リンク先は Android SDK の規約で、**食い違っている**。POM と Google の AAR を正とした | 同上 | TPL(`billing-9.0.0.aar`) |
| com.google.android.play:hsdp | 2.0.1 | APK `hsdp.properties`、GR | Android SDK License(POM) | 同上 | TPL |
| Google Data Transport(transport-api / -backend-cct / -runtime)、Firebase Encoders(-, -json, -proto) | 3.0.0 / 3.1.8 / 3.1.8、17.0.0 / 18.0.0 / 16.0.0 | APK、GR(Billing が引き込む) | Apache-2.0(POM) | **要る**(Apache 本文)+ 中のオープンソース(TPL) | POM、TPL |
| AndroidX(activity, annotation, appcompat, arch.core, browser, collection, concurrent-futures, constraintlayout(-core), core(-ktx), cursoradapter, customview, datastore, drawerlayout, emoji2, fragment, interpolator, lifecycle 8種, loader, privacysandbox.ads, profileinstaller, resourceinspection-annotation, room, savedstate, sqlite, startup, tracing, vectordrawable, versionedparcelable, viewpager, webkit, work-runtime) | 提案ファイル PART B の一覧どおり | APK の `META-INF/androidx.*.version`、GR | Apache-2.0(POM のあるものは POM。POM が無い appcompat 等は AAR 内の Apache ヘッダー、または Google の TPL に「AndroidX ... library」として Apache 本文で載っている)。**constraintlayout / constraintlayout-core / resourceinspection-annotation はローカルに根拠なし(推測: Apache-2.0)** | **要る**(Apache 本文) | POM / TPL |
| androidx.games:games-activity(`libgame.so`)、games-frame-pacing(`libswappywrapper.so`) | 4.4.0 / 2.1.2 | APK の `lib/`、GR(`unityLibrary/build.gradle` 20〜21行、Unity が追加) | Apache-2.0(AAR 内のヘッダー「Licensed under the Apache License, Version 2.0」。POM はキャッシュに無い) | **要る** | AAR 内のヘッダー |
| Kotlin 標準ライブラリ | 2.1.0(jdk7/jdk8 は 1.8.22) | APK の `kotlin/*.kotlin_builtins`、GR | Apache-2.0(POM) | **要る** | POM、TPL の「Kotlin」 |
| kotlinx.coroutines | 1.8.0 | APK `META-INF/kotlinx_coroutines_*.version`, `DebugProbesKt.bin`、GR | Apache-2.0(POM) | **要る** | POM |
| Guava / failureaccess / listenablefuture | 31.1-android / 1.0.1 / 9999.0 | GR(privacysandbox.ads が引き込む) | Apache-2.0(`guava-parent` POM) | **要る** | POM |
| error_prone_annotations / j2objc-annotations | 2.11.0 / 1.3 | GR | Apache-2.0(POM) | 要る | POM |
| javax.inject | 1 | GR | POM なし。Google の TPL の「JSR 330」(Apache)と同じもの(推測) | 要る | TPL「JSR 330」 |
| jsr305 | 3.0.2 | GR | POM なし。Google の TPL の「JSR 305」(BSD 型の文面) | **要る** | TPL「JSR 305」 |
| org.jetbrains:annotations | 23.0.0 | GR | **ローカルに根拠なし**(推測: Apache-2.0) | 要る(推測) | — |
| checker-qual(Checker Framework) | 3.12.0 | GR(Guava が引き込む) | **MIT**(POM、jar の `META-INF/LICENSE.txt`) | **要る** | jar の `META-INF/LICENSE.txt` |
| Google の AAR の中のオープンソース(Conscrypt, Dagger, Error Prone, ExoPlayer, flatbuffers, Gson, ICU4C, J2ObjC, Kotlin, mp4parser, PCRE, Protocol Buffers, RE2, STL, Tink, Volley, zlib など。同じ本文をまとめて 33 本) | — | 上の Google の AAR に含まれる(TPL の一覧) | Apache-2.0 / MIT / BSD / ICU / PCRE / zlib ほか | **要る**(Google: 「開発者が適切に表示する責任がある」) | TPL(18個の AAR) |
| `libc++_shared.so`(NDK の C++ 標準ライブラリ) | NDK 同梱 | APK の `lib/`(Unity が入れる) | LLVM(Apache-2.0 with LLVM Exceptions、推測) | **未確認**(LLVM の例外は「自分のソースをコンパイルした結果に埋め込まれた部分」向けで、共有ライブラリそのものの再配布に当てはまるかは判断できない) | `Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK/NOTICE`(未読・巨大) |
| `googlemobileads-unity.aar` / `GoogleMobileAdsPlugin.androidlib` | 11.5.0 | APK | Apache-2.0(1-A の Google Mobile Ads Unity Plugin と同じ) | 1-A で対応 | — |
| 自作の `Assets/Plugins/Android/SaveTransferBridge.java` | — | APK | 自作 | 要らない | — |

**Google のオープンソース表示の扱い**(`https://developers.google.com/android/guides/opensource` を 2026-10-26 に確認): Google Play services が使うオープンソースについて「you as a developer are responsible for appropriately displaying the notices」とあり、その方法として `oss-licenses-plugin` と `play-services-oss-licenses`(表示用の Activity)を勧めている。Unity の Gradle ではこの仕組みを入れていないので、今回は AAR の `third_party_licenses.txt` を集めて提案ファイルに入れた(同じ本文は1回だけ、名前をまとめて表示)。

### 1-C. フォント

- `Assets` 以下に `.ttf / .otf / .ttc / .fnt / .fontsettings / *SDF*.asset / .woff` は **0件**(確認済み)。`TextMesh Pro` フォルダも無い。
- 文字は Unity 組み込みの `LegacyRuntime.ttf`(`Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`、`CardFusionUI.cs` 175行、`CreditPlaque.cs` 76行 ほか)。ビルドの `unity default resources` では、このフォントの代わりの名前に `Arial` が入っている。書体そのものが何か(と、その使用条件)は**未確認**(Unity 本体の一部として Unity の規約の範囲と推測)。
- 言語によっては `LocFonts.cs` が `Font.GetOSInstalledFontNames()` / `CreateDynamicFontFromOSFont` で**端末の OS のフォント**を使う(Myanmar Text、Noto Sans Myanmar、Khmer UI など)。フォントファイルは配らないので表示は不要。
- **エンドロールの巨大文字**(`Resources/LastDungeon/Glyphs/glyph_*.png` 60枚)は Windows 付属の Palatino Linotype Bold(© Linotype)を画像にしたもの(`Docs/AssetProvenance_2026-10-07.md` 1-C)。**ライセンス表示ではなく使用許諾の問題で、未解決**(下の質問 3)。

### 1-D. 製品に入らないもの(エディター・開発用)

APK の `ScriptingAssemblies.json` と WIN の Managed に無いことで確認した: External Dependency Manager(1.2.190、Editor 専用、Apache-2.0)、Services Deployment / Deployment API(Glob=MIT を含む)、Test Framework / Performance(Perfolizer=MIT)/ ext.nunit(NUnit=MIT)、nuget.mono-cecil(Mono.Cecil=MIT)、Cloud Code の `Editor/.CloudCodeBindingsGenerator/Newtonsoft.Json.dll`。これらは表示不要。

---

## 2. Unity のライセンスごとの扱い

| ライセンス | 該当 | ゲーム内の表示 |
|---|---|---|
| Unity Companion License(UCL v1.4) | Netcode, Transport, Collections, Mathematics, Burst(コード), uGUI, Newtonsoft の包み, Profiling Core | Unity の公式ページ(`https://unity.com/legal/licenses/unity-companion-license`、2026-10-26 確認)の 5条は「ライセンスと著作権表示を Work の substantial portions に付ける。無理なら通常その種の表示を置く場所に」。3.1条でゲームは派生物に当たらないので、表示の対象は**パッケージの部分だけ**。製品の DLL に含まれる部分がそれに当たるかははっきりしない → **念のため各 `LICENSE.md` の短い文を載せる案**(提案ファイル PART A の最後) |
| Unity Package Distribution License | Multiplayer Tools, Multiplayer Center, Leaderboards, Burst(コード以外) | 公式ページでは「第三者表記が付いている物はその条件による」だけで、帰属表示の義務は無い → 不要 |
| Unity Terms of Service / IAP Service の規約 | Services 系、Purchasing | 規約への同意で使う。表示の義務は無い → 不要 |
| パッケージ内の Third Party Notices | 上の表のとおり | 製品に入る物(Newtonsoft、websocket-sharp、unity-websocket-webgl、musl(念のため))だけ載せる |

---

## 3. ゲーム内のライセンス画面に載せる必要があるもの

**両方のビルド(Android / Windows)**
1. MS Basic SoundFont — **収録済み**(今の `ThirdPartyNotices.txt`、変更なし)
2. Newtonsoft.Json ほか包みの MIT 4件
3. websocket-sharp(MIT)
4. unity-websocket-webgl(Apache-2.0 の表示 + 変更点の一覧)
5. Google Mobile Ads Unity Plugin(Apache-2.0 本文。Windows にも DLL が入っている)
6. musl(MIT、念のため)
7. (念のため)UCL のパッケージ 8件の `LICENSE.md`

**Android だけ**
8. Google Play services / UMP / Play Billing / Data Transport / Firebase Encoders の中のオープンソースの本文 33 本(TPL。同じ本文はまとめた)
9. AndroidX・AGDK(GameActivity / Frame Pacing)・Kotlin・kotlinx.coroutines・Guava などの Apache-2.0 のライブラリ名の一覧(本文は 5 の Apache 本文を共用)
10. Checker Framework qualifiers(MIT)
11. JSR 305(8 の中の本文を参照)

提案ファイルの大きさ: 約 50万文字・9,095行(PART A 約 3.4万文字、PART B が残り。Kotlin の本文だけで約 11万文字)。

## 4. 載せなくてよいもの(理由)

| もの | 理由 |
|---|---|
| Google Play services / UMP / Billing の本体(Android SDK License) | オープンソースではなく Google の SDK の規約。規約に表示の義務は無い(中のオープンソースは 3-8 で対応) |
| Unity Services / IAP(Unity Terms of Service、IAP の規約) | 規約での利用。表示の義務なし |
| UPDL のパッケージ(Multiplayer Tools / Center / Leaderboards) | UPDL に帰属表示の義務なし |
| Pugrad(Unlicense)、ASN.1-Editor(Public domain) | 権利を放棄しているため |
| SLEEF(Boost 1.0) | 生成された機械語だけなら表示不要と本文に明記 |
| LLVM / NCSA / Mono.Cecil / Smash / xxHash / gRPC / Protobuf / mimalloc(Burst の中) | Burst のコンパイラー側でエディターにだけある(推測)。LLVM は生成物に埋め込まれた部分の例外あり |
| moq4、rollup、Glob、NUnit、Perfolizer、Mono.Cecil(パッケージ単体)、EDM4U | 製品に入らない(1-D) |
| Apple StoreKit | Android/Windows に入らない |
| unity-websocket-webgl の `WebSocket.jslib` | WebGL 専用(.meta で WebGL だけ有効)。ただし C# の部分は入るので 3-4 は必要 |
| OS のフォント | 配っていない |
| 自作のコード・音・絵 | 第三者の物ではない |

## 5. マスターへの確認事項

1. **表示の分け方**: Android の部分(約 47万文字)を Steam 版にも出すか。出しても違反ではないが、`ThirdPartyNotices.txt`(両方)+ `ThirdPartyNotices_Android.txt`(Android だけ)に分けて、Android の時だけ後者も読むのがすっきりする。
2. **画面の作り**: 今の `LicensesUi.cs` は全文を1つの `GUI.Label` で描いている。IMGUI は1回の描画の頂点数に上限(約 6.5万頂点 ≒ 1.6万文字)があり、約 50万文字は**そのままでは表示が切れる・重い**おそれ。項目ごとのページ分け、または段落ごとに分けて見えている所だけ描く形が必要(Assets は今回変更していない)。`QaSweep.Licenses.cs` の確認項目にも新しい項目を足すとよい。
3. **エンドロールの文字(Palatino Linotype)**: 前回からの未解決。画像にして配ってよいかは Windows のフォントの規約次第で**未確認**。自由に使えるフォント(SIL OFL 等)で作り直すなら、そのフォントの表示(OFL 本文)を足す。
4. **Unity 本体の第三者分**(UnityPlayer / libunity / IL2CPP / Mono / PhysX 等)と、Windows の `D3D12Core.dll`・`dstorage*.dll`、Android の `libc++_shared.so`: ゲームに表示する義務があるかは手元のファイルでは確認できなかった。Unity のサポート、または Unity の規約(Unity Software Additional Terms 等)で確認するか、念のため載せるかを決めてほしい。
5. **Google の推奨方式**: Google は `oss-licenses-plugin` での表示を勧めている。Unity の Gradle にこれを足す(Android の「ライセンス」から Google の Activity を開く)か、今回のように本文を集めた文を自前の画面で出すか。後者は依存の版が変わるたびに作り直しが要る(今回の作り方は `Library/Bee/.../Gradle` で `dependencies` を実行 → AAR の `third_party_licenses.*` を集める)。
6. **Unity IAP の Billing の表記の食い違い**: Unity の Third Party Notices は「Google Play Billing = Apache 2.0」と書いているが、Google の POM は Android SDK License。今回は Google 側を正とし、Unity の文は載せていない。Unity の文もそのまま載せておく方が安心なら追加する。
7. **constraintlayout / resourceinspection-annotation / org.jetbrains:annotations**: ローカルに POM もライセンスの記載も無かった(推測: Apache-2.0)。必要なら Maven Central / Google Maven の POM で確認する。
8. **「念のため」の項目**(musl、UCL の8件)を載せるかどうか。載せても害は無い。
9. `Docs/AssetProvenance_2026-10-07.md` 2章の「Asset Store 素材・外部プラグイン なし / `Assets/Plugins` なし」は 10-10 の広告・IAP 導入より前の記録で、今は古い(AdMob・UMP・IAP・`Assets/Plugins/Android` がある)。必要なら更新する。

## 6. 補足(再現の手順)

- Android の依存一覧: `Library/Bee/Android/Prj/IL2CPP/Gradle` で、Unity 同梱の OpenJDK と `Tools/gradle/lib/gradle-launcher-9.1.0.jar` を使って `--offline -q :launcher:dependencies --configuration releaseRuntimeClasspath`(ネットワーク不要、約1分)。
- Google の本文: 上の一覧の Google の AAR(18個)から `third_party_licenses.json`(名前 → 開始位置・長さ)と `third_party_licenses.txt` を取り出し、本文を切り出して空白を無視して同じ物をまとめた。Kotlin は写しごとに中身が少しずつ違うため、「Files:」の段落ごとに重複を除いて1つにした。キャッシュにあった `asset-delivery-2.3.0` と `core-common-2.0.4` は依存一覧に無いので除いた。
- 提案ファイルの本文はすべて上記の元ファイルからプログラムで写したもの(言い換えなし)。各項目の見出し・「Applies to」・出典の行だけが追加の文。
