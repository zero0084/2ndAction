using UnityEngine;

// 翻訳の反映待ち(エステルのレビュー待ち)の文の一覧(2026-10-15)。
//  ・画面は Loc.Reviewed(新しい文, 前の文) / Loc.Ready(新しい文) で出す: 日本語と、訳が入った言語だけ新しい文。ほかの言語は前の(レビュー済みの)文のまま
//  ・前の文は古い仕様の説明なので、公開版までに必ず訳を入れる → 製品版の Android ビルドは、ここの文の訳が全言語に無いと止まる(Editor/ReleaseBuildGuard)
//  ・訳を入れたら、ここ(Pending)から外す
//  2026-10-15: ID 10007〜10009 はエステルのレビュー(Docs/LocReview_2026-10-15b/estelle)を 30言語へ反映済み → 一覧から外した。
//  ID 10010 / 10011(RETURN TO HOME の確認)はレビュー待ち(Docs/LocReview_2026-10-15c)。
//    日本語の元の文もレビューで直った(10008 / 10009)ので、画面の文はここの定数を使う
public static class LocPendingReview
{
    // ID 10007: CPU サポートの画面の説明の下 / マルチの CO-OP の説明の下
    public const string MileShareNote = "獲得MILEは参加人数で分配";
    // ID 10008: CPU サポートの画面の説明
    public const string SupportDesc = "CPU の仲間と一緒に走ります(最大3人)。仲間は自動で戦い、倒れても少しすると戻ってきます。MILEは自分とCPUの仲間の合計人数で割り、自分の分だけ受け取れます。サポートを使ったランはランキングの対象外です(解放・自己ベストは通常どおり)。";
    // ID 10009: 疾走出発の画面の説明
    public const string SprintDesc = "攻略済みの区間を一気に駆け抜け、選んだ関門の少し手前から走り始めます。ボス報酬のカードはランダムに自動取得。5,000mごとのリングに上下で高さを合わせると追加で1枚選べます。飛ばした距離のMILEは獲得できません。到着後は、到着地点の距離に応じて上がったプレイヤーLvで走り始めます。";

    // ID 10010 / 10011(2026-10-15 第3弾): 一時停止の「RETURN TO HOME」の確認。訳は行ごと(1行目「ホームへ戻ります。」は既存の訳)。
    // 前の文 = ReturnHomeOld(3行目の「CONTINUEできます。」の訳は "CONTINUE from here." = 中断した地点からと読めるので、新しい文では使わない)
    public const string ReturnHomeLine1 = "ホームへ戻ります。";
    public const string ReturnHomeLine2 = "ランは終了せず、直前のチェックポイント({0}m)からCONTINUEできます。"; // {0} = 距離(3桁区切り)
    public const string ReturnHomeLine3 = "それより後に獲得したMILE・EXP・カードは取り消されます。";
    public const string ReturnHomeOld = "ホームへ戻ります。\nランは終了せず、この続きから\nCONTINUEできます。";
    public static bool ReturnHomeReady => Loc.Ready(ReturnHomeLine2) && Loc.Ready(ReturnHomeLine3);
    public static string ReturnHomeText(float checkpointMeters) => ReturnHomeReady
        ? Loc.T(ReturnHomeLine1) + "\n" + Loc.F(ReturnHomeLine2, Mathf.FloorToInt(checkpointMeters).ToString("N0")) + "\n" + Loc.T(ReturnHomeLine3)
        : ReturnHomeOld;

    // ID 10048〜10050(2026-10-22 画面側の完成度向上 第2段階): 英語の開発表示風だった見出しを日本語の言葉へ。前の文 = 今までの英語。
    //  エステルのレビュー(Docs/LocReview_2026-10-22)を 30言語へ反映済み → Pending から外した(Lv. はラテン文字のままに揃えた)
    public const string GachaStageNext = "カードガチャ Lv.{0}  次の進化まで {1}m"; // ID 10049 {0}=段階 {1}=距離(3桁区切り)
    public const string GachaStageMax = "カードガチャ Lv.{0}  最終進化済み";        // ID 10050(エステルの提案で「進化は最大」から変更)

    // ID 10051〜10053(2026-10-22 RESULT の正式化): エステルのレビュー(Docs/LocReview_2026-10-23)を30言語へ反映済み(keys_extra21)
    public const string ResultNewBest = "NEW BEST!";                           // ID 10051(前: NEW + BEST)
    public const string ResultTakeHome = "持ち帰った MILE";                      // ID 10052(前: TOTAL MILE)
    public const string ResultRunBuild = "ラン中の強化(ラン中だけ・持ち帰りません)"; // ID 10053(前: UPGRADES OBTAINED)

    // ID 10054〜10055(2026-10-23 タップ攻撃の復活): 練習の説明に足す行。エステルのレビューを30言語へ反映済み(keys_extra22)
    public const string TapAttackLine = "タップ: その場で前へ攻撃(踏み込みなし)。前へフリックすると、踏み込みながら攻撃します。"; // ID 10054
    public const string TapAirLine = "浮いた敵へは、空中でタップを続けると追撃できます。上へフリックで高さを戻し、下へフリックで締めます。";    // ID 10055

