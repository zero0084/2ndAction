using System.Collections.Generic;
using UnityEngine;

// ガチャのマップ別の見た目(2026-11-06、マスターの依頼。Docs/Gacha_2026-11-06/request.md)。
//  構造: 共通の筐体(今の5段階の絵 = GameManager.GachaMachineArt)+ 土地ごとのスキン(このクラス)。
//   ・スキン = 後ろの光 / 筐体に重ねる飾りの絵(Resources/UI/GachaSkin/<land>_*.png、無ければ描かない)/ 窓・取り出し口の光の色 / 粒子
//   ・抽選・確率・レア度・カードの面・結果画面の並びは変えない(見た目だけ)
//  どの土地の見た目になるか: 引いたカードの分類(CardMapCategories.Of)。台を選ぶ仕組みではない
//   (選んでも中身が同じなのに「その土地のカードが出る」と誤解させないため。マップごとの台で中身も分けるかはマスターの判断待ち)。
//   回している間(揺れ)に、先頭のカードの土地の予兆 → 出た時に排出の粒子 → 台はその土地の見た目のまま(次に引くまで)。
//  ★4以上: 今のレア度の演出が主役。土地の粒子は数を減らし、結果の板の後ろにだけ出す(カード・★を隠さない)。
//  負荷: 画像は小さな飾り数枚 + 既存の効果の絵。粒子は同時に最大 MaxParticles、出るのは回した時と出た時だけ(常時のループは後ろの光の明滅のみ)。
public class GachaSkinFx
{
    public const int MaxParticles = 48;
    public static bool Enabled = true; // 開発版 -gachaSkin 0 で無効(比べる用)

    // ---------------------------------------------------------------- 色
    public static Color Accent(CardMapCategory k)
    {
        switch (k)
        {
            case CardMapCategory.Wilderness: return new Color(1f, 0.52f, 0.2f);   // 赤橙
            case CardMapCategory.Cave: return new Color(0.3f, 0.95f, 0.8f);        // 青緑
            case CardMapCategory.Sky: return new Color(1f, 0.92f, 0.62f);          // 白金
            case CardMapCategory.LastDungeon: return new Color(0.72f, 0.3f, 1f);   // 紫
            default: return new Color(1f, 0.85f, 0.45f);                           // 共通: 今の金
        }
    }
    // 後ろの光(少し暗め・広め)
    static Color Aura(CardMapCategory k)
    {
        switch (k)
        {
            case CardMapCategory.Wilderness: return new Color(0.95f, 0.42f, 0.16f, 0.30f);
            case CardMapCategory.Cave: return new Color(0.2f, 0.85f, 0.75f, 0.30f);
            case CardMapCategory.Sky: return new Color(1f, 0.95f, 0.8f, 0.34f);
            case CardMapCategory.LastDungeon: return new Color(0.55f, 0.12f, 0.75f, 0.36f);
            default: return new Color(1f, 0.85f, 0.5f, 0f); // 共通は後ろの光なし(今の見た目)
        }
    }
    public static string LabelSrc(CardMapCategory k) => CardMapCategories.LabelSrc(k);

    // 筐体の上の位置(絵 572x687 の割合。5段階とも同じ外形)
    static readonly Vector2 OrbUv = new Vector2(0.565f, 0.505f);
    static readonly Vector2 GaugeUv = new Vector2(0.73f, 0.50f);
    static readonly Vector2 TrayUv = new Vector2(0.62f, 0.80f);
    static readonly Vector2 BaseUv = new Vector2(0.55f, 0.95f);
    // 飾りの絵の範囲: 筐体の絵(572x687)の中心を中心に 1.6 倍(飾りの絵は 640x768。筐体は中央の 400x481 = (120,144) から)
    public const float DecoScale = 1.6f;
    static Rect DecoRect(Rect m) => new Rect(m.center.x - m.width * DecoScale * 0.5f, m.center.y - m.height * DecoScale * 0.5f, m.width * DecoScale, m.height * DecoScale);
    static Vector2 At(Rect m, Vector2 uv) => new Vector2(m.x + m.width * uv.x, m.y + m.height * uv.y);

    // ---------------------------------------------------------------- 状態
    public CardMapCategory Skin { get; private set; } = CardMapCategory.Common;
    CardMapCategory prevSkin = CardMapCategory.Common;
    float blend = 1f;               // prevSkin → Skin の切り替わり(0..1)
    CardMapCategory antLand; float antT = -1f, antDur = 0.5f; object antKey; bool preFlashed;
    public const float PreFlashFrom = 0.72f; // 回している時間のこの割合から、排出の直前の一瞬だけ土地の色/模様
    CardMapCategory disLand; float disT = -1f; bool disHigh;
    bool panelBurstPending; CardMapCategory panelLand; bool panelHigh;
    float time;
    Rect lastMachine;
    public int Spins, Reveals; // 確認用
    public static int ParticlePeak;

