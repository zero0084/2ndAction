using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - プレイヤー1人分の「その瞬間の見た目」。
// 操作している端末(Owner)が一定間隔でこれを送り、受け取った側はRemotePlayerAvatarで再現する。
// 位置は「論理X」(FloatingOriginで戻した量を足した値)をdoubleで送る - 端末ごとに
// FloatingOriginがシーンを戻すタイミングは異なるので、Transformの値そのままでは比較できない。
// VFX/SEは送らない(Stateとコマ番号から各端末で絵を選ぶだけ)。
public struct NetPlayerSnapshot : INetworkSerializable
{
    public const byte FlagGrounded = 1 << 0;
    public const byte FlagVisible = 1 << 1;
    public const byte FlagTeleported = 1 << 2; // 落下復帰などの瞬間移動 - 受信側は補間せずに飛ばす
    public const byte FlagRunning = 1 << 3;    // 走行中(ダウン/ゲームオーバー/帰還演出中でない) - 自動スローの判定対象

    public double Time;        // 送信側の実時間(秒)。受信側は差分だけを使うので端末間の時計合わせは不要
    public double X;           // 論理X
    public float Y;
    public float VX, VY;       // 送信間隔あたりの実測速度(外挿用)
    public float RootRotZ;     // 坂での傾き(PlayerController.transform.rotation)
    public float RootScaleX;   // 後方攻撃中の左右反転(PlayerController.transform.localScale.x)
    public float VisPosX, VisPosY, VisRotZ, VisScaleX, VisScaleY; // Visual子Transform(被弾のけぞり等の姿勢)
    public uint ColorRGBA;     // 被弾フラッシュ等
    public byte State;         // PlayerAnimator.State
    public byte Frame;
    public byte AttackStage;
    public byte FinishTier;
    public byte Flags;
    // 自動スロー(2026-09-27): スロー適用前の継続的な走行速度(m/s)。HOSTが全員の最高速度で倍率を決める。
    public float RunSpeed;
    // マルチプレイPhase 2.5/3: 走行開始位置からの距離(m)。ダウン/復活/最終距離の判定に使う。
    public double Distance;

    // 2026-10-12(同期の版 4): 詰めた形で送る(77B → 41B、色が白でない時だけ +4B)。受け取った側の値の型は今までどおり。
    //  時刻 = ミリ秒(uint) / X・距離 = mm(int、±2147km) / Y = cm(short、±327m) / 速度・姿勢の小数 = 半精度 / 走る速さ = float のまま
    //  色は白(0xFFFFFFFF)の時は送らない(Flags の最上位ビットで「色あり」を知らせる。受け取った側の Flags には残さない)
    const byte WireHasColor = 0x80;
    public const int WireBytes = 41, WireColorBytes = 4; // 計測用
    public int WireSize => WireBytes + (ColorRGBA != 0xFFFFFFFFu ? WireColorBytes : 0);

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        uint ms = 0; int xmm = 0, dmm = 0; short ycm = 0;
        ushort vx = 0, vy = 0, rr = 0, rs = 0, vpx = 0, vpy = 0, vr = 0, vsx = 0, vsy = 0;
        byte flags = 0;
        if (s.IsWriter)
        {
            ms = (uint)System.Math.Max(0.0, System.Math.Round(Time * 1000.0));
            xmm = (int)System.Math.Round(System.Math.Max(-2.1e6, System.Math.Min(2.1e6, X)) * 1000.0);
            dmm = (int)System.Math.Round(System.Math.Max(-2.1e6, System.Math.Min(2.1e6, Distance)) * 1000.0);
            ycm = (short)Mathf.Clamp(Mathf.RoundToInt(Y * 100f), short.MinValue, short.MaxValue);
            vx = Mathf.FloatToHalf(VX); vy = Mathf.FloatToHalf(VY);
            rr = Mathf.FloatToHalf(RootRotZ); rs = Mathf.FloatToHalf(RootScaleX);
            vpx = Mathf.FloatToHalf(VisPosX); vpy = Mathf.FloatToHalf(VisPosY); vr = Mathf.FloatToHalf(VisRotZ);
            vsx = Mathf.FloatToHalf(VisScaleX); vsy = Mathf.FloatToHalf(VisScaleY);
            flags = (byte)(Flags & 0x7F);
            if (ColorRGBA != 0xFFFFFFFFu) flags |= WireHasColor;
        }
        s.SerializeValue(ref ms);
        s.SerializeValue(ref xmm);
        s.SerializeValue(ref ycm);
        s.SerializeValue(ref vx); s.SerializeValue(ref vy);
        s.SerializeValue(ref rr); s.SerializeValue(ref rs);
        s.SerializeValue(ref vpx); s.SerializeValue(ref vpy); s.SerializeValue(ref vr); s.SerializeValue(ref vsx); s.SerializeValue(ref vsy);
        s.SerializeValue(ref State);
        s.SerializeValue(ref Frame);
        s.SerializeValue(ref AttackStage);
        s.SerializeValue(ref FinishTier);
        s.SerializeValue(ref flags);
        s.SerializeValue(ref RunSpeed);
        s.SerializeValue(ref dmm);
        if ((flags & WireHasColor) != 0) s.SerializeValue(ref ColorRGBA);
        if (s.IsReader)
        {
            Time = ms / 1000.0;
            X = xmm / 1000.0;
            Distance = dmm / 1000.0;
            Y = ycm / 100f;
            VX = Mathf.HalfToFloat(vx); VY = Mathf.HalfToFloat(vy);
            RootRotZ = Mathf.HalfToFloat(rr); RootScaleX = Mathf.HalfToFloat(rs);
            VisPosX = Mathf.HalfToFloat(vpx); VisPosY = Mathf.HalfToFloat(vpy); VisRotZ = Mathf.HalfToFloat(vr);
            VisScaleX = Mathf.HalfToFloat(vsx); VisScaleY = Mathf.HalfToFloat(vsy);
            Flags = (byte)(flags & 0x7F);
            if ((flags & WireHasColor) == 0) ColorRGBA = 0xFFFFFFFFu;
        }
    }

    public static uint PackColor(Color c)
    {
        Color32 c32 = c;
        return (uint)(c32.r | (c32.g << 8) | (c32.b << 16) | (c32.a << 24));
    }

    public static Color UnpackColor(uint v) => new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));
}

