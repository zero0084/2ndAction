using UnityEngine;

// エリアルコンボ改修(2026-10-18): 成功が伝わる演出(控えめ・短く)。
//  打ち上げ: 敵から上へ伸びる光の軌跡(Resources/Effects/AirRise、無ければ斬撃の絵を縦に)
//  叩き落とし: 敵から下へ走る軌跡(Resources/Effects/AirSlam)+ 強めの手応えの音。着地の衝撃は SlamImpactRoutine
//  空中追撃の段(AIR 2/AIR 3…)は ComboCounterUI が「N HIT」の下に小さく出す(敵/足場/予告に重ねない)
// どれも加算で重ねる短い絵で、ヒットストップや画面の揺れは足さない(疾走感を止めない)。
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

    public static void Launch(EnemyController e)
    {
        if (e == null) return;
        Load();
        Launches++;
        Vector3 c = e.ComboAimPoint;
        var s = rise != null ? rise : fallback;
        if (s == null) return;
        // 絵は縦長(下 → 上)。無い時は斬撃の絵を縦に
        float rot = rise != null ? 0f : 90f;
        var fx = OneShotSpriteEffect.CreateTweened(s, c + new Vector3(0f, 0.6f, 0f), new Color(1f, 0.97f, 0.85f, 0.95f), duration: 0.24f,
            startScale: 0.55f, endScale: 1.0f, drift: new Vector3(0f, 1.6f, 0f), rotationDegrees: rot, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.15f);
        UseAdd(fx);
    }

    // ↓が当たって下へ押した(まだ締めではない): 小さめの下向きの軌跡だけ(音は通常の命中音)
    public static int DownHits;
    public static void DownHit(EnemyController e)
    {
        if (e == null) return;
        Load();
        DownHits++;
        var s = slam != null ? slam : fallback;
        if (s == null) return;
        float rot = slam != null ? 0f : -90f;
        var fx = OneShotSpriteEffect.CreateTweened(s, e.ComboAimPoint + new Vector3(0f, 0.1f, 0f), new Color(1f, 0.9f, 0.75f, 0.7f), duration: 0.16f,
            startScale: 0.4f, endScale: 0.65f, drift: new Vector3(0f, -1.0f, 0f), rotationDegrees: rot, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.1f);
        UseAdd(fx);
    }

    public static void Slam(EnemyController e)
    {
        if (e == null) return;
        Load();
        Slams++;
        Vector3 c = e.ComboAimPoint;
        var s = slam != null ? slam : fallback;
        if (s != null)
        {
            float rot = slam != null ? 0f : -90f;
            var fx = OneShotSpriteEffect.CreateTweened(s, c + new Vector3(0f, -0.3f, 0f), new Color(1f, 0.85f, 0.7f, 0.95f), duration: 0.2f,
                startScale: 0.6f, endScale: 1.05f, drift: new Vector3(0f, -1.8f, 0f), rotationDegrees: rot, sortingOrder: RenderOrder.CombatFx, holdFraction: 0.1f);
            UseAdd(fx);
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySeAt(SeId.StrongHit, c);
    }

    static void UseAdd(OneShotSpriteEffect fx)
    {
        if (fx == null || add == null) return;
        var r = fx.GetComponent<SpriteRenderer>();
        if (r != null) r.sharedMaterial = add;
    }
}
