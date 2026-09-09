using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// デバッグ専用の使い捨てツール(2026-09-09) - マスターから「攻撃エフェクトが
// 表示されていない」という実機動画の報告を受け、原因調査のために作成。この
// セッションにはUnity Editorのゲーム画面を直接見る手段がないため、
// PortraitPreviewCapture.csと同じ手法(Editor専用、batchmode上でカメラを
// 直接RenderTextureへレンダリングしPNG保存)で、各攻撃のVFX呼び出し直後の
// Game View相当を静止画として確認する。シーンは絶対に保存しない。
// PortraitPreviewCapture.cs同様、今後も戦闘VFXの見た目確認に再利用可能な
// 常設ツールとして残す(通常/上/下降の3攻撃すべてを1回の実行で撮影する)。
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

        player.transform.position = new Vector3(0f, 0f, 0f);

        CaptureOne(player, cam, "normal", player.attackSlashVisual,
            new Vector3(1.5f, 0.5f, 0f), 1.5f, false);
        CaptureOne(player, cam, "up", player.upAttackSlashVisual,
            new Vector3(0.3f, 1.7f, 0f), 1.15f, false);
        CaptureOne(player, cam, "down", player.downAttackSlashVisual,
            new Vector3(0.15f, -0.4f + 1.1f, 0f), 1f, true);
    }

    static void CaptureOne(PlayerController player, Camera cam, string label, AttackSlashVisual visual, Vector3 forcedLocalPos, float scale, bool sustained)
    {
        if (visual == null)
        {
            Debug.LogError($"AttackVfxCapture[{label}]: visual is null");
            return;
        }

        // Edit-mode scenes opened via OpenScene do NOT run Awake() the way
        // Play mode does, so AttackSlashVisual's private `sr` field would
        // still be null here - force it via reflection first so this test
        // matches actual runtime behavior instead of throwing.
        MethodInfo awakeMethod = typeof(AttackSlashVisual).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        awakeMethod?.Invoke(visual, null);

        visual.transform.localPosition = forcedLocalPos;
        if (sustained) visual.ShowSustained(scale);
        else visual.PlaySingle(scale, 1f);

        SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
        Debug.Log($"AttackVfxCapture[{label}]: enabled={sr.enabled}, sprite={sr.sprite}, sortingOrder={sr.sortingOrder}, worldPos={visual.transform.position}, localScale={visual.transform.localScale}");

        // Hide every other nearby SpriteRenderer (background art etc.) for
        // an isolated zoom shot, then restore them for a gameplay-scale shot.
        foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(r.transform.position, player.transform.position) < 5f
                && r.gameObject.name != "Visual" && r != sr)
            {
                r.enabled = false;
            }
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        cam.transform.position = new Vector3(player.transform.position.x + forcedLocalPos.x * 0.5f, player.transform.position.y + forcedLocalPos.y * 0.5f + 0.3f, -10f);
        cam.orthographicSize = 2.6f;
        RenderAndSave(cam, $"attack_vfx_{label}_zoom.png");

        foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(r.transform.position, player.transform.position) < 5f) r.enabled = true;
        }
        cam.backgroundColor = new Color(0.75f, 0.85f, 0.97f);
        cam.transform.position = new Vector3(player.transform.position.x + 3f, player.transform.position.y + 1f, -10f);
        cam.orthographicSize = 10.4f;
        RenderAndSave(cam, $"attack_vfx_{label}_gameplay.png");

        if (sustained) visual.HideSustained();
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
