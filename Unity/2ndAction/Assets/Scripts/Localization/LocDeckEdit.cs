// タイトル画面 / デッキ編集の操作の仕上げ / カードの分解(2026-10-31)の新しい文。ID 10323〜
//  画面は Loc.T(定数) で出す。日本語/英語はエステルのレビュー済み(Docs/LocReview_2026-10-31b、8件を修正)。30言語は Tools/loc の keys_extra36
public static class LocDeckEdit
{
    // ---- タイトル画面
    public const string TapToStart = "タップして開始";                                   // ID 10323(Android など、タッチの機器)
    public const string PressToStart = "キー・クリック・パッドのボタンで開始";                    // ID 10324(PC)

    // ---- デッキ編集: 操作の案内・ドラッグ
    public const string Hint = "タップ: 出し入れ  長押し: 詳細  ドラッグ: 移動";    // ID 10325(何も選んでいない時の小さな詳細)
    public const string ReplaceConfirm = "デッキの「{0}」を「{1}」に入れ替えますか？";      // ID 10326 {0}=デッキにあるカード {1}=入れるカード
    public const string DropToRemove = "ここで離すとデッキから外します";                    // ID 10327(デッキのカードをドラッグ中の一覧の上)
    public const string DeckFull = "デッキがいっぱいです(12枚まで)";                        // ID 10328
    public const string NoFreeCopy = "空きカードがありません(デッキ・キャラカードで使用中)"; // ID 10329

    // ---- デッキ編集: 大きな詳細
    public const string LvEffects = "レベル別の効果";                                       // ID 10330(見出し)
    public const string Current = "現在";                                                // ID 10331(「Lv5(現在)」)
    public const string ComboHead = "COMBO(一緒に持つと発動)";                            // ID 10332
    public const string NotEligible = "対象外";                                           // ID 10333(FINAL EVOLUTION の対象でないカード)
    public const string StateHead = "使用状況";                                            // ID 10334
    public const string StateInDeck = "デッキに入っています";                               // ID 10335
    public const string StateEquipped = "キャラカードに装備中";                             // ID 10336
    public const string StatePreset = "ほかのデッキ(プリセット)に入っています";              // ID 10337
    public const string StateFree = "装備していません";                                     // ID 10338
    public const string UnlockHead = "解放条件";                                            // ID 10339
    public const string UnlockLine = "最高到達距離 {0}m / カードガチャ Lv.{1}";              // ID 10340 {0}=距離(3桁区切り) {1}=段階
    public const string OwnedLine = "所持 {0}枚(分解可能 {1}枚)";                     // ID 10341

    // ---- カードの分解
    public const string Dismantle = "分解";                                                // ID 10342(ボタン。後ろに「+{0} MILE」)
    public const string DismantleConfirm = "このカード1枚を分解して {0} MILE を受け取りますか？";   // ID 10343 {0}=MILE
    public const string DismantleIrreversible = "分解したカードは元に戻せません。";                  // ID 10344
    public const string DismantleLast = "これはこのカードの最後の1枚です。分解すると、もう一度手に入れるまで使えません。"; // ID 10345
    public const string DismantleRare = "レア度の高いカードです(★{0} Lv.{1})。";                    // ID 10346 {0}=★の数 {1}=Lv
    public const string DismantleEquipped = "使用中のカードは分解できません。デッキ・プリセット・キャラカードから外してください。"; // ID 10347
    public const string DismantleDone = "分解しました: +{0} MILE";                                   // ID 10348 {0}=MILE

    public static readonly string[] All =
    {
        TapToStart, PressToStart, Hint, ReplaceConfirm, DropToRemove, DeckFull, NoFreeCopy, LvEffects, Current, ComboHead, NotEligible,
        StateHead, StateInDeck, StateEquipped, StatePreset, StateFree, UnlockHead, UnlockLine, OwnedLine, Dismantle, DismantleConfirm,
        DismantleIrreversible, DismantleLast, DismantleRare, DismantleEquipped, DismantleDone,
    };
}
