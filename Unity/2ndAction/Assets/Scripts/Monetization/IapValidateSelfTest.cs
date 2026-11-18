#if UNITY_ANDROID && OMM_UNITY_IAP && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// 広告なしパスのレシートの検証の自己試験(2026-11-18、開発版の Android だけ。起動の引数 -qaIapValidate)。
//  本物の Google の鍵・購入は使わない。その場で試験用の RSA 鍵を作り、Google Play と同じ形のレシート(SHA1withRSA の署名)を作って、
//  製品版と同じ検証の本体(UnityIapStoreService.ValidateWith)へ通す。IL2CPP の削り(Managed Stripping)で検証の部品が消えていないかも分かる。
//  結果は logcat の "[IapSelfTest]" の行(最後に "[IapSelfTest] RESULT pass=N fail=M")
public class IapValidateSelfTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(QaArgs.All, "-qaIapValidate") < 0) return;
        Run();
    }

    static int pass, fail;
    static void Check(bool ok, string what) { if (ok) pass++; else fail++; Debug.Log($"[IapSelfTest] {(ok ? "PASS" : "FAIL")} {what}"); }

    public static void Run()
    {
        pass = fail = 0;
        try
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            using (var other = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false; other.PersistKeyInCsp = false;
                byte[] pub = LanCertBuilder.RsaPublicKeyDer(rsa.ExportParameters(false));
                string pkg = Application.identifier, id = MonetizationConfig.NoAdsProductId;

                string good = Receipt(rsa, Json(pkg, id, 0));
                Check(UnityIapStoreService.ValidateWith(pub, good, out string w1), $"valid purchase of {id} is accepted ({w1})");

                // 署名の後に中身を書き換えた(購入の状態を偽った等)
                string json = Json(pkg, id, 0);
                string sig = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(json), new SHA1CryptoServiceProvider()));
                string tampered = Wrap(json.Replace("\"orderId\":\"GPA.TEST-0001\"", "\"orderId\":\"GPA.TEST-9999\""), sig);
                Check(!UnityIapStoreService.ValidateWith(pub, tampered, out string w2), $"tampered receipt is rejected ({w2})");

                Check(!UnityIapStoreService.ValidateWith(pub, Receipt(other, Json(pkg, id, 0)), out string w3), $"receipt signed by another key is rejected ({w3})");
                Check(!UnityIapStoreService.ValidateWith(pub, Receipt(rsa, Json("com.example.other", id, 0)), out string w4), $"receipt for another app is rejected ({w4})");
                Check(!UnityIapStoreService.ValidateWith(pub, Receipt(rsa, Json(pkg, "other_product", 0)), out string w5), $"receipt for another product is rejected ({w5})");
                Check(!UnityIapStoreService.ValidateWith(pub, Receipt(rsa, Json(pkg, id, 1)), out string w6), $"cancelled purchase (state 1) is rejected ({w6})");
                Check(!UnityIapStoreService.ValidateWith(pub, Receipt(rsa, Json(pkg, id, 2)), out string w7), $"pending purchase (state 2) is rejected ({w7})");
                Check(!UnityIapStoreService.ValidateWith(pub, "{\"Store\":\"GooglePlay\",\"Payload\":\"broken\"}", out string w8), $"broken receipt is rejected ({w8})");
            }
        }
        catch (Exception e) { fail++; Debug.Log("[IapSelfTest] FAIL exception " + e); }
        Debug.Log($"[IapSelfTest] RESULT pass={pass} fail={fail}");
    }

    static string Json(string pkg, string product, int state) =>
        "{\"orderId\":\"GPA.TEST-0001\",\"packageName\":\"" + pkg + "\",\"productId\":\"" + product + "\",\"purchaseTime\":1763400000000,\"purchaseState\":" + state +
        ",\"purchaseToken\":\"selftest-token\",\"quantity\":1,\"acknowledged\":false}";

    static string Receipt(RSACryptoServiceProvider key, string json)
    {
        string sig = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(json), new SHA1CryptoServiceProvider()));
        return Wrap(json, sig);
    }

    // Unity IAP の共通のレシート: {"Store":"GooglePlay","TransactionID":..,"Payload":"{\"json\":..,\"signature\":..}"}
    static string Wrap(string json, string sig)
    {
        string payload = "{\"json\":\"" + Esc(json) + "\",\"signature\":\"" + Esc(sig) + "\"}";
        return "{\"Store\":\"GooglePlay\",\"TransactionID\":\"selftest-token\",\"Payload\":\"" + Esc(payload) + "\"}";
    }
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
#endif
