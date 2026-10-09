using System;
using UnityEngine;

// 広告なしパス(買い切り・非消費型、2026-10-10、依頼I)。
//  特典: ゲームオーバー後の広告なし / 成功ランの MILE 2倍を広告なしで(1ラン1回) / 広告ガチャ相当を広告なしで1日3回(広告の回数と共通の枠)
//  ・購入権利はストアのアカウントのもの。端末には「持っている」と最後に確認した時刻だけ控える(設定の扱い = テスト用データと通常のデータで共通、
//    セーブの初期化でも消えない)。育成セーブの復元とは別(再インストール/機種変更はストアから復元)
//  ・持っているかの確認: 起動時/復帰時/「購入を復元」。ストアに繋がらない時(オフライン)は控えのまま(すぐ広告ありへ戻さない)。
//    ストアに繋がって「持っていない」と2回続けて返った時だけ外す(返金/取消)
//  ・模擬の購入(開発版)は別のキーに保存し、本物の購入権利には混ざらない。製品版では読まない
public static class NoAdsPass
{
    public const string Key = "NoAdsEntitlementV1";
    public const string DevMockKey = "Dev.MockNoAdsEntitlementV1";

    [Serializable] class State { public bool owned; public string tokenHash = ""; public long grantedUtc; public long lastConfirmedUtc; public int absentChecks; public bool pending; }
    static State real, mock;

    static string StoreKey => Monetization.Mode == MonetizationMode.Mock ? DevMockKey : Key;
    static State S
    {
        get
        {
            bool isMock = Monetization.Mode == MonetizationMode.Mock;
            ref State s = ref (isMock ? ref mock : ref real);
            if (s != null) return s;
            string raw = SaveStore.GetString(StoreKey, "");
            try { s = string.IsNullOrEmpty(raw) ? new State() : JsonUtility.FromJson<State>(raw) ?? new State(); } catch { s = new State(); }
            return s;
        }
    }
    static void Write() { SaveStore.SetString(StoreKey, JsonUtility.ToJson(S)); SaveStore.Save(); }

    // 持っている(Steam/None ではいつも false = 何も変わらない)
    public static bool Owned => Monetization.Mode != MonetizationMode.None && S.owned;
    public static bool PendingPayment => Monetization.Mode != MonetizationMode.None && S.pending;
    public static DateTime LastConfirmed => S.lastConfirmedUtc > 0 ? new DateTime(S.lastConfirmedUtc, DateTimeKind.Utc) : DateTime.MinValue;
    public static string LastNote { get; private set; } = "";
    public static bool Busy { get; private set; }

    // 起動時: ストアの初期化 → 権利の確認
    public static void StartStore()
    {
        var st = Monetization.Store;
        if (st == null) return;
        st.LatePurchase += r => AdManager.Post(() =>
        {
            if (r.status == PurchaseStatus.Purchased || r.status == PurchaseStatus.AlreadyOwned) { Grant(r.token, "late " + r.status); LastNote = "広告なしパスが有効になりました"; }
            else if (r.status == PurchaseStatus.Pending) { S.pending = true; Write(); }
        });
        st.Initialize(ok => AdManager.Post(() => { if (ok) Check("start"); else LastNote = "ストアに接続できませんでした。最後に確認した状態のままです"; }));
    }

    // 権利の確認(起動/復帰/「購入を復元」)
    public static void Check(string why, Action<RestoreResult> done = null)
    {
        var st = Monetization.Store;
        if (st == null || Busy) { done?.Invoke(new RestoreResult { ok = false, message = "ストアを利用できません" }); return; }
        Busy = true;
        st.Restore(MonetizationConfig.NoAdsProductId, r => AdManager.Post(() =>
        {
            Busy = false;
            if (!r.ok) { LastNote = "ストアに接続できませんでした。最後に確認した状態のままです"; Debug.Log($"[NoAds] check ({why}) offline: keep owned={S.owned}"); done?.Invoke(r); return; }
            if (r.owned) Grant(r.token, "restore " + why);
            else if (S.owned)
            {
                S.absentChecks++;
                if (S.absentChecks >= 2) { S.owned = false; S.absentChecks = 0; LastNote = "購入が取り消されたため、広告なしパスは無効になりました"; Debug.Log("[NoAds] revoked (store reports not owned twice)"); }
                else LastNote = "購入を確認できませんでした。次の起動でもう一度確認します";
                Write();
            }
            else { LastNote = "購入の記録はありませんでした"; }
            done?.Invoke(r);
        }));
    }

    // 購入(ストアの画面へ)。成功(検証済み)の時だけ有効に。保留/キャンセル/失敗では有効にしない
    public static void Purchase(Action<PurchaseResult> done)
    {
        var st = Monetization.Store;
        if (st == null || Busy) { done?.Invoke(new PurchaseResult { status = PurchaseStatus.NotReady, message = "ストアを利用できません" }); return; }
        Busy = true;
        st.Purchase(MonetizationConfig.NoAdsProductId, r => AdManager.Post(() =>
        {
            Busy = false;
            switch (r.status)
            {
                case PurchaseStatus.Purchased:
                case PurchaseStatus.AlreadyOwned: Grant(r.token, r.status.ToString()); S.pending = false; Write(); LastNote = "広告なしパスが有効になりました"; break;
                case PurchaseStatus.Pending: S.pending = true; Write(); LastNote = "お支払いの完了待ちです。完了すると自動で有効になります"; break;
                case PurchaseStatus.Cancelled: LastNote = "購入をキャンセルしました"; break;
                default: LastNote = "購入できませんでした。時間をおいてお試しください"; break;
            }
            Debug.Log($"[NoAds] purchase {r.status} {r.message}");
            done?.Invoke(r);
        }));
    }

    static void Grant(string token, string via)
    {
        bool was = S.owned;
        S.owned = true; S.pending = false; S.absentChecks = 0;
        S.lastConfirmedUtc = DateTime.UtcNow.Ticks;
        if (!was) S.grantedUtc = DateTime.UtcNow.Ticks;
        if (!string.IsNullOrEmpty(token)) S.tokenHash = Hash(token);
        Write();
        if (!was) Debug.Log($"[NoAds] granted via {via}");
    }

    static string Hash(string s)
    {
        unchecked { ulong h = 1469598103934665603UL; foreach (char c in s) { h ^= c; h *= 1099511628211UL; } return h.ToString("x16"); }
    }

    public static string PriceText => Monetization.Store != null ? Monetization.Store.LocalizedPrice(MonetizationConfig.NoAdsProductId) : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版(模擬だけ): 状態を直接変える(本物の購入権利には触れない)
    public static void DevMockSet(bool owned) { if (Monetization.Mode != MonetizationMode.Mock) return; S.owned = owned; S.pending = false; S.absentChecks = 0; Write(); }
#endif
}
