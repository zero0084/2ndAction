using System.Collections.Generic;
using UnityEngine;

// ===== カードの2つの成長をつなぐ入口(2026-10-04) =====
//  メタ育成(ホーム): 所持Lv1〜9 → Lv9 MAX → Mastery ★1〜5 → AWAKENED   … CardMastery
//  ラン中:          能力Lv(そのランで適用した回数、上限9)                  … GameManager.GetAbilityRunStack
// Final Evolution(別工程)はこの入口だけを見る:
//  ・資格 = そのランで能力が Lv9 に届いた(所持Lv9 だから最初から資格、にはしない。下の注意)
//  ・AWAKENED 済みなら、Final Evolution に小さな追加特典(内容は別工程)
// 注意(現行仕様): 所持 Lv9 のカード(v2キー、強化量9)をキャラカードに付けると、ランの開始時点で能力 Lv9 になる。
//   デッキの Lv9 カードをレベルアップで選んだ場合も1回で Lv9 になる。どこまでを「ランで届いた」と数えるかは
//   Final Evolution の工程で決める。今は両方を取れるようにしている(ReachedMaxInRun / MaxAtRunStart)。
public static class CardProgression
{
    // ---- メタ育成(ホーム側の永久の記録) ----
    public static bool IsMaxReached(string cardId) => CardMastery.IsMaxReached(cardId);
    public static int MasteryLevel(string cardId) => CardMastery.MasteryLevel(cardId);
    public static int MasteryProgress(string cardId) => CardMastery.MasteryProgress(cardId);
    public static int MasteryNeed(string cardId) => CardMastery.NeedForNext(cardId);
    public static bool IsAwakened(string cardId) => CardMastery.IsAwakened(cardId);

    // ---- ラン中 ----
    public static int RunAbilityLevel(string cardId)
    {
        var gm = GameManager.Instance;
        return gm != null ? gm.GetAbilityRunStack(GameManager.MainAbilityOf(cardId) ?? CardMastery.BaseIdOf(cardId)) : 0;
    }
    public static bool ReachedMaxInRun(string cardId) => RunAbilityLevel(cardId) >= CardVariant.MaxLevel;
    // ランの開始時(キャラカードを付けた直後)の能力Lv。CONTINUE でも同じ値(キャラカードだけを数える)
    public static int RunStartLevel(string cardId)
    {
        var gm = GameManager.Instance;
        return gm != null ? gm.RunStartAbilityLevel(GameManager.MainAbilityOf(cardId) ?? CardMastery.BaseIdOf(cardId)) : 0;
    }
    public static bool MaxAtRunStart(string cardId) => RunStartLevel(cardId) >= CardVariant.MaxLevel;

    // FINAL EVOLUTION の資格(2026-10-04 確定): そのランで能力Lvが実際に9(キャラカードで開始時から9でも資格あり)。
    // 実際に進化できる(READY)のは資格を得てから一定距離の後(FinalEvolution)
    public static bool FinalEvolutionEligible(string cardId) => ReachedMaxInRun(cardId);
    // AWAKENED 済みなら Final Evolution に追加の特典を付けられる(必須ではない)
    public static bool FinalEvolutionAwakenedBonus(string cardId) => IsAwakened(cardId);
}

// ===== AWAKENED のカード固有の効果(データ) =====
// 全カード共通の AWAKENED(枠の光/紋章/コレクション表示)とは別に、カードごとに小さな追加効果を後から足せる。
// 通常の数値(攻撃/速度/EXP…)を上げる物にはしない(Card Balance V3 を壊さない)。見た目/演出/小さな恩恵の方向。
// 今は「予定」だけを登録し、実装済み(implemented)の物はまだ無い。実装したら kind を処理する所(ゲーム側)で
// AwakenedEffects.Active(cardId, kind) を見る。
public enum AwakenedEffectKind
{
    None,
    HitVfx,            // 命中時の小さな強化VFX
    AfterimageBoost,   // 高速時の残像の強化
    BloodAura,         // 回復時の血のオーラ
    ReviveFx,          // 復活の演出の強化
    ProcVfx,           // 追加攻撃の発動時の専用VFX
    UltimateFinale,    // ULTIMATE の最終演出/専用オーラ/BUFF演出/締めのVFX
}

public class AwakenedEffectDef
{
    public string cardId;
    public AwakenedEffectKind kind;
    public string description;
    public bool implemented;   // ゲーム側の処理ができたら true
}

public static class AwakenedEffects
{
    // 予定(マスターの案)。implemented=false の間は何も起きない
    static readonly AwakenedEffectDef[] table =
    {
        new AwakenedEffectDef { cardId = "attack_up", kind = AwakenedEffectKind.HitVfx, description = "命中時の小さな強化VFX" },
        new AwakenedEffectDef { cardId = "speed_up", kind = AwakenedEffectKind.AfterimageBoost, description = "高速時の残像の強化" },
        new AwakenedEffectDef { cardId = "vampire", kind = AwakenedEffectKind.BloodAura, description = "回復時の Blood Aura" },
        new AwakenedEffectDef { cardId = "phoenix", kind = AwakenedEffectKind.ReviveFx, description = "復活の演出の強化 / 小さな追加の恩恵" },
        new AwakenedEffectDef { cardId = "double_attack", kind = AwakenedEffectKind.ProcVfx, description = "発動時の専用VFX" },
        new AwakenedEffectDef { cardId = UltimateArt.CardId, kind = AwakenedEffectKind.UltimateFinale, description = "必殺技の最終演出/専用オーラ/BUFF演出/締めのVFX(ダメージは上げない)" },
    };

    static Dictionary<string, AwakenedEffectDef> map;
    public static AwakenedEffectDef Get(string cardId)
    {
        if (map == null) { map = new Dictionary<string, AwakenedEffectDef>(); foreach (var d in table) map[d.cardId] = d; }
        return cardId != null && map.TryGetValue(CardMastery.BaseIdOf(cardId), out var e) ? e : null;
    }
    public static IReadOnlyList<AwakenedEffectDef> All => table;
    // ゲーム側が見る: このカードが AWAKENED 済みで、その種類の固有効果が実装済みか
    public static bool Active(string cardId, AwakenedEffectKind kind)
    {
        var d = Get(cardId);
        return d != null && d.implemented && d.kind == kind && CardMastery.IsAwakened(cardId);
    }
}
