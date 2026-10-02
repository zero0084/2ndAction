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

        // 派手なアニメーション化(2026-09-10) - 通常/上攻撃はPlaySingleの
        // 1枚絵演出から5コマの実コマ送り(PlayFrames)へ移行したので、検証
        // ショットも「最も派手なピークのコマ」(frames[1])を表示させて撮る。
        CaptureOne(player, cam, "normal", player.attackSlashVisual,
            new Vector3(1.5f, 0.5f, 0f), 1.5f, false, 1f, null, 1);
        // 不具合修正(2026-09-10) - UpAttackHitboxをY=1.7→0.9・高さ1.4→2.0
        // へ変更(キャラクター本体と重なるように)したのに合わせて検証位置
        // も更新。
        CaptureOne(player, cam, "up", player.upAttackSlashVisual,
            new Vector3(0.3f, 0.9f, 0f), 1.15f, false, 1f, null, 1);
        CaptureOne(player, cam, "down", player.downAttackSlashVisual,
            new Vector3(0.15f, -0.4f + 1.1f, 0f), 1f, true);
        // 不具合修正(2026-09-10) - 新設した下降攻撃・着地衝撃VFXの検証。
        CaptureOne(player, cam, "down_land", player.downAttackLandSlashVisual,
            new Vector3(0f, 0f, 0f), 1f, false);

        // 不具合修正(2026-09-10) - 「空中上攻撃のエフェクトが攻撃範囲拡張
        // とともにプレイヤーから離れてしまう」検証用。Attack Range Upカード
        // を2枚積んだ状態(AttackRangeMultiplier=3)を再現し、
        // PlayerController.DoUpAttackと全く同じpositionRangeFactorの式で
        // 位置を計算、実際のupAttackHitbox位置(離れて正しい)と並べて確認
        // できるよう、Hitboxの位置にも目印を置く。
        float boostedRangeMultiplier = 3f;
        Vector3 upBase = new Vector3(0.3f, 1.7f, 0f);
        float positionRangeFactor = 1f + (boostedRangeMultiplier - 1f) * 0.5f;
        Vector3 boostedVfxPos = upBase * positionRangeFactor;
        Vector3 boostedHitboxPos = upBase * boostedRangeMultiplier;
        Debug.Log($"AttackVfxCapture[up-boosted]: rangeMultiplier={boostedRangeMultiplier}, vfxPos={boostedVfxPos}, actualHitboxPos={boostedHitboxPos}");
        CaptureOne(player, cam, "up_boosted", player.upAttackSlashVisual, boostedVfxPos, 1.15f, false, boostedRangeMultiplier, boostedHitboxPos);
    }

    static void CaptureOne(PlayerController player, Camera cam, string label, AttackSlashVisual visual, Vector3 forcedLocalPos, float scale, bool sustained, float rangeMultiplier = 1f, Vector3? hitboxMarkerLocalPos = null, int framesPreviewIndex = -1)
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

        SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();

        visual.transform.localPosition = forcedLocalPos;
        if (sustained)
        {
            visual.ShowSustained(scale);
        }
        else if (framesPreviewIndex >= 0 && visual.frames != null && visual.frames.Length > 0)
        {
            // 派手なアニメーション化(2026-09-10) - PlayFramesはframes[0]
            // (細い先行線)をセットするだけなので、静止画検証では指定コマ
            // (通常はピークのframes[1])へ強制的に差し替えて撮る。
            visual.PlayFrames(scale, rangeMultiplier);
            int idx = Mathf.Clamp(framesPreviewIndex, 0, visual.frames.Length - 1);
            sr.sprite = visual.frames[idx];
        }
        else
        {
            visual.PlaySingle(scale, rangeMultiplier);
        }
        Debug.Log($"AttackVfxCapture[{label}]: enabled={sr.enabled}, sprite={sr.sprite}, sortingOrder={sr.sortingOrder}, worldPos={visual.transform.position}, localScale={visual.transform.localScale}");

        // 不具合修正(2026-09-10)検証用 - 実際のHitbox位置(離れて正しい)を
        // 小さな黄色いマーカーで示し、VFXがそこへ向かって伸びているように
        // 見えるか(=離れて浮いてしまっていないか)を1枚の画像で比較できる
        // ようにする。
        GameObject marker = null;
        if (hitboxMarkerLocalPos.HasValue)
        {
            marker = new GameObject("__HitboxMarker");
            marker.transform.SetParent(player.transform);
            marker.transform.localPosition = hitboxMarkerLocalPos.Value;
            marker.transform.localScale = Vector3.one * 0.15f;
            var markerSr = marker.AddComponent<SpriteRenderer>();
            Texture2D tex = new Texture2D(4, 4);
            Color[] px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.yellow;
            tex.SetPixels(px);
            tex.Apply();
            markerSr.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            markerSr.sortingOrder = 10;
        }

        // Hide every other nearby SpriteRenderer (background art etc.) for
        // an isolated zoom shot, then restore them for a gameplay-scale shot.
        foreach (SpriteRenderer r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (Vector3.Distance(r.transform.position, player.transform.position) < 5f
                && r.gameObject.name != "Visual" && r != sr && r != (marker != null ? marker.GetComponent<SpriteRenderer>() : null))
            {
                r.enabled = false;
            }
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        float zoomTargetX = hitboxMarkerLocalPos.HasValue ? hitboxMarkerLocalPos.Value.x * 0.5f : forcedLocalPos.x * 0.5f;
        float zoomTargetY = hitboxMarkerLocalPos.HasValue ? hitboxMarkerLocalPos.Value.y * 0.5f : forcedLocalPos.y * 0.5f;
        cam.transform.position = new Vector3(player.transform.position.x + zoomTargetX, player.transform.position.y + zoomTargetY + 0.3f, -10f);
        cam.orthographicSize = hitboxMarkerLocalPos.HasValue ? 4f : 2.6f;
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
        if (marker != null) Object.DestroyImmediate(marker);
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
