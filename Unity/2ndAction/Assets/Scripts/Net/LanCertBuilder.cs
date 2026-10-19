using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

// LOCAL PLAY の暗号化(LanSecurity)用: 自己署名の X.509 証明書(v3、RSA 2048 + SHA-256)を作る(2026-10-19)。
// Unity の Mono/IL2CPP の .NET では CertificateRequest.CreateSelfSigned が PlatformNotSupportedException になるため、
// 証明書の決まった形(DER: ITU-T X.690 / RFC 5280)をここで組み立て、鍵の生成と署名は .NET の標準の RSA(RSACryptoServiceProvider)で行う。
// 暗号の処理そのもの(DTLS)は Unity Transport が行う。ここは「証明書という入れ物」を作るだけ(独自の暗号ではない)。
public static class LanCertBuilder
{
    public struct Result { public byte[] certDer; public byte[] keyDer; } // keyDer = PKCS#1 RSAPrivateKey

    public static Result CreateSelfSigned(string commonName, int days)
    {
        using (var rsa = new RSACryptoServiceProvider(2048))
        {
            rsa.PersistKeyInCsp = false;
            RSAParameters k = rsa.ExportParameters(true);
            var now = DateTime.UtcNow;
            byte[] serial = new byte[9];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(serial);
            serial[0] &= 0x7F; if (serial[0] == 0) serial[0] = 0x01; // 正の数

            byte[] sigAlg = Seq(Oid("1.2.840.113549.1.1.11"), Null());           // sha256WithRSAEncryption
            byte[] name = Seq(Set(Seq(Oid("2.5.4.3"), Utf8(commonName))));          // CN=
            byte[] validity = Seq(UtcTime(now.AddDays(-1)), UtcTime(now.AddDays(days)));
            byte[] rsaPub = Seq(Int(k.Modulus), Int(k.Exponent));
            byte[] spki = Seq(Seq(Oid("1.2.840.113549.1.1.1"), Null()), BitString(rsaPub)); // rsaEncryption
            // 拡張: basicConstraints(CA:TRUE, critical)/ keyUsage(署名・鍵の暗号化・証明書の署名, critical)/ subjectAltName(DNS:commonName)
            byte[] extBasic = Seq(Oid("2.5.29.19"), Bool(true), Octet(Seq(Bool(true))));
            byte[] extKeyUsage = Seq(Oid("2.5.29.15"), Bool(true), Octet(BitStringBits(0xA4, 2)));   // digitalSignature(0) keyEncipherment(2) keyCertSign(5)
            byte[] extSan = Seq(Oid("2.5.29.17"), Octet(Seq(Tlv(0x82, Encoding.ASCII.GetBytes(commonName)))));
            byte[] exts = Tlv(0xA3, Seq(extBasic, extKeyUsage, extSan));
            byte[] tbs = Seq(Tlv(0xA0, Int(new byte[] { 2 })), Int(serial), sigAlg, name, validity, name, spki, exts);

            byte[] sig;
            using (var sha = SHA256.Create()) sig = rsa.SignHash(sha.ComputeHash(tbs), CryptoConfig.MapNameToOID("SHA256"));
            byte[] cert = Seq(tbs, sigAlg, BitString(sig));

            byte[] key = Seq(Int(new byte[] { 0 }), Int(k.Modulus), Int(k.Exponent), Int(k.D), Int(k.P), Int(k.Q), Int(k.DP), Int(k.DQ), Int(k.InverseQ));
            return new Result { certDer = cert, keyDer = key };
        }
    }

    // ---- DER(X.690)の最小限の組み立て ----
    static byte[] Tlv(byte tag, byte[] v)
    {
        var o = new List<byte>(v.Length + 6) { tag };
        int n = v.Length;
        if (n < 0x80) o.Add((byte)n);
        else if (n <= 0xFF) { o.Add(0x81); o.Add((byte)n); }
        else if (n <= 0xFFFF) { o.Add(0x82); o.Add((byte)(n >> 8)); o.Add((byte)n); }
        else { o.Add(0x83); o.Add((byte)(n >> 16)); o.Add((byte)(n >> 8)); o.Add((byte)n); }
        o.AddRange(v);
        return o.ToArray();
    }
    static byte[] Cat(byte[][] parts) { var o = new List<byte>(); foreach (var p in parts) o.AddRange(p); return o.ToArray(); }
    static byte[] Seq(params byte[][] parts) => Tlv(0x30, Cat(parts));
    static byte[] Set(params byte[][] parts) => Tlv(0x31, Cat(parts));
    static byte[] Null() => new byte[] { 0x05, 0x00 };
    static byte[] Bool(bool b) => new byte[] { 0x01, 0x01, (byte)(b ? 0xFF : 0x00) };
    static byte[] Octet(byte[] v) => Tlv(0x04, v);
    static byte[] Utf8(string s) => Tlv(0x0C, Encoding.UTF8.GetBytes(s));
    static byte[] BitString(byte[] v) { var b = new byte[v.Length + 1]; Buffer.BlockCopy(v, 0, b, 1, v.Length); return Tlv(0x03, b); } // 余りのビット 0
    static byte[] BitStringBits(byte bits, int unused) => Tlv(0x03, new[] { (byte)unused, bits });
    static byte[] UtcTime(DateTime t) => Tlv(0x17, Encoding.ASCII.GetBytes(t.ToUniversalTime().ToString("yyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "Z"));

    // 符号なしの大きな整数(先頭の 0 を詰め、最上位のビットが立っていれば 0x00 を付ける)
    static byte[] Int(byte[] mag)
    {
        int i = 0;
        while (i < mag.Length - 1 && mag[i] == 0) i++;
        int len = mag.Length - i;
        bool pad = (mag[i] & 0x80) != 0;
        var v = new byte[len + (pad ? 1 : 0)];
        Buffer.BlockCopy(mag, i, v, pad ? 1 : 0, len);
        return Tlv(0x02, v);
    }

    static byte[] Oid(string dotted)
    {
        var parts = dotted.Split('.');
        var o = new List<byte> { (byte)(int.Parse(parts[0]) * 40 + int.Parse(parts[1])) };
        for (int i = 2; i < parts.Length; i++)
        {
            long v = long.Parse(parts[i]);
            var stack = new Stack<byte>();
            stack.Push((byte)(v & 0x7F));
            v >>= 7;
            while (v > 0) { stack.Push((byte)(0x80 | (v & 0x7F))); v >>= 7; }
            o.AddRange(stack);
        }
        return Tlv(0x06, o.ToArray());
    }
}
