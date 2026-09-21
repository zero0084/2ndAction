using System.Collections.Generic;
using UnityEngine;

// Home画面「操作できる場所を控えめな動きで目立たせる」待機演出(2026-09-21)。
//
// 対象5か所(扉/ガチャ/肖像画/カード編集(ベッド上のカード)/カード合成(本の光))
// と、部屋を漂う光の粒子。すべて「静止 → 短い動作 → 静止」を繰り返し、
// 待ち時間だけがランダム(動作そのものは決まったスムーズなカーブ)。
//
// 設計上の約束:
//  - 見た目(この描画)とタップ判定(GameManager側の固定Rect)は完全に別。
//    ここでは一切GUI.Buttonを使わない。
//  - 状態はこのインスタンスだけが持つ。Home非表示中はReset()され、
//    途中の姿勢/コイン/粒子は残らない(多重起動も起きない)。
//  - 位置はすべて毎フレームbgRoomRect(背景の実表示Rect)から計算するので、
//    画面比率/向きが変わっても背景とズレない。
//  - 動作していない間は「何も描かない」か「静止時と同一の描画」にして、
//    元の位置・角度・大きさへ正確に戻す。
[System.Serializable]
public class HomeIdleSettings
{
    public bool enabled = true;
    [Tooltip("演出全体の再生速度(1=通常)。確認/調整用")]
    public float playbackSpeed = 1f;
    [Header("再生タイミング")]
    public float waitMin = 6f;
    public float waitMax = 15f;
    [Tooltip("ホーム表示直後の初回待ち時間の下限(対象ごとにさらにずらす)")]
    public float firstWaitMin = 3f;
    public float firstWaitStagger = 2.2f;
    [Tooltip("同時に動く対象の上限")]
    public int maxConcurrent = 2;

    [Header("扉")]
    public float doorOpenDegrees = 19f;
    public float doorDuration = 3.6f;
    [Range(0f, 1f)] public float doorLightIntensity = 1f;

    [Header("ガチャ")]
    public float gachaWobbleDegrees = 4.5f;
    public float gachaDuration = 1.9f;
    public float gachaWobbleCycles = 3.5f;
    [Range(0f, 1f)] public float coinChance = 0.5f;
    public int coinCountMin = 2;
    public int coinCountMax = 3;

    [Header("肖像画")]
    public float portraitSwingDegrees = 2.6f;
    public float portraitDuration = 2.6f;
    public float portraitSwingCycles = 2.5f;

    [Header("カード編集(ベッド上のカード)")]
    public float cardLiftPx = 13f;      // 1080p基準の持ち上がり量
    public float cardTiltDegrees = 16f;
    public float cardDuration = 3.0f;
    public float cardStagger = 0.32f;

    [Header("カード合成(本の光)")]
    [Range(0f, 1f)] public float bookGlowMax = 0.92f;
    public float bookGlowDuration = 4.4f;

    [Header("光の粒子")]
    public bool particlesEnabled = true;
    [Range(0, 48)] public int particleCount = 20;
    [Range(0f, 1f)] public float particleMaxAlpha = 0.55f;
    public float particleSpeed = 1f;
}

public class HomeIdleFx
{
    public enum Target { Door = 0, Gacha = 1, Portrait = 2, Cards = 3, Book = 4 }
    const int TargetCount = 5;

    // 背景素材(1536x1024)上の座標(px) - カード素材の切り出し位置。
    // 順序はGameManager.homeIdleCards(A,B,C,D,E,G,H)と一致させる。
    static readonly Vector4[] CardRects =
    {
        new Vector4(139, 652, 85, 34),
        new Vector4(241, 683, 81, 35),
        new Vector4(178, 703, 67, 41),
        new Vector4(209, 743, 94, 37),
        new Vector4(218, 758, 109, 47),
        new Vector4(29, 839, 198, 111),
        new Vector4(156, 818, 114, 47),
    };
    // 動く順番(下の大きいカードから奥へ、パラパラ順に)
    static readonly int[] CardOrder = { 5, 6, 4, 3, 2, 1, 0 };
    static readonly int[] DrawOrder = { 0, 1, 2, 3, 4, 6, 5 };
    // 扉の葉(パディング6px込み)の背景上の位置。ヒンジは右端。
    static readonly Vector4 DoorRect = new Vector4(607, 186, 259, 487);
    const float DoorHingePx = 253f;  // DoorRect.x基準のヒンジ位置(葉の右端)
    const float BgW = 1536f, BgH = 1024f;

