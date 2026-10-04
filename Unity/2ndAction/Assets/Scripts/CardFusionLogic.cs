using System.Collections.Generic;
using System.Text;
using UnityEngine;

// カード合成改修(2026-09-26) - 合成のルールと確定処理(UIから独立、テスト可能)。
//
//  ・合成Lv(2026-09-28改訂) = 同名/異名で両方成功 … 2枚の合計 / 異名で片側だけ成功 … 成功した側のLvだけ
//    (失敗側のLv・能力・強化量は一切加算しない)。入力2枚の合計がLv.9を超える組み合わせは、片側成功の
//    可能性があっても合成不可・消費なし(上限Lv.9)
//  ・同名(主能力のカードIDが同じ)… 成功率100%、両方の能力一式を継承(同じ能力は強化量を合算)
//  ・異名 … 抽選はカードごとの「能力一式」単位。メイン側50%・素材側25%(独立)
//        両方成功: 両側の能力一式 / メインのみ: メイン側だけ / 素材のみ: 素材側だけ(主能力も素材側)
//        両方失敗: 完成品なし。2枚消費し、MILEを還元
//  ・還元MILE = Σ(レア度(★の数) × 合成Lv × RefundCoefficient)
//  ・能力は主能力1+サブ能力8=最大9種類。結果が9種類を超える可能性がある組み合わせは合成不可
//  ・カード長期育成(2026-10-04): 同名で Lv9 を超える分は捨てずに Mastery へ(CardMastery)
//      メインが Lv9 MAX … 素材だけを消費して Mastery +素材のLv(メインは変わらない) = Kind.Mastery
//      2枚の合計が Lv9 を超える … 完成品は Lv9、超えた分(合計-9)を Mastery へ(以前は合成不可だった)
//      AWAKENED(★5)済みのカードを Lv9 MAX のメインにした合成は、カードを消費しない(★5 の後の使い道は今後)
//
// 確定処理(Execute)は抽選→所持カードとMILEの変更をメモリ上で行い→両方を書いてから
// PlayerPrefs.Save()を1回だけ呼ぶ。演出やリザルトは確定済みの結果(FusionResult)を
// 表示するだけで、スキップや再表示で再抽選することはない。
public static class CardFusionLogic
{
    public static float MainInheritChance = 0.50f;
    public static float MaterialInheritChance = 0.25f;
    public static int RefundCoefficient = 50;

    // 開発用の結果固定(テスト/デバッグ専用)。nullなら通常の確率で抽選する。
    public enum ForcedOutcome { BothSuccess, MainOnly, MaterialOnly, BothFail }
    public static ForcedOutcome? DebugForcedOutcome;

    public enum Kind { SameName, CrossBoth, CrossMainOnly, CrossMaterialOnly, CrossFail, Mastery }

    public class FusionResult
    {
        public Kind kind;
        public string mainKey, materialKey;
        public CardVariant main, material;
        public bool mainInherited, materialInherited;
        public string resultKey;      // 完成品(失敗時null)
        public CardVariant result;
        public CardVariant baseline;  // NEW/強化の比較元(完成品の主能力側の元カード)
        public int refundMile;
        public int mileAfter;
        public int masteryGain;              // Mastery に入る量(Lv9 を超えた分 / Lv9 MAX への素材のLv)
        public CardMastery.Gain mastery;     // 確定後の Mastery の変化
        public bool IsSuccess => kind != Kind.CrossFail;
    }

    // 同名で Lv9 MAX のメインへ素材を入れる合成(素材だけ消費して Mastery)
    public static bool IsMasteryFusion(CardVariant a, CardVariant b) => IsSameName(a, b) && a.level >= CardVariant.MaxLevel;
    // Mastery へ入る量(同名のみ)。Lv9 MAX のメインなら素材のLv、合計が Lv9 を超えるなら超えた分
    public static int MasteryGainFor(CardVariant a, CardVariant b)
    {
        if (!IsSameName(a, b)) return 0;
        if (a.level >= CardVariant.MaxLevel) return b.level;
        return Mathf.Max(0, a.level + b.level - CardVariant.MaxLevel);
    }

    // ===== 選択可否 ===== //

