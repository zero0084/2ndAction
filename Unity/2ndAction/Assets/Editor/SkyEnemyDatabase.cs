using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 天空回廊の固有Enemy 8種(2026-09-28)のデータと素材の取り込み。
//
// 素材: Assets/Art/SkyEnemy/<id>/
//   idle.png(静止絵=ポートレート) / move/*.png(移動・羽ばたきのコマ) /
//   telegraph.png attack.png recover.png hit.png death.png (+ dormant/wake/charge/dive は種によって)
// 無い素材は手続き的な仮素材を作る(既にあるファイルは上書きしない=実イラストに差し替えたらそのまま使われる)。
// 取り込み: 種ごとの目標の高さ(world)に合わせてPPUを決め(同じ種の全ポーズで共通)、足元中央をピボットにする。
// 敵データ: Assets/Resources/Enemies/<id>.asset。既存のアセットは調整値を上書きしない(素材の参照だけ毎回同期)。
public static class SkyEnemyDatabase
{
    const string ArtRoot = "Assets/Art/SkyEnemy";
    const string EnemyDir = "Assets/Resources/Enemies";
    static readonly string[] SkyOnly = { "sky_corridor" };

    public class SkySpec
    {
        public string id, name;
        public EnemyBehaviorKind behavior;
        public EnemyMovementType movement;
        public EnemyCategory category;
        public EnemyAiTier tier;
        public float targetHeight;        // 静止絵の高さ(world)
        public float hp = 1f;
        public int mile = 1;
        public Vector2 collider = Vector2.one, colliderOffset = Vector2.zero;
        public float launch = 1f, knockback = 1f, hover;
        public bool facingRight;          // 素材の向き(true=右向きに描かれている)
        public string[] poses;            // この種が使うポーズ名
        public int moveFrames;            // 移動コマ数(0=なし)
        public Color body, accent;
        public int canvasW = 256, canvasH = 256;
    }

    static readonly string[] Basic = { "telegraph", "attack", "recover", "hit", "death" };

