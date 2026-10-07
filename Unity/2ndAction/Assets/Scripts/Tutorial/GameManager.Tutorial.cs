using UnityEngine;

// 初回チュートリアル(2026-10-07)から使う GameManager の入口
public partial class GameManager
{
    // ホームの上に出る確認/結果(ガチャの結果・NEW RUN の確認・停止メニュー)
    public bool HomeModalOpen => gachaResultOpen || showNewRunConfirm || showPauseMenu;

    public void SaveDeckNow() => SaveDeck();

    // 練習のカード: このランの中だけ効く(所持/デッキには書かない。通常のレベルアップの取得と同じ能力ごとの上限)
    public void TutorialApplyCard(string cardId)
    {
        var card = CardDatabase.FindById(cardId);
        if (card == null) return;
        ApplyRunCardCapped(card, 1, "Tutorial pick");
        upgradeHistory.Add(card);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void AddRunMileForQa(int n) { RunEnemyMile += n; } // 自動テスト用: このランの MILE を足す
#endif
}
