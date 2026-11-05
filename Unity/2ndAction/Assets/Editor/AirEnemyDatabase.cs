using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 空中雑魚 18 種(2026-11-05)の素材の取り込みと敵データ。
//
// 素材: Assets/Art/AirEnemy/<id>/  (Tools/air/slice.py がエステルのシートから作る)
//   idle.png / move/fly_01..04.png(羽ばたき 1-2-3-2)/ telegraph.png attack.png recover.png hit.png death.png
//   全コマ同じ正方形のキャンバス・体の重心がキャンバスの中心 → ピボットは中心(0.5, 0.5)。羽ばたいても体が跳ねない。
// 縮尺: 待機の絵の不透明部分の長い辺 = size(m)。同じ種の全コマで共通の PPU。
// 当たり判定: 待機の絵の不透明部分の幅/高さの約 55%/50%(翼の先まで含めない)。
// 素材の無い種は作らない(出現の候補 = AirMinionTuning は、敵データの無い id を飛ばす)。
// 敵データ: Assets/Resources/Enemies/<id>.asset。既存のアセットは調整値(HP 等)を上書きしない。素材の参照だけ毎回同期する。
public static class AirEnemyDatabase
{
    const string ArtRoot = "Assets/Art/AirEnemy";
    const string EnemyDir = "Assets/Resources/Enemies";

    class Spec
    {
        public string id, name, stage;
        public EnemyBehaviorKind behavior;
        public float size;           // 長い辺(m)
        public float hp;             // 距離の HP に掛ける(小型 1〜2 発、中型は短い連撃)
        public int mile = 1;
        public float launch = 1.2f, knockback = 1.1f, hover = 0f;
    }

    static readonly EnemyBehaviorKind A = EnemyBehaviorKind.AirLow, B = EnemyBehaviorKind.AirHigh, C = EnemyBehaviorKind.AirDiver,
        D = EnemyBehaviorKind.AirCrosser, E = EnemyBehaviorKind.AirSwarm;

    static IEnumerable<Spec> Specs()
    {
        // 荒野街道
        yield return new Spec { id = "air_sand_swallow", name = "SAND SWALLOW", stage = "wasteland_road", behavior = A, size = 1.25f, hp = 0.6f };
        yield return new Spec { id = "air_vulture", name = "WASTELAND VULTURE", stage = "wasteland_road", behavior = B, size = 1.9f, hp = 1.3f, mile = 2, launch = 0.9f, knockback = 0.8f };
        yield return new Spec { id = "air_red_eagle", name = "RED-CROWNED EAGLE", stage = "wasteland_road", behavior = C, size = 1.6f, hp = 0.9f, mile = 2 };
        yield return new Spec { id = "air_sand_spirit", name = "SANDSTORM SPIRIT", stage = "wasteland_road", behavior = D, size = 1.6f, hp = 0.7f, mile = 2 };
        yield return new Spec { id = "air_locust", name = "LOCUST", stage = "wasteland_road", behavior = E, size = 0.75f, hp = 0.35f };
        // 自然洞窟
        yield return new Spec { id = "air_moth", name = "GIANT MOTH", stage = "natural_cave", behavior = A, size = 1.3f, hp = 0.6f };
        yield return new Spec { id = "air_jellyfish", name = "FLOATING JELLYFISH", stage = "natural_cave", behavior = B, size = 1.5f, hp = 1.2f, mile = 2, launch = 0.9f, knockback = 0.8f };
        yield return new Spec { id = "air_thread_spider", name = "THREAD SPIDER", stage = "natural_cave", behavior = C, size = 1.25f, hp = 0.9f, mile = 2 };
        yield return new Spec { id = "air_crystal_dragonfly", name = "CRYSTAL DRAGONFLY", stage = "natural_cave", behavior = D, size = 1.6f, hp = 0.7f, mile = 2 };
        yield return new Spec { id = "air_bat_swarm", name = "LITTLE BAT", stage = "natural_cave", behavior = E, size = 0.7f, hp = 0.35f };
        // 天空回廊
        yield return new Spec { id = "air_sky_wisp", name = "SKY WISP", stage = "sky_corridor", behavior = A, size = 1.1f, hp = 0.6f };
        yield return new Spec { id = "air_cloud_manta", name = "CLOUD MANTA", stage = "sky_corridor", behavior = B, size = 2.3f, hp = 1.4f, mile = 2, launch = 0.85f, knockback = 0.75f };
        yield return new Spec { id = "air_sky_swallow", name = "SKY SWALLOW", stage = "sky_corridor", behavior = E, size = 0.9f, hp = 0.35f };
        // ラスダン
        yield return new Spec { id = "air_wraith", name = "WRAITH", stage = "last_corridor", behavior = A, size = 1.5f, hp = 0.7f };
        yield return new Spec { id = "air_black_imp", name = "BLACK-WINGED IMP", stage = "last_corridor", behavior = C, size = 1.6f, hp = 1.0f, mile = 2 };
        yield return new Spec { id = "air_ancient_drone", name = "ANCIENT FLYING WEAPON", stage = "last_corridor", behavior = B, size = 1.8f, hp = 1.4f, mile = 2, launch = 0.85f, knockback = 0.75f };
        yield return new Spec { id = "air_mech_drake", name = "MECH DRAKE", stage = "last_corridor", behavior = D, size = 1.8f, hp = 0.8f, mile = 2 };
        yield return new Spec { id = "air_reaper_familiar", name = "REAPER FAMILIAR", stage = "last_corridor", behavior = E, size = 0.85f, hp = 0.4f };
    }