    // ID 10056〜10058(2026-10-23 MS Basic のライセンス表示): 設定の「ライセンス」。レビュー待ち(Docs/LocReview_2026-10-23d)。
    //  ライセンスの本文は原文(英語)のまま出すので訳さない
    public const string LicensesTitle = "ライセンス";                                                   // ID 10056(設定の行と画面の見出し)
    public const string LicensesOpen = "表示する";                                                     // ID 10057
    // ID 10059(2026-10-23 マスター依頼): RESULT の成功の見出し。「GAME CLEAR」はゲーム全体のクリアに見えるため、帰還の成功と分かる言葉へ。
    //  見出しの文字だけ(成功/失敗の判定・MILE・記録・保存は変えない)。訳が入るまでは前の文(GAME CLEAR)
    public const string ResultEscaped = "脱出成功！";                                                   // ID 10059(英: ESCAPED!)
    public const string ResultEscapedOld = "GAME CLEAR";
    public const string LicensesNote = "このゲームで使っている素材の著作権表示とライセンスです(原文のまま掲載しています)。"; // ID 10058

    // ID 10135〜10145(2026-10-26 公開前の案内の整備): 設定の「このゲームについて」(バージョン/制作クレジット/ライセンス/お問い合わせ)。
    //  © の表記・問い合わせのメールの件名(マスター指定)・ライセンスの本文は訳さない
    public const string AboutTitle = "このゲームについて";                                                 // ID 10135
    public const string VersionLabel = "バージョン";                                                       // ID 10136
    public const string CopyButton = "コピー";                                                            // ID 10137
    public const string CreditsTitle = "制作クレジット";                                                    // ID 10138(設定の行と画面の見出し)
    public const string ContactTitle = "お問い合わせ";                                                      // ID 10139
    public const string ContactOpen = "メールを送る";                                                       // ID 10140
    public const string ContactNote = "メールアプリが開かない時は、次のアドレスへ送ってください: {0}";             // ID 10141 {0}=アドレス
    public const string CopyAddress = "アドレスをコピー";                                                   // ID 10142
    public const string Copied = "コピーしました";                                                         // ID 10143
    public const string CreditsNote = "このゲームを作った人と、使っている素材です。";                            // ID 10144
    public const string ContactHint = "件名と、バージョン・利用環境の欄は自動で入ります。";                       // ID 10145
    // ID 10146 / 10147: サーバーのデータの削除がまだアプリの中からできない環境(production に未配置)の案内
    public const string DeletionByMailOnly = "アプリの中からの削除は準備中です。削除をご希望の時は、プレイヤーIDを書いて上のアドレスへメールでご依頼ください。"; // ID 10146
    public const string DeletionNoteByMail = "ランキングの記録など、サーバー上のデータの削除は、メールでご依頼いただけます(この端末のカードや進行は消えません)。";       // ID 10147

    // 最高速度の設定(2026-10-24、設定 → 操作)。エステルのレビュー済み・30言語へ反映済み(Tools/loc/keys_extra25.tsv)
    public const string MaxSpeedTitle = "最高速度";                                                     // ID 10063
    public const string MaxSpeedStandardSuffix = "(標準)";                                              // ID 10064(100% の後ろ)
    public const string MaxSpeedEstimate = "{0}: 約 {1}km/h";                                           // ID 10065 {0}=キャラ名 {1}=速さ
    public const string MaxSpeedSlow = "ゆっくり";                                                       // ID 10066
    public const string MaxSpeedStandard = "標準";                                                       // ID 10067
    public const string MaxSpeedFast = "速い";                                                           // ID 10068
    public const string MaxSpeedNote = "自然に加速した時の最高速度です。キャラごとの速さの違いと速さのカードの効果はそのまま残ります。"; // ID 10069
    public const string MaxSpeedLocked = "ラン中は変更できません。次のランから反映されます。";             // ID 10070
    public const string MaxSpeedBelowAssist = "この最高速度では、高速補助が作動しない場合があります(オート開始速度より遅いため)。"; // ID 10071

    // ID 10012〜10047(2026-10-19): データとプライバシー(プライバシーポリシー/データの削除)と LOCAL の暗号化。レビュー待ち(Docs/LocReview_2026-10-19)。
    //  前の文が無い新しい画面なので、訳が入るまでは日本語のまま出る(開発版だけ)。製品版の Android ビルドは全言語の訳が入るまで止まる
    //  2026-10-23: 10010〜10047・10056〜10059 をエステルのレビュー(Tools/loc/est23、42件 × 29言語)で 30言語へ反映 → すべて外した
    //  (10026 の「購入を復元」は各言語の既存のボタン名 8133 にそろえた: est23/fix10026.py)
    public static readonly (string id, string key)[] Pending =
    {
    };
}
