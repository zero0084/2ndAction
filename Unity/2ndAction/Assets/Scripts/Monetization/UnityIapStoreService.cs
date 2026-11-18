#if UNITY_ANDROID && OMM_UNITY_IAP
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

// 本物の購入(Unity IAP v5 / Google Play Billing、2026-10-10、依頼I)。
// パッケージ(com.unity.purchasing 5.x)を入れて、Android の Scripting Define Symbols に OMM_UNITY_IAP を足した時だけ入る(Steam/PC には入らない)。
// ・非消費型の 1 商品(MonetizationConfig.NoAdsProductId)
// ・購入の通知(OnPurchasePending)→ レシートを検証(CrossPlatformValidator: Google の公開鍵。GooglePlayTangle は IAP の難読化ツールで作る)
//   → 有効にしてから ConfirmPurchase(= Google への acknowledge。3日以内にしないと自動で返金される)
// ・支払い待ち(OnPurchaseDeferred)は有効にしない。完了したら OnPurchasePending で届く → LatePurchase
// ・持っているかの確認は FetchPurchases(Android の「復元」)。接続できない時は ok=false(持っていないとは扱わない)
// ・2026-11-14: 「持っていない」と答えるのは、商品の情報が取れていて、かつレシートを検証できる時だけ。
//   検証の準備が無い(GooglePlayTangle 未設定)/商品の情報が取れていない時に注文が見つからない・検証できない場合は ok=false(分からない)
//   = 既にある権利を外す材料にしない
public class UnityIapStoreService : IStoreService
{
    public string Name => "unity-iap";
    public bool Ready { get; private set; }
    public event Action<PurchaseResult> LatePurchase;
    StoreController store;
    readonly Dictionary<string, Product> products = new Dictionary<string, Product>();
    bool ProductKnown => products.ContainsKey(MonetizationConfig.NoAdsProductId);
    Action<PurchaseResult> purchaseCallback;
    Action<RestoreResult> restoreCallback;

