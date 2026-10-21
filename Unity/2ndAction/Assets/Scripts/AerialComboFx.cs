using UnityEngine;

// エリアルコンボ改修(2026-10-18): 成功が伝わる演出(控えめ・短く)。
//  打ち上げ: 敵から上へ伸びる光の軌跡(Resources/Effects/AirRise、無ければ斬撃の絵を縦に)
//  叩き落とし: 敵から下へ走る軌跡(Resources/Effects/AirSlam)+ 強めの手応えの音。着地の衝撃は SlamImpactRoutine
//  空中追撃の段(AIR 2/AIR 3…)は ComboCounterUI が「N HIT」の下に小さく出す(敵/足場/予告に重ねない)
// どれも加算で重ねる短い絵で、ヒットストップや画面の揺れは足さない(疾走感を止めない)。
// ボスの BREAK(2026-10-18): 同じ絵をボスの大きさに合わせて(scale)。BREAK の始まり/回復の予告の輪もここ
public static class AerialComboFx
{
    static Sprite rise, slam, fallback;
    static bool loaded;
    static Material add;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        rise = Resources.Load<Sprite>("Effects/AirRise");
        slam = Resources.Load<Sprite>("Effects/AirSlam");
        fallback = Resources.Load<Sprite>("Effects/slash");
        add = Resources.Load<Material>("Effects/SpriteAdd");
    }

    public static int Launches, Slams; // 確認用(QA)

    public static void Launch(EnemyController e) { if (e != null) Launch(e.ComboAimPoint, 1f); }
    public static void Launch(Vector3 c, float scale)
    {
        Load();
        Launches++;
        var s = rise != null ? rise : fallback;
        if (s == null) return;
        // 絵は縦長(下 → 上)。無い時は斬撃の絵を縦に
        float rot = rise != null ? 0f : 90f;
        var fx = OneShotSpriteEffect.CreateTweened(s, c + new Vector3(0f, 0.6f * scale, 0f), new Color(1f, 0.97f, 0.85f, 0.95f), duration: 0.24f,
            startScale: 0.55f * scale, endScale: 1.0f * scale, drift: new Vector3(0f, 1.6f * scale, 0f), rotationDegrees: rot, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.15f);
        UseAdd(fx);
    }

    // ↓が当たって下へ押した(まだ締めではない): 小さめの下向きの軌跡だけ(音は通常の命中音)
    public static int DownHits;
    public static void DownHit(EnemyController e) { if (e != null) DownHit(e.ComboAimPoint, 1f); }
    public static void DownHit(Vector3 c, float scale)
    {
        Load();
        DownHits++;
        var s = slam != null ? slam : fallback;
        if (s == null) return;
        float rot = slam != null ? 0f : -90f;
        var fx = OneShotSpriteEffect.CreateTweened(s, c + new Vector3(0f, 0.1f, 0f), new Color(1f, 0.9f, 0.75f, 0.7f), duration: 0.16f,
            startScale: 0.4f * scale, endScale: 0.65f * scale, drift: new Vector3(0f, -1.0f * scale, 0f), rotationDegrees: rot, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.1f);
        UseAdd(fx);
    }

    public static void Slam(EnemyController e) { if (e != null) Slam(e.ComboAimPoint, 1f); }
    public static void Slam(Vector3 c, float scale)
    {
        Load();
        Slams++;
        var s = slam != null ? slam : fallback;
        if (s != null)
        {
            float rot = slam != null ? 0f : -90f;
            var fx = OneShotSpriteEffect.CreateTweened(s, c + new Vector3(0f, -0.3f * scale, 0f), new Color(1f, 0.85f, 0.7f, 0.95f), duration: 0.2f,
                startScale: 0.6f * scale, endScale: 1.05f * scale, drift: new Vector3(0f, -1.8f * scale, 0f), rotationDegrees: rot, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.1f);
            UseAdd(fx);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.StrongHit, c);
    }

    // ---------------------------------------------------------------- ボスの BREAK(2026-10-18)

    // BREAK の始まり: 体の中心から広がる金色の輪(2重)と火花。短く、予告の色(赤)とは違う色
    public static void BreakBurst(Vector3 c, float size)
    {
        Load();
        var r1 = OneShotSpriteEffect.CreateTweened(BossFx.Ring(), c, new Color(1f, 0.85f, 0.35f, 0.95f), 0.42f, size * 0.3f, size * 1.9f, 0.9f, 0f, default, 0f, RenderOrder.CombatFx, 0.08f);
        UseAdd(r1);
        var r2 = OneShotSpriteEffect.CreateTweened(BossFx.Ring(), c, new Color(1f, 1f, 1f, 0.8f), 0.3f, size * 0.2f, size * 1.2f, 0.8f, 0f, default, 0f, RenderOrder.CombatFx, 0.05f);
        UseAdd(r2);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), c, new Color(1f, 0.92f, 0.5f, 1f), 14, 0.45f, size * 0.06f, size * 0.12f, size * 2.2f, 1.5f, RenderOrder.CombatFx);
    }

    // 叩きつけの締め(ボス): 地面に広がる衝撃の輪 + 下向きの軌跡。揺れは呼ぶ側で(短く)
    public static void BossSlam(Vector3 c, Vector3 ground, float size)
    {
        Slam(c, Mathf.Clamp(size * 0.45f, 1f, 2.4f));
        var r = OneShotSpriteEffect.CreateTweened(BossFx.Ring(), ground + new Vector3(0f, 0.25f, 0f), new Color(1f, 0.8f, 0.5f, 0.9f), 0.36f, size * 0.4f, size * 2.0f, 0.85f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        UseAdd(r);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), ground + new Vector3(0f, 0.2f, 0f), new Color(0.85f, 0.75f, 0.6f, 0.85f), 16, 0.55f, 0.35f, 0.8f, 4f, 2.6f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.SlamImpact, ground);
    }

    // 回復が近い: 体へ縮んでいく白い輪(1回)。予告(赤)とも BREAK(金)とも違う色
    public static void RecoverWarn(Vector3 c, float size)
    {
        var r = OneShotSpriteEffect.CreateTweened(BossFx.Ring(), c, new Color(0.75f, 0.9f, 1f, 0.85f), 0.5f, size * 1.6f, size * 0.5f, 0.8f, 0f, default, 0f, RenderOrder.CombatFx, 0.1f);
        UseAdd(r);
    }

    // 2026-10-21: 締めの合図(回復の予告)。「そろそろ↓で締めよう」が伝わる、ほかの攻撃の光と見分けられる色と形:
    //  ・紫の輪が BREAK の終わりの瞬間へ向けて体へ縮む(残り時間)。攻撃の光(白/金)・予告の赤・BREAK の金のどれとも違う色
    //  ・頭の上に下向きの白銀の山形(↓)が、下へ弾むように点滅する
    //  体の色も同じ紫で点滅させる(呼ぶ側の FinishTint)
    public static readonly Color FinishTint = new Color(0.86f, 0.66f, 1f, 1f);
    public static readonly Color FinishColor = new Color(0.78f, 0.45f, 1f, 0.95f);
    public static int FinishCues; // 確認用(QA)
    public static void FinishCue(Component host, System.Func<Vector3> center, float size, float seconds, System.Func<bool> alive)
    {
        if (host == null || center == null) return;
        if (ComboRevision.Legacy) { RecoverWarn(center(), size); return; }
        FinishCues++;
        var go = new GameObject("FinishCue");
        var cue = go.AddComponent<FinishCueFx>();
        cue.Init(host, center, size, seconds, alive);
    }
    public static Material AddMaterial { get { Load(); return add; } }

    static void UseAdd(OneShotSpriteEffect fx)
    {
        if (fx == null || add == null) return;
        var r = fx.GetComponent<SpriteRenderer>();
        if (r != null) r.sharedMaterial = add;
    }
}

