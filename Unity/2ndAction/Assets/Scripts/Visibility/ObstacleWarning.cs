using System.Collections.Generic;
using UnityEngine;

// 障害物の警告(2026-10-11、実機の要望「接触する障害物が背景/地形に紛れて分かりづらい」)。
//  ・対象: 当たると被害のある障害物(ObstacleController: 石/木/壁/大岩/ラスダンの落下物)と洞窟の天井の針。
//    背景の飾り(DecorationScatter 等、当たり判定なし)は対象にしない(ObstacleController.All と洞窟の針だけを見る)
//  ・近づいた物だけ: 速さに合わせた先読みの距離(LookAheadSeconds 秒ぶん、最小/最大あり)の中で、近い順に最大 MaxWarned 個。
//    通り過ぎた物/壊れた物/当たり判定が無くなった物はすぐ消す。落下物は着地するまで(既に専用の光の柱がある)出さない
//  ・見た目: 絵の外側だけの赤い縁(SpriteRim と同じ形、塗りつぶさない)+ 小さな「!」(色だけに頼らない)。
//    点滅はゆっくり(約0.8秒周期、明るさ 55〜100%)。白い閃光は使わない。設定で「輪郭のみ」(点滅なし)を選べる
//  ・暗い洞窟でも見えるよう、縁は暗さの幕(CaveLighting、sortingOrder 100)より手前に描く(縁は絵の外側だけなので絵を隠さない)
//  ・破壊/被弾の演出(白く光る/赤く点滅)と区別するため、色は赤の縁と「!」の札だけ(絵の色は変えない)
public class ObstacleWarning : MonoBehaviour
{
    public static ObstacleWarning Instance { get; private set; }
    public const float LookAheadSeconds = 1.6f, MinAhead = 14f, MaxAhead = 110f;
    public const int MaxWarned = 4;
    public static int WarnedNow { get; private set; }   // 確認用
    public static int WarnedTotal { get; private set; }

    static readonly int MainTexId = Shader.PropertyToID("_MainTex"), UvRectId = Shader.PropertyToID("_UvRect"),
        Step1Id = Shader.PropertyToID("_Step1"), Step2Id = Shader.PropertyToID("_Step2"),
        DarkId = Shader.PropertyToID("_DarkColor"), LightId = Shader.PropertyToID("_LightColor"), AlphaId = Shader.PropertyToID("_Alpha");
    static readonly Color InnerRed = new Color(0.42f, 0.02f, 0.02f), OuterRed = new Color(1f, 0.22f, 0.16f);

    Material rimMat;
    MaterialPropertyBlock mpb;

    class Mark
    {
        public SpriteRenderer sr; public MeshRenderer rim; public MeshFilter mf; public Mesh mesh;
        public Sprite builtFor; public float builtPad = -1f; public Vector2 uvMin, uvMax, uvPerLocal;
        public bool active; public float since;
    }
    readonly Dictionary<SpriteRenderer, Mark> marks = new Dictionary<SpriteRenderer, Mark>();
    readonly List<(SpriteRenderer sr, float dx)> candidates = new List<(SpriteRenderer, float)>();
    readonly HashSet<SpriteRenderer> want = new HashSet<SpriteRenderer>();
    readonly List<SpriteRenderer> drop = new List<SpriteRenderer>();
    readonly List<Vector3> bangs = new List<Vector3>(); // 「!」を出す位置(ワールド、絵の上)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("[ObstacleWarning]");
        DontDestroyOnLoad(go);
        go.AddComponent<ObstacleWarning>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        mpb = new MaterialPropertyBlock();
        var rm = Resources.Load<Material>("Effects/SpriteRim");
        if (rm != null) rimMat = new Material(rm);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        var gm = GameManager.Instance; var pc = PlayerController.Instance;
        bool running = gm != null && gm.HasStarted && !gm.IsGameOver && pc != null && !gm.SprintActive && GameSettings.ObstacleWarn != 0;
        candidates.Clear(); want.Clear(); bangs.Clear();
        if (running)
        {
            float px = pc.transform.position.x, py = pc.transform.position.y;
            float ahead = Mathf.Clamp(pc.CurrentAutoRunSpeed * LookAheadSeconds, MinAhead, MaxAhead);
            foreach (var oc in ObstacleController.All)
            {
                if (oc == null || oc.Broken || !oc.isActiveAndEnabled || oc.Visual == null) continue;
                var fd = oc.GetComponent<FallingDebris>();
                if (fd != null && !fd.IsLanded) continue; // 落下中は専用の光の柱と影が出ている
                float dx = oc.transform.position.x - px;
                if (dx < -0.5f || dx > ahead) continue;
                if (Mathf.Abs(oc.transform.position.y - py) > 9f) continue; // 別の段(上/下のルート)の遠い物は出さない
                if (!AnyColliderOn(oc)) continue;
                candidates.Add((oc.Visual, dx));
            }
            var cave = TerrainManager.Instance != null ? TerrainManager.Instance.cave : null;
            if (cave != null && cave.Active)
                foreach (var sr in cave.SpikeArt)
                {
                    if (sr == null || !sr.gameObject.activeInHierarchy) continue;
                    float dx = sr.transform.position.x - px;
                    if (dx < -0.5f || dx > ahead) continue;
                    candidates.Add((sr, dx));
                }
            candidates.Sort((a, b) => a.dx.CompareTo(b.dx));
            for (int i = 0; i < candidates.Count && want.Count < MaxWarned; i++) want.Add(candidates[i].sr);
        }
        WarnedNow = want.Count;

