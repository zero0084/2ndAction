using UnityEngine;

// 判断型の道中イベント(2026-11-05)の敵の役割。EnemyController に付けて、被ダメージ/ノックバック/接触の扱いと見た目を変える。
//  ・SkyBlocker   上空封鎖の飛ぶ敵: 決めた高さに留まる(地面へ降りてこない = 地上は安全)
//  ・GroundBlocker 地上封鎖の重い敵: 地上の攻撃で後ろへ下がらない(体当たりが当たる。空中へ逃げれば簡単)
//  ・Heavy        高火力敵: 体当たり/攻撃のダメージ +N ハート。赤紫の軽いオーラ + 武器の光。HP は普通(倒せばすぐ危険が消える)
//  ・Brute        エリアルで退かす敵: 地上の攻撃はほとんど効かず下がらない / 打ち上げは大きく浮く / 空中で N 回当てると大きく吹き飛ぶ(撃破)。
//                 鋼色の鎧 + 頭の上の上向きの印(打ち上げが効く)
public enum DecisionRole { None, SkyBlocker, GroundBlocker, Heavy, Brute }

public class DecisionEnemy : MonoBehaviour
{
    public DecisionRole role;
    public double anchorLogicalX; public float anchorY; // SkyBlocker: 留まる位置(X は論理座標 = 浮動原点のずらしに強い)
    public int airHits;           // Brute: 空中で当てた回数
    public static int BruteFlings, BruteGroundHits, BruteLaunches, HeavyHitsDealt; // 確認用
    EnemyController ec;
    SpriteRenderer body, aura, mark, markEdge, glint;
    float nextClank;
    float phase;

    static DecisionEventTuning T => DecisionEventTuning.I;
    public static DecisionEnemy Of(Component c) => c != null ? c.GetComponent<DecisionEnemy>() : null;

    public bool ResistGround => role == DecisionRole.Brute || (role == DecisionRole.GroundBlocker && T.groundBlockersResistKnockback);
    // 攻撃が当たっても止まらない敵は、命中の直後の「体の接触の猶予」(Contact Grace)を与えない(当てても下がらないので体当たりは当たる)
    public bool IgnoresContactGrace => ResistGround;
    public int ExtraHearts => role == DecisionRole.Heavy ? Mathf.Max(0, T.heavyExtraHearts) : 0;

    public static DecisionEnemy Attach(GameObject go, DecisionRole role)
    {
        if (go == null) return null;
        var d = go.GetComponent<DecisionEnemy>() ?? go.AddComponent<DecisionEnemy>();
        d.role = role;
        d.anchorLogicalX = FloatingOrigin.ToLogical(go.transform.position.x); d.anchorY = go.transform.position.y;
        d.Setup();
        return d;
    }

    void Setup()
    {
        ec = GetComponent<EnemyController>();
        body = GetComponentInChildren<SpriteRenderer>();
        phase = Random.value * 6f;
        switch (role)
        {
            case DecisionRole.Heavy:
                aura = MakeChild("HeavyAura", OneShotSpriteEffect.SoftDotSprite(), new Color(1f, 0.32f, 1f, 0.55f), 0, 1.6f); // 赤紫(体と同じ順で、少し奥)。エステル: 明度 +15%
                if (aura != null) aura.transform.localPosition += new Vector3(0f, 0f, 0.02f);
                glint = MakeChild("HeavyGlint", OneShotSpriteEffect.SoftDotSprite(), new Color(1f, 0.55f, 0.88f, 1f), 2, 0.42f); // 武器の光(エステル: +20%)
                if (ec != null) ec.maxHp = Mathf.Max(1, Mathf.RoundToInt(ec.maxHp * Mathf.Max(0.1f, T.heavyHpMul)));
                break;
            case DecisionRole.Brute:
                if (body != null) body.color = Color.Lerp(body.color, new Color(0.62f, 0.72f, 0.92f, body.color.a), 0.45f); // 鋼の鎧
                markEdge = MakeChild("LaunchMarkEdge", ChevronSprite(), new Color(0.03f, 0.05f, 0.1f, 0.9f), 1, 0f); // 暗い縁取り(洞窟の青白い光に埋もれない = エステル)
                mark = MakeChild("LaunchMark", ChevronSprite(), new Color(0.55f, 0.95f, 1f, 0.95f), 2, 0f);
                if (markEdge != null) markEdge.transform.localScale *= 1.25f * 1.22f;
                if (mark != null) mark.transform.localScale *= 1.25f;
                if (ec != null)
                {
                    ec.airLaunchUpSpeed *= Mathf.Max(0.5f, T.bruteLaunchMul);
                    ec.hitKnockbackDistance = 0.02f; // 地上の細かい後退もほぼ無し(踏ん張る)
                }
                break;
            case DecisionRole.SkyBlocker:
                transform.localScale *= 1.5f; // 上空を「ふさぐ」形に見えるよう大きめ(当たりも一緒に)
                break;
            case DecisionRole.GroundBlocker:
                if (body != null) body.color = Color.Lerp(body.color, new Color(0.85f, 0.7f, 0.6f, body.color.a), 0.2f);
                if (ec != null && T.groundBlockersResistKnockback) ec.hitKnockbackDistance = 0.02f;
                break;
        }
    }

