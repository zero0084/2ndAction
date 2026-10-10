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
public class UnityIapStoreService : IStoreService
{
    public string Name => "unity-iap";
    public bool Ready { get; private set; }
    public event Action<PurchaseResult> LatePurchase;
    StoreController store;
    readonly Dictionary<string, Product> products = new Dictionary<string, Product>();
    Action<PurchaseResult> purchaseCallback;
    Action<RestoreResult> restoreCallback;

    public async void Initialize(Action<bool> done)
    {
        try
        {
            store = UnityIAPServices.StoreController();
            store.OnPurchasePending += OnPending;
            store.OnPurchaseConfirmed += OnConfirmed;
            store.OnPurchaseFailed += OnFailed;
            store.OnPurchaseDeferred += OnDeferred;
            store.OnProductsFetched += ps => { foreach (var p in ps) products[p.definition.id] = p; };
            store.OnStoreConnected += () => Debug.Log("[IAP] store connected");
            store.OnStoreDisconnected += d => { Ready = false; Debug.LogWarning($"[IAP] store disconnected: {d?.Message}"); };
            store.OnProductsFetchFailed += f => Debug.LogWarning($"[IAP] products fetch failed: {f?.FailureReason} ({f?.FailedFetchProducts?.Count} products) - the product must exist in Play Console");
            store.OnPurchasesFetched += OnFetched;
            store.OnPurchasesFetchFailed += f => { restoreCallback?.Invoke(new RestoreResult { ok = false, message = f.FailureReason.ToString() }); restoreCallback = null; };
            await store.Connect();
            store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(MonetizationConfig.NoAdsProductId, ProductType.NonConsumable) });
            Ready = true;
            done(true);
        }
        catch (Exception e) { Debug.LogWarning($"[IAP] init failed: {e.Message}"); done(false); }
    }

    public string LocalizedPrice(string id) => products.TryGetValue(id, out var p) && p.metadata != null ? p.metadata.localizedPriceString : null;

    public void Purchase(string id, Action<PurchaseResult> done)
    {
        if (!Ready || !products.TryGetValue(id, out var p)) { done(new PurchaseResult { status = PurchaseStatus.NotReady, productId = id, message = "store not ready" }); return; }
        purchaseCallback = done;
        store.PurchaseProduct(p);
    }

    public void Restore(string id, Action<RestoreResult> done)
    {
        if (!Ready) { done(new RestoreResult { ok = false, message = "store not ready" }); return; }
        restoreCallback = done;
        store.FetchPurchases();
    }

    void OnFetched(Orders orders)
    {
        bool owned = false; string token = "";
        foreach (var o in orders.ConfirmedOrders)
            foreach (var item in o.CartOrdered.Items())
                if (item.Product.definition.id == MonetizationConfig.NoAdsProductId && Validate(o.Info.Receipt)) { owned = true; token = o.Info.TransactionID; }
        restoreCallback?.Invoke(new RestoreResult { ok = true, owned = owned, token = token });
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

    void OnFailed(FailedOrder f) => Finish(new PurchaseResult { status = f.FailureReason == PurchaseFailureReason.UserCancelled ? PurchaseStatus.Cancelled : PurchaseStatus.Failed, message = f.Details });
    void OnDeferred(DeferredOrder d) => Finish(new PurchaseResult { status = PurchaseStatus.Pending });

    void Finish(PurchaseResult r)
    {
        if (purchaseCallback != null) { var cb = purchaseCallback; purchaseCallback = null; cb(r); }
        else LatePurchase?.Invoke(r); // 前の起動の購入/支払い待ちの完了
    }

    // レシートの検証。GooglePlayTangle(Play Console のライセンスキーから IAP の難読化ツールで作る)が入るまでは
    // OMM_IAP_TANGLE を付けない = 検証できない = 付与しない(確定もしないので、テスト購入は Google 側で自動返金される)
    static bool Validate(string receipt)
    {
#if OMM_IAP_TANGLE
        try
        {
            var v = new CrossPlatformValidator(GooglePlayTangle.Data(), null, Application.identifier);
            v.Validate(receipt);
            return true;
        }
        catch (Exception e) { Debug.LogWarning($"[IAP] invalid receipt: {e.Message}"); return false; }
#else
        Debug.LogWarning("[IAP] receipt validation is not set up (GooglePlayTangle / OMM_IAP_TANGLE missing) - not granting");
        return false;
#endif
    }
}
#endif