    public static IEnumerable<SkySpec> Specs()
    {
        // 1. 雲精霊: 基本雑魚/Combo素材(T0、自発攻撃なし、ゆっくり上下する)
        yield return new SkySpec { id = "sky_slime", name = "SKY SLIME", behavior = EnemyBehaviorKind.None, movement = EnemyMovementType.Ground, category = EnemyCategory.Normal,
            tier = EnemyAiTier.T0, targetHeight = 1.0f, hp = 0.8f, mile = 1, collider = new Vector2(0.8f, 0.8f), launch = 1.2f, knockback = 1.1f, hover = 0.12f,
            poses = new[] { "hit", "death" }, moveFrames = 0, body = new Color(0.93f, 0.96f, 1f), accent = new Color(0.55f, 0.75f, 1f) };
        // 2. 天空獣: 地上の高速型(T1〜T2)
        yield return new SkySpec { id = "sky_hound", name = "SKY HOUND", behavior = EnemyBehaviorKind.SkyHound, movement = EnemyMovementType.Ground, category = EnemyCategory.Normal,
            tier = EnemyAiTier.T1, targetHeight = 1.05f, hp = 1f, mile = 2, collider = new Vector2(0.8f, 0.85f),
            poses = Basic, moveFrames = 2, body = new Color(0.78f, 0.84f, 0.95f), accent = new Color(0.35f, 0.55f, 0.95f), canvasW = 320 };
        // 3. ハーピー: 基本の飛行(T2)
        yield return new SkySpec { id = "harpy", name = "HARPY", behavior = EnemyBehaviorKind.Harpy, movement = EnemyMovementType.Flying, category = EnemyCategory.Flying,
            tier = EnemyAiTier.T2, targetHeight = 1.25f, hp = 1f, mile = 2, collider = new Vector2(0.45f, 0.7f),
            poses = new[] { "telegraph", "dive", "recover", "hit", "death" }, moveFrames = 2, body = new Color(0.95f, 0.8f, 0.7f), accent = new Color(0.55f, 0.4f, 0.7f), canvasW = 320 };
        // 4. ガーゴイル: 待ち伏せ(石像→起動、T1〜T3)
        yield return new SkySpec { id = "gargoyle", name = "GARGOYLE", behavior = EnemyBehaviorKind.Gargoyle, movement = EnemyMovementType.Ground, category = EnemyCategory.Normal,
            tier = EnemyAiTier.T1, targetHeight = 1.5f, hp = 1.6f, mile = 3, collider = new Vector2(0.55f, 0.85f), launch = 0.85f,
            poses = new[] { "dormant", "wake", "telegraph", "attack", "recover", "hit", "death" }, moveFrames = 0, body = new Color(0.55f, 0.55f, 0.62f), accent = new Color(1f, 0.7f, 0.25f), canvasW = 320 };
        // 5. 天空騎士: 標準的な能動戦闘(T3)
        yield return new SkySpec { id = "celestial_knight", name = "CELESTIAL KNIGHT", behavior = EnemyBehaviorKind.CelestialKnight, movement = EnemyMovementType.Ground, category = EnemyCategory.Normal,
            tier = EnemyAiTier.T3, targetHeight = 1.35f, hp = 1.8f, mile = 3, collider = new Vector2(0.6f, 0.9f), knockback = 0.8f,
            poses = Basic, moveFrames = 2, body = new Color(0.92f, 0.9f, 0.8f), accent = new Color(0.95f, 0.8f, 0.3f) };
        // 6. 古代守護兵: Heavy/Wall(T1〜T3)
        yield return new SkySpec { id = "ancient_sentinel", name = "ANCIENT SENTINEL", behavior = EnemyBehaviorKind.AncientSentinel, movement = EnemyMovementType.Ground, category = EnemyCategory.Heavy,
            tier = EnemyAiTier.T1, targetHeight = 1.85f, hp = 3.2f, mile = 5, collider = new Vector2(0.7f, 0.9f), launch = 0.55f, knockback = 0.45f,
            poses = Basic, moveFrames = 2, body = new Color(0.62f, 0.66f, 0.72f), accent = new Color(0.4f, 0.9f, 1f), canvasW = 320, canvasH = 320 };
        // 7. 雷精霊: 飛行/Area Control(T4)
        yield return new SkySpec { id = "storm_spirit", name = "STORM SPIRIT", behavior = EnemyBehaviorKind.StormSpirit, movement = EnemyMovementType.Flying, category = EnemyCategory.Flying,
            tier = EnemyAiTier.T4, targetHeight = 1.3f, hp = 1.4f, mile = 4, collider = new Vector2(0.5f, 0.65f),
            poses = new[] { "charge", "recover", "hit", "death" }, moveFrames = 0, body = new Color(0.55f, 0.6f, 0.95f), accent = new Color(1f, 0.95f, 0.45f) };
        // 8. 天空追跡者: T5 Hit & Away
        yield return new SkySpec { id = "sky_hunter", name = "SKY HUNTER", behavior = EnemyBehaviorKind.SkyHunter, movement = EnemyMovementType.Flying, category = EnemyCategory.Flying,
            tier = EnemyAiTier.T5, targetHeight = 1.2f, hp = 1.6f, mile = 6, collider = new Vector2(0.45f, 0.65f),
            poses = new[] { "telegraph", "dive", "recover", "hit", "death" }, moveFrames = 2, body = new Color(0.3f, 0.32f, 0.42f), accent = new Color(0.95f, 0.35f, 0.3f), canvasW = 320 };
    }

    [MenuItem("Tools/OneMoreMile/Build Sky Enemies (art + database)")]
    public static void BuildMenu() => Build();

    // バッチ用: 天空のEnemy + Encounter Profile(既存のProfileは上書きしない)
    public static void BuildSkyStage()
    {
        Build();
        EncounterProfileBuilder.Build();
    }

