using System.Collections.Generic;
using UnityEngine;

// エンドロールの最後: 「ONE MORE MILE?」とYES/NOの巨大な石の文字(2026-09-30)。
// 通常のUIボタンは使わず、プレイヤーがキャラクターを操作して(攻撃して)選ぶ:
//  ・YESは進む先(右)、NOは来た道(左)。プレイヤーは真ん中で止まって立っている(自動前進なし・敵なし・時間制限なし)
//  ・前攻撃でYES、後ろ攻撃でNOに当たる(飛び道具のキャラも同じ)。どちらも途中まで壊してよい(考え直せる)
//  ・HPは攻撃の回数(攻撃力は無関係)。当てるたびに揺れ、ひびが入り(3段階)、欠けらが散り、傾いていく
//  ・先にHP0になった方が答え。確定した瞬間にもう一方は攻撃を受け付けなくなる(薄くなる)
public class OneMoreMileChoice : MonoBehaviour
{
    public ChoiceWord Yes { get; private set; }
    public ChoiceWord No { get; private set; }
    public bool Decided { get; private set; }
    public bool ChoseYes { get; private set; }
    public System.Action<bool> Chosen;
    public readonly List<CreditLetter> QuestionLetters = new List<CreditLetter>();
    float riseT;
    public bool Risen => riseT >= 1.4f;

    public static OneMoreMileChoice Build(float playerX, float groundY, StaffCreditsData data)
    {
        var go = new GameObject("[OneMoreMileChoice]");
        var c = go.AddComponent<OneMoreMileChoice>();
        c.Construct(playerX, groundY, data);
        return c;
    }

    void Construct(float px, float gy, StaffCreditsData data)
    {
        // 問いかけ(頭上に浮かぶ。攻撃すると揺れるだけ)
        float qCap = 1.05f;
        float qW = GlyphFont.Measure(data.question) * qCap;
        float x = px - qW * 0.5f;
        foreach (char ch in data.question)
        {
            var sp = GlyphFont.Get(ch);
            if (sp == null) { x += GlyphFont.SpaceWidth * qCap; continue; }
            float w = sp.bounds.size.x * qCap;
            var l = CreditLetter.Create(transform, ch, new Vector3(x + w * 0.5f, gy + 4.6f, 0f), qCap, CreditsRoad.LetterOrder, false);
            if (l != null) { l.platform = false; l.bobAmplitude = 0.1f; l.SetTint(new Color(1f, 1f, 1f, 0f)); QuestionLetters.Add(l); }
            x += w + GlyphFont.Tracking * qCap;
        }
        // YES = 右(進む先)、NO = 左(来た道)。前/後ろの攻撃がちょうど届く間合い
        float cap = 1.8f;
        Yes = ChoiceWord.Build(transform, data.yesText, px + 0.95f, gy, cap, true, data.choiceHp);
        No = ChoiceWord.Build(transform, data.noText, px - 0.95f, gy, cap, false, data.choiceHp);
        Yes.Owner = this; No.Owner = this;
    }

    void Update()
    {
        // 地面の下からせり上がる(先に問いかけが浮かび、YES/NOが続く)
        if (riseT < 1.4f)
        {
            riseT += Time.deltaTime;
            float q = Mathf.Clamp01(riseT / 0.9f);
            foreach (var l in QuestionLetters) if (l != null && !l.Broken) l.SetTint(new Color(1f, 1f, 1f, q));
            float r = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((riseT - 0.3f) / 1.1f));
            Yes.SetRise(r); No.SetRise(r);
        }
    }

    public void Decide(ChoiceWord w)
    {
        if (Decided) return;
        Decided = true;
        ChoseYes = w.IsYes;
        var other = w.IsYes ? No : Yes;
        other.Lock();
        Debug.Log($"[LastDungeon][Choice] {(ChoseYes ? "YES" : "NO")} broken (hits YES {Yes.Hits}/{Yes.MaxHp}, NO {No.Hits}/{No.MaxHp})");
        if (ChoseYes)
        {
            Yes.ShatterAll(1f);          // YESが派手に砕ける
            No.SinkAll(3.2f, 1.6f);      // NOは崩れて地面へ沈み、消える
        }
        else
        {
            No.ShatterAll(-1f);          // NOが崩れる
            Yes.SinkAll(3.2f, 1.8f);     // YESは沈んで消える(前へ進む道が開くが、走り続けるのはほんの少しだけ)
        }
        foreach (var l in QuestionLetters) if (l != null && !l.Broken) l.Sink(-2.5f, 2.2f); // 問いかけは空へ昇って消える
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(ChoseYes ? 0.22f : 0.12f, 0.35f);
        Chosen?.Invoke(ChoseYes);
    }
}