    struct P { public Vector2 pos, vel; public float age, life, size, rot, spin, drag, grav; public int kind; public Color col; public bool panel; }
    readonly List<P> ps = new List<P>(MaxParticles);
    enum K { Dot, Sand, Spark, Shard, Feather, Rune, Puff }

    // ---------------------------------------------------------------- 画像
    static Texture2D dot, ring, rune0, rune1, rune2, spark;
    static Texture2D shard, feather, puff;
    static readonly Dictionary<string, Texture2D> deco = new Dictionary<string, Texture2D>();
    static bool loaded;
    static void Load()
    {
        if (loaded) return; loaded = true;
        dot = MakeGlow(32); ring = MakeRing(96, 0.86f); spark = MakeSpark();
        rune0 = Resources.Load<Texture2D>("UI/GachaSkin/rune0") ?? MakeRune(0); rune1 = Resources.Load<Texture2D>("UI/GachaSkin/rune1") ?? MakeRune(1); rune2 = Resources.Load<Texture2D>("UI/GachaSkin/rune2") ?? MakeRune(2); // エステルの絵(無ければ線の形)
        shard = Resources.Load<Texture2D>("Effects/crystalshard");
        feather = Resources.Load<Texture2D>("UI/GachaSkin/feather") ?? Resources.Load<Texture2D>("Effects/flamefeather");
        puff = Resources.Load<Texture2D>("Effects/cloudpuff");
    }
    // 筐体の飾り(エステルと作る絵。無ければ描かない): <land>_back(筐体の後ろ)/ <land>_front(前)
    static Texture2D Deco(CardMapCategory k, string part)
    {
        string key = KeyOf(k) + "_" + part;
        if (!deco.TryGetValue(key, out var t)) { t = Resources.Load<Texture2D>("UI/GachaSkin/" + key); deco[key] = t; }
        return t;
    }
    static readonly Texture2D[] emblems = new Texture2D[5]; static readonly bool[] emblemTried = new bool[5];
    static Texture2D Emblem(CardMapCategory k) { int i = (int)k; if (i < 0 || i >= 5) return null; if (!emblemTried[i]) { emblemTried[i] = true; emblems[i] = Resources.Load<Texture2D>("UI/GachaSkin/emblem_" + KeyOf(k)); } return emblems[i]; }
    public static string KeyOf(CardMapCategory k) => k switch
    {
        CardMapCategory.Wilderness => "wilderness", CardMapCategory.Cave => "cave", CardMapCategory.Sky => "sky", CardMapCategory.LastDungeon => "lastdungeon", _ => "common",
    };

    // ---------------------------------------------------------------- きっかけ
    // 回し始め(揺れの間)。key = 見せる順番待ちの先頭のカード(同じカードで2回始めない)
    public void OnSpin(object key, CardMapCategory land, float duration)
    {
        if (!Enabled || ReferenceEquals(key, antKey)) return;
        antKey = key; antLand = land; antT = 0f; antDur = Mathf.Max(0.15f, duration); Spins++;
        preFlashed = false;
        if (lastMachine.width > 0f) SpawnSpin(lastMachine, CardMapCategory.Common); // エステル: 回している間は共通の金の光と振動だけ(結果を先に見せない)
    }
    // カードが出た(結果を開いた)
    public void OnReveal(CardMapCategory land, int rarity)
    {
        if (!Enabled) return;
        Reveals++;
        antT = -1f; antKey = null;
        if (land != Skin) { prevSkin = Skin; Skin = land; blend = 0f; }
        disLand = land; disT = 0f; disHigh = rarity >= 4;
        if (lastMachine.width > 0f) SpawnDischarge(lastMachine, land, disHigh);
        panelBurstPending = true; panelLand = land; panelHigh = disHigh;
    }
    public void DebugSetSkin(CardMapCategory k) { Skin = k; prevSkin = k; blend = 1f; }
    public void ResetFx() { ps.Clear(); antT = -1f; antKey = null; disT = -1f; panelBurstPending = false; }

    public void Update(float dt)
    {
        time += dt;
        if (blend < 1f) blend = Mathf.Min(1f, blend + dt / 0.35f);
        if (antT >= 0f)
        {
            antT += dt;
            if (!preFlashed && antT >= antDur * PreFlashFrom) { preFlashed = true; if (lastMachine.width > 0f && antLand != CardMapCategory.Common) SpawnSpin(lastMachine, antLand); }
            if (antT > antDur + 0.3f) antT = -1f;
        }
        if (disT >= 0f) { disT += dt; if (disT > 1.2f) disT = -1f; }
        for (int i = ps.Count - 1; i >= 0; i--)
        {
            var p = ps[i];
            p.age += dt;
            if (p.age >= p.life) { ps.RemoveAt(i); continue; }
            p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
            p.vel.y += p.grav * dt;
            p.pos += p.vel * dt;
            p.rot += p.spin * dt;
            ps[i] = p;
        }
        if (ps.Count > ParticlePeak) ParticlePeak = ps.Count;
    }

