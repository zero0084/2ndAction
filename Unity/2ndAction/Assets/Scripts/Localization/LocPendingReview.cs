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

    public static readonly (string id, string key)[] Pending = { ("10010", ReturnHomeLine2), ("10011", ReturnHomeLine3) };
}
