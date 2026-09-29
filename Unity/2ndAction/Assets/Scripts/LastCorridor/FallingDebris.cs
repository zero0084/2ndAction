using UnityEngine;

// LAST CORRIDOR(2026-09-29): 「落ちてくる構造物」「閉じてくる扉」。
// 障害物そのもの(当たり判定/耐久力/マルチの共有)は普通の障害物のまま。絵だけを上に持ち上げておき、
// プレイヤーが近づくと落とす(扉はゆっくり降りてくる)。当たり判定は最初から着地位置にあるので、
// 遊びの上では「見えてから反応できる距離に置かれた普通の障害物」と同じで、理不尽な当たり方はしない:
//  ・落ち始めは「着地してから、プレイヤーが届くまでに leadTime 秒以上ある」距離(走行速度に比例)
//  ・落ちる前から足元に影と光の印を出す(どこに落ちるか分かる)
//  ・高速補助(HighSpeedAssist)は当たり判定を見るので、最初から普通の障害物として扱える
// マルチ: 落ちるかどうかは位置から決まる(全員同じ)。落ちるタイミングは各端末の自分のプレイヤー基準(見た目だけ)。
public class FallingDebris : MonoBehaviour
{
    public enum Kind { Fall, Gate }
    public static int Started, Landed, Skipped;
    bool checkedOnce;
    // 診断: 着地した瞬間、プレイヤーが届くまでに残っていた秒数の最小値(理不尽な当たり方をしていないかの確認)
    public static float MinLeadSeconds = float.PositiveInfinity;

    public float leadTime = 1.1f;      // 着地〜プレイヤー到達までに最低これだけの秒数を残す
    public float gravity = 42f;
    public float gateSpeed = 7.5f;     // 扉が降りてくる速さ(u/秒)

    ObstacleController oc;
    Kind kind;
    float lift, startLift, vel;
    bool falling, landed;
    SpriteRenderer shadow, mark;
    float t;

    public bool IsLanded => landed;

    public static FallingDebris Attach(ObstacleController o, Kind k, float height)
    {
        var d = o.gameObject.AddComponent<FallingDebris>();
        d.oc = o; d.kind = k;
        d.lift = d.startLift = Mathf.Max(2f, height);
        o.visualLift = d.lift;
        d.MakeMarks();
        return d;
    }

    void MakeMarks()
    {
        float w = 1.6f;
        if (oc.Visual != null) w = Mathf.Max(1f, oc.Visual.bounds.size.x);
        var sg = new GameObject("DebrisShadow");
        sg.transform.SetParent(transform, false);
        shadow = sg.AddComponent<SpriteRenderer>();
        shadow.sprite = OneShotSpriteEffect.SoftDotSprite();
        shadow.sortingOrder = RenderOrder.EnvironmentFx;
        sg.transform.position = transform.position + new Vector3(0f, 0.05f, 0f);
        sg.transform.localScale = new Vector3(w * 1.2f, 0.35f, 1f);
        shadow.color = new Color(0f, 0f, 0f, 0f);

        // 真上へ伸びる細い光(シアン): 「ここに何か来る」印
        var mg = new GameObject("DebrisMark");
        mg.transform.SetParent(transform, false);
        mark = mg.AddComponent<SpriteRenderer>();
        mark.sprite = OneShotSpriteEffect.SoftDotSprite();
        mark.sortingOrder = RenderOrder.Ground - 2;
        mg.transform.position = transform.position + new Vector3(0f, startLift * 0.5f, 0f);
        mg.transform.localScale = new Vector3(w * 0.35f, startLift + 2f, 1f);
        mark.color = new Color(0.3f, 0.95f, 1f, 0f);
    }

    void Update()
    {
        if (landed) return;
        if (oc == null || oc.Broken || !oc.isActiveAndEnabled) { Finish(false); return; }
        var pc = PlayerController.Instance;
        float dt = Time.deltaTime;
        t += dt;
        if (!falling && pc != null)
        {
            float speed = Mathf.Max(pc.CurrentAutoRunSpeed, pc.runSpeed, 4f);
            float dx = transform.position.x - pc.transform.position.x;
            float travel = kind == Kind.Gate ? startLift / gateSpeed : Mathf.Sqrt(2f * startLift / gravity);
            if (dx <= speed * (travel + leadTime) + 2f)
            {
                // 置かれた時点で既に近すぎる(超高速で先読み範囲がぎりぎり等): 落とさず普通の障害物として置く
                // (見えてから反応できる時間を削らない)
                if (!checkedOnce && dx < speed * (travel * 0.5f + leadTime)) { Skipped++; Finish(false); return; }
                falling = true; Started++;
            }
            checkedOnce = true;
        }
        if (falling)
        {
            // 落ちている途中にプレイヤーが急に速くなった(加速カード/速度の段階が上がった等)場合でも、
            // 着地〜到達までの時間を削らない: 残りが短くなったらその場で着地させる
            if (pc != null)
            {
                float spd = Mathf.Max(pc.CurrentAutoRunSpeed, pc.runSpeed, 4f);
                if (transform.position.x - pc.transform.position.x < spd * leadTime * 0.85f) lift = 0f;
            }
            if (kind == Kind.Gate) lift -= gateSpeed * dt;
            else { vel += gravity * dt; lift -= vel * dt; }
            if (lift <= 0f) { lift = 0f; oc.visualLift = 0f; Finish(true); return; }
        }
        oc.visualLift = lift;
        float k = 1f - lift / Mathf.Max(0.01f, startLift);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 7f);
        if (shadow != null) shadow.color = new Color(0f, 0f, 0f, falling ? 0.25f + 0.4f * k : 0.12f + 0.08f * pulse);
        if (mark != null) mark.color = new Color(0.3f, 0.95f, 1f, falling ? 0.18f * (1f - k) : 0.07f + 0.07f * pulse);
    }

    void Finish(bool land)
    {
        landed = true;
        if (oc != null) oc.visualLift = 0f;
        if (shadow != null) Destroy(shadow.gameObject);
        if (mark != null) Destroy(mark.gameObject);
        if (!land) return;
        Landed++;
        Vector3 p = transform.position;
        var pcl = PlayerController.Instance;
        if (pcl != null)
        {
            float dxl = p.x - pcl.transform.position.x;
            if (dxl > -1f) MinLeadSeconds = Mathf.Min(MinLeadSeconds, dxl / Mathf.Max(1f, pcl.CurrentAutoRunSpeed));
        }
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), p + new Vector3(0f, 0.2f, 0f), new Color(0.75f, 0.78f, 0.85f, 0.8f),
            kind == Kind.Gate ? 6 : 10, 0.45f, 0.35f, 0.8f, 4.5f, 1.6f, RenderOrder.EnvironmentFx);
        var pc = PlayerController.Instance;
        if (pc != null && Mathf.Abs(p.x - pc.transform.position.x) < 30f)
        {
            var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cf != null) cf.Shake(kind == Kind.Gate ? 0.1f : 0.16f, 0.18f);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BigEnemyAttack, kind == Kind.Gate ? 0.5f : 0.7f);
        }
    }
}
