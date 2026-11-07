// プロフィール/走る理由/常設チャレンジ/バッヂ/定型コミュニケーションの文(2026-11-07)。訳の鍵は日本語の文(Loc の決まり)。
//  ID 10800〜10899(既存の最大 10642 の後。メインの作業と重ならないよう 10800 から)。
//   10800〜10811 走る理由(10811 ONE MORE MILE は全言語で英字のまま)
//   10820〜10845 チャレンジの達成条件(チャレンジ名・バッヂ名・カテゴリ名は英字で全言語そのまま = カード名と同じ扱い)
//   10850〜10889 画面の文
//   10890〜10899 定型コミュニケーション
//  訳はアイリーンの下書き(Tools/loc/keys_extra_prof.tsv → out/<code>.prof.tsv)。エステルのレビューが済むまで LocPendingReview.Drafts に載せ、
//  製品版のビルドは止まる(Editor/ReleaseBuildGuard)。レビュー用の表は Docs/Profile_2026-11-07/
public static class LocProfile
{
    // ---- 画面の文(10850〜) ----
    public const string Title = "プロフィール";                       // 既存(設定の見出しと同じ鍵。訳済み)
    public const string WhyIRun = "走る理由";                          // 10850(英: WHY I RUN)
    public const string ReasonNone = "まだ選んでいません";              // 10851
    public const string Change = "変更";                               // 10852
    public const string ReasonPickTitle = "走る理由を選ぶ";             // 10853
    public const string NameUnset = "名前は未設定";                     // 10854(コードネームが空の時。設定 → プロフィールで決める)
    public const string StatBest = "BEST";                             // 既存
    public const string StatTotal = "累計距離";                         // 既存(Total Distance)
    public const string StatTopSpeed = "最高速度";                      // 既存(Top Speed)
    public const string Reach100 = "100km到達";                         // 10855
    public const string NotYet = "未到達";                              // 10856
    public const string Reached = "到達";                               // 10857(ラスダンの到達)
    public const string Cleared = "クリア";                             // 10858(ラスダンのクリア)
    public const string Unknown = "？？？";                              // 記号(訳さない。まだ解放していないマップの名前の代わり)
    public const string BadgesHeader = "バッヂ";                         // 10859(英: BADGES)
    public const string BadgeCount = "獲得バッヂ {0} / {1}";             // 10860
    public const string OpenBadges = "バッヂとチャレンジ";               // 10861(一覧を開くボタン/一覧の見出し)
    public const string SlotEmpty = "空き";                              // 既存(Empty)
    public const string Equip = "装備";                                  // 10862
    public const string Unequip = "外す";                                // 10863
    public const string Locked = "未獲得";                               // 10864
    public const string Done = "達成";                                   // 10865
    public const string EquipHint = "枠を選んでから、獲得したバッヂの「装備」を押してください(最大3個)"; // 10866
    public const string LockedNote = "未獲得のバッヂは装備できません";    // 10867
    public const string Reward = "報酬 +{0} MILE";                        // 10868
    public const string Progress = "{0} / {1}";                           // 記号(訳さない)
    public const string ChallengeComplete = "CHALLENGE COMPLETE";         // 10869(全言語で英字のまま。マスター指定の見出し)
    public const string RestoredTitle = "チャレンジ";                      // 10870(お知らせの見出し)
    public const string RestoredBody = "これまでの記録から {0} 個のチャレンジを達成済みにしました。バッヂと合計 {1} MILE を受け取りました。"; // 10871
    public const string NoSpeedYet = "—";                                 // 記号
    public const string AllCategory = "すべて";                           // 既存(All。カード編集の絞り込みと同じ鍵)
    public const string ShowDone = "達成済み";                            // 10873(一覧の絞り込み)
    public const string ShowLocked = "未達成";                            // 10874(一覧の絞り込み)
    public const string Records = "戦績";                                // 10876(戦績の見出し)
    public const string SelectedSlot = "{0}番目の枠を選択中";              // 10877
    public const string QuickChatHeader = "定型メッセージ";                // 10878(マルチの部屋の見出し)

    // 走る理由(ID は保存する番号。並びは表示の順)
    public static readonly (int id, string text)[] Reasons =
    {
        (1, "もっと先を見たい"),        // 10800
        (2, "100kmを越えたい"),         // 10801
        (3, "強くなりたい"),            // 10802
        (4, "記録を更新したい"),        // 10803
        (5, "仲間と走りたい"),          // 10804
        (6, "誰かに勝ちたい"),          // 10805
        (7, "カードを集めたい"),        // 10806
        (8, "ボスを倒したい"),          // 10807
        (9, "まだ止まりたくない"),      // 10808
        (10, "理由なんていらない"),     // 10809
        (11, "ただ走るのが好き"),       // 10810
        (12, "ONE MORE MILE"),          // 10811(英字のまま)
    };
    public static string ReasonText(int id) { foreach (var r in Reasons) if (r.id == id) return r.text; return null; }

    // 定型コミュニケーション(ID は通信で送る番号 1〜。0 は使わない。並びは表示の順)
    public enum ChatGroup { Greet, Action, End }
    public static readonly (byte id, ChatGroup group, string text)[] Chat =
    {
        (1, ChatGroup.Greet, "よろしく!"),        // 10890
        (2, ChatGroup.Greet, "ナイス!"),          // 10891
        (3, ChatGroup.Greet, "ありがとう!"),      // 10892
        (4, ChatGroup.Action, "行こう!"),         // 10893
        (5, ChatGroup.Action, "助けて!"),         // 10894
        (6, ChatGroup.Action, "先に行って!"),     // 10895
        (7, ChatGroup.Action, "もう一回!"),       // 10896
        (8, ChatGroup.End, "抜けます"),           // 10897
        (9, ChatGroup.End, "また走ろう!"),        // 10898
    };
    public static string ChatText(byte id) { foreach (var c in Chat) if (c.id == id) return c.text; return null; }

    // 下書きの訳(エステルのレビュー待ち)の文。ReleaseBuildGuard が製品版のビルドを止める
    public static System.Collections.Generic.IEnumerable<string> AllDraftKeys()
    {
        foreach (var r in Reasons) if (r.id != 12) yield return r.text;
        foreach (var c in ChallengeDefs.All) yield return c.condition;
        yield return WhyIRun; yield return ReasonNone; yield return Change; yield return ReasonPickTitle; yield return NameUnset;
        yield return Reach100; yield return NotYet; yield return Reached; yield return Cleared; yield return BadgesHeader; yield return BadgeCount;
        yield return OpenBadges; yield return Equip; yield return Unequip; yield return Locked; yield return Done; yield return EquipHint; yield return LockedNote;
        yield return Reward; yield return RestoredTitle; yield return RestoredBody; yield return ShowDone; yield return ShowLocked;
        yield return Records; yield return SelectedSlot; yield return QuickChatHeader;
        foreach (var c in Chat) yield return c.text;
    }
}
