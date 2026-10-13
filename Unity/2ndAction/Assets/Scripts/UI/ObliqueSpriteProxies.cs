using System.Collections.Generic;
using UnityEngine;

// 縦画面の「斜め上から見る」表示で、立っている物(敵/ボス/障害物/弾/攻撃の光など)がカメラの方を向くようにする(2026-10-11、実機の要望)。
//  ・斜めのカメラは道を横から約77°の向きで見るので、世界の XY 平面に貼った絵はそのままだと横幅が約2割に縮んで細く見えていた
//    (プレイヤーの絵と地上の敵の絵だけは Billboard でカメラへ向けていた)。
//  ・当たり判定を持つ物(ボス等は絵と当たり判定が同じ物)を回すと判定まで傾くので、元の絵は動かさずに隠し(forceRenderingOff)、
//    同じ場所に「カメラへ向けた写し」を子として置いて毎フレーム絵/色/向き/重なり順を写す。当たり判定・攻撃範囲は変えない。
//  ・対象は描画順 1 以上の絵(RenderOrder: 敵/プレイヤー/ボス/戦闘の光/斬撃/世界の文字)。地面(0)・地面の下・背景(負)は平らのまま。
//    既に Billboard を持つ物(プレイヤー本体/地上の敵の絵)は対象外。
//  ・斜めのカメラが止まったら(縦→横の回転/表示方式の変更)写しを消して元の絵を戻す(APK 295 の「向きが残って細くなる」を再発させない)。
//  ・2026-10-13: ボスの絵は格子のセル(BossRig、Visual の下の Cell_i_j)に分かれている。セルを1枚ずつその場で向けると、
//    セルの並び(世界の横方向)だけが斜めのカメラで縮み、ボスが細い縦の帯のように重なって見えていた(メカドラゴンで発見、全ボス共通)。
//    セルは Visual からのずれもカメラへ向けて回し、ボス全体を1枚の絵としてカメラへ向ける(当たり判定は今までどおり動かさない)。
[DefaultExecutionOrder(10000)]
public class ObliqueSpriteProxies : MonoBehaviour
{
    static ObliqueSpriteProxies inst;
    public static int ProxyCount => inst != null ? inst.map.Count : 0;
    public static bool Active { get; private set; }

    class Proxy { public SpriteRenderer src, dst; public Transform pivot; }

    // ボスの格子のセル(BossRig)なら、その親(Visual)を回転の中心にする
    static Transform RigPivot(SpriteRenderer sr)
    {
        var p = sr.transform.parent;
        return p != null && sr.name.StartsWith("Cell_") && p.name == "Visual" ? p : null;
    }
    readonly Dictionary<SpriteRenderer, Proxy> map = new Dictionary<SpriteRenderer, Proxy>();
    readonly List<SpriteRenderer> dead = new List<SpriteRenderer>();
    readonly HashSet<SpriteRenderer> proxySet = new HashSet<SpriteRenderer>();
    float scanAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("[ObliqueSpriteProxies]");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<ObliqueSpriteProxies>();
    }

    static bool Eligible(SpriteRenderer sr)
    {
        if (sr == null || sr.sortingOrder >= 100) return false;
        if (sr.sortingOrder < 1 && sr.gameObject.name != "CeilingSpike") return false; // 洞窟の天井の針は描画順 -1 だが立っている危険物(障害物の警告の縁と合わせる)
        if (sr.GetComponent<Billboard>() != null) return false;          // 既にカメラへ向く(プレイヤー本体/地上の敵)
        if (sr.GetComponentInParent<Canvas>() != null) return false;     // 画面の UI
        if (sr.GetComponentInParent<Billboard>() != null) return false;  // 向く物の子(一緒に向いている)
        return true;
    }

    void LateUpdate()
    {
        var rig = PortraitCameraRig.Instance;
        bool on = rig != null && rig.IsActive && rig.cam != null;
        Active = on;
        if (!on) { if (map.Count > 0) ClearAll(); return; }
        if (Time.unscaledTime >= scanAt) { scanAt = Time.unscaledTime + 0.2f; Scan(); }
        Quaternion rot = rig.cam.transform.rotation;
        dead.Clear();
        foreach (var kv in map)
        {
            var p = kv.Value;
            if (p.src == null || p.dst == null) { if (p.dst != null) Destroy(p.dst.gameObject); dead.Add(kv.Key); continue; }
            var s = p.src; var d = p.dst;
            s.forceRenderingOff = true;
            d.enabled = s.enabled && s.gameObject.activeInHierarchy;
            if (!d.enabled) continue;
            if (d.sprite != s.sprite) d.sprite = s.sprite;
            d.color = s.color; d.flipX = s.flipX; d.flipY = s.flipY;
            d.sortingLayerID = s.sortingLayerID; d.sortingOrder = s.sortingOrder;
            if (d.sharedMaterial != s.sharedMaterial) d.sharedMaterial = s.sharedMaterial;
            if (d.drawMode != s.drawMode) d.drawMode = s.drawMode;
            if (s.drawMode != SpriteDrawMode.Simple) d.size = s.size;
            d.maskInteraction = s.maskInteraction;
            // 子にすると、縦横の倍率が違う親(槍/技の光など)の下で回した時に歪むので、写しは独立した物にして位置/大きさ/傾き(Z)を写す
            var st = s.transform;
            d.transform.position = p.pivot != null ? p.pivot.position + rot * (st.position - p.pivot.position) : st.position;
            d.transform.rotation = rot * Quaternion.Euler(0f, 0f, st.eulerAngles.z);
            Vector3 ls = st.lossyScale;
            d.transform.localScale = new Vector3(ls.x, ls.y, 1f);
        }
        foreach (var k in dead) map.Remove(k);
    }

    void Scan()
    {
        proxySet.Clear();
        foreach (var kv in map) if (kv.Value.dst != null) proxySet.Add(kv.Value.dst);
        foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (map.ContainsKey(sr) || proxySet.Contains(sr) || !Eligible(sr)) continue;
            var go = new GameObject("ObliqueProxy");
            go.transform.SetParent(transform, false); // 元の絵とは別(倍率の歪みを避ける)。元が消えたら次の LateUpdate で消す
            var d = go.AddComponent<SpriteRenderer>();
            d.sprite = sr.sprite; d.sharedMaterial = sr.sharedMaterial; d.sortingLayerID = sr.sortingLayerID; d.sortingOrder = sr.sortingOrder;
            map[sr] = new Proxy { src = sr, dst = d, pivot = RigPivot(sr) };
        }
    }

    void ClearAll()
    {
        foreach (var kv in map)
        {
            if (kv.Value.src != null) kv.Value.src.forceRenderingOff = false;
            if (kv.Value.dst != null) Destroy(kv.Value.dst.gameObject);
        }
        map.Clear();
    }

    // 写しが既にある元の絵(障害物の警告の縁などが合わせる)
    public static bool HasProxy(SpriteRenderer sr) => inst != null && sr != null && inst.map.ContainsKey(sr);
}
