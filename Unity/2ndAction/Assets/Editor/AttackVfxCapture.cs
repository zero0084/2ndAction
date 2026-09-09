using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// デバッグ専用の使い捨てツール(2026-09-09) - マスターから「攻撃エフェクトが
// 表示されていない」という実機動画の報告を受け、原因調査のために作成。この
// セッションにはUnity Editorのゲーム画面を直接見る手段がないため、
// PortraitPreviewCapture.csと同じ手法(Editor専用、batchmode上でカメラを
// 直接RenderTextureへレンダリングしPNG保存)で、通常攻撃のPlaySingle呼び出し
// 直後のGame View相当を静止画として確認する。シーンは絶対に保存しない。
public static class AttackVfxCapture
{
    [MenuItem("Tools/2ndAction/Capture Attack VFX")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        Camera cam = Camera.main;
        if (player == null || cam == null)
        {
            Debug.LogError($"AttackVfxCapture: missing dependency (player={player}, cam={cam})");
            return;
        }

        Debug.Log($"AttackVfxCapture: attackSlashVisual={player.attackSlashVisual}, attackHitbox={player.attackHitbox}");

        player.transform.position = new Vector3(0f, 0f, 0f);

        // Directly force the same call DoAttack() makes for a stage-3 combo
        // hit (largest scale, easiest to spot if it renders at all).
        if (player.attackSlashVisual != null)
        {
            // Edit-mode scenes opened via OpenScene do NOT run Awake() the
            // way Play mode does, so AttackSlashVisual's private `sr` field
            // would still be null here - force it via reflection first so
            // this test matches actual runtime behavior instead of throwing.
            MethodInfo awakeMethod = typeof(AttackSlashVisual).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            awakeMethod?.Invoke(player.attackSlashVisual, null);

            player.attackSlashVisual.transform.localPosition = new Vector3(1.0f + 2f * 0.25f, 0.5f, 0f);
            player.attackSlashVisual.PlaySingle(1.5f, 1f);
            Debug.Log($"AttackVfxCapture: after PlaySingle - sprite renderer enabled={player.attackSlashVisual.GetComponent<SpriteRenderer>().enabled}, sprite={player.attackSlashVisual.GetComponent<SpriteRenderer>().sprite}, color={player.attackSlashVisual.GetComponent<SpriteRenderer>().color}, sortingOrder={player.attackSlashVisual.GetComponent<SpriteRenderer>().sortingOrder}, worldPos={player.attackSlashVisual.transform.position}, localScale={player.attackSlashVisual.transform.localScale}");
        }

        // Debug: enumerate every SpriteRenderer near the player to find out
        // what else might be rendering at/near world origin.
        foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(r.transform.position, player.transform.position) < 5f)
            {
                Debug.Log($"AttackVfxCapture: nearby SpriteRenderer '{r.gameObject.name}' pos={r.transform.position} sprite={r.sprite} enabled={r.enabled} sortingOrder={r.sortingOrder} scale={r.transform.lossyScale}");
            }
        }

        // Shot 1: tight isolated zoom (background art hidden) so the VFX's
        // own shape/size is unambiguous.
        foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(r.transform.position, player.transform.position) < 5f
                && r.gameObject.name != "Visual" && r.gameObject.name != "AttackSlash")
            {
                r.enabled = false;
            }
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        cam.transform.position = new Vector3(player.transform.position.x + 0.5f, player.transform.position.y + 0.7f, -10f);
        cam.orthographicSize = 2.2f;
        RenderAndSave(cam, "attack_vfx_preview_zoom.png");

        // Shot 2: same position/scale as actual gameplay (orthographicSize
        // 10.4, matching CameraFollow's own camera setup in SceneBuilder)
        // against the REAL sky background, to judge visibility the way the
        // player would actually see it on a real device.
        foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(r.transform.position, player.transform.position) < 5f)
            {
                r.enabled = true;
            }
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.75f, 0.85f, 0.97f);
        cam.transform.position = new Vector3(player.transform.position.x + 3f, player.transform.position.y + 1f, -10f);
        cam.orthographicSize = 10.4f;
        RenderAndSave(cam, "attack_vfx_preview_gameplay.png");
    }

    static void RenderAndSave(Camera cam, string fileName)
    {
        int width = 900;
        int height = 900;
        RenderTexture rt = new RenderTexture(width, height, 24);
        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        bool prevEnabled = cam.enabled;

        cam.enabled = true;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        cam.enabled = prevEnabled;
        RenderTexture.active = prevActive;
        rt.Release();

        string outPath = Path.Combine(Application.dataPath, "../../../" + fileName);
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Debug.Log("AttackVfxCapture: saved " + Path.GetFullPath(outPath));
    }
}