    SpriteRenderer MakeChild(string name, Sprite sprite, Color c, int orderOffset, float sizeOfBody)
    {
        if (sprite == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.color = c;
        sr.sortingOrder = (body != null ? body.sortingOrder : RenderOrder.Enemy) + orderOffset;
        var b = body != null ? body.bounds : new Bounds(transform.position + Vector3.up * 0.6f, Vector3.one);
        if (sizeOfBody > 0f)
        {
            float s = Mathf.Max(b.size.x, b.size.y) * sizeOfBody / Mathf.Max(0.01f, sprite.bounds.size.x);
            go.transform.localScale = Vector3.one * s / Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));
            go.transform.position = b.center;
        }
        else
        {
            go.transform.localScale = Vector3.one * 0.55f / Mathf.Max(0.01f, sprite.bounds.size.y) / Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));
            go.transform.position = new Vector3(b.center.x, b.max.y + 0.45f, 0f);
        }
        return sr;
    }

    // 被ダメージ(EnemyController.OnTriggerEnter2D から)。Brute だけ変える
    public int ModifyIncoming(int damage, PlayerAttackKind kind, bool launched, int hp)
    {
        if (role != DecisionRole.Brute) return damage;
        if (!launched)
        {
            if (kind == PlayerAttackKind.Up) { BruteLaunches++; return damage; } // 打ち上げは普通のダメージ(大きく浮く)
            BruteGroundHits++;
            if (Time.time >= nextClank) // 地上の攻撃が効かない手応え(硬い音 + 火花。下がらない = エステル)
            {
                nextClank = Time.time + 0.18f;
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossHit, 0.45f);
                var bb = body != null ? body.bounds.center : transform.position + Vector3.up * 0.8f;
                OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), bb, new Color(0.85f, 0.95f, 1f, 0.95f), 6, 0.22f, 0.18f, 0.4f, 3.5f, 1f, RenderOrder.CombatFx);
            }
            return Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Clamp(T.bruteGroundDamageMul, 0.01f, 1f)));
        }
        airHits++;
        if (airHits >= Mathf.Max(1, T.bruteAirHitsToFling)) { BruteFlings++; return Mathf.Max(damage, hp); } // 大きく吹き飛ぶ(撃破の FINISH)
        return Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Max(1f, T.bruteAirDamageMul)));
    }

    void LateUpdate()
    {
        if (ec == null) return;
        phase += Time.deltaTime;
        if (aura != null)
        {
            float p = 0.5f + 0.5f * Mathf.Sin(phase * 5f);
            var c = aura.color; c.a = Mathf.Lerp(0.33f, 0.69f, p); aura.color = c;
            if (body != null) aura.transform.position = body.bounds.center;
        }
        if (glint != null && body != null)
        {
            float g = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(phase * 7f + 1f), 3f);
            var c = glint.color; c.a = ec.CanDealContactDamage ? Mathf.Lerp(0.2f, 1f, g) : 0f; glint.color = c;
            var bb = body.bounds;
            glint.transform.position = new Vector3(bb.center.x - bb.extents.x * 0.45f * Mathf.Sign(transform.lossyScale.x), bb.center.y + bb.extents.y * 0.35f, -0.01f);
        }
        if (mark != null)
        {
            float p = 0.5f + 0.5f * Mathf.Sin(phase * 6f);
            var c = mark.color; c.a = ec.CanDealContactDamage ? 0.95f : 0f; mark.color = c; // 点滅させない(エステル)
            if (body != null) mark.transform.position = new Vector3(body.bounds.center.x, body.bounds.max.y + 0.5f + 0.05f * p, 0f); // 頭の上、少し離す
            if (markEdge != null) { markEdge.transform.position = mark.transform.position + new Vector3(0f, 0f, 0.01f); var ce = markEdge.color; ce.a = c.a * 0.9f; markEdge.color = ce; }
        }
        // 上空封鎖: 決めた高さに留まる(攻撃で浮いた/倒れている間は普通の動き)
        if (role == DecisionRole.SkyBlocker && ec.CanDealContactDamage && ec.AerialState == EnemyController.EnemyAerialState.Grounded)
        {
            var target = new Vector3((float)(anchorLogicalX - FloatingOrigin.Offset), anchorY + 0.18f * Mathf.Sin(phase * 2.2f), transform.position.z);
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }

    // 上向きの印(^ を2つ重ねた形)
    static Sprite chevron;
    public static Sprite ChevronSprite()
    {
        if (chevron != null) return chevron;
        const int W = 32, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W - 0.5f, v = (y + 0.5f) / H;
                float a = 0f;
                foreach (float top in new[] { 0.95f, 0.55f })
                {
                    float d = Mathf.Abs((top - v) - Mathf.Abs(u) * 0.9f); // ^ の線
                    if (v < top && v > top - 0.45f) a = Mathf.Max(a, Mathf.Clamp01(1f - d / 0.08f));
                }
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px); tex.Apply();
        chevron = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32f);
        return chevron;
    }
}
