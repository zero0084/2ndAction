using UnityEngine;

// Distance Level Design Ver.1.1, item 4 - simplified from Ver.1's 4-stage
// Morning/Day/Evening/Night cycle (which "上手く機能していなかった") down
// to JUST Day <-> Night, per the brief's explicit "まずDayとNightの2状態
// だけを確実に動かしてください... 完成後にMorning/Eveningを追加する予定
// です". Still a single continuous function of distance (nightAmount),
// still stateless/re-entrant (see Apply) so a Debug Warp can never leave a
// stale time-of-day behind - "Debug Jump後に以前の時間帯が残らないよう".
//
// Blends the existing Day background (SceneBuilder's own "Background"
// GameObject/BackgroundFollower, untouched) with the Night background
// layered on top of it (own BackgroundFollower, same cover-scale auto-
// sizing, just a different sortingOrder and an Alpha this class drives) -
// a plain Cross Fade, no Evening tint any more.
//
// Deliberately does NOT touch anything gameplay-relevant (Player/Enemy/
// Projectile/pit/ground rendering, all separate SpriteRenderers with their
// own sortingOrder, are completely unaffected) - "PlayerやEnemyまで一緒に
// 暗くしない...Backgroundだけを変化させる" is satisfied structurally, not
// by tuning: this only ever adjusts the two background layers' own alpha.
public class WorldTimeCycle : MonoBehaviour
{
    public static WorldTimeCycle Instance { get; private set; }

    [Header("Schedule (item 4 - distance within one 20,000m cycle)")]
    public float cycleDistance = 20000f;
    // "0m Day / 10,000m付近 Day->Night CrossFade / 15,000m Night / 20,000m
    // 付近 Night->Day CrossFade" from the brief, expressed as 4 control
    // points: nightAmount is 0 up to nightRampStart, ramps to 1 by
    // nightFullAt, holds at 1 until dayRampStart, then ramps back to 0 by
    // cycleDistance (which wraps to 0 = Day again).
    public float nightRampStart = 10000f;
    public float nightFullAt = 15000f;
    public float dayRampStart = 18000f;

    [Header("Layers")]
    // The EXISTING background (SceneBuilder's "Background" GameObject) -
    // stays fully opaque always; Night crossfades IN on top of it, so
    // there's never a moment with both layers partially transparent at
    // once (no gap can ever show through).
    public SpriteRenderer dayLayer;
    public SpriteRenderer nightLayer;

    [Header("Debug")]
    public bool debugLogEnabled = true;

    public string CurrentTimeName { get; private set; } = "Day";
    // 高速走行の視認性補正 - 夜の度合い(0=昼〜1=夜)。雲など背景演出が夜に動きを抑えるために参照する。
    public float NightAmount { get; private set; }
    string loggedTimeName = "";

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (GameManager.Instance == null) return;
        float raw = GameManager.Instance.MaxDistance;
        // Purely a function of `d` every frame - no accumulated/persisted
        // ramp state - so a Debug Warp landing anywhere just recomputes
        // correctly next frame with nothing left over from before.
        float d = Mathf.Repeat(raw, Mathf.Max(1f, cycleDistance));
        Apply(d);
    }

    // 自然洞窟(2026-09-21) - 洞窟では昼夜の切り替え(空の背景の入れ替え)を行わない。
    // 洞窟側(CaveStage)が背景と明るさを持つので、夜レイヤーは常に透明のままにする。
    public bool forceDayOnly;

    void Apply(float d)
    {
        if (forceDayOnly)
        {
            NightAmount = 0f;
            if (nightLayer != null)
            {
                Color nc = nightLayer.color;
                nc.a = 0f;
                nightLayer.color = nc;
            }
            return;
        }
        float nightAmount = ComputeNightAmount(d);
        NightAmount = nightAmount;

        if (nightLayer != null)
        {
            Color c = nightLayer.color;
            c.a = nightAmount;
            nightLayer.color = c;
        }
        if (dayLayer != null)
        {
            dayLayer.color = Color.white; // Ver.1.1 - no Evening tint any more, always full color
        }

        string name = nightAmount >= 0.999f ? "Night" : (nightAmount <= 0.001f ? "Day" : (d < nightFullAt ? "Day->Night" : "Night->Day"));
        CurrentTimeName = name;
        if (name != loggedTimeName)
        {
            loggedTimeName = name;
            if (debugLogEnabled) Debug.Log($"[WorldTime] {name}");
        }
    }

    float ComputeNightAmount(float d)
    {
        if (d < nightRampStart) return 0f;
        if (d < nightFullAt) return Mathf.InverseLerp(nightRampStart, nightFullAt, d);
        if (d < dayRampStart) return 1f;
        if (d < cycleDistance) return 1f - Mathf.InverseLerp(dayRampStart, cycleDistance, d);
        return 0f; // at/after the wrap point itself
    }
}