// YES/NOの1語(石の文字のまとまり)。壁(プレイヤーは手前で止まる)であり、攻撃の回数でHPが減る。
public class ChoiceWord : MonoBehaviour, IWorldSolid
{
    public bool IsYes { get; private set; }
    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public int Hits => MaxHp - Hp;
    public bool Locked { get; private set; }
    public bool Gone { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public OneMoreMileChoice Owner;
    public readonly List<CreditLetter> Letters = new List<CreditLetter>();
    readonly List<Vector3> letterBase = new List<Vector3>();
    readonly List<float> letterTilt = new List<float>();
    BoxCollider2D col;
    float rise, groundY, innerX;
    int crack;
    readonly Collider2D[] hitCols = new Collider2D[6];
    readonly int[] hitSwings = new int[6];
    int hitNext;
    public static int TotalHits, LockedHitsIgnored;

    // innerX: プレイヤー側の面のX(YESなら左端、NOなら右端)
    public static ChoiceWord Build(Transform parent, string text, float innerX, float groundY, float cap, bool yes, int hp)
    {
        var go = new GameObject(yes ? "ChoiceYES" : "ChoiceNO");
        go.transform.SetParent(parent, false);
        var w = go.AddComponent<ChoiceWord>();
        w.IsYes = yes;
        w.MaxHp = w.Hp = Mathf.Max(1, hp);
        w.groundY = groundY;
        w.innerX = innerX;
        w.Width = GlyphFont.Measure(text) * cap + 0.3f;
        w.Height = cap * 1.15f;
        float left = yes ? innerX : innerX - w.Width;
        go.transform.position = new Vector3(left + w.Width * 0.5f, groundY, 0f);
        float x = left + 0.15f;
        foreach (char c in text)
        {
            var sp = GlyphFont.Get(c);
            if (sp == null) { x += GlyphFont.SpaceWidth * cap; continue; }
            float cw = sp.bounds.size.x * cap;
            var l = CreditLetter.Create(go.transform, c, new Vector3(x + cw * 0.5f, groundY, 0f), cap, CreditsRoad.LetterOrder, true);
            if (l != null)
            {
                l.platform = false;
                l.HitRouter = (letter, atk) => { w.OnAttack(atk); return true; };
                w.Letters.Add(l);
                w.letterBase.Add(l.transform.position);
                w.letterTilt.Add(0f);
            }
            x += cw + GlyphFont.Tracking * cap;
        }
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        w.col = go.AddComponent<BoxCollider2D>();
        w.col.isTrigger = true;
        w.col.size = new Vector2(w.Width, w.Height);
        w.col.offset = new Vector2(0f, w.Height * 0.5f);
        w.SetRise(0f);
        WorldPlatforms.Register(w);
        return w;
    }

    void OnDestroy() { WorldPlatforms.Unregister(this); }

    // せり上がり(0=地面の下、1=地上)
    public void SetRise(float r)
    {
        rise = r;
        for (int i = 0; i < Letters.Count; i++)
        {
            var l = Letters[i];
            if (l == null || l.Broken) continue;
            Vector3 p = l.transform.position;
            p.y = letterBase[i].y - (1f - r) * (Height + 0.4f);
            l.transform.position = p;
        }
        if (col != null) col.enabled = r >= 0.95f && !Gone;
    }

    public bool SolidActive => !Gone && isActiveAndEnabled && rise > 0.5f;
    public bool IsPlatform => false;
    public bool IsWall => true;
    public Rect SolidRect
    {
        get
        {
            Vector3 p = transform.position;
            return Rect.MinMaxRect(p.x - Width * 0.5f, p.y, p.x + Width * 0.5f, p.y + Height);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("PlayerAttack")) OnAttack(other);
    }

    public void OnAttack(Collider2D atk)
    {
        if (Gone || atk == null || rise < 0.95f) return;
        var info = atk.GetComponent<PlayerAttackInfo>();
        int swing = info != null ? info.SwingId : 0;
        for (int i = 0; i < hitCols.Length; i++) if (hitCols[i] == atk && hitSwings[i] == swing) return; // 同じ振りでは1回
        hitCols[hitNext] = atk; hitSwings[hitNext] = swing; hitNext = (hitNext + 1) % hitCols.Length;
        Vector3 at = atk.bounds.ClosestPoint(transform.position + new Vector3(0f, Height * 0.5f, 0f));
        if (Locked)
        {
            // 答えが決まった後は受け付けない(鈍い音と小さな火花だけ)
            LockedHitsIgnored++;
            OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), at, new Color(0.6f, 0.6f, 0.65f, 0.7f), 3, 0.25f, 0.1f, 0.25f, 2f, 1f, RenderOrder.CombatFx);
            return;
        }
        if (Owner != null && Owner.Decided) return;
        TotalHits++;
        Hp = Mathf.Max(0, Hp - 1);
        float dmg = 1f - (float)Hp / MaxHp;
        // ひび(3段階)と傾き/欠けら
        int stage = dmg >= 0.75f ? 3 : dmg >= 0.5f ? 2 : dmg >= 0.2f ? 1 : 0;
        if (stage != crack)
        {
            crack = stage;
            for (int i = 0; i < Letters.Count; i++)
            {
                Letters[i].SetCrackStage(stage);
                letterTilt[i] = (Random.value < 0.5f ? -1f : 1f) * stage * Random.Range(1.5f, 3.5f);
            }
            // 欠け: 大きな欠けらが落ちる
            OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), at, new Color(0.55f, 0.4f, 0.2f, 1f), 6 + stage * 3, 0.7f, 0.25f, 0.55f, 5f, 3f, RenderOrder.CombatFx);
        }
        foreach (var l in Letters) l.Kick(0.55f + 0.25f * stage);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), at, new Color(1f, 0.85f, 0.45f, 1f), 8, 0.4f, 0.15f, 0.45f, 5f, 1.5f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(stage >= 2 ? SeId.StrongHit : SeId.Hit, 0.8f);
        var cf = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cf != null) cf.Shake(0.04f + 0.02f * stage, 0.1f);
        if (Hp <= 0 && Owner != null) Owner.Decide(this);
    }

    public void Lock()
    {
        Locked = true;
        foreach (var l in Letters) if (l != null && !l.Broken) l.SetTint(new Color(0.75f, 0.75f, 0.8f, 0.55f));
    }

    public void ShatterAll(float dir)
    {
        Gone = true;
        WorldPlatforms.Unregister(this);
        if (col != null) col.enabled = false;
        foreach (var l in Letters) if (l != null && !l.Broken) l.Shatter(dir * Random.Range(0.5f, 1.2f));
        Vector3 c = transform.position + new Vector3(0f, Height * 0.5f, 0f);
        OneShotSpriteEffect.CreateScatterBurst(OneShotSpriteEffect.SoftDotSprite(), c, IsYes ? new Color(1f, 0.9f, 0.5f, 1f) : new Color(0.7f, 0.65f, 0.6f, 1f), IsYes ? 40 : 22, 1f, 0.3f, 1.1f, IsYes ? 10f : 6f, 2.5f, RenderOrder.CombatFx);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.BossDefeat, IsYes ? 1f : 0.6f);
    }

    public void SinkAll(float depth, float duration)
    {
        Gone = true;
        WorldPlatforms.Unregister(this);
        if (col != null) col.enabled = false;
        foreach (var l in Letters) if (l != null && !l.Broken) l.Sink(depth, duration);
    }

    void Update()
    {
        // 傾き(ひびの段階に応じて文字が少しずつずれる)
        if (Gone) return;
        for (int i = 0; i < Letters.Count; i++)
        {
            var l = Letters[i];
            if (l == null || l.Broken) continue;
            // 文字自身の揺れは子(Glyph)が受け持つので、ここでは土台の傾きだけ
            l.transform.localRotation = Quaternion.Lerp(l.transform.localRotation, Quaternion.Euler(0f, 0f, letterTilt[i]), Time.deltaTime * 6f);
        }
    }
}
