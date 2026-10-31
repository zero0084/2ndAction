using System.Collections.Generic;
using UnityEngine;

// ボスの HP / BREAK の欄(2026-10-31、UI 全体の改善)。複数のボスで頭の上の HP の帯が重なり、どの敵の HP か分からなかった →
//  画面上の専用の欄に1体1行でまとめる(1本のゲージにまとめない)。横画面 = 中央上の Lv の下 / 縦画面 = 画面の下(地面の上)。
//  ・行: ボスの姿の小さな絵(識別) + 番号(2体以上の時) + HP の帯(段階の境目の目盛り) + BREAK の帯(BREAK 中は点滅)
//  ・2体以上の時は、ボスの頭の上にも同じ色の番号の印(欄の行と結び付ける)
//  ・値は DragonHealthBar(各ボスが今までどおり更新する)を読むだけ。撃破の演出で帯が消え始めたら行も消える
//  ・表示の設定: ボスの HP(HudPrefs.Item.BossHp)/ BREAK(BreakGauge)。改修前の HUD(-hudLegacy)では出さない(頭の上の帯)
public class BossHudPanel : MonoBehaviour
{
    public static BossHudPanel Instance { get; private set; }
    public const int MaxRows = 4;
    static readonly Color[] RowColors =
    {
        new Color(1f, 0.82f, 0.32f), new Color(0.45f, 0.85f, 1f), new Color(1f, 0.5f, 0.82f), new Color(0.55f, 1f, 0.55f),
    };
    public static Color RowColor(int i) => RowColors[Mathf.Clamp(i, 0, RowColors.Length - 1)];

    // 横画面で欄の下端(実際の画素。出ていない時は 0)。中央の知らせ(N HIT/BREAK!)はこの下へ
    public static float VisibleBottomPx { get; private set; }
    public static int LastRows { get; private set; }
    public static Rect LastRect { get; private set; }

