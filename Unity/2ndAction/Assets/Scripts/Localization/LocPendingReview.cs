// 翻訳の反映待ち(エステルのレビュー待ち)の文の一覧(2026-10-15)。
//  ・画面は Loc.Reviewed(新しい文, 前の文) / Loc.Ready(新しい文) で出す: 日本語と、訳が入った言語だけ新しい文。ほかの言語は前の(レビュー済みの)文のまま
//  ・前の文は古い仕様の説明なので、公開版までに必ず訳を入れる → 製品版の Android ビルドは、ここの文の訳が全言語に無いと止まる(Editor/ReleaseBuildGuard)
//  ・訳を入れたら、ここから外す(画面の Loc.Reviewed はそのままでよい)
public static class LocPendingReview
{
    // ID 10007: CPU サポートの画面の説明の下 / マルチの CO-OP の説明の下
    public const string MileShareNote = "獲得MILEは参加人数で分配";
    // ID 10008: CPU サポートの画面の説明(前の文 = SupportOld)
    public const string SupportDesc = "CPU の仲間と一緒に走ります(最大3人)。仲間は自動で戦い、倒れても少しすると戻ってきます。仲間の取り分はあなたには入りません。サポートを使ったランはランキングの対象外です(解放・自己ベストは通常どおり)。";
    public const string SupportOld = "CPU の仲間と一緒に走ります(最大3人)。仲間は自動で戦い、倒れても少しすると戻ってきます。報酬はあなたの分だけです。サポートを使ったランはランキングの対象外です(MILE・解放・自己ベストは通常どおり)。";
    // ID 10009: 疾走出発の画面の説明(前の文 = SprintOld)
    public const string SprintDesc = "攻略済みの区間を一気に駆け抜け、選んだ関門の少し手前から走り始めます。ボス報酬のカードはランダムに自動取得。5,000mごとのリングに上下で高さを合わせると追加で1枚選べます。飛ばした距離のMILEは付きません。プレイヤーレベルは到着の距離に合わせて上がった状態で始まります。";
    public const string SprintOld = "攻略済みの区間を一気に駆け抜け、選んだ関門の少し手前から走り始めます。ボス報酬のカードはランダムに自動取得。5,000mごとのリングに上下で高さを合わせると追加で1枚選べます。飛ばした距離のMILE・EXPは付きません。";

    public static readonly (string id, string key)[] Pending =
    {
        ("10007", MileShareNote),
        ("10008", SupportDesc),
        ("10009", SprintDesc),
    };
}