        // 外れた物の縁を消す(壊れた/通り過ぎた物はすぐ)
        drop.Clear();
        foreach (var kv in marks)
        {
            if (kv.Key == null) { if (kv.Value.rim != null) Destroy(kv.Value.rim.gameObject); drop.Add(kv.Key); continue; }
            if (!want.Contains(kv.Key) && kv.Value.active) { kv.Value.active = false; if (kv.Value.rim != null) kv.Value.rim.enabled = false; }
        }
        foreach (var k in drop) marks.Remove(k);
        if (marks.Count > 64) PruneInactive();

        var cam = ActiveCamera();
        bool blink = GameSettings.ObstacleWarn == 1;
        float now = Time.time;
        foreach (var sr in want)
        {
            if (!marks.TryGetValue(sr, out var m)) { m = Bind(sr); if (m == null) continue; marks[sr] = m; }
            if (!m.active) { m.active = true; m.since = now; WarnedTotal++; }
            float pulse = blink ? Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Cos((now - m.since) * Mathf.PI * 2f / 0.8f)) : 0.95f;
            float fadeIn = Mathf.Clamp01((now - m.since) / 0.15f);
            UpdateRim(m, cam, pulse * fadeIn);
            var b = sr.bounds;
            bool ceiling = sr.name == "CeilingSpike";
            bangs.Add(ceiling ? new Vector3(b.center.x, b.min.y - 0.35f, 0f) : new Vector3(b.center.x, b.max.y + 0.45f, 0f));
        }
    }

    static bool AnyColliderOn(ObstacleController oc)
    {
        foreach (var c in oc.GetComponentsInChildren<Collider2D>()) if (c != null && c.enabled) return true;
        return false;
    }

    void PruneInactive()
    {
        drop.Clear();
        foreach (var kv in marks) if (!kv.Value.active) { if (kv.Value.rim != null) Destroy(kv.Value.rim.gameObject); drop.Add(kv.Key); }
        foreach (var k in drop) marks.Remove(k);
    }

    static Camera ActiveCamera()
    {
        var rig = PortraitCameraRig.Instance;
        if (rig != null && rig.IsActive && rig.cam != null) return rig.cam;
        return Camera.main;
    }

    Mark Bind(SpriteRenderer sr)
    {
        if (rimMat == null || sr == null) return null;
        var go = new GameObject("HazardRim");
        go.transform.SetParent(sr.transform, false);
        var m = new Mark { sr = sr };
        m.mf = go.AddComponent<MeshFilter>();
        m.mesh = new Mesh { name = "HazardRimQuad" };
        m.mf.sharedMesh = m.mesh;
        m.rim = go.AddComponent<MeshRenderer>();
        m.rim.sharedMaterial = rimMat;
        m.rim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m.rim.receiveShadows = false;
        return m;
    }

    const float Ring1Px = 2.0f, Ring2Px = 4.4f; // 画面の高さ 720px あたりの太さ
    static readonly int[] Tris = { 0, 2, 1, 2, 3, 1 };
    readonly Vector3[] verts = new Vector3[4];
    readonly Vector2[] uvs = new Vector2[4];

    void UpdateRim(Mark m, Camera cam, float alpha)
    {
        var sr = m.sr; Sprite s = sr.sprite;
        bool show = s != null && sr.enabled && sr.gameObject.activeInHierarchy && cam != null;
        m.rim.enabled = show;
        if (!show) return;
        float worldPerPx720 = (cam.orthographic ? cam.orthographicSize * 2f : 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(1f, Vector3.Distance(cam.transform.position, sr.transform.position))) / 720f;
        Vector3 ls = sr.transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(ls.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ls.y));
        float r2w = Ring2Px * worldPerPx720;
        float pad = r2w * 1.3f;
        if (s != m.builtFor || Mathf.Abs(pad - m.builtPad) > pad * 0.05f)
        {
            Vector2 vMin = new Vector2(float.MaxValue, float.MaxValue), vMax = new Vector2(float.MinValue, float.MinValue);
            Vector2 uvMin = vMin, uvMax = vMax;
            var sv = s.vertices; var su = s.uv;
            for (int i = 0; i < sv.Length; i++) { vMin = Vector2.Min(vMin, sv[i]); vMax = Vector2.Max(vMax, sv[i]); uvMin = Vector2.Min(uvMin, su[i]); uvMax = Vector2.Max(uvMax, su[i]); }
            Vector2 size = vMax - vMin;
            m.uvPerLocal = new Vector2((uvMax.x - uvMin.x) / Mathf.Max(0.0001f, size.x), (uvMax.y - uvMin.y) / Mathf.Max(0.0001f, size.y));
            m.uvMin = uvMin; m.uvMax = uvMax;
            float padX = pad / sx, padY = pad / sy;
            float x0 = vMin.x - padX, x1 = vMax.x + padX, y0 = vMin.y - padY, y1 = vMax.y + padY;
            verts[0] = new Vector3(x0, y0, 0f); verts[1] = new Vector3(x1, y0, 0f); verts[2] = new Vector3(x0, y1, 0f); verts[3] = new Vector3(x1, y1, 0f);
            for (int i = 0; i < 4; i++) uvs[i] = new Vector2(uvMin.x + (verts[i].x - vMin.x) * m.uvPerLocal.x, uvMin.y + (verts[i].y - vMin.y) * m.uvPerLocal.y);
            m.mesh.Clear(); m.mesh.vertices = verts; m.mesh.uv = uvs; m.mesh.triangles = Tris; m.mesh.RecalculateBounds();
            m.builtFor = s; m.builtPad = pad;
        }
        m.rim.transform.localScale = new Vector3(sr.flipX ? -1f : 1f, sr.flipY ? -1f : 1f, 1f);
        m.rim.sortingLayerID = sr.sortingLayerID;
        // 洞窟の暗さの幕(100)より手前。縁は絵の外側だけなので絵そのものは隠さない
        m.rim.sortingOrder = CaveLighting.DarknessOn ? 101 : sr.sortingOrder + 1;
        float r1w = Ring1Px * worldPerPx720;
        mpb.Clear();
        mpb.SetTexture(MainTexId, s.texture);
        mpb.SetVector(UvRectId, new Vector4(m.uvMin.x, m.uvMin.y, m.uvMax.x, m.uvMax.y));
        mpb.SetVector(Step1Id, new Vector4(r1w / sx * m.uvPerLocal.x, r1w / sy * m.uvPerLocal.y, 0f, 0f));
        mpb.SetVector(Step2Id, new Vector4(r2w / sx * m.uvPerLocal.x, r2w / sy * m.uvPerLocal.y, 0f, 0f));
        mpb.SetColor(DarkId, new Color(InnerRed.r, InnerRed.g, InnerRed.b, 0.9f));
        mpb.SetColor(LightId, new Color(OuterRed.r, OuterRed.g, OuterRed.b, 1f));
        mpb.SetFloat(AlphaId, alpha * sr.color.a);
        m.rim.SetPropertyBlock(mpb);
    }

    // ===== 「!」の札(IMGUI、HUD の後ろ) =====
    static Texture2D white;
    static Texture2D White() { if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; } return white; }
    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || bangs.Count == 0) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        var cam = ActiveCamera();
        if (cam == null) return;
        GUI.depth = 5; // HUD(既定 0)より後ろ
        float k = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, 1f, 2f);
        float s = 22f * k;
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17f * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        Color keep = GUI.color;
        bool blink = GameSettings.ObstacleWarn == 1;
        float a = blink ? Mathf.Lerp(0.7f, 1f, 0.5f + 0.5f * Mathf.Cos(Time.time * Mathf.PI * 2f / 0.8f)) : 1f;
        foreach (var w in bangs)
        {
            Vector3 sp = cam.WorldToScreenPoint(w);
            if (sp.z <= 0f || sp.x < -s || sp.x > Screen.width + s || sp.y < -s || sp.y > Screen.height + s) continue;
            Rect r = new Rect(sp.x - s * 0.5f, Screen.height - sp.y - s * 0.5f, s, s);
            // 暗い縁 + 赤い札 + 白い「!」
            GUI.color = new Color(0.15f, 0f, 0f, 0.85f * a); GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), White());
            GUI.color = new Color(0.86f, 0.12f, 0.1f, a); GUI.DrawTexture(r, White());
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(r, "!", st);
        }
        GUI.color = keep;
    }
}