// 受信したスナップショットを時刻順に保持し、「少し過去」の時点を補間して取り出す。
// 高速走行でも破綻しないための要点:
//  - 再生時刻は受信側の実時間で進め、送信側の時刻(=届いた最新時刻 - 遅延)へ緩やかに寄せる
//    (端末間の時計合わせに依存せず、揺らぎ(ジッター)があっても一定速度で再生できる)。
//  - パケットが途切れた時だけ直近の速度で短時間外挿し、それ以上は止める(ワープの防止)。
//  - 外挿→補間へ戻る際などに生じる小さな段差は、表示側のオフセットとして短時間で減衰させる
//    (位置そのものを遅らせるスムージングはしない - 高速時に大きく遅れて見えるため)。
public class NetSnapshotInterpolator
{
    public float InterpDelay = 0.1f;         // 30Hz送信で約3パケット分(LOCAL)
    // ONLINE(2026-10-07): インターネット越しは届く間隔の揺らぎが大きいので、少し多めに貯めてから再生する(見た目だけ。判定/権威は変わらない)
    public static float OnlineInterpDelay = 0.15f;
    float Delay => NetSession.Connection == ConnectionType.Online ? Mathf.Max(InterpDelay, OnlineInterpDelay) : InterpDelay;
    public float MaxExtrapolation = 0.2f;
    public float TeleportDistance = 6f;      // これ以上の段差は補間せずに瞬間移動として扱う
    public float ErrorDecayTime = 0.12f;

    readonly List<NetPlayerSnapshot> buf = new List<NetPlayerSnapshot>(64);
    double playT;
    bool clockStarted;

    bool haveRendered;
    double lastTargetX, renderOffsetX;
    float lastTargetY, renderOffsetY;

    public int Count => buf.Count;
    public double LatestTime => buf.Count > 0 ? buf[buf.Count - 1].Time : 0.0;
    public double PlaybackLag => buf.Count > 0 ? buf[buf.Count - 1].Time - playT : 0.0;

    public void Clear()
    {
        buf.Clear();
        clockStarted = false;
        haveRendered = false;
        renderOffsetX = 0.0;
        renderOffsetY = 0f;
    }

    public void Add(NetPlayerSnapshot s)
    {
        // UDP(非信頼)なので順序の入れ替わり/重複があり得る - 時刻順に挿入し、重複は捨てる。
        int i = buf.Count;
        while (i > 0 && buf[i - 1].Time > s.Time) i--;
        if (i > 0 && buf[i - 1].Time == s.Time) return;
        buf.Insert(i, s);
        // 古すぎるもの(再生時刻より十分前)は捨てる。補間に必要な1つ前だけは残す。
        while (buf.Count > 2 && clockStarted && buf[1].Time < playT - 0.5) buf.RemoveAt(0);
        if (buf.Count > 60) buf.RemoveAt(0);
    }