    // 使用中の理由(キャラカード装備/デッキ)。使えるならnull。
    public static string LockReason(string key, int wantCount = 1)
    {
        var gm = GameManager.Instance;
        CardInventory.Stack s = CardInventory.FindByKey(key);
        if (s == null) return "このカードを所持していません";
        int deck = gm != null ? gm.GetDeckLockedCountForStack(key, s.level) : 0;
        int chara = gm != null ? gm.GetCharacterCardLockedCountForStack(key, s.level) : 0;
        int free = s.count - deck - chara;
        if (free >= wantCount) return null;
        if (deck > 0 && chara > 0) return "デッキとキャラクターカードで使用中のため選べません";
        if (chara > 0) return "キャラクターカードに装備中のため選べません";
        if (deck > 0) return "デッキで使用中のため選べません";
        return wantCount >= 2 ? "同じ性能のカードが2枚必要です(所持1枚)" : "このカードを所持していません";
    }

    public static int AvailableCount(string key)
    {
        var gm = GameManager.Instance;
        CardInventory.Stack s = CardInventory.FindByKey(key);
        if (s == null) return 0;
        return gm != null ? gm.GetAvailableCountForStack(key, s.level) : s.count;
    }

    public static bool IsSameName(CardVariant a, CardVariant b) => a != null && b != null && a.mainId == b.mainId;

    // 実行できない理由。実行できるならnull。
    public static string BlockReason(string mainKey, string materialKey)
    {
        if (string.IsNullOrEmpty(mainKey) || string.IsNullOrEmpty(materialKey)) return "メインカードと素材カードを選んでください";
        CardVariant a = CardVariant.Parse(mainKey), b = CardVariant.Parse(materialKey);
        if (a == null || b == null) return "カードの情報を読み込めません";
        bool sameStack = mainKey == materialKey;
        if (IsMasteryFusion(a, b))
        {
            // メイン(Lv9 MAX)は消費しないので、デッキ/キャラカードで使用中でもよい。素材の1枚だけが空いていればよい
            CardInventory.Stack ms = CardInventory.FindByKey(mainKey);
            if (ms == null) return "このカードを所持していません";
            if (sameStack)
            {
                if (ms.count < 2) return "同じ性能のカードが2枚必要です(所持1枚)";
                if (AvailableCount(mainKey) < 1) return "素材にする1枚がデッキ/キャラクターカードで使用中です";
            }
            else { string lockM = LockReason(materialKey); if (lockM != null) return lockM; }
            if (CardMastery.IsAwakened(a.mainId)) return "AWAKENED(★5)済みのため、これ以上Masteryは進みません(カードは消費しません。★5後の余ったカードの使い道は今後追加します)";
            return null;
        }
        string lockA = LockReason(mainKey, sameStack ? 2 : 1);
        if (lockA != null) return lockA;
        if (!sameStack) { string lockB = LockReason(materialKey); if (lockB != null) return lockB; }
        int level = a.level + b.level;
        if (level > CardVariant.MaxLevel && !IsSameName(a, b)) return $"合成後のLvが{level}になるため合成できません(上限Lv.{CardVariant.MaxLevel})";
        var union = Merge(a, b, a.mainId);
        if (union.AbilityCount > CardVariant.MaxAbilities) return $"能力が{union.AbilityCount}種類になるため合成できません(上限{CardVariant.MaxAbilities}種類)";
        return null;
    }

    // ===== 結果の組み立て ===== //

    static CardVariant Merge(CardVariant a, CardVariant b, string mainId)
    {
        var r = new CardVariant { mainId = mainId };
        if (a != null) foreach (var x in a.abilities) r.AddAbility(x.id, x.stacks);
        if (b != null) foreach (var x in b.abilities) r.AddAbility(x.id, x.stacks);
        r.Normalize();
        return r;
    }

    // 結果ごとの完成Lv(2026-09-28改訂)。両方継承=合計、片側だけ=成功した側のLvのみ、両方失敗=0(完成品なし)。
    public static int ResultLevel(CardVariant a, CardVariant b, Kind kind) => kind switch
    {
        Kind.SameName => a.level + b.level,
        Kind.CrossBoth => a.level + b.level,
        Kind.CrossMainOnly => a.level,
        Kind.CrossMaterialOnly => b.level,
        _ => 0,
    };

