using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// LOCAL PLAY(LAN)の通信の暗号化(2026-10-19、Google Play の「データは転送時に暗号化されますか」に根拠を持って答えるため)。
//  ・方式: Unity Transport の DTLS(Relay と同じ暗号化。独自の暗号は作らない)。NGO の UnityTransport.UseEncryption + SetServerSecrets / SetClientSecrets。
//  ・証明書: HOST がアプリの起動ごとに新しい自己署名の証明書(RSA 2048 + SHA-256、名前 "omm-lan-host"、30日。LanCertBuilder)を作る。秘密鍵は HOST の端末の中だけ(送らない・保存しない)。
//  ・JOIN 側は、部屋の案内(LanDiscovery の一斉送信/問い合わせの返事)に入っている HOST の証明書を「信頼する証明書」として使う(証明書の固定)。
//    案内そのものは暗号化していないので、同じ LAN にいる攻撃者が偽の部屋を名乗る(なりすまし)ことは防げない。盗み見(受け身の盗聴)は防げる。
//  ・部屋の案内には個人の情報を入れない(部屋の名前は "ROOM xxxx" の汎用の名前。コードネームは暗号化した接続の中だけで送る)。
//  ・証明書を作れない端末では部屋を作らない(暗号化なしの通信には戻さない)。
public static class LanSecurity
{
    public const string ServerName = "omm-lan-host";
    // 開発版だけ -lanPlain で暗号化を止められる(暗号化の有無で、通信の中身にコードネームが見えるかを比べる試験用)。製品版は常に true
    public static bool Enabled
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devPlain < 0) devPlain = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lanPlain") >= 0 ? 1 : 0;
            return devPlain == 0;
#else
            return true;
#endif
        }
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static int devPlain = -1;
#endif

    static string certPem, keyPem, certB64;
    static Task warm;
    public static string LastError { get; private set; } = "";
    public static float LastGenerateSeconds { get; private set; }
    public static int CertsLearned { get; private set; }

    // HOST の証明書(base64 の DER。部屋の案内に入れる)
    public static string HostCertB64 { get { EnsureHost(); return certB64 ?? ""; } }
    public static bool HostReady { get { EnsureHost(); return certPem != null; } }

    // 先に作っておく(鍵の生成は端末によって 0.1〜1秒ほどかかる)
    public static void Prewarm()
    {
        if (certPem != null || warm != null) return;
        warm = Task.Run(() => Generate());
    }

    static void EnsureHost()
    {
        if (certPem != null) return;
        if (warm != null) { try { warm.Wait(5000); } catch { } }
        if (certPem == null) Generate();
    }

    static readonly object gen = new object();
    static void Generate()
    {
        lock (gen)
        {
            if (certPem != null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // .NET の CertificateRequest は Unity(Mono/IL2CPP)では使えない(PlatformNotSupportedException)ので、LanCertBuilder で組み立てる
                var r = LanCertBuilder.CreateSelfSigned(ServerName, 30);
                certB64 = Convert.ToBase64String(r.certDer);
                keyPem = Pem("RSA PRIVATE KEY", r.keyDer);
                certPem = Pem("CERTIFICATE", r.certDer);
                LastError = "";
            }
            catch (Exception e)
            {
                certPem = keyPem = certB64 = null;
                LastError = e.GetType().Name + ": " + e.Message;
                Debug.LogWarning("[LAN] secure: could not create the host certificate (" + LastError + ")");
            }
            LastGenerateSeconds = (float)sw.Elapsed.TotalSeconds;
            if (certPem != null) Debug.Log($"[LAN] secure: host certificate ready ({LastGenerateSeconds:F2}s, {certB64.Length} chars)");
        }
    }

    static string Pem(string label, byte[] der)
    {
        string b = Convert.ToBase64String(der);
        var sb = new StringBuilder();
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        for (int i = 0; i < b.Length; i += 64) sb.Append(b, i, Math.Min(64, b.Length - i)).Append('\n');
        sb.Append("-----END ").Append(label).Append("-----\n");
        return sb.ToString();
    }

    // ---- JOIN 側: 部屋の案内で受け取った HOST の証明書(IP ごと) ----
    static readonly Dictionary<string, string> known = new Dictionary<string, string>();
    public static void RememberHostCert(string ip, string b64)
    {
        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(b64)) return;
        try { Convert.FromBase64String(b64); } catch { return; } // 壊れた案内は使わない
        if (!known.TryGetValue(ip, out var old) || old != b64) { known[ip] = b64; CertsLearned++; }
    }
    public static bool HasHostCert(string ip) => !string.IsNullOrEmpty(ip) && known.ContainsKey(ip);
    public static string HostCertPemFor(string ip) => known.TryGetValue(ip ?? "", out var b) ? Pem("CERTIFICATE", Convert.FromBase64String(b)) : null;

    // HOST: UnityTransport へ(秘密鍵はここで渡すだけ。ログ/保存へは出さない)
    public static bool ApplyServer(Unity.Netcode.Transports.UTP.UnityTransport t)
    {
        if (!Enabled) { t.UseEncryption = false; return true; }
        EnsureHost();
        if (certPem == null) return false;
        t.UseEncryption = true;
        t.SetServerSecrets(certPem, keyPem);
        return true;
    }

    public static bool ApplyClient(Unity.Netcode.Transports.UTP.UnityTransport t, string hostIp)
    {
        if (!Enabled) { t.UseEncryption = false; return true; }
        string pem = HostCertPemFor(hostIp);
        if (pem == null) return false;
        t.UseEncryption = true;
        t.SetClientSecrets(ServerName, pem);
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 開発版の確認: -lanCertTest で起動直後に証明書を作り、結果をログへ(Android の IL2CPP でも作れるか。エミュレーター/実機で確かめる)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootTest()
    {
        if (Array.IndexOf(QaArgs.All, "-lanCertTest") < 0) return; // QaArgs = 起動の引数 + qa_args.txt(Android)
        EnsureHost();
        Debug.Log($"[LAN] cert test: {(certPem != null ? "OK" : "FAILED")} {LastGenerateSeconds:F2}s fp {Fingerprint(certB64)} platform {Application.platform} backend {(Application.platform == RuntimePlatform.Android ? "IL2CPP" : "-")} err '{LastError}'");
    }
#endif

    // 指紋(確認用の表示/ログ。証明書の先頭を短く)
    public static string Fingerprint(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "-";
        using (var sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Convert.FromBase64String(b64));
            return BitConverter.ToString(h, 0, 6).Replace("-", "");
        }
    }
}
