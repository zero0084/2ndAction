using System.Collections.Generic;
using UnityEngine;

// 属性の状態中の体の表示と頭上のマーク(2026-10-30、視認性の改修)。ElementStatus が状態のある間だけ毎フレーム呼ぶ。性能には関係しない。
//  炎上 … 体に小さな炎が残って揺れる(上位カードほど数と大きさが少し増える)
//  冷気 … 足元と体の端に薄い霜 / 凍結 … 体の下側と端を氷の欠片で覆う(体の中央は覆わない = 本体が見える)
//  帯電 … 体のまわりに小さな電気が走る(落雷の直後と、HIGH VOLTAGE の停止の間)
//  出血 … 赤黒い滴が下へ落ちる(炎の火の粉は上へ、血は下へ = 色が近くても動きで分かる)
//  マーク … 頭上に小さな丸い印(文字なし)。雑魚は2つ・ボスは3つまで、並びは 凍結/冷気 → 炎 → 雷 → 血 → 風。
//  負荷: 1フレームに体の表示を出す敵は MaxFullPerFrame まで(それ以上は炎1つ・霜なしの簡易)、マークは MaxMarkedPerFrame 体まで、画面外は出さない
public class ElementAura
{
    public const int MaxFullPerFrame = 18, MaxMarkedPerFrame = 24;
    static int frame = -1, fullThisFrame, markedThisFrame;
    public static int PeakAuras, PeakMarked; // 確認用

    readonly ElementStatus st;
    readonly Transform root;
    readonly List<SpriteRenderer> flames = new List<SpriteRenderer>(), cores = new List<SpriteRenderer>(), frost = new List<SpriteRenderer>(), marks = new List<SpriteRenderer>();
    readonly List<Vector2> flameUv = new List<Vector2>(), frostUv = new List<Vector2>();
    readonly float seed;
    float nextArc, nextDrop;
    readonly Dictionary<ElementArt.Mark, float> onset = new Dictionary<ElementArt.Mark, float>();
    DragonHealthBar bar; float barLookup = -9f;

    public ElementAura(ElementStatus s)
    {
        st = s;
        root = new GameObject("ElemAura").transform;
        root.SetParent(ElementVfx.I.transform, false);
        seed = Random.value * 100f;
    }

    SpriteRenderer Sr(List<SpriteRenderer> list, int i, Sprite sp, bool add, int order)
    {
        while (list.Count <= i)
        {
            var go = new GameObject("a"); go.transform.SetParent(root, false);
            var r = go.AddComponent<SpriteRenderer>();
            list.Add(r);
        }
        var sr = list[i];
        if (sr.sprite != sp) sr.sprite = sp;
        var mat = add ? ElementVfx.AddMat : SharedMaterials.SpritesDefault;
        if (sr.sharedMaterial != mat) sr.sharedMaterial = mat;
        sr.sortingOrder = order;
        sr.enabled = true;
        return sr;
    }
    static void HideFrom(List<SpriteRenderer> list, int from) { for (int i = from; i < list.Count; i++) if (list[i] != null) list[i].enabled = false; }

    public void Hide() { HideFrom(flames, 0); HideFrom(cores, 0); HideFrom(frost, 0); HideFrom(marks, 0); }
    public void Dispose() { if (root != null) Object.Destroy(root.gameObject); }

    public void Update(Component target)
    {
        if (target == null || root == null) return;
        if (frame != Time.frameCount) { frame = Time.frameCount; fullThisFrame = markedThisFrame = 0; }
        Bounds b = st.Bounds; float s = st.Size;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 v = cam.WorldToViewportPoint(b.center);
            if (v.x < -0.15f || v.x > 1.15f || v.y < -0.3f || v.y > 1.3f) { Hide(); return; } // 画面外
        }
        bool full = fullThisFrame < MaxFullPerFrame; fullThisFrame++;
        if (fullThisFrame > PeakAuras) PeakAuras = fullThisFrame;
        float t = Time.time;