    public static int RefundFor(CardVariant a, CardVariant b) =>
        a.rarity * a.level * RefundCoefficient + b.rarity * b.level * RefundCoefficient;

    public static FusionResult Resolve(string mainKey, string materialKey, bool mainOk, bool materialOk)
    {
        CardVariant a = CardVariant.Parse(mainKey), b = CardVariant.Parse(materialKey);
        var r = new FusionResult { mainKey = mainKey, materialKey = materialKey, main = a, material = b };
        if (IsMasteryFusion(a, b))
        {
            // メイン(Lv9 MAX)はそのまま残り、素材だけが Mastery になる
            r.kind = Kind.Mastery;
            r.mainInherited = r.materialInherited = true; // 演出: 素材の光がメインへ集まる
            r.result = a.Clone();
            r.baseline = a;
            r.resultKey = mainKey;
            r.masteryGain = MasteryGainFor(a, b);
            return r;
        }
        if (IsSameName(a, b))
        {
            r.kind = Kind.SameName;
            r.mainInherited = r.materialInherited = true;
            r.result = Merge(a, b, a.mainId);
            r.result.rarity = Mathf.Max(a.rarity, b.rarity);
            r.baseline = a;
            r.masteryGain = MasteryGainFor(a, b);
            if (r.masteryGain > 0) ClampStacks(r.result); // Lv9 を超えた分は Mastery へ(能力の強化量も9まで)
        }
        else if (mainOk && materialOk)
        {
            r.kind = Kind.CrossBoth;
            r.mainInherited = r.materialInherited = true;
            r.result = Merge(a, b, a.mainId);
            r.result.rarity = Mathf.Min(5, Mathf.Max(a.rarity, b.rarity) + 1);
            r.baseline = a;
        }
        else if (mainOk)
        {
            r.kind = Kind.CrossMainOnly;
            r.mainInherited = true;
            r.result = Merge(a, null, a.mainId);
            r.result.rarity = a.rarity;
            r.baseline = a;
        }
        else if (materialOk)
        {
            r.kind = Kind.CrossMaterialOnly;
            r.materialInherited = true;
            r.result = Merge(b, null, b.mainId);
            r.result.rarity = b.rarity;
            r.baseline = b;
        }
        else
        {
            r.kind = Kind.CrossFail;
            r.refundMile = RefundFor(a, b);
            return r;
        }
        r.result.level = Mathf.Min(CardVariant.MaxLevel, ResultLevel(a, b, r.kind));
        r.resultKey = r.result.ToKey();
        return r;
    }

    static void ClampStacks(CardVariant v)
    {
        for (int i = 0; i < v.abilities.Count; i++)
            if (v.abilities[i].stacks > CardVariant.MaxLevel) v.abilities[i] = new CardVariant.Ability { id = v.abilities[i].id, stacks = CardVariant.MaxLevel };
    }

    // ===== 確定 ===== //

    static bool executing;
    public static FusionResult LastResult { get; private set; }

