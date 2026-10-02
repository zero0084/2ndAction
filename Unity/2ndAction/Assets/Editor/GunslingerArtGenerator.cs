using System.IO;
using UnityEditor;
using UnityEngine;

// 二丁拳銃士追加(2026-09-23) - CaveEnemyArtGenerator/CaveBossFxと全く同じ
// 理由(この環境からChatGPT等の外部画像生成を直接呼び出せない)による暫定
// プレースホルダー素材。手続き的に描画した「二丁拳銃を持つ人型シルエット」
// のPNGを実際にディスクへ書き出し、既存のスプライト取り込みパイプライン
// (ConfigureAndLoadSpriteWithFootPivot/ConfigureSpriteFolderImportWith
// FootPivotUniformSize等)へそのまま流せる形にする。
//
// 1つの汎用ポーズ描画関数(DrawPose)を全State共通で使い、パラメータ
// (腕の角度/前傾/しゃがみ/脚位相/マズルフラッシュの有無)だけを差し替える
// ことで「同一キャラクター・同一比率・同一カメラ角度」を構造的に保証する
// (CaveEnemyArtGeneratorのDrawFn方式と同じ狙い)。
//
// 重要: これはあくまで仮素材(シルエットのみ)。将来ChatGPT/Grok等で本物の
// イラストを生成したら、Assets/Art/配下の同名ファイル/フォルダを差し替える
// だけで良い(CharacterDatabaseBuilderはパス参照のみ)。
public static class GunslingerArtGenerator
{
    const string ArtDir = "Assets/Art";
    const string PortraitFolder = "Assets/Art/UI/Characters";
    const int W = 200, H = 260;
    static readonly Color32 Jacket = new Color32(42, 46, 56, 255);
    static readonly Color32 Skin = new Color32(214, 176, 148, 255);
    static readonly Color32 Hair = new Color32(60, 42, 40, 255);
    static readonly Color32 GunColor = new Color32(28, 28, 32, 255);
    static readonly Color32 Accent = new Color32(150, 170, 190, 255);
    static readonly Color32 Flash = new Color32(255, 235, 150, 255);

