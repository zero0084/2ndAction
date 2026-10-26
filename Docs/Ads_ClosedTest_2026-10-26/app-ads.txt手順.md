# app-ads.txt の設置(OneMoreMile / AdMob)

## 置く内容(1行)

```
google.com, pub-2863190967263764, DIRECT, f08c47fec0942fa0
```

- `pub-2863190967263764`: マスターの AdMob パブリッシャー ID(公開して問題ない値)。
- `f08c47fec0942fa0`: Google の認証局 ID(AdMob の案内の標準の値)。
- 最終確認は、AdMob の「アプリ → すべてのアプリを表示 → app-ads.txt → app-ads.txt の設定方法」に出る行と、一字一句同じであることをマスターが確認してください。

ファイル名は `app-ads.txt`(すべて小文字)、文字コードは UTF-8 のテキストです。

## 置く場所の決まり(Google のヘルプより)

- **開発者のウェブサイトのドメインの一番上**に置きます(`https://<ホスト名>/app-ads.txt`)。
  - AdMob は、Google Play のストア掲載情報の「ウェブサイト」に書いたホスト名を使って探します。
  - ストアに書くのはホスト名だけでよく、app-ads.txt の URL までは要りません。
- 調べに来る仕組み
  - サブドメインは1段だけさかのぼって探します(例: `a.b.example.com` → `b.example.com`)。
  - `www.` / `m.` は飛ばして探します。
  - 別の場所への転送(リダイレクト)も使えます。
- **Google サイト(今のプライバシーポリシーの `sites.google.com/view/...`)には置けません**。ドメインの一番上にファイルを置けないためです。
- 反映まで: ストア掲載情報を変えてから最大24時間、確認されるまでも24時間以上かかります。

## 無料で置ける方法(有料の独自ドメインは不要)

| 方法 | 公開される URL の例 | 手間 | おすすめ |
|---|---|---|---|
| **Firebase Hosting**(無料の Spark プラン) | `https://onemoremile-xxxx.web.app/app-ads.txt` | Google アカウントで作れる。Firebase CLI で `firebase deploy` | ◎ Google のヘルプが代わりの置き場所として案内している |
| **GitHub Pages のユーザーサイト** | `https://<ユーザー名>.github.io/app-ads.txt` | `<ユーザー名>.github.io` という名前のリポジトリを作り、一番上に置くだけ | ○ ユーザーサイトはドメインの一番上になる。**`<ユーザー名>.github.io/<リポジトリ名>/` の形(プロジェクトサイト)は一番上にならないので不可** |
| Cloudflare Pages | `https://<名前>.pages.dev/app-ads.txt` | Cloudflare アカウント、フォルダをアップロード | ○ |

どの方法でも、そのサイトのトップページ(`index.html`)に、ゲームの名前とプライバシーポリシー(今の Google サイトの URL)へのリンクを置いておくと、「開発者のウェブサイト」として自然です。

## 手順(Firebase Hosting の例)

1. https://console.firebase.google.com でプロジェクトを作ります(Analytics は不要、無料の Spark プランのまま)。
2. PC に Firebase CLI を入れ、`firebase login` → 空のフォルダで `firebase init hosting` を実行します(公開するフォルダは `public`)。
3. `public/app-ads.txt` に上の1行を書き、`public/index.html` に簡単な紹介とプライバシーポリシーへのリンクを置きます。
4. `firebase deploy` で公開します。表示された `https://<プロジェクト>.web.app/app-ads.txt` をブラウザで開いて、1行が見えることを確認します。
5. Play Console → ストアでの表示 → ストアの設定 → 連絡先の詳細 →「ウェブサイト」に `https://<プロジェクト>.web.app` を入れて保存します。
6. 24時間ほど後に、AdMob の「アプリ → app-ads.txt」で「確認済み」になることを確認します。

アイリーン側で、公開用のファイル一式(app-ads.txt と index.html)を作って渡すこともできます。公開の操作(アカウント・デプロイ)はマスターが行ってください。

## 注意

- ストアの「ウェブサイト」を変えると、ストアの掲載情報に表示されます。
- AdMob とストアの紐付け(保留中)と同じ Play のアプリ(パッケージ名 `com.zero0084.action2nd`)であることを確認してから行ってください。
- 本番広告に切り替える前に「確認済み」になっていることが望ましいです。テスト広告の間は無くても動きます。

出典: [Set up an app-ads.txt file for your app(AdMob ヘルプ)](https://support.google.com/admob/answer/9363762?hl=en)、[Authorized Sellers for Apps(AdMob 開発者向け)](https://developers.google.com/admob/ios/app-ads)
