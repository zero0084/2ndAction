using System.Collections.Generic;
using UnityEngine;

// 死神三姉妹の連携技「死の舞踏」の合図(2026-10-20): 三人の体の中心を結ぶ光の三角形。合図から全員の番が終わるまで出し、
// 番が来た姉妹の線ほど明るく太くする(長女=赤 → 次女=紫 → 三女=青白)。ゲームは止めない・当たり判定は無い
public class ReaperComboLink : MonoBehaviour
{
    readonly List<ReaperSisterBoss> sisters = new List<ReaperSisterBoss>();
    readonly List<LineRenderer> lines = new List<LineRenderer>();
    float life, age;
    public static int Shown;

    public static void Show(List<ReaperSisterBoss> who, float seconds)
    {
        var go = new GameObject("[ReaperComboLink]");
        var c = go.AddComponent<ReaperComboLink>();
        c.sisters.AddRange(who);
        c.sisters.Sort((a, b) => ((int)a.sister).CompareTo((int)b.sister));
        c.life = seconds;
        for (int i = 0; i < c.sisters.Count; i++)
        {
            var lr = new GameObject("Link" + i).AddComponent<LineRenderer>();
            lr.transform.SetParent(go.transform, false);
            lr.sharedMaterial = SharedMaterials.SpritesDefault;
            lr.positionCount = 2;
            lr.sortingOrder = RenderOrder.CombatFx - 1;
            lr.numCapVertices = 4;
            c.lines.Add(lr);
        }
        Shown++;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        int n = sisters.Count;
        float fadeIn = Mathf.Clamp01(age / 0.25f), fadeOut = Mathf.Clamp01((life - age) / 0.4f);
        for (int i = 0; i < n; i++)
        {
            var a = sisters[i]; var b = sisters[(i + 1) % n];
            var lr = lines[i];
            bool ok = a != null && b != null && !a.IsDead && !b.IsDead && a.isActiveAndEnabled && b.isActiveAndEnabled;
            lr.enabled = ok;
            if (!ok) continue;
            lr.SetPosition(0, a.CenterWorld); lr.SetPosition(1, b.CenterWorld);
            // 番(合図から 2.0秒 + 0.9秒ずつ)が来た姉妹から出る線を強く
            float turn = 2.0f + (int)a.sister * 0.9f;
            float hot = Mathf.Clamp01(1f - Mathf.Abs(age - turn) / 0.5f);
            Color ca = ReaperFx.SisterGlow(a.sister), cb = ReaperFx.SisterGlow(b.sister);
            float alpha = (0.35f + 0.5f * hot + 0.15f * Mathf.Sin(age * 18f)) * fadeIn * fadeOut;
            ca.a = alpha; cb.a = alpha * 0.8f;
            lr.startColor = ca; lr.endColor = cb;
            float w = 0.08f + 0.12f * hot;
            lr.startWidth = w; lr.endWidth = w * 0.7f;
        }
    }
}