        // ---- 炎(体に残る小さな炎)
        int nf = 0;
        if (st.Burning)
        {
            int tier = ElementVfx.Tier(ElementType.Fire);
            int want = full ? Mathf.Clamp(Mathf.RoundToInt(s * 1.4f) + (tier >= 2 ? 1 : 0), 2, 6) : 1;
            float size = 0.72f * Mathf.Sqrt(s) * (1f + 0.15f * (tier - 1)) * (1f + 0.4f * st.BurnBoost);
            for (int i = 0; i < want; i++)
            {
                while (flameUv.Count <= i) flameUv.Add(new Vector2(Random.Range(0.12f, 0.88f), Random.Range(0.1f, 0.75f)));
                var sr = Sr(flames, i, ElementArt.Flame, false, RenderOrder.CombatFx);
                var core = Sr(cores, i, ElementArt.Flame, true, RenderOrder.CombatFx + 1);
                Vector2 uv = flameUv[i];
                float ph = seed + i * 1.7f;
                Vector3 at = new Vector3(Mathf.Lerp(b.min.x, b.max.x, uv.x), Mathf.Lerp(b.min.y, b.max.y, uv.y), 0f);
                float fl = (0.75f + 0.5f * Mathf.Repeat(ph * 0.37f, 1f)) * (1f + 0.22f * Mathf.Sin(t * 13f + ph) + 0.1f * Mathf.Sin(t * 29f + ph * 2f)); // 長短を付けて揺らす
                sr.transform.position = at;
                sr.transform.localScale = new Vector3(size * (1f - 0.08f * Mathf.Sin(t * 17f + ph)) * (i % 2 == 0 ? 1f : -1f), size * fl, 1f); // 交互に左右反転(先の曲がりがそろわない)
                sr.color = new Color(1f, 0.38f + 0.1f * Mathf.Sin(t * 9f + ph), 0.06f, 0.92f);
                core.transform.position = at - new Vector3(0f, size * 0.12f, 0f);
                core.transform.localScale = new Vector3(size * 0.5f, size * 0.55f * fl, 1f);
                core.color = new Color(1f, 0.9f, 0.45f, 0.9f);
                nf++;
            }
        }
        HideFrom(flames, nf); HideFrom(cores, nf);