    // ---------------------------------------------------------------- 描画: 筐体
    // 筐体の絵の前(後ろの光・後ろの飾り)
    public void DrawBehind(Rect m, float alpha)
    {
        if (!Enabled) return;
        Load(); lastMachine = m;
        Color keep = GUI.color;
        DrawAuraFor(m, prevSkin, alpha * (1f - blend));
        DrawAuraFor(m, Skin, alpha * blend);
        float pf = PreFlashAlpha(); if (pf > 0f && antLand != Skin) DrawAuraFor(m, antLand, alpha * pf); // 排出の直前に土地の飾りが一瞬
        // 予兆: 天空の光の輪 / ラスダンの封印の文字は筐体の後ろから広がる
        if (antT >= 0f)
        {
            float u = Mathf.Clamp01(antT / antDur);
            Color a = Accent(antLand);
            if (antLand == CardMapCategory.Sky && u > PreFlashFrom)
            {
                float k = (u - PreFlashFrom) / (1f - PreFlashFrom); float r = m.width * (0.5f + 0.9f * k);
                Vector2 c = At(m, OrbUv);
                GUI.color = new Color(a.r, a.g, a.b, alpha * 0.85f * (1f - k));
                GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), ring);
            }
        }
        GUI.color = keep;
    }
    void DrawAuraFor(Rect m, CardMapCategory k, float alpha)
    {
        if (alpha <= 0.01f) return;
        Color au = Aura(k);
        if (au.a > 0f)
        {
            float breathe = 0.85f + 0.15f * Mathf.Sin(time * 1.6f);
            Vector2 c = new Vector2(m.center.x, m.y + m.height * 0.52f);
            float w = m.width * 1.55f, h = m.height * 1.15f;
            GUI.color = new Color(au.r, au.g, au.b, au.a * alpha * breathe);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), dot);
        }
        var back = Deco(k, "back");
        if (back != null) { GUI.color = new Color(1f, 1f, 1f, alpha); GUI.DrawTexture(DecoRect(m), back, ScaleMode.StretchToFill); }
    }

    // 筐体の絵の後(前の飾り・窓の光・予兆・粒子)
    public void DrawFront(Rect m, float alpha)
    {
        if (!Enabled) return;
        Load(); lastMachine = m;
        Color keep = GUI.color;
        DrawFrontFor(m, prevSkin, alpha * (1f - blend));
        DrawFrontFor(m, Skin, alpha * blend);
        float pf = PreFlashAlpha(); if (pf > 0f && antLand != Skin) DrawFrontFor(m, antLand, alpha * pf);
        if (antT >= 0f) DrawSpin(m, alpha);
        if (disT >= 0f)
        {
            // 取り出し口の閃光
            float k = disT / 1.2f; Color a = Accent(disLand); Vector2 c = At(m, TrayUv);
            float r = m.width * (0.25f + 0.35f * k) * (disHigh ? 0.7f : 1f);
            GUI.color = new Color(a.r, a.g, a.b, alpha * 0.8f * (1f - k) * (disHigh ? 0.5f : 1f));
            GUI.DrawTexture(new Rect(c.x - r, c.y - r * 0.6f, r * 2f, r * 1.2f), dot);
        }
        DrawParticles(alpha, false, default);
        GUI.color = keep;
    }
    void DrawFrontFor(Rect m, CardMapCategory k, float alpha)
    {
        if (alpha <= 0.01f) return;
        var front = Deco(k, "front");
        if (front != null) { GUI.color = new Color(1f, 1f, 1f, alpha); GUI.DrawTexture(DecoRect(m), front, ScaleMode.StretchToFill); }
        if (k == CardMapCategory.Common) return;
        // 窓(丸い窓・目盛り)の光を土地の色へ: 小さな光を重ねるだけ
        Color a = Accent(k);
        float pulse = k == CardMapCategory.Cave ? 0.75f + 0.25f * Mathf.Sin(time * 2.4f) : 0.9f;
        Vector2 o = At(m, OrbUv); float r = m.width * 0.13f;
        GUI.color = new Color(a.r, a.g, a.b, 0.55f * alpha * pulse);
        GUI.DrawTexture(new Rect(o.x - r, o.y - r, r * 2f, r * 2f), dot);
        Vector2 g = At(m, GaugeUv); float gw = m.width * 0.07f, gh = m.height * 0.2f;
        GUI.color = new Color(a.r, a.g, a.b, 0.45f * alpha * pulse);
        GUI.DrawTexture(new Rect(g.x - gw, g.y - gh * 0.5f, gw * 2f, gh), dot);
    }

    // 排出の直前(回している時間の PreFlashFrom 以降)の土地の飾りの明滅 0..1
    float PreFlashAlpha()
    {
        if (antT < 0f || antLand == CardMapCategory.Common) return 0f;
        float u = antT / antDur; if (u < PreFlashFrom) return 0f;
        float k = Mathf.Clamp01((u - PreFlashFrom) / (1f - PreFlashFrom));
        return 0.35f + 0.45f * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2f));
    }

    void DrawSpin(Rect m, float alpha)
    {
        float u = Mathf.Clamp01(antT / antDur);
        Color a = Accent(antLand);
        Vector2 o = At(m, OrbUv);
        switch (u < PreFlashFrom ? CardMapCategory.Common : antLand)
        {
            case CardMapCategory.Wilderness:
            {   // 内部が赤橙に光る(だんだん強く)
                float r = m.width * (0.14f + 0.12f * u);
                GUI.color = new Color(a.r, a.g * 0.9f, a.b, alpha * (0.35f + 0.6f * u));
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, r * 2f, r * 2f), dot);
                break;
            }
            case CardMapCategory.Cave:
            {   // 結晶が脈打つ(2回)→ 強く
                float beat = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2.5f));
                float r = m.width * (0.12f + 0.1f * beat + 0.1f * u);
                GUI.color = new Color(a.r, a.g, a.b, alpha * (0.3f + 0.4f * beat + 0.3f * u));
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, r * 2f, r * 2f), dot);
                Vector2 g = At(m, GaugeUv); float gw = m.width * 0.08f, gh = m.height * 0.22f;
                GUI.DrawTexture(new Rect(g.x - gw, g.y - gh * 0.5f, gw * 2f, gh), dot);
                break;
            }
            case CardMapCategory.Sky:
            {   // 風が集まる(粒子)→ 中心が白く光る
                float r = m.width * (0.1f + 0.14f * u);
                GUI.color = new Color(1f, 0.97f, 0.88f, alpha * (0.25f + 0.65f * u));
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, r * 2f, r * 2f), dot);
                break;
            }
            case CardMapCategory.LastDungeon:
            {   // 封印の文字が筐体のまわりで光る → 最後にほどけて外へ
                int n = 6; float rr = m.width * (0.55f + (u > 0.7f ? (u - 0.7f) / 0.3f * 0.6f : 0f));
                float fade = u > 0.7f ? 1f - (u - 0.7f) / 0.3f : Mathf.Clamp01(u * 2.5f);
                for (int i = 0; i < n; i++)
                {
                    float ang = time * 0.8f + i * Mathf.PI * 2f / n;
                    Vector2 c = new Vector2(o.x + Mathf.Cos(ang) * rr, o.y + Mathf.Sin(ang) * rr * 0.75f);
                    float s = m.width * 0.16f;
                    GUI.color = new Color(a.r, a.g, a.b, alpha * 0.35f * fade);
                    GUI.DrawTexture(new Rect(c.x - s, c.y - s, s * 2f, s * 2f), dot);
                    GUI.color = new Color(0.95f, 0.75f, 1f, alpha * 0.95f * fade);
                    GUI.DrawTexture(new Rect(c.x - s * 0.45f, c.y - s * 0.45f, s * 0.9f, s * 0.9f), RuneTex(i));
                }
                float r = m.width * (0.12f + 0.1f * u);
                GUI.color = new Color(a.r, a.g, a.b, alpha * (0.3f + 0.5f * u));
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, r * 2f, r * 2f), dot);
                break;
            }
            default:
            {
                float r = m.width * (0.1f + 0.08f * u);
                GUI.color = new Color(a.r, a.g, a.b, alpha * 0.5f * u);
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, r * 2f, r * 2f), dot);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- 描画: 結果の板
    // 板の後ろ(カード・★を隠さない)。開いた最初の1回だけ板のふちから外へ散る
    public void DrawPanelBehind(Rect panel, float alpha)
    {
        if (!Enabled) return;
        Load();
        if (panelBurstPending) { panelBurstPending = false; SpawnPanel(panel, panelLand, panelHigh); }
        // 板のふちの淡い色(土地の色。★4以上の光より弱い)
        if (panelLand != CardMapCategory.Common)
        {
            Color a = Accent(panelLand); float pad = 10f;
            Color keep = GUI.color;
            GUI.color = new Color(a.r, a.g, a.b, alpha * (panelHigh ? 0.18f : 0.32f));
            GUI.DrawTexture(new Rect(panel.x - pad * 3f, panel.y - pad * 3f, panel.width + pad * 6f, panel.height + pad * 6f), dot);
            GUI.color = keep;
        }
        DrawParticles(alpha, true, panel);
    }

    // 結果の板の左上: カードの土地の小さな札(紋 + 名前)。共通のカードも出す(どの土地のカードか分かる)
    public static void DrawLandBadge(Rect at, CardMapCategory k, float s)
    {
        Load();
        Color a = Accent(k);
        Color keep = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.1f, 0.85f);
        GUI.DrawTexture(at, Texture2D.whiteTexture);
        GUI.color = new Color(a.r, a.g, a.b, 0.9f);
        GUI.DrawTexture(new Rect(at.x, at.yMax - 2f * s, at.width, 2f * s), Texture2D.whiteTexture);
        float e = at.height - 6f * s;
        DrawEmblem(new Rect(at.x + 4f * s, at.y + 3f * s, e, e), k, false, 1f);
        GUI.color = keep;
        LocGUI.Label(new Rect(at.x + e + 8f * s, at.y, at.width - e - 10f * s, at.height), LabelSrc(k), UiKit.Label(15f * s, TextAnchor.MiddleLeft, true, a));
    }

    public static Texture2D GlowTex { get { Load(); return dot; } }

    // カードの真上の見出し(紋 + 土地の名前、1行。土地の色)
    public static void DrawLandHeader(Rect r, CardMapCategory k, float s)
    {
        Load();
        Color a = Accent(k);
        var st = UiKit.Label(16f * s, TextAnchor.MiddleLeft, true, a);
        string shown = Loc.Auto(LabelSrc(k));
        float tw = st.CalcSize(new GUIContent(shown)).x, e = r.height - 2f * s, gap = 6f * s;
        float x = r.center.x - (e + gap + tw) * 0.5f;
        DrawEmblem(new Rect(x, r.y + 1f * s, e, e), k, false, 1f);
        LocGUI.Label(new Rect(x + e + gap, r.y, tw + 4f, r.height), LabelSrc(k), st);
    }

    // 縦の札(紋の下に名前)。結果の板でカードの左の余白に置く
    public static void DrawLandBadgeV(Rect area, CardMapCategory k, float s)
    {
        Load();
        Color a = Accent(k);
        float e = Mathf.Min(area.width, 52f * s);
        var er = new Rect(area.center.x - e * 0.5f, area.y, e, e);
        DrawEmblem(er, k, false, 1f);
        var st = new GUIStyle(UiKit.Label(14f * s, TextAnchor.UpperCenter, true, a)) { wordWrap = true };
        LocGUI.Label(new Rect(area.x, er.yMax + 2f * s, area.width, Mathf.Max(20f * s, area.height - e - 2f * s)), LabelSrc(k), st);
    }

    // 土地の紋(絵が無い間は色の丸 + 形)。locked = 暗いシルエット + 鍵
    public static void DrawEmblem(Rect r, CardMapCategory k, bool locked, float alpha)
    {
        Load();
        Color keep = GUI.color;
        var art = Emblem(k);
        Color a = Accent(k);
        if (art != null)
        {
            GUI.color = locked ? new Color(0.12f, 0.12f, 0.14f, 0.9f * alpha) : new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, art, ScaleMode.ScaleToFit);
        }
        else
        {
            GUI.color = locked ? new Color(0.16f, 0.16f, 0.18f, 0.95f * alpha) : new Color(a.r * 0.35f, a.g * 0.35f, a.b * 0.35f, 0.95f * alpha);
            GUI.DrawTexture(r, dot);
            GUI.color = locked ? new Color(0.3f, 0.3f, 0.32f, alpha) : new Color(a.r, a.g, a.b, alpha);
            var inner = new Rect(r.x + r.width * 0.22f, r.y + r.height * 0.22f, r.width * 0.56f, r.height * 0.56f);
            GUI.DrawTexture(inner, k == CardMapCategory.LastDungeon ? RuneTex(1) : k == CardMapCategory.Cave && shard != null ? shard : k == CardMapCategory.Sky ? ring : dot);
        }
        if (locked && UiIcons.Lock != null)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.9f * alpha);
            float l = r.width * 0.5f;
            GUI.DrawTexture(new Rect(r.center.x - l * 0.5f, r.center.y - l * 0.5f, l, l), UiIcons.Lock, ScaleMode.ScaleToFit);
        }
        GUI.color = keep;
    }

    // ---------------------------------------------------------------- 粒子
    void Add(Vector2 pos, Vector2 vel, float life, float size, K kind, Color col, float drag = 1.5f, float grav = 0f, bool panel = false)
    {
        if (ps.Count >= MaxParticles) return;
        ps.Add(new P { pos = pos, vel = vel, life = life, size = size, kind = (int)kind, col = col, drag = drag, grav = grav, rot = Random.Range(0f, 360f), spin = Random.Range(-200f, 200f), panel = panel });
    }
    static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

    void SpawnSpin(Rect m, CardMapCategory land)
    {
        float w = m.width; Vector2 o = At(m, OrbUv), b = At(m, BaseUv);
        switch (land)
        {
            case CardMapCategory.Wilderness: // 砂が少し舞う(足元から横へ)
                for (int i = 0; i < 9; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    Add(b + new Vector2(side * w * Random.Range(0.1f, 0.4f), -Random.Range(0f, w * 0.1f)), new Vector2(side * Random.Range(0.6f, 1.6f) * w, -Random.Range(0.3f, 0.9f) * w),
                        Random.Range(0.6f, 1.0f), w * Random.Range(0.035f, 0.06f), K.Sand, new Color(0.85f, 0.66f, 0.42f, 0.9f), 2.6f, w * 0.6f);
                }
                Add(b, new Vector2(-w * 0.5f, -w * 0.15f), 0.8f, w * 0.5f, K.Puff, new Color(0.8f, 0.62f, 0.42f, 0.45f), 2f);
                Add(b, new Vector2(w * 0.5f, -w * 0.15f), 0.8f, w * 0.5f, K.Puff, new Color(0.8f, 0.62f, 0.42f, 0.45f), 2f);
                break;
            case CardMapCategory.Cave: // 結晶の光の粒がゆっくり上がる
                for (int i = 0; i < 6; i++)
                    Add(o + new Vector2(Random.Range(-0.35f, 0.35f) * w, Random.Range(0f, 0.4f) * w), new Vector2(0f, -Random.Range(0.2f, 0.5f) * w), Random.Range(0.6f, 0.9f), w * Random.Range(0.04f, 0.07f), K.Dot, new Color(0.45f, 1f, 0.85f, 0.9f), 0.5f);
                break;
            case CardMapCategory.Sky: // 風が集まる(外から中心へ)
                for (int i = 0; i < 10; i++)
                {
                    float ang = i * 36f + Random.Range(-10f, 10f); float r = w * Random.Range(0.8f, 1.1f);
                    Vector2 p = o + Dir(ang) * r; Vector2 v = (o - p) * 2.2f + new Vector2(-(o - p).y, (o - p).x) * 0.8f;
                    Add(p, v, 0.45f, w * Random.Range(0.035f, 0.055f), K.Dot, new Color(1f, 0.98f, 0.9f, 0.95f), 0.2f);
                }
                break;
            case CardMapCategory.LastDungeon: // 黒い粒が中心へ吸われる
                for (int i = 0; i < 6; i++)
                {
                    Vector2 p = o + Dir(Random.Range(0f, 360f)) * w * 0.7f;
                    Add(p, (o - p) * 1.8f, 0.5f, w * 0.05f, K.Dot, new Color(0.15f, 0.05f, 0.2f, 0.9f), 0.2f);
                }
                break;
            default:
                for (int i = 0; i < 4; i++) Add(o, Dir(Random.Range(200f, 340f)) * w * 0.8f, 0.5f, w * 0.04f, K.Dot, new Color(1f, 0.9f, 0.55f, 0.9f), 2f);
                break;
        }
    }

    void SpawnDischarge(Rect m, CardMapCategory land, bool high)
    {
        float w = m.width; Vector2 t = At(m, TrayUv);
        int f = high ? 2 : 1; // ★4以上は数を減らす(レア度の演出を主役に)
        switch (land)
        {
            case CardMapCategory.Wilderness:
                for (int i = 0; i < 8 / f; i++) Add(t, Dir(Random.Range(200f, 340f)) * w * Random.Range(1.4f, 2.6f), Random.Range(0.35f, 0.6f), w * Random.Range(0.05f, 0.08f), K.Spark, new Color(1f, 0.75f, 0.3f, 1f), 1.2f, w * 3.5f);
                for (int i = 0; i < 8 / f; i++) Add(t, Dir(Random.Range(190f, 350f)) * w * Random.Range(0.6f, 1.4f), Random.Range(0.6f, 1.0f), w * Random.Range(0.03f, 0.05f), K.Sand, new Color(0.88f, 0.68f, 0.42f, 0.95f), 2f, w * 1.6f);
                break;
            case CardMapCategory.Cave:
                for (int i = 0; i < 7 / f; i++) Add(t, Dir(Random.Range(200f, 340f)) * w * Random.Range(1.0f, 2.0f), Random.Range(0.6f, 0.9f), w * Random.Range(0.08f, 0.13f), K.Shard, new Color(0.6f, 1f, 0.92f, 1f), 1.6f, w * 2.2f);
                for (int i = 0; i < 8 / f; i++) Add(t, Dir(Random.Range(180f, 360f)) * w * Random.Range(0.4f, 1.0f), Random.Range(0.7f, 1.1f), w * Random.Range(0.04f, 0.07f), K.Dot, new Color(0.35f, 1f, 0.85f, 0.9f), 1.2f, -w * 0.3f);
                break;
            case CardMapCategory.Sky:
                for (int i = 0; i < 6 / f; i++) Add(t, Dir(Random.Range(210f, 330f)) * w * Random.Range(0.6f, 1.3f), Random.Range(0.9f, 1.3f), w * Random.Range(0.12f, 0.17f), K.Feather, new Color(1f, 1f, 1f, 1f), 1.8f, w * 0.35f);
                for (int i = 0; i < 10 / f; i++) Add(t, Dir(Random.Range(180f, 360f)) * w * Random.Range(0.5f, 1.4f), Random.Range(0.6f, 1.0f), w * Random.Range(0.035f, 0.06f), K.Dot, new Color(1f, 0.95f, 0.75f, 0.95f), 1.4f, -w * 0.2f);
                break;
            case CardMapCategory.LastDungeon:
                for (int i = 0; i < 10 / f; i++) Add(t, Dir(Random.Range(190f, 350f)) * w * Random.Range(0.6f, 1.6f), Random.Range(0.6f, 1.0f), w * Random.Range(0.05f, 0.08f), K.Dot, new Color(0.12f, 0.04f, 0.16f, 0.95f), 1.6f, -w * 0.2f);
                for (int i = 0; i < 4 / f; i++) Add(t, Dir(Random.Range(220f, 320f)) * w * Random.Range(0.5f, 1.0f), Random.Range(0.8f, 1.1f), w * Random.Range(0.09f, 0.12f), K.Rune, new Color(0.85f, 0.55f, 1f, 1f), 1.5f, -w * 0.25f);
                break;
            default:
                for (int i = 0; i < 6 / f; i++) Add(t, Dir(Random.Range(200f, 340f)) * w * Random.Range(0.6f, 1.2f), 0.6f, w * 0.045f, K.Dot, new Color(1f, 0.88f, 0.5f, 0.95f), 1.6f, w * 0.4f);
                break;
        }
    }

    // 結果の板のふちから外へ(板の後ろに描く)
    void SpawnPanel(Rect panel, CardMapCategory land, bool high)
    {
        int n = land == CardMapCategory.Common ? 0 : (high ? 6 : 14);
        float u = Mathf.Min(panel.width, panel.height);
        for (int i = 0; i < n; i++)
        {
            // ふちの上の点と外向き
            float per = Random.Range(0f, 1f) * 2f * (panel.width + panel.height);
            Vector2 p, nrm;
            if (per < panel.width) { p = new Vector2(panel.x + per, panel.y); nrm = Vector2.down; }
            else if ((per -= panel.width) < panel.height) { p = new Vector2(panel.xMax, panel.y + per); nrm = Vector2.right; }
            else if ((per -= panel.height) < panel.width) { p = new Vector2(panel.xMax - per, panel.yMax); nrm = Vector2.up; }
            else { per -= panel.width; p = new Vector2(panel.x, panel.yMax - per); nrm = Vector2.left; }
            Vector2 v = (nrm + new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(-0.4f, 0.4f))) * u * Random.Range(0.12f, 0.3f);
            switch (land)
            {
                case CardMapCategory.Wilderness:
                    if (i % 3 == 0) Add(p, v * 1.6f, 0.5f, u * 0.025f, K.Spark, new Color(1f, 0.72f, 0.3f, 1f), 1.4f, u * 0.4f, true);
                    else Add(p, v, Random.Range(0.8f, 1.2f), u * 0.016f, K.Sand, new Color(0.88f, 0.68f, 0.42f, 0.9f), 1.2f, u * 0.15f, true);
                    break;
                case CardMapCategory.Cave:
                    if (i % 3 == 0) Add(p, v, 1.0f, u * 0.04f, K.Shard, new Color(0.6f, 1f, 0.92f, 1f), 1.2f, u * 0.2f, true);
                    else Add(p, v * 0.6f, Random.Range(0.9f, 1.3f), u * 0.022f, K.Dot, new Color(0.35f, 1f, 0.85f, 0.9f), 0.8f, -u * 0.05f, true);
                    break;
                case CardMapCategory.Sky:
                    if (i % 3 == 0) Add(p, v * 0.7f, 1.4f, u * 0.05f, K.Feather, Color.white, 1.2f, u * 0.06f, true);
                    else Add(p, v * 0.6f, Random.Range(0.9f, 1.3f), u * 0.02f, K.Dot, new Color(1f, 0.95f, 0.75f, 0.95f), 0.8f, -u * 0.04f, true);
                    break;
                case CardMapCategory.LastDungeon:
                    if (i % 3 == 0) Add(p, v * 0.6f, 1.2f, u * 0.04f, K.Rune, new Color(0.85f, 0.55f, 1f, 1f), 1.0f, -u * 0.04f, true);
                    else Add(p, v * 0.7f, Random.Range(0.8f, 1.2f), u * 0.025f, K.Dot, new Color(0.14f, 0.04f, 0.2f, 0.95f), 0.9f, -u * 0.03f, true);
                    break;
            }
        }
    }

    void DrawParticles(float alpha, bool panelLayer, Rect panel)
    {
        if (ps.Count == 0) return;
        Color keepC = GUI.color;
        Matrix4x4 keepM = GUI.matrix;
        for (int i = 0; i < ps.Count; i++)
        {
            var p = ps[i];
            if (p.panel != panelLayer) continue;
            if (panelLayer && panel.Contains(p.pos)) continue; // 板の内側には描かない(カード・★を隠さない)
            float k = p.age / p.life;
            float a = p.col.a * alpha * (k < 0.15f ? k / 0.15f : 1f - Mathf.Max(0f, (k - 0.5f) / 0.5f));
            if (a <= 0.01f) continue;
            Texture2D tex; float sx = p.size, sy = p.size; bool rotate = false;
            switch ((K)p.kind)
            {
                case K.Sand: tex = Texture2D.whiteTexture; sx = sy = p.size * 0.6f; rotate = true; break;
                case K.Spark: tex = spark; sx = p.size * 2.2f; sy = p.size * 0.5f; rotate = true; break;
                case K.Shard: tex = shard != null ? shard : dot; rotate = true; break;
                case K.Feather: tex = feather != null ? feather : dot; rotate = true; break;
                case K.Rune: tex = RuneTex(i); break;
                case K.Puff: tex = puff != null ? puff : dot; sx = p.size * (1f + k); sy = p.size * 0.6f * (1f + k); break;
                default: tex = dot; break;
            }
            GUI.color = new Color(p.col.r, p.col.g, p.col.b, a);
            Rect r = new Rect(p.pos.x - sx * 0.5f, p.pos.y - sy * 0.5f, sx, sy);
            if (rotate)
            {
                float ang = (K)p.kind == K.Spark ? Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg : p.rot;
                GUI.matrix = keepM; GUIUtility.RotateAroundPivot(ang, p.pos);
                GUI.DrawTexture(r, tex);
                GUI.matrix = keepM;
            }
            else GUI.DrawTexture(r, tex);
        }
        GUI.matrix = keepM; GUI.color = keepC;
    }

    public int ParticleCount => ps.Count;

    // ---------------------------------------------------------------- 手続きの小さな絵
    static Texture2D RuneTex(int i) { Load(); return (i % 3) == 0 ? rune0 : (i % 3) == 1 ? rune1 : rune2; }
    static Texture2D MakeGlow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
            float a = (1f - d) * (1f - d);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(); return tex;
    }
    static Texture2D MakeRing(int size, float inner)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01((1f - d) * 14f) * Mathf.Clamp01((d - inner) * 14f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(); return tex;
    }
    static Texture2D MakeSpark()
    {
        const int w = 32, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            float u = x / (float)(w - 1), v = Mathf.Abs(y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);
            float a = Mathf.Clamp01(u * 1.2f) * (1f - v * v) * (u > 0.85f ? (1f - u) / 0.15f : 1f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply(); return tex;
    }
    // 古代文字(線だけの簡単な形 3 種。エステルの絵が来たら置き換え)
    static Texture2D MakeRune(int variant)
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        void Line(float x0, float y0, float x1, float y1)
        {
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f; int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t) * (n - 1)), y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t) * (n - 1));
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                    float a = dx == 0 && dy == 0 ? 1f : 0.55f; int k = yy * n + xx; if (px[k].a < a) px[k] = new Color(1f, 1f, 1f, a);
                }
            }
        }
        switch (variant)
        {
            case 0: Line(0.5f, 0.1f, 0.5f, 0.9f); Line(0.5f, 0.35f, 0.8f, 0.15f); Line(0.5f, 0.6f, 0.8f, 0.4f); break;      // ᚠ
            case 1: Line(0.3f, 0.1f, 0.3f, 0.9f); Line(0.3f, 0.1f, 0.7f, 0.35f); Line(0.7f, 0.35f, 0.3f, 0.6f); Line(0.3f, 0.6f, 0.75f, 0.9f); break; // ᚱ
            default: Line(0.5f, 0.1f, 0.2f, 0.5f); Line(0.2f, 0.5f, 0.5f, 0.9f); Line(0.5f, 0.9f, 0.8f, 0.5f); Line(0.8f, 0.5f, 0.5f, 0.1f); Line(0.5f, 0.1f, 0.5f, 0.9f); break; // 菱形の封印
        }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DevArgs()
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-gachaSkin");
        if (i >= 0 && i + 1 < a.Length && a[i + 1] == "0") Enabled = false;
    }
#endif
}