    // 2026-11-18: 起動時に繋がらなかった/途中で切れた時は、次の確認(復帰・「購入を復元」)で繋ぎ直す。通知の登録は1回だけ(繋ぎ直しで二重にしない)
    bool hooked, connecting;
    public async void Initialize(Action<bool> done)
    {
        if (connecting) { done(false); return; }
        connecting = true;
        try
        {
            if (store == null) store = UnityIAPServices.StoreController();
            if (!hooked)
            {
                hooked = true;
                store.OnPurchasePending += OnPending;
                store.OnPurchaseConfirmed += OnConfirmed;
                store.OnPurchaseFailed += OnFailed;
                store.OnPurchaseDeferred += OnDeferred;
                store.OnProductsFetched += ps => { foreach (var p in ps) products[p.definition.id] = p; Debug.Log($"[IAP] products fetched: {string.Join(",", ps.Select(p => p.definition.id + "=" + (p.metadata != null ? p.metadata.localizedPriceString : "?")))}"); };
                store.OnStoreConnected += () => Debug.Log("[IAP] store connected");
                store.OnStoreDisconnected += d => { Ready = false; Debug.LogWarning($"[IAP] store disconnected: {d?.Message}"); };
                store.OnProductsFetchFailed += f => Debug.LogWarning($"[IAP] products fetch failed: {f?.FailureReason} ({f?.FailedFetchProducts?.Count} products) - the product must exist (and be active) in Play Console");
                store.OnPurchasesFetched += OnFetched;
                store.OnPurchasesFetchFailed += f => { restoreCallback?.Invoke(new RestoreResult { ok = false, message = f.FailureReason.ToString() }); restoreCallback = null; };
            }
            await store.Connect();
            store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(MonetizationConfig.NoAdsProductId, ProductType.NonConsumable) });
            Ready = true;
            connecting = false;
            done(true);
        }
        catch (Exception e) { connecting = false; Debug.LogWarning($"[IAP] init failed: {e.Message}"); done(false); }
    }

    public string LocalizedPrice(string id) => products.TryGetValue(id, out var p) && p.metadata != null && !string.IsNullOrEmpty(p.metadata.localizedPriceString) ? p.metadata.localizedPriceString : null;

    public void Purchase(string id, Action<PurchaseResult> done)
    {
        if (!Ready || !products.TryGetValue(id, out var p)) { done(new PurchaseResult { status = PurchaseStatus.NotReady, productId = id, message = "store not ready" }); return; }
        purchaseCallback = done;
        store.PurchaseProduct(p);
    }

    public void Restore(string id, Action<RestoreResult> done)
    {
        if (!Ready)
        {
            // 2026-11-18: 繋ぎ直してから確かめる(以前は起動時に繋がらないと、アプリを再起動するまで「繋がらない」のままだった)
            Initialize(ok =>
            {
                if (!ok) { done(new RestoreResult { ok = false, message = "store not ready" }); return; }
                restoreCallback = done;
                store.FetchPurchases();
            });
            return;
        }
        restoreCallback = done;
        store.FetchPurchases();
    }

    void OnFetched(Orders orders)
    {
        bool owned = false, unverifiable = false, deferred = false; string token = "";
        foreach (var o in orders.ConfirmedOrders)
            foreach (var item in o.CartOrdered.Items())
            {
                if (item.Product.definition.id != MonetizationConfig.NoAdsProductId) continue;
                if (Validate(o.Info.Receipt)) { owned = true; token = o.Info.TransactionID; }
                else if (!CanValidate) unverifiable = true;
            }
        // 支払いの完了待ち(コンビニ払い等): 持っているとは扱わない。完了すると OnPurchasePending で届く。
        //  支払った後で確定(acknowledge)前の注文(PendingOrders)は、IAP が OnPurchasePending へ回す(検証 → 確定 → 有効)
        foreach (var o in orders.DeferredOrders)
            foreach (var item in o.CartOrdered.Items())
                if (item.Product.definition.id == MonetizationConfig.NoAdsProductId) deferred = true;
        RestoreResult r;
        if (owned) r = new RestoreResult { ok = true, owned = true, token = token };
        else if (unverifiable) r = new RestoreResult { ok = false, message = "an order exists but receipt validation is not set up - unknown (not treated as 'not owned')" };
        else if (!ProductKnown) r = new RestoreResult { ok = false, message = "product not fetched from the store - unknown (not treated as 'not owned')" };
        else r = new RestoreResult { ok = true, owned = false, pending = deferred };
        if (!r.ok) Debug.LogWarning("[IAP] restore: " + r.message);
        restoreCallback?.Invoke(r);
        restoreCallback = null;
    }

    void OnPending(PendingOrder order)
    {
        // 検証できた時だけ確定(acknowledge)して有効に。検証できない時は確定もしない(推測で付与しない)
        if (!Validate(order.Info.Receipt)) { Finish(new PurchaseResult { status = PurchaseStatus.Failed, message = "receipt validation failed" }); return; }
        store.ConfirmPurchase(order);
    }

    void OnConfirmed(Order order)
    {
        if (order is ConfirmedOrder c) Finish(new PurchaseResult { status = PurchaseStatus.Purchased, productId = MonetizationConfig.NoAdsProductId, token = c.Info.TransactionID });
        else if (order is FailedOrder f) Finish(new PurchaseResult { status = f.FailureReason == PurchaseFailureReason.DuplicateTransaction ? PurchaseStatus.AlreadyOwned : PurchaseStatus.Failed, message = f.Details });
    }

    // 2026-11-18: 「もう持っている」(DuplicateTransaction)は AlreadyOwned → NoAdsPass がストアで確かめ直す(検証してから有効に。ここでは有効にしない)。
    //  「前の支払いが完了待ち」(ExistingPurchasePending)は Pending
    void OnFailed(FailedOrder f)
    {
        var st = f.FailureReason == PurchaseFailureReason.UserCancelled ? PurchaseStatus.Cancelled
               : f.FailureReason == PurchaseFailureReason.DuplicateTransaction ? PurchaseStatus.AlreadyOwned
               : f.FailureReason == PurchaseFailureReason.ExistingPurchasePending ? PurchaseStatus.Pending
               : PurchaseStatus.Failed;
        Debug.Log($"[IAP] purchase failed: {f.FailureReason} -> {st} ({f.Details})");
        Finish(new PurchaseResult { status = st, message = f.Details });
    }
    void OnDeferred(DeferredOrder d) => Finish(new PurchaseResult { status = PurchaseStatus.Pending });

    void Finish(PurchaseResult r)
    {
        if (purchaseCallback != null) { var cb = purchaseCallback; purchaseCallback = null; cb(r); }
        else LatePurchase?.Invoke(r); // 前の起動の購入/支払い待ちの完了
    }

    // レシートの検証。GooglePlayTangle(Play Console のライセンスキーから IAP の難読化ツールで作る)が入るまでは
    // OMM_IAP_TANGLE を付けない = 検証できない = 付与しない(確定もしないので、テスト購入は Google 側で自動返金される)
#if OMM_IAP_TANGLE
    const bool CanValidate = true;
#else
    const bool CanValidate = false;
#endif
    static bool Validate(string receipt)
    {
#if OMM_IAP_TANGLE
        bool ok = ValidateWith(GooglePlayTangle.Data(), receipt, out string why);
        if (!ok) Debug.LogWarning("[IAP] receipt not accepted: " + why);
        return ok;
#else
        Debug.LogWarning("[IAP] receipt validation is not set up (GooglePlayTangle / OMM_IAP_TANGLE missing) - not granting");
        return false;
#endif
    }

    // 2026-11-18: 検証の本体(公開鍵を外から渡す = 開発版の自己試験 IapValidateSelfTest が、その場で作った試験用の鍵で同じ処理を通せる)。
    //  Google の署名(SHA1withRSA)・パッケージ名(Application.identifier)を Unity IAP が確かめ、さらに
    //  「商品 ID が広告なしパス」「購入の状態が Purchased(取り消し/完了待ちではない)」を確かめる
    public static bool ValidateWith(byte[] googlePublicKey, string receipt, out string why)
    {
        try
        {
            var v = new CrossPlatformValidator(googlePublicKey, Application.identifier);
            var receipts = v.Validate(receipt);
            foreach (var pr in receipts)
            {
                if (!(pr is GooglePlayReceipt g)) continue;
                if (g.productID != MonetizationConfig.NoAdsProductId) { why = "other product " + g.productID; continue; }
                if (g.purchaseState != GooglePurchaseState.Purchased) { why = "purchase state " + g.purchaseState; return false; }
                why = "";
                return true;
            }
            why = "no receipt for " + MonetizationConfig.NoAdsProductId;
            return false;
        }
        catch (Exception e) { why = e.GetType().Name + ": " + e.Message; return false; }
    }
}
#endif