    // 合成を1回だけ確定する。実行できなければnull(何も消費しない)。
    public static FusionResult Execute(string mainKey, string materialKey, out string error)
    {
        error = null;
        if (executing) { error = "合成処理中です"; return null; }
        executing = true;
        try
        {
            error = BlockReason(mainKey, materialKey);
            if (error != null) return null;
            CardVariant a = CardVariant.Parse(mainKey), b = CardVariant.Parse(materialKey);
            bool mainOk, materialOk;
            if (IsSameName(a, b)) { mainOk = materialOk = true; }
            else if (DebugForcedOutcome.HasValue)
            {
                var f = DebugForcedOutcome.Value;
                mainOk = f == ForcedOutcome.BothSuccess || f == ForcedOutcome.MainOnly;
                materialOk = f == ForcedOutcome.BothSuccess || f == ForcedOutcome.MaterialOnly;
            }
            else
            {
                mainOk = Random.value < MainInheritChance;
                materialOk = Random.value < MaterialInheritChance;
            }
            FusionResult r = Resolve(mainKey, materialKey, mainOk, materialOk);

            // ---- メモリ上で所持カード/MILE/Masteryを変更 → まとめて保存 ----
            if (r.kind == Kind.Mastery)
            {
                if (!CardInventory.ConsumeForMasteryInMemory(mainKey, materialKey)) { error = "カードが不足しています"; return null; }
            }
            else if (!CardInventory.ApplyFusionInMemory(mainKey, materialKey, r.resultKey)) { error = "カードが不足しています"; return null; }
            var gm = GameManager.Instance;
            if (r.refundMile > 0 && gm != null) gm.AddMileWithoutFlush(r.refundMile);
            if (r.result != null && r.result.level >= CardVariant.MaxLevel) CardMastery.MarkMaxReachedInMemory(r.result.mainId);
            if (r.masteryGain > 0) r.mastery = CardMastery.AddProgressInMemory(r.result.mainId, r.masteryGain, $"fusion {r.kind}");
            CardInventory.WriteWithoutFlush();
            CardMastery.WriteWithoutFlush();
            PlayerPrefs.Save();
            r.mileAfter = gm != null ? gm.TotalOwnedMile : 0;
            LastResult = r;
            Debug.Log($"[Fusion] {r.kind} main={mainKey} material={materialKey} -> {(r.resultKey ?? "none")} refund={r.refundMile}");
            return r;
        }
        finally { executing = false; }
    }

    // ===== 合成前の説明文 ===== //