    [MenuItem("Tools/OneMoreMile/Build Air Enemies (art + database)")]
    public static void BuildMenu() => Build();

    // バッチ: -executeMethod AirEnemyDatabase.BuildBatch
    public static void BuildBatch() { Build(); EnemyDatabase.Reset(); }

    public static void Build()
    {
        if (!Directory.Exists(EnemyDir)) Directory.CreateDirectory(EnemyDir);
        int n = 0;
        foreach (var spec in Specs())
        {
            string dir = $"{ArtRoot}/{spec.id}";
            if (!File.Exists($"{dir}/idle.png")) { Debug.Log($"AirEnemyDatabase: skip {spec.id} (no art yet)"); continue; }
            var art = ImportArt(spec, dir, out Vector2 colScale);
            Upsert(spec, art, colScale);
            n++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"AirEnemyDatabase: done ({n} species)");
    }

    class Art { public Sprite idle; public Sprite[] move; public EnemyPoseSprites poses = new EnemyPoseSprites(); }

    static Art ImportArt(Spec spec, string dir, out Vector2 colScale)
    {
        string idlePath = $"{dir}/idle.png";
        RectInt b = ContentBounds(idlePath, out Vector2Int size);
        float ppu = Mathf.Max(1f, Mathf.Max(b.width, b.height) / Mathf.Max(0.1f, spec.size));
        colScale = new Vector2(Mathf.Clamp(b.width / (float)size.x * 0.55f, 0.15f, 0.9f), Mathf.Clamp(b.height / (float)size.y * 0.5f, 0.15f, 0.9f));
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
        Sprite P(string pose) { string p = $"{dir}/{pose}.png"; return File.Exists(p) ? ImportOne(p, ppu) : null; }
        art.poses.telegraph = P("telegraph"); art.poses.attack = P("attack"); art.poses.recover = P("recover");
        art.poses.hit = P("hit"); art.poses.death = P("death");
        art.poses.dive = P("dive") ?? art.poses.attack;
        return art;
    }

    // 中心ピボット、Full Rect、同じ種は共通の PPU
    static Sprite ImportOne(string path, float ppu)
    {
        if (!File.Exists(path)) return null;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = ppu;
        imp.alphaIsTransparency = true;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.filterMode = FilterMode.Bilinear;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.mipmapEnabled = false;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        imp.SetTextureSettings(settings);
        imp.spritePivot = new Vector2(0.5f, 0.5f);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static RectInt ContentBounds(string path, out Vector2Int size)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        size = new Vector2Int(w, h);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 32) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        Object.DestroyImmediate(tex);
        if (maxX < 0) return new RectInt(0, 0, w, h);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static void Upsert(Spec spec, Art art, Vector2 colScale)
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
            def.movementType = EnemyMovementType.Flying;
            def.category = EnemyCategory.Flying;
            def.behaviorKind = spec.behavior;
            def.hpMultiplier = spec.hp;
            def.bigKnockbackOnHit = false;
            def.enableVisualFacing = true;
            def.mileReward = spec.mile;
            def.visualScaleMultiplier = 1f;
            def.aiTier = EnemyAiTier.T1;
            def.stageIds = new[] { spec.stage };
            def.launchScale = spec.launch;
            def.knockbackScale = spec.knockback;
            def.idleHoverAmplitude = spec.hover;
            AssetDatabase.CreateAsset(def, path);
        }
        // 素材の参照・当たり判定は毎回同期(絵の差し替えで大きさが変わる)
        def.sprite = art.idle;
        def.runFrames = art.move != null && art.move.Length > 0 ? art.move : null;
        def.poses = art.poses;
        def.attackSprite = art.poses.attack != null ? art.poses.attack : art.poses.dive;
        def.defaultFacingRight = false; // 素材は左向き(右から左へ飛んでくる)
        def.bodyColliderScale = colScale;
        def.bodyColliderOffset = Vector2.zero;
        EditorUtility.SetDirty(def);
        Debug.Log($"AirEnemyDatabase: {(created ? "created" : "synced")} {spec.id} (move={def.runFrames?.Length ?? 0}, collider={colScale})");
    }
}