    readonly List<DragonHealthBar> bars = new List<DragonHealthBar>();
    readonly Dictionary<DragonHealthBar, SpriteRenderer> iconSr = new Dictionary<DragonHealthBar, SpriteRenderer>();
    GUIStyle badge;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[BossHudPanel]");
        DontDestroyOnLoad(go);
        go.AddComponent<BossHudPanel>();
    }
    void Awake() { Instance = this; }

    // 今の欄に並ぶボス(先に出たボスが上)
    public static void Collect(List<DragonHealthBar> into)
    {
        into.Clear();
        foreach (var b in DragonHealthBar.All)
        {
            if (b == null || b.target == null || !b.isActiveAndEnabled || !b.target.gameObject.activeInHierarchy) continue;
            if (!b.Revealed) continue;
            into.Add(b);
        }
        into.Sort((a, c) => a.Serial.CompareTo(c.Serial));
        if (into.Count > MaxRows) into.RemoveRange(MaxRows, into.Count - MaxRows);
    }

    void LateUpdate() { if (!Visible()) VisibleBottomPx = 0f; }

    bool Visible()
    {
        var gm = GameManager.Instance;
        if (HudPrefs.Legacy || gm == null || !gm.HasStarted || gm.IsGameOver || gm.RunScreenOpen) return false;
        if (!HudPrefs.Show(HudPrefs.Item.BossHp) && !HudPrefs.Show(HudPrefs.Item.BreakGauge)) return false;
        return true;
    }

    void OnGUI()
    {
        if (!Visible()) { VisibleBottomPx = 0f; LastRows = 0; return; }
        Collect(bars);
        LastRows = bars.Count;
        if (bars.Count == 0) { VisibleBottomPx = 0f; return; }
        GUI.depth = 5; // HUD(GameManager)より奥、カードの3択などより奥
        bool hp = HudPrefs.Show(HudPrefs.Item.BossHp), brk = HudPrefs.Show(HudPrefs.Item.BreakGauge);
        float k = GameManager.HudK;
        float rowH = (hp && brk ? 30f : 22f) * k, pad = 5f * k;
        float h = pad * 2f + rowH * bars.Count + 3f * k * (bars.Count - 1);
        Rect slot = GameManager.BossPanelSlotPx(h);
        if (slot.width <= 0f) return;
        LastRect = slot;
        VisibleBottomPx = GameManager.HudStacked ? 0f : slot.yMax;

        // 板は設計の単位で描く(枠の線の太さを HUD とそろえる)
        Matrix4x4 keepM = GUI.matrix;
        GUI.matrix = keepM * Matrix4x4.Scale(new Vector3(k, k, 1f));
        Rect r = new Rect(slot.x / k, slot.y / k, slot.width / k, slot.height / k);
        HudStyle.Panel(r);
        bool multi = bars.Count >= 2;
        float y = r.y + pad / k;
        for (int i = 0; i < bars.Count; i++)
        {
            DrawRow(bars[i], i, new Rect(r.x + 8f, y, r.width - 16f, rowH / k), multi, hp, brk);
            y += rowH / k + 3f;
        }
        GUI.matrix = keepM;
        if (multi) for (int i = 0; i < bars.Count; i++) DrawWorldMarker(bars[i], i, k);
    }

    void DrawRow(DragonHealthBar b, int index, Rect r, bool multi, bool hp, bool brk)
    {
        Color col = RowColor(index);
        float icon = r.height;
        // ボスの姿(識別用)
        Rect ir = new Rect(r.x, r.y, icon, icon);
        DrawBossIcon(b, ir);
        float x = ir.xMax + 4f;
        if (multi)
        {
            if (badge == null) badge = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            badge.fontSize = Mathf.RoundToInt(Mathf.Min(16f, r.height * 0.6f));
            Rect br = new Rect(x, r.y + (r.height - 18f) * 0.5f, 18f, 18f);
            HudStyle.Text(br, (index + 1).ToString(), badge, col);
            x = br.xMax + 2f;
        }
        float bw = r.xMax - x;
        bool broken = b.SubBroken && b.SubEnabled;
        if (hp)
        {
            float bh = brk && b.SubEnabled ? Mathf.Min(12f, r.height * 0.45f) : Mathf.Min(12f, r.height * 0.55f);
            Rect hr = new Rect(x, brk && b.SubEnabled ? r.y + r.height * 0.18f : r.y + (r.height - bh) * 0.5f, bw, bh);
            bool flash = Time.time < b.FlashUntil;
            Color fill = flash ? Color.Lerp(new Color(0.9f, 0.18f, 0.18f), Color.white, Mathf.PingPong(Time.time * 10f, 1f)) : new Color(0.9f, 0.18f, 0.18f);
            HudStyle.Bar(hr, b.Fraction, fill);
            if (b.PhaseThresholds != null)
            {
                Color keep = GUI.color;
                GUI.color = new Color(1f, 0.95f, 0.75f, 0.85f * keep.a);
                foreach (float th in b.PhaseThresholds) GUI.DrawTexture(new Rect(hr.x + hr.width * Mathf.Clamp01(th) - 0.5f, hr.y - 1f, 1.5f, hr.height + 2f), Texture2D.whiteTexture);
                GUI.color = keep;
            }
            // 行の色の細い印(左端): 頭の上の印と同じ色
            if (multi) { Color keep = GUI.color; GUI.color = new Color(col.r, col.g, col.b, keep.a); GUI.DrawTexture(new Rect(hr.x, hr.yMax + 1f, Mathf.Min(24f, hr.width), 1.5f), Texture2D.whiteTexture); GUI.color = keep; }
        }
        if (brk && b.SubEnabled)
        {
            float sh = 5f;
            Rect sr = new Rect(x, hp ? r.yMax - sh - r.height * 0.12f : r.y + (r.height - sh) * 0.5f, bw, sh);
            Color sc = broken ? Color.Lerp(Color.white, new Color(1f, 0.6f, 0.2f), Mathf.PingPong(Time.time * 6f, 1f))
                              : Color.Lerp(new Color(1f, 0.82f, 0.25f), new Color(1f, 0.35f, 0.2f), b.SubFraction);
            HudStyle.Bar(sr, b.SubFraction, sc, 0.6f);
            if (broken)
            {
                var st = HudStyle.ValueStyle(11, TextAnchor.MiddleRight);
                HudStyle.Text(new Rect(x, sr.y - 13f, bw, 12f), "BREAK!", st, new Color(1f, 0.85f, 0.4f));
            }
        }
    }

    // ボスの今の絵を小さな四角に(縦横比のまま、下揃え)。絵が見つからない時は色の丸
    void DrawBossIcon(DragonHealthBar b, Rect r)
    {
        if (!iconSr.TryGetValue(b, out var sr) || sr == null)
        {
            sr = null; float best = 0f;
            foreach (var s in b.target.GetComponentsInChildren<SpriteRenderer>())
            {
                if (s == null || s.sprite == null) continue;
                float a = s.bounds.size.x * s.bounds.size.y;
                if (a > best) { best = a; sr = s; }
            }
            iconSr[b] = sr;
        }
        Color keep = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f * keep.a);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = keep;
        if (sr == null || sr.sprite == null || sr.sprite.texture == null) return;
        var sp = sr.sprite;
        Rect tr = sp.textureRect;
        Texture tex = sp.texture;
        float aspect = tr.width / Mathf.Max(1f, tr.height);
        float w = r.width, h = r.height;
        if (aspect > 1f) h = w / aspect; else w = h * aspect;
        Rect dst = new Rect(r.x + (r.width - w) * 0.5f, r.yMax - h, w, h);
        Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
        bool flip = sr.flipX ^ (sr.transform.lossyScale.x < 0f);
        if (flip) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
        GUI.DrawTextureWithTexCoords(dst, tex, uv, true);
    }

    // 2体以上: ボスの頭の上(今まで HP の帯があった所)に、欄の行と同じ色の番号
    void DrawWorldMarker(DragonHealthBar b, int index, float k)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(b.transform.position);
        if (sp.z < 0f) return;
        float x = sp.x, y = Screen.height - sp.y;
        if (x < -20f || x > Screen.width + 20f || y < -20f || y > Screen.height + 20f) return;
        float s = 20f * k;
        Rect rr = new Rect(x - s * 0.5f, y - s * 0.5f, s, s);
        Color col = RowColor(index);
        Color keep = GUI.color;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, rr.center);
        GUI.color = new Color(0f, 0f, 0f, 0.6f * keep.a);
        GUI.DrawTexture(new Rect(rr.x - 2f, rr.y - 2f, rr.width + 4f, rr.height + 4f), Texture2D.whiteTexture);
        GUI.color = new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.92f * keep.a);
        GUI.DrawTexture(rr, Texture2D.whiteTexture);
        GUI.color = new Color(col.r, col.g, col.b, keep.a);
        GUI.DrawTexture(new Rect(rr.x, rr.y, rr.width, 1.5f * k), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rr.x, rr.yMax - 1.5f * k, rr.width, 1.5f * k), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rr.x, rr.y, 1.5f * k, rr.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rr.xMax - 1.5f * k, rr.y, 1.5f * k, rr.height), Texture2D.whiteTexture);
        GUI.matrix = m;
        GUI.color = keep;
        if (badge == null) badge = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
        badge.fontSize = Mathf.RoundToInt(13f * k);
        HudStyle.Text(rr, (index + 1).ToString(), badge, Color.white);
    }
}