        // ---- 霜 / 凍結の氷
        int nr = 0;
        if (full && (st.Chilled || st.Frozen))
        {
            bool fz = st.Frozen;
            int want = fz ? Mathf.Clamp(Mathf.RoundToInt(3 + s * 1.5f), 4, 8) : Mathf.Clamp(Mathf.RoundToInt(2 + s), 2, 4);
            for (int i = 0; i < want; i++)
            {
                // 体の下側と左右の端(中央は空ける = 本体が見える)
                while (frostUv.Count <= i) { float side = (frostUv.Count % 2 == 0) ? Random.Range(0.02f, 0.3f) : Random.Range(0.7f, 0.98f); frostUv.Add(new Vector2(frostUv.Count < 2 ? Random.Range(0.25f, 0.75f) : side, frostUv.Count < 2 ? Random.Range(0.0f, 0.12f) : Random.Range(0.05f, 0.6f))); }
                Vector2 uv = frostUv[i];
                var sr = Sr(frost, i, fz ? ElementArt.IceChunk : ElementArt.Crystal, !fz, RenderOrder.CombatFx);
                sr.transform.position = new Vector3(Mathf.Lerp(b.min.x, b.max.x, uv.x), Mathf.Lerp(b.min.y, b.max.y, uv.y), 0f);
                float sz = (fz ? 0.62f : 0.6f) * Mathf.Sqrt(s) * (fz ? 1f : 0.85f + 0.15f * Mathf.Sin(t * 4f + i));
                sr.transform.localScale = new Vector3(sz, sz, 1f);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, fz ? (uv.x < 0.5f ? 25f : -25f) + i * 7f : t * 30f + i * 40f);
                sr.color = fz ? new Color(1f, 1f, 1f, 0.9f) : new Color(ElementVfx.IceEdge.r, ElementVfx.IceEdge.g, ElementVfx.IceEdge.b, 0.75f);
                nr++;
            }
        }
        HideFrom(frost, nr);

        // ---- 帯電(小さな電気が体のまわりを走る)/ 出血(赤黒い滴が落ちる)— 粒は間引いて出す
        if (st.Charged && t >= nextArc && full)
        {
            nextArc = t + 0.09f;
            Vector3 p = new Vector3(Random.value < 0.5f ? b.min.x : b.max.x, Random.Range(b.min.y, b.max.y), 0f);
            if (Random.value < 0.5f) p = new Vector3(Random.Range(b.min.x, b.max.x), b.max.y, 0f);
            float ang = Random.Range(0f, 360f);
            ElementVfx.Spawn(ElementArt.Streak, p, ElementVfx.BoltEdge, 0.1f, 0.28f * Mathf.Sqrt(s), 0.18f * Mathf.Sqrt(s), Quaternion.Euler(0, 0, ang) * Vector3.right * 2f, 1f, 0.2f, false, 0f, low: true);
        }
        if (st.Bleeding && t >= nextDrop)
        {
            nextDrop = t + (full ? 0.28f : 0.6f);
            Vector3 p = new Vector3(Random.Range(b.min.x + b.size.x * 0.2f, b.max.x - b.size.x * 0.2f), Mathf.Lerp(b.min.y, b.max.y, Random.Range(0.3f, 0.7f)), 0f);
            ElementVfx.Spawn(ElementArt.Drop, p, Color.Lerp(ElementVfx.BloodCore, ElementVfx.BloodEdge, 0.45f), 0.55f, 0.17f * Mathf.Sqrt(s), 0.12f * Mathf.Sqrt(s), new Vector3(0f, -0.5f, 0f), 1f, 0.3f, false, 0f, 7f, low: true);
        }

        // ---- 頭上のマーク(文字なし)
        int nm = 0;
        if (markedThisFrame < MaxMarkedPerFrame)
        {
            markedThisFrame++;
            if (markedThisFrame > PeakMarked) PeakMarked = markedThisFrame;
            int max = st.IsBoss ? 3 : 2;
            float size = (st.IsBoss ? 0.76f : 0.56f) * Mathf.Clamp(Mathf.Sqrt(s), 1f, 1.4f);
            var want = Order(st);
            // 新しく付いた状態(0.6秒以内)は一度先頭へ出して強調(上限で隠れる状態も、付いた瞬間は必ず見える)。その後は優先順へ戻る
            for (int k = 0; k < want.Count; k++)
            {
                if (!onset.TryGetValue(want[k], out float since)) { onset[want[k]] = t; since = t; }
                if (t - since < 0.6f && k > 0) { var m = want[k]; want.RemoveAt(k); want.Insert(0, m); }
            }
            foreach (ElementArt.Mark m in System.Enum.GetValues(typeof(ElementArt.Mark))) if (!want.Contains(m)) onset.Remove(m);
            int n = Mathf.Min(max, want.Count);
            float y = b.max.y + size * 0.75f + 0.08f;
            if (st.IsBoss)
            {
                // ボスは HP バーの上(バーと重ならない)
                if (bar == null && Time.time - barLookup > 1f) { barLookup = Time.time; bar = DragonHealthBar.For(target.transform); }
                if (bar != null && bar.isActiveAndEnabled) y = Mathf.Max(y, bar.transform.position.y + 0.25f + size * 0.6f);
            }
            for (int i = 0; i < n; i++)
            {
                var sr = Sr(marks, i, ElementArt.MarkSprite(want[i]), false, RenderOrder.WorldUi);
                float x = b.center.x + (i - (n - 1) * 0.5f) * size * 1.08f;
                float age = onset.TryGetValue(want[i], out float s0) ? t - s0 : 9f;
                float pop = age < 0.35f ? 1f + 0.45f * Mathf.Sin(age / 0.35f * Mathf.PI) : want[i] == ElementArt.Mark.Freeze ? 1f + 0.06f * Mathf.Sin(t * 8f) : 1f;
                sr.transform.position = new Vector3(x, y, 0f);
                sr.transform.localScale = Vector3.one * size * pop;
                sr.color = Color.white;
                nm++;
            }
        }
        HideFrom(marks, nm);
    }

    static readonly List<ElementArt.Mark> order = new List<ElementArt.Mark>(6);
    static List<ElementArt.Mark> Order(ElementStatus st)
    {
        order.Clear();
        if (st.Frozen) order.Add(ElementArt.Mark.Freeze); else if (st.Chilled) order.Add(ElementArt.Mark.Chill);
        if (st.Burning) order.Add(ElementArt.Mark.Burn);
        if (st.Charged) order.Add(ElementArt.Mark.Shock);
        if (st.Bleeding) order.Add(ElementArt.Mark.Bleed);
        if (st.Windswept) order.Add(ElementArt.Mark.Wind);
        return order;
    }
}
