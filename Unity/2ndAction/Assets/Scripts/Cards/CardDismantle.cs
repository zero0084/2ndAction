using UnityEngine;

// カードの分解(2026-10-31)。所持カード1枚(物理的な1枚 = (cardId, Lv) の束から1枚)を MILE に戻す。デッキ編集の長押しの詳細から。
//  ・払い戻し = ★の数 × Lv × 50(合成の「両方失敗」の払い戻し CardFusionLogic.RefundCoefficient と同じ式)
//      ★1 50 / ★2 100 / ★3 150 / ★4 200 / ★5 250(Lv1 の時。Lv は「その1枚に注ぎ込んだ Lv1 の枚数」以下なので Lv 倍でも元は取れない)
//      ガチャ1回 500 MILE の期待値: 段階3以上 ≒ 88(約18%)、段階1 ≒ 69(約14%)。1枚あたりの上限 250 < 500 → 分解→ガチャ→分解 で増えない
//  ・装備中(デッキ/使っていないプリセット/どれかのキャラのキャラカード枠)の分は分解できない。空きの枚数(GetAvailableCountForStack)からだけ。
//    勝手に外さない(外すのはプレイヤー)
//  ・最後の1枚も分解できる(画面で強い確認)。ゲームの進行に必要なカードは無い(解放は距離、初期デッキ/ULTIMATE/チュートリアルは所持に依らない)
//  ・Lv9 の束を消す前に MAX 到達の記録を確定する(MAX x/112 の数が減らない)。Mastery の進み/AWAKENED は所持と別の記録なので変わらない
//  ・保存は1回(SaveStore のまとめ書き: 所持カード + MILE + Mastery の記録を同時に)
//  ・枚数 n を受け付ける(将来のまとめて分解用。画面は今は1枚ずつ)
public static class CardDismantle
{
    public static int CoefficientPerStarLevel => CardFusionLogic.RefundCoefficient;

    public enum Block { None, NotOwned, Equipped }

    public static int RarityOf(string key)
    {
        CardDefinition def = CardDatabase.FindById(key);
        return def != null ? Mathf.Clamp(def.rarity, 1, 5) : 1;
    }

    public static int RefundFor(string key, int level, int n = 1) => RarityOf(key) * Mathf.Clamp(level, 1, CardVariant.MaxLevel) * CoefficientPerStarLevel * Mathf.Max(1, n);

    // 分解できる枚数(装備・プリセットで使っている分を除く)
    public static int FreeCount(string key, int level)
    {
        var gm = GameManager.Instance;
        return gm != null ? gm.GetAvailableCountForStack(key, level) : CardInventory.GetCount(key, level);
    }

    public static Block Check(string key, int level, int n = 1)
    {
        if (string.IsNullOrEmpty(key) || CardInventory.GetCount(key, level) < Mathf.Max(1, n)) return Block.NotOwned;
        if (FreeCount(key, level) < Mathf.Max(1, n)) return Block.Equipped;
        return Block.None;
    }

    // このカード(Lv/合成の違いも含めた同じ名前)の最後の1枚か
    public static bool IsLastOfName(string key, int n = 1)
    {
        string mainId = CardMastery.BaseIdOf(key);
        return CardInventory.GetTotalCountByName(mainId) <= Mathf.Max(1, n);
    }

    public static int Executed { get; private set; } // 確認用
    public static string LastLog { get; private set; } = "";

    // 分解する。失敗(持っていない/装備中)は false で何も変えない
    public static bool Execute(string key, int level, int n, out int mileGained)
    {
        mileGained = 0;
        n = Mathf.Max(1, n);
        var gm = GameManager.Instance;
        if (gm == null) return false;
        Block b = Check(key, level, n); // 確認の窓の間に状態が変わっていても、ここでもう一度確かめる
        if (b != Block.None) { Debug.Log($"[Dismantle] refused {key} Lv{level} x{n}: {b}"); return false; }
        int before = CardInventory.GetCount(key, level);
        int mile = RefundFor(key, level, n);
        string baseId = CardMastery.BaseIdOf(key);
        SaveStore.BeginBatch();
        try
        {
            if (level >= CardVariant.MaxLevel) { CardMastery.MarkMaxReachedInMemory(baseId); CardMastery.Save(); }
            if (!CardInventory.RemoveCard(key, level, n)) { Debug.Log($"[Dismantle] remove failed {key} Lv{level} x{n}"); return false; }
            if (CardInventory.GetCount(key, level) <= 0 && CardInventory.GetTotalCount(key) <= 0) CardInventory.ClearNewUnconfirmed(key);
            gm.AddMile(mile);
        }
        finally { SaveStore.EndBatch(); }
        mileGained = mile;
        Executed++;
        LastLog = $"[Dismantle] cardId={key} Lv{level} ★{RarityOf(key)} x{n}: count {before}->{CardInventory.GetCount(key, level)}, MILE +{mile} (total {gm.TotalOwnedMile}), remaining of this card {CardInventory.GetTotalCountByName(baseId)}";
        Debug.Log(LastLog);
        return true;
    }
}