// 締めの合図の見た目(AerialComboFx.FinishCue)。ボスが消える/BREAK が終わる/時間が来たら消える
public class FinishCueFx : MonoBehaviour
{
    Component host;
    System.Func<Vector3> center;
    System.Func<bool> alive;
    float size, seconds, t;
    SpriteRenderer ring, arrow;

    public void Init(Component h, System.Func<Vector3> c, float s, float secs, System.Func<bool> a)
    {
        host = h; center = c; size = s; seconds = Mathf.Max(0.2f, secs); alive = a;
        ring = MakeChild("Ring", BossFx.Ring(), true);
        arrow = MakeChild("Arrow", UiIcons.ChevronSprite, false);
        if (arrow != null)
        {
            arrow.transform.localRotation = Quaternion.Euler(0f, 0f, -90f); // 右向きの「>」を下向きに
            arrow.sortingOrder = RenderOrder.WorldUi + 1; // ボスの HP の帯より手前(頭の上で帯に隠れない)
        }
        Place();
    }

    SpriteRenderer MakeChild(string n, Sprite s, bool additive)
    {
        if (s == null) return null;
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = s;
        r.sortingOrder = RenderOrder.CombatFx + 2;
        if (additive && AerialComboFx.AddMaterial != null) r.sharedMaterial = AerialComboFx.AddMaterial;
        return r;
    }

    void Place()
    {
        Vector3 c = center();
        transform.position = new Vector3(c.x, c.y, 0f);
        float k = Mathf.Clamp01(t / seconds);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 22f);
        if (ring != null)
        {
            float sc = Mathf.Lerp(size * 1.5f, size * 0.42f, k);
            ring.transform.localScale = new Vector3(sc, sc, 1f);
            var col = AerialComboFx.FinishColor; col.a *= Mathf.Lerp(0.55f, 1f, pulse) * (k < 0.1f ? k / 0.1f : 1f);
            ring.color = col;
        }
        if (arrow != null)
        {
            float bounce = Mathf.Repeat(t * 4f, 1f); // 1秒に4回、上から下へ
            float a = Mathf.Sin(bounce * Mathf.PI);
            float s = Mathf.Clamp(size * 0.28f, 0.6f, 1.4f) * 1.28f * 2.4f; // 128px / 100ppu。絵の山形は画像の中央の一部だけなので大きめに
            arrow.transform.localScale = new Vector3(s, s, 1f);
            arrow.transform.localPosition = new Vector3(0f, size * 0.62f + 1.1f - bounce * 0.5f, 0f); // HP の帯の上
            arrow.color = new Color(0.95f, 0.72f, 1f, 0.35f + 0.65f * a); // 紫(輪と同じ系統)。空の明るい所でも見える濃さ
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        if (host == null || (alive != null && !alive()) || t > seconds + 0.15f) { Destroy(gameObject); return; }
        Place();
    }
}
