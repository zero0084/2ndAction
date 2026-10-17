using UnityEngine;

// アニメーション強化(2026-10-17、試作のエフェクト): 命中の衝撃をコマのアニメーションで出す(Resources/Effects/HitImpact/impact_00〜)。
// 黒い背景で描いた光の絵を、絵の色のまま加算で重ねる(Effects/SpriteAdd)。
// ヒットストップ中も進む(実時間)ので、止まった画面の上で衝撃が弾ける。最初の2コマは少し長めに見せて「当たった瞬間」を強める。
// 絵が無い時は false を返す(呼ぶ側は今までの1枚の火花を出す)。
public class HitImpactFx : MonoBehaviour
{
    static Sprite[] frames;
    static bool loaded;
    static Material addMat;
    // 1コマの長さ(秒)。合計で約 0.2秒(6コマ)
    static readonly float[] FrameTime = { 0.035f, 0.04f, 0.035f, 0.03f, 0.03f, 0.03f };

    SpriteRenderer sr;
    float t, scale;
    int shown = -1;
    Color tint;

    public static bool Available
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                var all = Resources.LoadAll<Sprite>("Effects/HitImpact");
                if (all != null && all.Length > 0)
                {
                    System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
                    frames = all;
                }
                addMat = Resources.Load<Material>("Effects/SpriteAdd");
            }
            return frames != null && frames.Length > 0;
        }
    }

    // size: 衝撃の直径(ワールドの単位)。強い攻撃ほど大きく。rotation: 向きを毎回少し変えると同じ絵の繰り返しに見えにくい
    public static bool Spawn(Vector3 pos, float size, Color tint, int sortingOrder = RenderOrder.CombatFx)
    {
        if (!Available) return false;
        var g = new GameObject("HitImpact");
        g.transform.position = new Vector3(pos.x, pos.y, 0f);
        g.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-20f, 20f));
        var fx = g.AddComponent<HitImpactFx>();
        fx.sr = g.AddComponent<SpriteRenderer>();
        if (addMat != null) fx.sr.sharedMaterial = addMat;
        fx.sr.sortingOrder = sortingOrder;
        fx.tint = tint;
        float w = frames[0].bounds.size.x;
        fx.scale = size / Mathf.Max(0.01f, w);
        g.transform.localScale = Vector3.one * fx.scale;
        fx.Show(0);
        FloatingOrigin.Shifted += fx.OnShift;
        return true;
    }

    void OnShift(float s) { transform.position -= new Vector3(s, 0f, 0f); }
    void OnDestroy() { FloatingOrigin.Shifted -= OnShift; }

    void Show(int i)
    {
        if (i == shown) return;
        shown = i;
        sr.sprite = frames[Mathf.Min(i, frames.Length - 1)];
        sr.color = tint;
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float acc = 0f;
        for (int i = 0; i < frames.Length; i++)
        {
            acc += i < FrameTime.Length ? FrameTime[i] : 0.03f;
            if (t < acc) { Show(i); return; }
        }
        Destroy(gameObject);
    }
}