    // dt: 受信側の実経過時間(Time.unscaledDeltaTime)。
    public bool Sample(float dt, out NetPlayerSnapshot discrete, out double x, out float y, out float visPosX, out float visPosY, out float visRotZ, out float visScaleX, out float visScaleY, out float rootRotZ)
    {
        discrete = default; x = 0; y = 0; visPosX = visPosY = visRotZ = rootRotZ = 0f; visScaleX = visScaleY = 1f;
        if (buf.Count == 0) return false;

        double target = LatestTime - Delay;
        if (!clockStarted || System.Math.Abs(playT - target) > 0.5)
        {
            playT = target;
            clockStarted = true;
        }
        else
        {
            // 最新パケットからの遅れが目標から外れていたら、再生速度を±10%まで変えて寄せる。
            double drift = target - playT;
            float rate = 1f + Mathf.Clamp((float)drift * 2f, -0.1f, 0.1f);
            playT += dt * rate;
        }

        NetPlayerSnapshot a = buf[0], b = buf[0];
        bool bracketed = false;
        for (int i = buf.Count - 1; i >= 0; i--)
        {
            if (buf[i].Time <= playT)
            {
                a = buf[i];
                if (i + 1 < buf.Count) { b = buf[i + 1]; bracketed = true; }
                else b = a;
                break;
            }
            a = buf[i]; b = buf[i]; // playTより前のパケットが無い場合は最古のもので保持
        }

        double tx; float ty;
        if (bracketed && !((b.Flags & NetPlayerSnapshot.FlagTeleported) != 0 && playT < b.Time))
        {
            float t = (float)((playT - a.Time) / System.Math.Max(1e-6, b.Time - a.Time));
            t = Mathf.Clamp01(t);
            tx = a.X + (b.X - a.X) * t;
            ty = Mathf.Lerp(a.Y, b.Y, t);
            visPosX = Mathf.Lerp(a.VisPosX, b.VisPosX, t);
            visPosY = Mathf.Lerp(a.VisPosY, b.VisPosY, t);
            visRotZ = Mathf.LerpAngle(a.VisRotZ, b.VisRotZ, t);
            visScaleX = Mathf.Lerp(a.VisScaleX, b.VisScaleX, t);
            visScaleY = Mathf.Lerp(a.VisScaleY, b.VisScaleY, t);
            rootRotZ = Mathf.LerpAngle(a.RootRotZ, b.RootRotZ, t);
        }
        else
        {
            // 最新パケットより先を要求された(=パケットが途切れている) - 短時間だけ外挿。
            float ahead = Mathf.Clamp((float)(playT - a.Time), 0f, MaxExtrapolation);
            tx = a.X + a.VX * ahead;
            ty = a.Y + ((a.Flags & NetPlayerSnapshot.FlagGrounded) != 0 ? 0f : a.VY * ahead);
            visPosX = a.VisPosX; visPosY = a.VisPosY; visRotZ = a.VisRotZ;
            visScaleX = a.VisScaleX; visScaleY = a.VisScaleY; rootRotZ = a.RootRotZ;
        }
        discrete = a;

        // 表示の段差をオフセットとして吸収し、短時間で0へ戻す(瞬間移動は吸収しない)。
        if (haveRendered)
        {
            double jumpX = tx - lastTargetX;
            float jumpY = ty - lastTargetY;
            double renderedX = lastTargetX + renderOffsetX;
            float renderedY = lastTargetY + renderOffsetY;
            bool teleport = System.Math.Abs(jumpX) > TeleportDistance || Mathf.Abs(jumpY) > TeleportDistance;
            if (teleport)
            {
                renderOffsetX = 0.0; renderOffsetY = 0f;
            }
            else
            {
                double expected = a.VX * dt;
                if (System.Math.Abs(jumpX - expected) > 0.35 || Mathf.Abs(jumpY) > 1.5f)
                {
                    renderOffsetX = renderedX - tx;
                    renderOffsetY = renderedY - ty;
                }
            }
        }
        float decay = Mathf.Exp(-dt / Mathf.Max(0.01f, ErrorDecayTime));
        renderOffsetX *= decay;
        renderOffsetY *= decay;
        lastTargetX = tx; lastTargetY = ty; haveRendered = true;

        x = tx + renderOffsetX;
        y = ty + renderOffsetY;
        return true;
    }
}