    public static string Preview(string mainKey, string materialKey)
    {
        CardVariant a = CardVariant.Parse(mainKey), b = CardVariant.Parse(materialKey);
        if (a == null || b == null) return "";
        var sb = new StringBuilder();
        int level = a.level + b.level;
        if (IsMasteryFusion(a, b))
        {
            var g = CardMastery.Simulate(a.mainId, b.level);
            int need = CardMastery.NeedForNext(a.mainId);
            sb.Append("<color=#ffd76a><b>【MASTERY】 Lv.9 MAX の育成</b></color>\n");
            sb.Append("メインカードはそのまま残り(Lv.9 MAX)、素材カードだけを消費します。\n\n");
            sb.Append($"今の Mastery: <color=#ffd76a>{CardMastery.StarsFor(g.levelBefore)}</color>  {(g.levelBefore >= CardMastery.MaxStars ? "AWAKENED" : g.progressBefore + " / " + need)}\n");
            sb.Append($"今回の合成: <b>+{b.level} Mastery</b>(素材 Lv.{b.level} = {b.level}枚分)\n");
            sb.Append($"合成後: <color=#ffd76a>{CardMastery.StarsFor(g.levelAfter)}</color>  {(g.levelAfter >= CardMastery.MaxStars ? "<color=#ffe08a><b>AWAKENED</b></color>" : g.progressAfter + " / " + MasteryTuning.Need(g.levelAfter))}");
            if (g.StarsGained > 1) sb.Append($"  <size=19>(★{g.StarsGained}つ分、余りは次の★へ繰り越し)</size>");
            sb.Append('\n');
            if (g.overflowAdded > 0) sb.Append($"<size=19>★5 の後に余る {g.overflowAdded} は捨てずに保管します</size>\n");
            if (b.AbilityCount > 1) sb.Append($"<color=#ff9a8a><size=19>素材のサブ能力({b.AbilityCount - 1}種)は引き継がれません</size></color>\n");
            sb.Append("\n<size=19>Mastery は通常の性能(攻撃/速度/EXP など)を上げません。育成の記録と AWAKENED への道のりです。</size>\n");
            sb.Append("\n<color=#ff9a8a>実行すると、素材カード1枚が消費されます。</color>");
            return sb.ToString();
        }
        if (IsSameName(a, b))
        {
            var merged = Merge(a, b, a.mainId);
            int over = MasteryGainFor(a, b);
            if (over > 0)
            {
                var g = CardMastery.Simulate(a.mainId, over);
                sb.Append("<color=#ffd76a><b>【同名強化 → Lv.9 MAX】 成功率 100%</b></color>\n");
                sb.Append($"合成Lv: Lv.{a.level} + Lv.{b.level} → <b>Lv.9 MAX</b>\n");
                sb.Append($"Lv.9 を超える <b>{over}</b> は捨てずに Mastery へ: {CardMastery.StarsFor(g.levelBefore)} → <color=#ffd76a>{CardMastery.StarsFor(g.levelAfter)}</color>{(g.levelAfter >= CardMastery.MaxStars ? " AWAKENED" : " " + g.progressAfter + " / " + MasteryTuning.Need(g.levelAfter))}\n\n");
                sb.Append("<b>継承する能力(強化量は9まで)</b>\n");
                foreach (var x in merged.abilities) sb.Append($"・{CardVariant.AbilityName(x.id)} <b>×{Mathf.Min(CardVariant.MaxLevel, x.stacks)}</b>\n");
                sb.Append("\n<color=#ff9a8a>実行すると、選んだ2枚は消費されます(完成カード Lv.9 MAX を1枚得ます)。</color>");
                return sb.ToString();
            }
            sb.Append("<color=#ffd76a><b>【同名強化】 成功率 100%</b></color>\n");
            sb.Append($"合成Lv: Lv.{a.level} + Lv.{b.level} → <b>Lv.{level}</b>\n\n");
            sb.Append("<b>継承する能力(同じ能力は強化量を合算)</b>\n");
            foreach (var x in merged.abilities)
            {
                int sa = a.StacksOf(x.id), sbb = b.StacksOf(x.id);
                string from = sa > 0 && sbb > 0 ? $"×{sa} + ×{sbb} → " : "";
                sb.Append($"・{CardVariant.AbilityName(x.id)} {from}<b>×{x.stacks}</b>  <size=18>({CardVariant.AbilityEffectText(x.id)} ×{x.stacks})</size>\n");
            }
        }
        else
        {
            sb.Append("<color=#9fd0ff><b>【異名合成】 結果は抽選で決まります</b></color>\n");
            sb.Append("<b>完成カードの合成Lv(結果ごと)</b>\n");
            sb.Append($"・両側成功: Lv.{a.level} + Lv.{b.level} → <b>Lv.{ResultLevel(a, b, Kind.CrossBoth)}</b>\n");
            sb.Append($"・メインのみ成功: <b>Lv.{ResultLevel(a, b, Kind.CrossMainOnly)}</b>(メイン側のLvのみ)\n");
            sb.Append($"・素材のみ成功: <b>Lv.{ResultLevel(a, b, Kind.CrossMaterialOnly)}</b>(素材側のLvのみ)\n");
            sb.Append($"・両側失敗: 完成カードなし・<color=#ffd76a>{RefundFor(a, b)} MILE</color> を還元\n\n");
            sb.Append($"<b>メイン側 能力一式 継承率 {Mathf.RoundToInt(MainInheritChance * 100)}%</b>\n");
            foreach (var x in a.abilities) sb.Append($"・{CardVariant.AbilityName(x.id)} ×{x.stacks}  <size=18>({CardVariant.AbilityEffectText(x.id)} ×{x.stacks})</size>\n");
            sb.Append($"\n<b>素材側 能力一式 継承率 {Mathf.RoundToInt(MaterialInheritChance * 100)}%</b>\n");
            foreach (var x in b.abilities) sb.Append($"・{CardVariant.AbilityName(x.id)} ×{x.stacks}  <size=18>({CardVariant.AbilityEffectText(x.id)} ×{x.stacks})</size>\n");
            sb.Append("\n両方成功: 両側の能力をすべて継承(同じ能力は強化量を合算)\n");
            sb.Append("片側だけ成功: 成功した側の能力・強化量・Lvだけを継承(失敗した側は残りません)\n");
            sb.Append($"素材側だけ成功した場合は、素材側の主能力が完成カードの主能力になります\n");
            sb.Append($"<size=18>(両側失敗の還元 =★{a.rarity}×Lv.{a.level}×{RefundCoefficient} + ★{b.rarity}×Lv.{b.level}×{RefundCoefficient})</size>\n");
        }
        sb.Append("\n<color=#ff9a8a>実行すると、選んだ2枚は消費されます。</color>");
        return sb.ToString();
    }
}
