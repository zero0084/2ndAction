#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// DEBUG →「広告/課金(模擬)…」(2026-10-10、依頼I)。模擬の広告と模擬の購入の振る舞いを選ぶ。
// 本物の購入を呼ぶボタンは置かない(模擬の権利を直接切り替えるだけ。本物の権利のキーには触れない)。
public partial class DebugPanel
{
    void DrawMonetizationPage(Rect p)
    {
        var gm = GameManager.Instance;
        float x = p.x + 24f, y = p.y + 62f, full = p.width - 48f, bh = 38f, bw = (full - 16f) / 3f;
        var lab = new GUIStyle(UiKit.Label(12f, TextAnchor.UpperLeft, false, new Color(0.9f, 0.92f, 1f))) { wordWrap = true };
        bool mockSaved = SaveStore.GetInt(Monetization.DevMockKey, 0) == 1;
        GUI.Label(new Rect(x, y, full, 20f), $"今: {Monetization.Mode}  広告={(Monetization.Ads != null ? Monetization.Ads.Name : "なし")}  ストア={(Monetization.Store != null ? Monetization.Store.Name : "なし")}  ({Platform.Name})", UiKit.Label(14f, TextAnchor.MiddleLeft, true, new Color(1f, 0.85f, 0.5f)));
        y += 24f;
        if (UiKit.Button(new Rect(x, y, full, bh), $"模擬の広告/購入: {(mockSaved ? "ON" : "OFF")}(次の起動から。起動引数 -monetizationMock 1 でも可)", 14f, mockSaved, false)) Monetization.DevSetMock(!mockSaved);
        y += bh + 8f;
        if (Monetization.Mode != MonetizationMode.Mock)
        {
            GUI.Label(new Rect(x, y, full, 60f), "模擬が OFF の間はここで操作できません(本物の SDK の時は、本物の購入をこの画面から呼ばない)", lab);
            return;
        }
        // 広告の振る舞い
        var ads = (MockAdService.Behaviour[])System.Enum.GetValues(typeof(MockAdService.Behaviour));
        string[] adNames = { "通常", "在庫なし", "読込失敗", "表示失敗", "途中で閉じる", "報酬が二重", "報酬が遅れる" };
        GUI.Label(new Rect(x, y, full, 18f), "広告の振る舞い", lab); y += 18f;
        for (int i = 0; i < ads.Length; i++)
        {
            Rect r = new Rect(x + (i % 4) * ((full - 24f) / 4f + 8f), y + (i / 4) * (bh + 6f), (full - 24f) / 4f, bh);
            if (UiKit.Button(r, adNames[i], 14f, MockAdService.Mode == ads[i], false)) MockAdService.Mode = ads[i];
        }
        y += 2f * (bh + 6f) + 4f;
        // 購入の振る舞い
        var st = (MockStoreService.Behaviour[])System.Enum.GetValues(typeof(MockStoreService.Behaviour));
        string[] stNames = { "成功", "支払い待ち→完了", "キャンセル", "失敗", "オフライン" };
        GUI.Label(new Rect(x, y, full, 18f), "購入の振る舞い(購入は 設定 → 広告なしパス から)", lab); y += 18f;
        float sw = (full - 32f) / 5f;
        for (int i = 0; i < st.Length; i++)
            if (UiKit.Button(new Rect(x + i * (sw + 8f), y, sw, bh), stNames[i], 13f, MockStoreService.Mode == st[i], false)) MockStoreService.Mode = st[i];
        y += bh + 8f;
        if (UiKit.Button(new Rect(x, y, bw, bh), $"返金の模擬: {(MockStoreService.Refunded ? "ON" : "OFF")}", 14f, MockStoreService.Refunded, false)) MockStoreService.Refunded = !MockStoreService.Refunded;
        if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), "模擬ストアの購入を消す", 14f, false, false)) MockStoreService.DevClearAccount();
        if (UiKit.Button(new Rect(x + 2f * (bw + 8f), y, bw, bh), $"端末の控え: {(NoAdsPass.Owned ? "有効" : "なし")}", 14f, NoAdsPass.Owned, false)) NoAdsPass.DevMockSet(!NoAdsPass.Owned);
        y += bh + 8f;
        // 時間/回数
        if (UiKit.Button(new Rect(x, y, bw, bh), $"間隔/起動5分を無視: {(AdManager.DevIgnoreTiming ? "ON" : "OFF")}", 13f, AdManager.DevIgnoreTiming, false)) AdManager.DevIgnoreTiming = !AdManager.DevIgnoreTiming;
        if (UiKit.Button(new Rect(x + bw + 8f, y, bw, bh), $"日付を進める(+{TrustedClock.DevShiftHours:F0}h)", 14f, TrustedClock.DevShiftHours != 0, false)) TrustedClock.DevShiftHours += 24;
        if (UiKit.Button(new Rect(x + 2f * (bw + 8f), y, bw, bh), "広告ガチャの回数を戻す", 14f, false, false)) { AdRewards.DevResetGacha(); TrustedClock.DevShiftHours = 0; }
        y += bh + 10f;
        string info = $"広告: {AdManager.LastNote}  開始 {AdManager.ShowsStarted} / 報酬 {AdManager.RewardsDelivered} / 二重の報酬を無視 {AdManager.DuplicateRewardsIgnored}  前回の広告から {(AdManager.SecondsSinceLastAd > 1e6f ? "-" : AdManager.SecondsSinceLastAd.ToString("F0") + "秒")}  起動から {Time.realtimeSinceStartup:F0}秒\n"
            + $"パス: {(NoAdsPass.Owned ? "有効" : "なし")}{(NoAdsPass.PendingPayment ? " 支払い待ち" : "")}  {NoAdsPass.LastNote}\n"
            + $"記録: {AdRewards.DevSummary}\n"
            + $"時刻: {TrustedClock.LastSyncNote}  日付 {TrustedClock.DayKey}  次の切り替えまで {TrustedClock.UntilNextDay:hh\\:mm}\n"
            + (gm != null ? $"直近の強制広告: 出した {gm.GameOverAdsShown}回 / 出さなかった理由 {gm.LastGameOverAdSkip}" : "");
        GUI.Label(new Rect(x, y, full, p.yMax - y - 8f), info, lab);
    }
}
#endif