    public HomeIdleSettings S;
    public Texture2D doorLeaf, doorBackdrop;
    public Texture2D[] cards;

    class Slot { public float wait; public float t = -1f; public float dur; public float sign = 1f; }
    readonly Slot[] slots = new Slot[TargetCount];
    float lastTick = -1f;
    bool wasActive;

    // ---- コイン ----
    class Coin { public Vector2 pos, vel; public float age, life, spin, spinSpeed, size; }
    readonly List<Coin> coins = new List<Coin>();
    static Texture2D coinTex;

    // ---- 粒子 ----
    class Mote { public Vector2 uv; public Vector2 vel; public float age, life, phase, size, warm, maxA; }
    readonly List<Mote> motes = new List<Mote>();

    static Texture2D softTex;
    static Texture2D SoftTex()
    {
        if (softTex != null) return softTex;
        const int n = 64;
        softTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        softTex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smoothstep: 縁が確実に0で四角い縁が出ない
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        softTex.SetPixels32(px);
        softTex.Apply();
        return softTex;
    }

    static Texture2D CoinTex()
    {
        if (coinTex != null) return coinTex;
        const int n = 32;
        coinTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        coinTex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f - n / 2f) / (n / 2f), dy = (y + 0.5f - n / 2f) / (n / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 1f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                float rim = Mathf.SmoothStep(0.72f, 0.86f, r);          // 縁は濃い金
                float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-0.35f, 0.4f)) * 1.6f);
                Color c = Color.Lerp(new Color(1f, 0.85f, 0.35f), new Color(0.75f, 0.52f, 0.12f), rim);
                c = Color.Lerp(c, new Color(1f, 0.97f, 0.7f), hl * 0.6f);
                float a = 1f - Mathf.SmoothStep(0.9f, 1f, r);
                px[y * n + x] = (Color32)new Color(c.r, c.g, c.b, a);
            }
        coinTex.SetPixels32(px);
        coinTex.Apply();
        return coinTex;
    }

    public HomeIdleFx(HomeIdleSettings settings)
    {
        S = settings;
        for (int i = 0; i < TargetCount; i++) slots[i] = new Slot();
        Reset();
    }

    // ホーム非表示(サブ画面/ラン中)で呼ぶ: 途中の姿勢・コイン・粒子を全消去。
    public void Reset()
    {
        for (int i = 0; i < TargetCount; i++)
        {
            slots[i].t = -1f;
            slots[i].wait = Random.Range(S.firstWaitMin, S.firstWaitMin + S.firstWaitStagger * TargetCount) + i * S.firstWaitStagger * 0.6f;
        }
        coins.Clear();
        motes.Clear();
        lastTick = -1f;
    }

    public void SetVisible(bool visible)
    {
        if (visible && !wasActive) Reset();
        if (!visible && wasActive) Reset();
        wasActive = visible;
    }

    int Concurrent()
    {
        int c = 0;
        for (int i = 0; i < TargetCount; i++) if (slots[i].t >= 0f) c++;
        return c;
    }

    float DurationOf(int i)
    {
        switch ((Target)i)
        {
            case Target.Door: return S.doorDuration;
            case Target.Gacha: return S.gachaDuration;
            case Target.Portrait: return S.portraitDuration;
            case Target.Cards: return S.cardDuration + S.cardStagger * (CardRects.Length - 1);
            default: return S.bookGlowDuration;
        }
    }

    // フレームに1回だけ進める(OnGUIはフレーム内で複数回呼ばれるため)。
    public void Tick()
    {
        float now = Time.unscaledTime;
        if (lastTick < 0f) { lastTick = now; return; }
        if (Mathf.Approximately(now, lastTick)) return;
        float dt = Mathf.Min(now - lastTick, 0.1f) * Mathf.Max(0.01f, S.playbackSpeed);
        lastTick = now;

        if (S.enabled)
        {
            for (int i = 0; i < TargetCount; i++)
            {
                Slot s = slots[i];
                if (s.t >= 0f)
                {
                    s.t += dt;
                    if (s.t >= s.dur)
                    {
                        s.t = -1f;
                        s.wait = Random.Range(S.waitMin, S.waitMax);
                    }
                }
                else
                {
                    s.wait -= dt;
                    if (s.wait <= 0f)
                    {
                        if (Concurrent() < Mathf.Max(1, S.maxConcurrent)) StartTarget(i);
                        else s.wait = Random.Range(0.6f, 1.5f); // 空くまで少し待つ
                    }
                }
            }
        }

        for (int i = coins.Count - 1; i >= 0; i--)
        {
            Coin c = coins[i];
            c.age += dt;
            if (c.age >= c.life) { coins.RemoveAt(i); continue; }
            c.vel.y += coinGravity * dt;
            c.pos += c.vel * dt;
            c.spin += c.spinSpeed * dt;
        }
        moteDt = dt;
    }
    float moteDt;
    float coinGravity;

    public void DebugStart(int i) { if (i >= 0 && i < TargetCount) { StartTarget(i); if (i == 1) coinsPending = 3; } }

    void StartTarget(int i)
    {
        Slot s = slots[i];
        s.t = 0f;
        s.dur = DurationOf(i);
        s.sign = Random.value < 0.5f ? -1f : 1f;
        if ((Target)i == Target.Gacha) coinsPending = Random.value < S.coinChance ? Random.Range(S.coinCountMin, S.coinCountMax + 1) : 0;
    }
    int coinsPending;

    float U(Target t)
    {
        Slot s = slots[(int)t];
        return s.t < 0f ? -1f : Mathf.Clamp01(s.t / Mathf.Max(0.01f, s.dur));
    }

    // 減衰する揺れ(0→…→0)。u=0で0、uが進むほど振幅が収まる。
    static float DampedSwing(float u, float cycles)
    {
        return Mathf.Sin(u * cycles * Mathf.PI * 2f) * Mathf.Pow(1f - u, 1.6f) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 8f));
    }

    static Rect FracRect(Rect b, float x0, float y0, float x1, float y1)
    {
        return new Rect(b.x + b.width * x0, b.y + b.height * y0, b.width * (x1 - x0), b.height * (y1 - y0));
    }
    static Rect PxRect(Rect bg, float x, float y, float w, float h)
    {
        return new Rect(bg.x + bg.width * x / BgW, bg.y + bg.height * y / BgH, bg.width * w / BgW, bg.height * h / BgH);
    }

    // ================= 扉 =================
    public void DrawDoor(Rect bg, float alpha)
    {
        float u = U(Target.Door);
        if (!S.enabled || u < 0f || doorLeaf == null) return;

        // 開き具合(0..1): 素早く少し開く → 少し留まる → ゆっくり閉じる
        float o;
        if (u < 0.30f) o = 1f - Mathf.Pow(1f - u / 0.30f, 2.2f);
        else if (u < 0.48f) o = 1f - 0.03f * Mathf.Sin((u - 0.30f) / 0.18f * Mathf.PI);
        else o = 1f - Mathf.SmoothStep(0f, 1f, (u - 0.48f) / 0.52f);
        if (o <= 0.0005f) return;

        float theta = S.doorOpenDegrees * o * Mathf.Deg2Rad;
        float cos = Mathf.Cos(theta), sin = Mathf.Sin(theta);
        Rect leaf = PxRect(bg, DoorRect.x, DoorRect.y, DoorRect.z, DoorRect.w);
        float scaleX = leaf.width / DoorRect.z;

        // 1) 開いた隙間の奥(暖かい光) - 扉の開きに応じてフェードイン。閉じれば消える。
        Color prev = GUI.color;
        float lightA = Mathf.Clamp01(o * 4f) * S.doorLightIntensity * alpha;
        if (doorBackdrop != null)
        {
            GUI.color = new Color(1f, 1f, 1f, lightA);
            GUI.DrawTexture(leaf, doorBackdrop, ScaleMode.StretchToFill);
        }

        // 2) 扉の葉: ヒンジ(右端)を固定し、縦の細い帯ごとに横幅をcosθへ圧縮+奥へ行くほど
        //    少し低く(遠近)。平面のまま揺れるのではなく「開いて見える」表現。
        int strips = 40;
        float hingeX = leaf.x + DoorHingePx * scaleX;
        float yc = leaf.y + leaf.height * 0.5f;
        float shade = Mathf.Lerp(1f, 0.82f, Mathf.Clamp01(sin / 0.4f));
        GUI.color = new Color(shade, shade * 0.985f, shade * 0.96f, alpha);
        for (int i = 0; i < strips; i++)
        {
            float s0 = i / (float)strips, s1 = (i + 1) / (float)strips;
            float d0 = DoorHingePx - s0 * DoorRect.z, d1 = DoorHingePx - s1 * DoorRect.z; // ヒンジからの距離(px, 左が正)
            float x0 = hingeX - d0 * cos * scaleX;
            float x1 = hingeX - d1 * cos * scaleX;
            float dm = Mathf.Max(0f, (d0 + d1) * 0.5f) / DoorHingePx;
            float hScale = 1f - 0.075f * sin / 0.33f * dm;   // 奥ほど低く
            float h = leaf.height * hScale;
            Rect r = new Rect(x0, yc - h * 0.5f, Mathf.Max(0.5f, x1 - x0) + 0.75f, h);
            GUI.DrawTextureWithTexCoords(r, doorLeaf, new Rect(s0, 0f, s1 - s0, 1f));
        }

        // 3) 床への光のこぼれ(開き具合に比例、閉じれば消える)
        Texture2D soft = SoftTex();
        float gapW = leaf.width * (1f - cos) * 1.4f + leaf.width * 0.08f * o;
        Rect spill = new Rect(leaf.x + leaf.width * 0.18f - gapW * 0.2f, leaf.yMax - leaf.height * 0.03f, leaf.width * 0.62f + gapW, leaf.height * 0.11f);
        GUI.color = new Color(1f, 0.86f, 0.5f, 0.42f * o * S.doorLightIntensity * alpha);
        GUI.DrawTexture(spill, soft);
        GUI.color = prev;
    }

    // ================= ガチャ =================
    // 現在の揺れ角(度) - GameManagerが機械本体の描画に回転を掛ける。
    public float GachaAngle()
    {
        float u = U(Target.Gacha);
        if (!S.enabled || u < 0f) return 0f;
        return DampedSwing(u, S.gachaWobbleCycles) * S.gachaWobbleDegrees * slots[(int)Target.Gacha].sign;
    }

    // 揺れに合わせてコインを飛ばす(揺れの序盤に1回だけ)。machineRectは非回転の矩形。
    public void UpdateCoins(Rect machineRect)
    {
        coinGravity = machineRect.width * 3.4f;
        float u = U(Target.Gacha);
        if (u >= 0.12f && coinsPending > 0)
        {
            int n = coinsPending;
            coinsPending = 0;
            for (int i = 0; i < n; i++)
            {
                float dir = (i % 2 == 0 ? 1f : -1f) * (Random.value < 0.5f ? 1f : -1f);
                coins.Add(new Coin
                {
                    pos = new Vector2(machineRect.center.x + machineRect.width * Random.Range(-0.12f, 0.12f), machineRect.y + machineRect.height * 0.28f),
                    vel = new Vector2(dir * machineRect.width * Random.Range(0.3f, 0.75f), -machineRect.width * Random.Range(1.1f, 1.6f)),
                    life = Random.Range(0.85f, 1.05f),
                    spin = Random.Range(0f, 6f),
                    spinSpeed = Random.Range(9f, 15f) * (Random.value < 0.5f ? -1f : 1f),
                    size = machineRect.width * Random.Range(0.15f, 0.19f),
                });
            }
        }
    }

    public void DrawCoins(float alpha)
    {
        if (coins.Count == 0) return;
        Texture2D tex = CoinTex();
        Color prev = GUI.color;
        foreach (Coin c in coins)
        {
            float k = c.age / c.life;
            float a = (k < 0.4f ? 1f : 1f - (k - 0.4f) / 0.6f) * 0.95f * alpha; // 落ちながら薄くなって消える
            float w = c.size * Mathf.Max(0.18f, Mathf.Abs(Mathf.Cos(c.spin)));
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(new Rect(c.pos.x - w * 0.5f, c.pos.y - c.size * 0.5f, w, c.size), tex);
        }
        GUI.color = prev;
    }

    // ================= 肖像画 =================
    public float PortraitAngle()
    {
        float u = U(Target.Portrait);
        if (!S.enabled || u < 0f) return 0f;
        return DampedSwing(u, S.portraitSwingCycles) * S.portraitSwingDegrees * slots[(int)Target.Portrait].sign;
    }

    // ================= カード編集(ベッド上のカード) =================
    public void DrawCards(Rect bg, float alpha)
    {
        if (cards == null) return;
        float u = U(Target.Cards);
        bool moving = S.enabled && u >= 0f;
        float t = moving ? slots[(int)Target.Cards].t : 0f;
        float px = bg.height / 1080f;
        Color prev = GUI.color;
        Matrix4x4 prevM = GUI.matrix;

        // 奥(H)を先に、手前(G)を後に描くため配列順(A..H)ではなく固定順で。
        for (int di = 0; di < DrawOrder.Length; di++)
        {
            int idx = DrawOrder[di];
            Texture2D tex = idx < cards.Length ? cards[idx] : null;
            if (tex == null) continue;
            Vector4 cr = CardRects[idx];
            Rect r = PxRect(bg, cr.x, cr.y, cr.z, cr.w);

            int order = System.Array.IndexOf(CardOrder, idx);
            float local = moving ? Mathf.Clamp01((t - order * S.cardStagger) / S.cardDuration) : 0f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (local <= 0f || local >= 1f)
            {
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill); // 静止時=素材そのまま(ズレ・拡縮なし)
                continue;
            }

            float e = Mathf.Sin(local * Mathf.PI);
            e = e * e * (3f - 2f * e) * 0.5f + e * 0.5f;
            float lift = e * S.cardLiftPx * px;
            float tilt = e * S.cardTiltDegrees * ((idx % 2 == 0) ? 1f : -1f);
            float flip = 1f - 0.34f * e; // めくれる(縦に少し縮む)

            // 落ち影(持ち上がった分だけ薄く出る)
            Color sh = new Color(0.05f, 0.03f, 0.1f, 0.4f * e * alpha);
            GUI.color = sh;
            GUI.DrawTexture(new Rect(r.x - r.width * 0.05f, r.y + r.height * 0.15f, r.width * 1.1f, r.height * 0.9f), SoftTex());

            GUI.color = new Color(1f, 1f, 1f, alpha);
            Vector2 c = r.center;
            GUIUtility.RotateAroundPivot(tilt, c);
            Rect dr = new Rect(r.x, c.y - r.height * flip * 0.5f - lift, r.width, r.height * flip);
            GUI.DrawTexture(dr, tex, ScaleMode.StretchToFill);
            GUI.matrix = prevM;
        }
        GUI.color = prev;
    }

    // ================= カード合成(本の光) =================
    public void DrawBookGlow(Rect bg, float alpha)
    {
        float u = U(Target.Book);
        if (!S.enabled || u < 0f) return;
        // ゆっくり明るくなる(立ち上がり35%) -> 少し明るさを保つ(20%) -> ゆっくり消える(45%)
        float env = u < 0.35f ? Mathf.SmoothStep(0f, 1f, u / 0.35f) : (u < 0.55f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (u - 0.55f) / 0.45f));
        if (env <= 0.001f) return;

        Texture2D soft = SoftTex();
        Color prev = GUI.color;
        // 本の隙間(ページ小口/表紙の際)に沿った3つの柔らかい光。円形グラデ+滑らか
        // な縁なので、周囲に四角い発光領域は出ない。
        // 漏れる光の範囲は少し広め(各ブロブを中心から約18%拡大)。本の形に沿った円形グラデ。
        DrawGlowBlob(soft, Grow(FracRect(bg, 0.865f, 0.885f, 1.0f, 0.975f), 1.18f), new Color(1f, 0.9f, 0.6f), 1.0f * env * S.bookGlowMax * alpha);
        DrawGlowBlob(soft, Grow(FracRect(bg, 0.775f, 0.855f, 0.9f, 0.935f), 1.18f), new Color(1f, 0.88f, 0.55f), 0.9f * env * S.bookGlowMax * alpha);
        DrawGlowBlob(soft, Grow(FracRect(bg, 0.8f, 0.8f, 0.93f, 0.875f), 1.18f), new Color(1f, 0.93f, 0.7f), 0.75f * env * S.bookGlowMax * alpha);
        // 16:9などで右端が切れる画面でも見えるよう、本の手前(左)側の縁にも1つ。
        DrawGlowBlob(soft, Grow(FracRect(bg, 0.72f, 0.845f, 0.86f, 0.945f), 1.15f), new Color(1f, 0.9f, 0.6f), 0.95f * env * S.bookGlowMax * alpha);
        GUI.color = prev;
    }

    static Rect Grow(Rect r, float k) { Vector2 c = r.center; return new Rect(c.x - r.width * k * 0.5f, c.y - r.height * k * 0.5f, r.width * k, r.height * k); }

    static void DrawGlowBlob(Texture2D soft, Rect r, Color c, float a)
    {
        GUI.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
        GUI.DrawTexture(r, soft);
    }

    // ================= 光の粒子 =================
    // quiet: ロゴ/肖像画など、視認性を優先して薄くする領域(画面座標)。
    public void DrawMotes(Rect bg, float alpha, Rect[] quiet)
    {
        if (!S.particlesEnabled || S.particleCount <= 0 || !S.enabled) return;
        Rect area = new Rect(0f, 0f, Screen.width, Screen.height);
        while (motes.Count < S.particleCount) motes.Add(NewMote(true));
        while (motes.Count > S.particleCount) motes.RemoveAt(motes.Count - 1);

        Texture2D soft = SoftTex();
        float dt = moteDt; moteDt = 0f;
        Color prev = GUI.color;
        float px = Mathf.Max(0.5f, Screen.height / 1080f);
        for (int i = 0; i < motes.Count; i++)
        {
            Mote m = motes[i];
            m.age += dt;
            if (m.age >= m.life) { motes[i] = m = NewMote(false); }
            float k = m.age / m.life;
            float fade = Mathf.SmoothStep(0f, 1f, k / 0.25f) * (1f - Mathf.SmoothStep(0f, 1f, (k - 0.6f) / 0.4f));
            m.uv += m.vel * dt * S.particleSpeed;
            float sway = Mathf.Sin(Time.unscaledTime * 0.45f + m.phase) * 0.012f;
            Vector2 p = new Vector2((m.uv.x + sway) * area.width, m.uv.y * area.height);

            // 場所ごとの見えやすさ: 窓の近くはやや見えやすく、暗い場所(ベッド/左下)は控えめ
            float bx = bg.width > 0f ? (p.x - bg.x) / bg.width : 0.5f;
            float by = bg.height > 0f ? (p.y - bg.y) / bg.height : 0.5f;
            float win = Mathf.Exp(-((bx - 0.92f) * (bx - 0.92f) / 0.05f + (by - 0.22f) * (by - 0.22f) / 0.06f));
            float w = 0.7f + 0.5f * win;
            if (bx < 0.34f && by > 0.55f) w *= 0.55f;

            float q = 1f;
            if (quiet != null) foreach (Rect z in quiet) if (z.Contains(p)) { q = 0.3f; break; }

            float a = m.maxA * S.particleMaxAlpha * fade * w * q * alpha;
            if (a <= 0.002f) continue;
            float sz = m.size * px;
            Color c = Color.Lerp(new Color(1f, 0.85f, 0.48f), new Color(1f, 0.96f, 0.86f), m.warm);
            GUI.color = new Color(c.r, c.g, c.b, a);
            GUI.DrawTexture(new Rect(p.x - sz, p.y - sz, sz * 2f, sz * 2f), soft);
        }
        GUI.color = prev;
    }

    Mote NewMote(bool prewarm)
    {
        // 大半は小さい点、ときどき少し大きい粒
        bool big = Random.value < 0.08f;
        float angle = Random.value * Mathf.PI * 2f; // 上昇一辺倒にしない(全方向へ)
        float spd = Random.Range(0.004f, 0.011f);
        var m = new Mote
        {
            uv = new Vector2(Random.Range(0.03f, 0.97f), Random.Range(0.06f, 0.96f)),
            vel = new Vector2(Mathf.Cos(angle) * spd, Mathf.Sin(angle) * spd * 0.7f - 0.0015f),
            life = Random.Range(9f, 17f),
            phase = Random.value * 6.28f,
            size = big ? Random.Range(9f, 14f) : Random.Range(2.8f, 5.4f),
            warm = Random.value,
            maxA = big ? Random.Range(0.55f, 0.75f) : Random.Range(0.75f, 1f),
        };
        m.age = prewarm ? Random.Range(0f, m.life) : 0f;
        return m;
    }
}