    [MenuItem("Tools/OneMoreMile/Generate Gunslinger Art (placeholder)")]
    public static void Generate()
    {
        Directory.CreateDirectory(PortraitFolder);

        // ポートレート(Character Select用) - 直立、両手の銃を胸の前で構える。
        WriteIfMissing($"{PortraitFolder}/gunslinger_portrait.png", W, H, new Pose { armAngle = 20f, lean = 0f, crouch = 0f, legPhase = 0f, flash = false });

        // 本番素材差し替え(2026-09-23、ChatGPT生成) - Attack/UpShot/
        // DownAttackは実イラスト2枚(idle/windup等)に差し替え済みのため、
        // コマ数をそれに合わせて縮小した(手続き的プレースホルダーが
        // 削除済みの旧コマindexを勝手に埋め直さないようにするため - この
        // Generateメソッド自体はWriteIfMissing/WriteSequence内部で「既存
        // ファイルはスキップ」するので、実イラストが既にある限り上書き
        // されることはない)。
        // Run6コマ拡張(2026-09-24) - GunslingerRun_v1は`gunslingerrun_0/1.png`
        // から`run_00〜run_05.png`(他キャラと同じ命名)へ実イラスト6コマに
        // 全面差し替えたため、このWriteSequence呼び出し自体を削除した
        // (残したままだと、旧ファイル名`gunslingerrun_0/1.png`が「存在
        // しない」と判定され、SceneBuilder.Build()を再実行するたびに小さな
        // プレースホルダーPNGがフォルダへ紛れ込み続けてしまっていた)。
        WriteSequence("GunslingerJumpStart_v1", 1, i => new Pose { armAngle = -20f, lean = -4f, crouch = 0.5f, legPhase = 0.25f, flash = false });
        WriteSequence("GunslingerJumpAir_v1", 1, i => new Pose { armAngle = -15f, lean = -6f, crouch = 0f, legPhase = 0.4f, flash = false });
        WriteSequence("GunslingerDoubleJump_v1", 1, i => new Pose { armAngle = -10f, lean = -8f, crouch = 0f, legPhase = 0.6f, flash = false });
        WriteSequence("GunslingerLand_v1", 1, i => new Pose { armAngle = -15f, lean = 6f, crouch = 0.6f, legPhase = 0.0f, flash = false });

        // Forward/Backward Shot(共有 - Backward側はPlayerController側の
        // transform.localScale.x反転で自動的にミラーされる)。0度=水平前方。
        WriteSequence("GunslingerAttack_v1", 1, i =>
        {
            float t = i / 2f;
            return new Pose { armAngle = 0f, lean = -2f, crouch = 0f, legPhase = 0f, flash = t < 0.7f };
        });

        // Up Shot - ジャンプしながら斜め上(約55度)。
        WriteSequence("GunslingerUpShot_v1", 1, i =>
        {
            float t = i / 1f;
            return new Pose { armAngle = 58f, lean = -10f, crouch = 0f, legPhase = 0.3f, flash = t < 0.6f };
        });

        // Down Shot(既存downAttackFramesフィールドを流用) - 斜め下(約-55度)。
        WriteSequence("GunslingerDownAttack_v1", 1, i =>
        {
            float t = i / 1f;
            return new Pose { armAngle = -58f, lean = 12f, crouch = 0f, legPhase = -0.1f, flash = t < 0.6f };
        });

        WriteBullet($"{ArtDir}/GunslingerBullet.png");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GunslingerArtGenerator] Generated placeholder gunslinger art under " + ArtDir);
    }

    struct Pose
    {
        public float armAngle; // 度。0=水平前方、+=上、-=下
        public float lean;     // 度。体幹の前後傾き(+で前傾)
        public float crouch;   // 0..1
        public float legPhase; // 0..1(走行位相) or 固定ポーズ用の任意値
        public bool flash;     // マズルフラッシュを描くか
    }

    static void WriteIfMissing(string path, int w, int h, Pose pose)
    {
        if (File.Exists(path)) return;
        WriteFrame(path, w, h, pose);
    }

    static void WriteSequence(string dirName, int count, System.Func<int, Pose> poseAt)
    {
        string dir = $"{ArtDir}/{dirName}";
        Directory.CreateDirectory(dir);
        string baseName = dirName.Replace("_v1", "").ToLowerInvariant();
        for (int i = 0; i < count; i++)
        {
            string path = $"{dir}/{baseName}_{i}.png";
            if (File.Exists(path)) continue;
            WriteFrame(path, W, H, poseAt(i));
        }
    }

    static void WriteFrame(string path, int w, int h, Pose pose)
    {
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
        DrawPose(px, w, h, pose);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // ============ 汎用ポーズ描画 ============
    static void DrawPose(Color32[] px, int w, int h, Pose pose)
    {
        float crouchDrop = pose.crouch * 0.06f;
        float leanRad = pose.lean * Mathf.Deg2Rad;
        // 体幹の傾き分だけ肩/腰のX基準をずらす(のけぞり/前傾を表現)。
        float torsoTiltX = Mathf.Sin(leanRad) * 0.05f;

        Vector2 hip = new Vector2(0.5f, 0.50f - crouchDrop);
        Vector2 shoulderR = new Vector2(0.60f + torsoTiltX, 0.72f - crouchDrop * 0.6f);
        Vector2 shoulderL = new Vector2(0.42f + torsoTiltX, 0.72f - crouchDrop * 0.6f);
        Vector2 head = new Vector2(0.52f + torsoTiltX * 1.4f, 0.88f - crouchDrop * 0.6f);

        // 脚(走行位相で前後に開く、固定ポーズはlegPhaseを小さい定数として渡す)。
        float legSwing = Mathf.Sin(pose.legPhase * Mathf.PI * 2f) * 0.14f;
        Vector2 footR = new Vector2(0.5f + 0.06f + legSwing, 0.02f);
        Vector2 footL = new Vector2(0.5f - 0.06f - legSwing, 0.02f);
        Vector2 kneeR = Vector2.Lerp(hip, footR, 0.5f) + new Vector2(0f, 0.03f);
        Vector2 kneeL = Vector2.Lerp(hip, footL, 0.5f) + new Vector2(0f, 0.03f);

        Fill(px, w, h, (u, v) => Mathf.Max(
            Bar(u, v, hip.x - 0.02f, hip.y, kneeR.x, kneeR.y, 0.032f),
            Bar(u, v, kneeR.x, kneeR.y, footR.x, footR.y, 0.026f)), Jacket);
        Fill(px, w, h, (u, v) => Mathf.Max(
            Bar(u, v, hip.x + 0.02f, hip.y, kneeL.x, kneeL.y, 0.032f),
            Bar(u, v, kneeL.x, kneeL.y, footL.x, footL.y, 0.026f)), Jacket);

        // 胴(トレンチコート風、腰から肩への台形をBoxで近似)。
        Fill(px, w, h, (u, v) => Box(u, v, 0.5f + torsoTiltX * 0.6f, (hip.y + shoulderR.y) * 0.5f, 0.15f, (shoulderR.y - hip.y) * 0.5f + 0.02f), Jacket);

        // 右腕(利き腕、armAngleで狙う方向を向く - 0度=水平前方、+で上、-で下)。
        float armRad = pose.armAngle * Mathf.Deg2Rad;
        Vector2 armDirR = new Vector2(Mathf.Cos(armRad), Mathf.Sin(armRad));
        Vector2 handR = shoulderR + armDirR * 0.30f;
        Fill(px, w, h, (u, v) => Bar(u, v, shoulderR.x, shoulderR.y, handR.x, handR.y, 0.022f), Jacket);

        // 左腕(添える方の銃、右腕よりやや控えめな角度で常に前方寄り)。
        float armRadL = (pose.armAngle * 0.6f) * Mathf.Deg2Rad;
        Vector2 armDirL = new Vector2(Mathf.Cos(armRadL), Mathf.Sin(armRadL));
        Vector2 handL = shoulderL + armDirL * 0.24f;
        Fill(px, w, h, (u, v) => Bar(u, v, shoulderL.x, shoulderL.y, handL.x, handL.y, 0.020f), Jacket);

        // 頭+ポニーテール。
        Fill(px, w, h, (u, v) => Ellipse(u, v, head.x, head.y, 0.075f, 0.085f), Skin);
        Fill(px, w, h, (u, v) => Mathf.Max(
            Ellipse(u, v, head.x, head.y + 0.03f, 0.085f, 0.07f),
            Bar(u, v, head.x - 0.06f, head.y + 0.02f, head.x - 0.14f - legSwing * 0.3f, head.y - 0.16f, 0.03f)), Hair);

        // 銃(両手、進行方向へ向いた小さな暗色の棒)。
        Fill(px, w, h, (u, v) => Bar(u, v, handR.x, handR.y, handR.x + armDirR.x * 0.09f, handR.y + armDirR.y * 0.09f, 0.016f), GunColor);
        Fill(px, w, h, (u, v) => Bar(u, v, handL.x, handL.y, handL.x + armDirL.x * 0.08f, handL.y + armDirL.y * 0.08f, 0.014f), GunColor);

        // マズルフラッシュ(利き腕側のみ、Attack/UpShot/DownShotの発射コマ)。
        if (pose.flash)
        {
            Vector2 tip = handR + armDirR * 0.11f;
            Fill(px, w, h, (u, v) => Ellipse(u, v, tip.x, tip.y, 0.030f, 0.030f), Flash);
        }

        // ジャケットの縁取り(暗い背景に埋もれないよう淡いアクセント)。
        Fill(px, w, h, (u, v) => Box(u, v, 0.5f + torsoTiltX * 0.6f, (hip.y + shoulderR.y) * 0.5f, 0.15f, (shoulderR.y - hip.y) * 0.5f + 0.02f) * 0.12f, Accent);
    }

    // 弾丸スプライト(細い横長の光の筋、PlayerBullet.Createが向きに応じて
    // 回転/色をtintするので、ここでは中央基準の水平な形だけ用意すればよい)。
    static void WriteBullet(string path)
    {
        if (File.Exists(path)) return;
        int w = 64, h = 24;
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
        Fill(px, w, h, (u, v) => Ellipse(u, v, 0.5f, 0.5f, 0.46f, 0.30f), new Color32(255, 255, 255, 255));
        Fill(px, w, h, (u, v) => Ellipse(u, v, 0.62f, 0.5f, 0.30f, 0.18f), new Color32(255, 255, 255, 255));
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // ---- shape helpers (CaveEnemyArtGeneratorと同じ考え方、このファイル内で自己完結) ---- //
    static float Ellipse(float u, float v, float cx, float cy, float rx, float ry, float edge = 0.02f)
    {
        float dx = (u - cx) / Mathf.Max(0.001f, rx), dy = (v - cy) / Mathf.Max(0.001f, ry);
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01((1f - r) / edge);
    }

    static float Box(float u, float v, float cx, float cy, float hw, float hh, float edge = 0.015f)
    {
        float dx = Mathf.Abs(u - cx) - hw, dy = Mathf.Abs(v - cy) - hh;
        float d = Mathf.Max(dx, dy);
        return Mathf.Clamp01(-d / edge + 0.5f);
    }

    static float Bar(float u, float v, float x0, float y0, float x1, float y1, float halfWidth)
    {
        Vector2 p = new Vector2(u, v), a = new Vector2(x0, y0), b = new Vector2(x1, y1);
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        Vector2 closest = a + ab * t;
        float d = Vector2.Distance(p, closest);
        return Mathf.Clamp01((halfWidth - d) / 0.012f);
    }

    static void Paint(Color32[] px, int w, int h, int x, int y, float a, Color32 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h || a <= 0f) return;
        int idx = y * w + x;
        byte newA = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
        if (newA <= px[idx].a) return;
        px[idx] = new Color32(c.r, c.g, c.b, newA);
    }

    static void Fill(Color32[] px, int w, int h, System.Func<float, float, float> shapeAt, Color32 c)
    {
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h; // v=0が画像下端(足元)、v=1が上端(頭側)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float a = shapeAt(u, v);
                if (a > 0f) Paint(px, w, h, x, y, a, c);
            }
        }
    }
}