    // SceneBuilder.Build / EnemyDatabaseBuilder からも呼ぶ。
    public static void Build()
    {
        if (!Directory.Exists(EnemyDir)) Directory.CreateDirectory(EnemyDir);
        foreach (var spec in Specs())
        {
            SkyEnemyPlaceholderArt.EnsureArt(spec, ArtRoot);
            AssetDatabase.Refresh();
            var art = ImportArt(spec);
            UpsertDefinition(spec, art);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SkyEnemyDatabase: done");
    }

    class Art { public Sprite idle; public Sprite[] move; public EnemyPoseSprites poses = new EnemyPoseSprites(); }

    static Art ImportArt(SkySpec spec)
    {
        string dir = $"{ArtRoot}/{spec.id}";
        string idlePath = $"{dir}/idle.png";
        int idleH = ContentBounds(idlePath).height;
        float ppu = Mathf.Max(1f, idleH / Mathf.Max(0.1f, spec.targetHeight));
        var art = new Art { idle = ImportOne(idlePath, ppu) };
        var move = new List<Sprite>();
        string moveDir = $"{dir}/move";
        if (Directory.Exists(moveDir))
        {
            var files = Directory.GetFiles(moveDir, "*.png");
            System.Array.Sort(files);
            foreach (var f in files) { var sp = ImportOne(f.Replace('\\', '/'), ppu); if (sp != null) move.Add(sp); }
        }
        art.move = move.ToArray();
        Sprite P(string pose) { string pth = $"{dir}/{pose}.png"; return File.Exists(pth) ? ImportOne(pth, ppu) : null; }
        art.poses.telegraph = P("telegraph"); art.poses.attack = P("attack"); art.poses.recover = P("recover");
        art.poses.hit = P("hit"); art.poses.death = P("death"); art.poses.dormant = P("dormant");
        art.poses.wake = P("wake"); art.poses.charge = P("charge"); art.poses.dive = P("dive");
        return art;
    }

    // 足元中央ピボット(不透明部分の下端・左右の中央)、Full Rect、同じ種は共通のPPU。
    static Sprite ImportOne(string path, float ppu)
    {
        if (!File.Exists(path)) return null;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        RectInt b = ContentBounds(path);
        Vector2Int size = ImageSize(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = ppu;
        imp.alphaIsTransparency = true;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.filterMode = FilterMode.Bilinear;
        imp.npotScale = TextureImporterNPOTScale.None;   // 2のべき乗へ引き伸ばさない(寸法の計算がずれる)
        imp.mipmapEnabled = false;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        imp.SetTextureSettings(settings);
        float px = size.x > 0 ? (b.x + b.width * 0.5f) / size.x : 0.5f;
        float py = size.y > 0 ? b.y / (float)size.y : 0f;           // b.yは下からの画素(不透明部分の下端)
        imp.spritePivot = new Vector2(px, py);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Vector2Int ImageSize(string path)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        var v = new Vector2Int(tex.width, tex.height);
        Object.DestroyImmediate(tex);
        return v;
    }

    // 不透明部分(alpha>32)の外接矩形。yは下から。
    static RectInt ContentBounds(string path)
    {
        if (!File.Exists(path)) return new RectInt(0, 0, 1, 1);
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 32) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        Object.DestroyImmediate(tex);
        if (maxX < 0) return new RectInt(0, 0, w, h);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static void UpsertDefinition(SkySpec spec, Art art)
    {
        string path = $"{EnemyDir}/{spec.id}.asset";
        var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
        bool created = def == null;
        if (created)
        {
            def = ScriptableObject.CreateInstance<EnemyDefinition>();
            def.enemyId = spec.id;
            def.displayName = spec.name;
            def.tint = Color.white;
            def.movementType = spec.movement;
            def.category = spec.category;
            def.behaviorKind = spec.behavior;
            def.hpMultiplier = spec.hp;
            def.bigKnockbackOnHit = false;
            def.enableVisualFacing = true;
            def.mileReward = spec.mile;
            def.visualScaleMultiplier = 1f;
            def.aiTier = spec.tier;
            def.stageIds = SkyOnly;
            def.bodyColliderScale = spec.collider;
            // 地上の敵は足元を下端に保つ(縮めた分だけ下へずらす)。飛ぶ敵は中心のまま。
            def.bodyColliderOffset = spec.colliderOffset != Vector2.zero ? spec.colliderOffset
                : (spec.movement == EnemyMovementType.Ground ? new Vector2(0f, -(1f - spec.collider.y) * 0.5f) : Vector2.zero);
            def.launchScale = spec.launch;
            def.knockbackScale = spec.knockback;
            def.idleHoverAmplitude = spec.hover;
            AssetDatabase.CreateAsset(def, path);
        }
        // 素材の参照だけは毎回同期する(実イラストへの差し替えを反映)。
        def.sprite = art.idle;
        def.runFrames = art.move != null && art.move.Length > 0 ? art.move : null;
        def.poses = art.poses;
        def.attackSprite = art.poses.attack != null ? art.poses.attack : art.poses.dive;
        def.defaultFacingRight = spec.facingRight;
        EditorUtility.SetDirty(def);
        Debug.Log($"SkyEnemyDatabase: {(created ? "created" : "synced")} {spec.id} (poses: {PoseList(art.poses)}, move={def.runFrames?.Length ?? 0})");
    }

    static string PoseList(EnemyPoseSprites p)
    {
        var l = new List<string>();
        if (p.telegraph) l.Add("telegraph"); if (p.attack) l.Add("attack"); if (p.recover) l.Add("recover");
        if (p.hit) l.Add("hit"); if (p.death) l.Add("death"); if (p.dormant) l.Add("dormant");
        if (p.wake) l.Add("wake"); if (p.charge) l.Add("charge"); if (p.dive) l.Add("dive");
        return string.Join(",", l);
    }
}
