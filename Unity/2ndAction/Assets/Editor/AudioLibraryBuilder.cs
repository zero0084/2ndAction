using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// AudioLibrary(Resources/Audio/AudioLibrary.asset)を作る(2026-09-29)。
// 既存の音(Audio/*.wav, Audio/SE/*.wav)はそのまま使い、足りない枠だけ仮音源(Audio/Placeholder/)を入れる。
// 既にアセットがある場合は上書きしない(Inspectorで差し替えた音を守る)。作り直すのはメニューの(overwrite)だけ。
public static class AudioLibraryBuilder
{
    const string AssetPath = "Assets/Resources/Audio/AudioLibrary.asset";
    const string A = "Assets/Audio/";
    const string P = "Assets/Audio/Placeholder/";

    [MenuItem("Tools/OneMoreMile/Audio/Build Audio Library (keep existing)")]
    public static void Build() => Build(false);

    [MenuItem("Tools/OneMoreMile/Audio/Rebuild Audio Library (overwrite)")]
    public static void BuildForce() => Build(true);

    public static void BuildBatch() => Build(false);

    static void Build(bool overwrite)
    {
        ConfigureImports();
        Directory.CreateDirectory("Assets/Resources/Audio");
        var existing = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetPath);
        if (existing != null && !overwrite) { Debug.Log("AudioLibraryBuilder: keep " + AssetPath); return; }
        var lib = Create();
        if (existing != null) { EditorUtility.CopySerialized(lib, existing); EditorUtility.SetDirty(existing); Debug.Log("AudioLibraryBuilder: overwrote " + AssetPath); }
        else { AssetDatabase.CreateAsset(lib, AssetPath); Debug.Log("AudioLibraryBuilder: created " + AssetPath); }
        AssetDatabase.SaveAssets();
    }

    static AudioClip C(string path)
    {
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (c == null) Debug.LogWarning("AudioLibraryBuilder: missing " + path);
        return c;
    }
    static AudioClip Bgm(string n) => C(P + "Bgm/" + n + ".wav");
    static AudioClip Amb(string n) => C(P + "Ambience/" + n + ".wav");
    static AudioClip Se(string n) => C(P + "SE/" + n + ".wav");
    static AudioClip OldSe(string n) => C(A + "SE/" + n + ".wav");

    static AmbienceSet Ambience(string loop, float loopVol, Vector2 interval, float shotVol, params string[] shots)
    {
        var a = new AmbienceSet { loop = Amb(loop), loopVolume = loopVol, interval = interval, oneShotVolume = shotVol };
        foreach (var s in shots) a.oneShots.Add(Amb(s));
        return a;
    }

    static SeEntry E(SeId id, float volume, float pitchJitter, float minInterval, params AudioClip[] clips)
    {
        var e = new SeEntry { id = id, volume = volume, pitchJitter = pitchJitter, minInterval = minInterval };
        foreach (var c in clips) if (c != null) e.clips.Add(c);
        return e;
    }

    static WeaponSeSet W(WeaponType w, AudioClip strong, float vol, params AudioClip[] normal)
    {
        var s = new WeaponSeSet { weapon = w, strong = strong, volume = vol };
        s.normal.AddRange(normal);
        return s;
    }

    static AudioLibrary Create()
    {
        var lib = ScriptableObject.CreateInstance<AudioLibrary>();
        lib.middleFrom = 10000f; lib.lateFrom = 70000f; lib.crossfadeSeconds = 1.6f;

        // ---- 道中BGM: Stage → 序盤/中盤/終盤 ----
        lib.stages.Add(new StageAudio { stageId = "sky_corridor", displayName = "Stage00 天空回廊",
            early = Bgm("bgm_sky_early"), middle = Bgm("bgm_sky_middle"), late = Bgm("bgm_sky_late"),
            ambience = Ambience("amb_sky_wind", 0.45f, new Vector2(8f, 16f), 0.35f, "amb_wind_whistle", "amb_bird_1") });
        lib.stages.Add(new StageAudio { stageId = "wasteland_road", displayName = "Stage01 荒野街道",
            early = C(A + "GameplayBgm.wav"), middle = Bgm("bgm_wasteland_middle"), late = Bgm("bgm_wasteland_late"),   // 序盤は既存の曲
            ambience = Ambience("amb_wasteland_wind", 0.35f, new Vector2(5f, 11f), 0.4f, "amb_bird_0", "amb_bird_2", "amb_grass_rustle") });
        lib.stages.Add(new StageAudio { stageId = "natural_cave", displayName = "Stage02 自然洞窟",
            early = Bgm("bgm_cave_early"), middle = Bgm("bgm_cave_middle"), late = Bgm("bgm_cave_late"),
            ambience = Ambience("amb_cave_air", 0.55f, new Vector2(4f, 9f), 0.45f, "amb_drip_0", "amb_drip_1", "amb_rock_far") });

        // ---- HOME(既存の曲) ----
        lib.homeBgm = C(A + "TitleBgm.wav");
        lib.homeAmbience = Ambience("amb_home_breeze", 0.3f, new Vector2(4f, 9f), 0.35f, "amb_bird_0", "amb_bird_1", "amb_bird_2");

        // ---- ボス/BONUS/ジングル ----
        lib.bossNormal = Bgm("bgm_boss_normal");
        lib.bossStrong = Bgm("bgm_boss_strong");
        lib.bossSpecial = Bgm("bgm_boss_special");
        lib.bossDeath = Bgm("bgm_boss_death");
        lib.bonusZoneBgm = Bgm("bgm_bonus_zone");
        lib.resultJingle = C(P + "Jingle/jingle_result.wav");
        lib.gameOverJingle = C(P + "Jingle/jingle_game_over.wav");

        // ---- SE(既存の音はそのまま、無い枠は仮音源) ----
        // 旧SE Pack(01〜05)は以前の音量補正(1/0.45)と同じ大きさにする
        const float Pack = 2.2f;
        var se = lib.se;
        se.Add(E(SeId.Jump, 1f, 0.03f, 0.03f, OldSe("JumpSe")));
        se.Add(E(SeId.DoubleJump, 1f, 0.03f, 0.03f, OldSe("DoubleJumpSe")));
        se.Add(E(SeId.Land, Pack, 0.04f, 0.05f, OldSe("05_landing")));
        se.Add(E(SeId.AttackUp, 0.9f, 0.04f, 0.04f, Se("atk_up_launch")));
        se.Add(E(SeId.AttackAir, 0.8f, 0.06f, 0.03f, Se("atk_air")));
        se.Add(E(SeId.AttackDown, 1f, 0.04f, 0.05f, Se("atk_down_slam")));
        se.Add(E(SeId.Hit, Pack, 0.03f, 0.02f, OldSe("01_attack_hit")));
        se.Add(E(SeId.StrongHit, 1.6f, 0.04f, 0.04f, Se("hit_strong")));
        se.Add(E(SeId.PlayerDamage, Pack, 0.02f, 0.1f, OldSe("02_player_damage")));
        se.Add(E(SeId.PlayerDeath, Pack, 0f, 0.5f, OldSe("04_player_death")));
        se.Add(E(SeId.EnemyAttack, 0.7f, 0.08f, 0.08f, Se("enemy_attack")));
        se.Add(E(SeId.EnemyHit, Pack, 0.03f, 0.02f, OldSe("01_attack_hit")));
        se.Add(E(SeId.EnemyDefeat, Pack, 0.04f, 0.03f, OldSe("03_enemy_defeat")));
        se.Add(E(SeId.BigEnemyAttack, 1f, 0.05f, 0.2f, Se("enemy_attack_big")));
        se.Add(E(SeId.BossAttack, 0.8f, 0.06f, 0.3f, Se("boss_attack")));
        se.Add(E(SeId.BossHit, 0.8f, 0.05f, 0.07f, Se("boss_hit")));
        se.Add(E(SeId.BossDefeat, 1.2f, 0f, 0.5f, Se("boss_defeat")));
        se.Add(E(SeId.BossFinalHit, 1.2f, 0f, 0.5f, Se("boss_final_hit")));
        se.Add(E(SeId.BossWarning, 0.8f, 0f, 0.5f, Se("boss_warning")));
        se.Add(E(SeId.BossAppear, 1f, 0f, 0.5f, Se("boss_appear")));
        se.Add(E(SeId.Milestone, 0.8f, 0f, 0.3f, Se("milestone")));
        se.Add(E(SeId.MilestoneClear, 0.9f, 0f, 0.3f, Se("milestone_clear")));
        se.Add(E(SeId.LevelUp, 0.8f, 0f, 0.3f, Se("level_up")));
        se.Add(E(SeId.Decide, 0.7f, 0.02f, 0.05f, Se("ui_decide")));
        se.Add(E(SeId.Cancel, 0.7f, 0.02f, 0.05f, Se("ui_cancel")));
        se.Add(E(SeId.CardSelect, 1f, 0.03f, 0.05f, OldSe("CardSelectSe")));
        se.Add(E(SeId.CardGet, 1f, 0f, 0.1f, OldSe("CardConfirmSe")));
        se.Add(E(SeId.CardFusion, 0.8f, 0f, 0.1f));      // 空 = 既存の合成演出の音(FusionSfx、手続き生成)を使う
        se.Add(E(SeId.FusionSuccess, 0.8f, 0f, 0.1f));
        se.Add(E(SeId.FusionFail, 0.8f, 0f, 0.1f));
        se.Add(E(SeId.StageSelect, 0.8f, 0.02f, 0.05f, Se("ui_stage_select")));
        se.Add(E(SeId.CharacterSelect, 0.8f, 0.02f, 0.05f, Se("ui_character_select")));
        se.Add(E(SeId.CountdownTick, 0.6f, 0f, 0.3f, Se("ui_countdown")));
        se.Add(E(SeId.RunStart, 0.8f, 0f, 0.5f, Se("ui_run_go")));
        se.Add(E(SeId.ScreenClose, 0.45f, 0.03f, 0.2f, Se("ui_screen_whoosh")));
        se.Add(E(SeId.ScreenOpen, 0.35f, 0.03f, 0.2f, Se("ui_screen_whoosh")));
        se.Add(E(SeId.Door, 0.8f, 0.02f, 0.2f, Se("home_door")));
        se.Add(E(SeId.DeckEdit, 0.8f, 0.03f, 0.1f, Se("home_deck_edit")));
        se.Add(E(SeId.Gacha, 0.9f, 0f, 0.3f, Se("home_gacha")));
        se.Add(E(SeId.Coin, 0.7f, 0.03f, 0.05f, Se("home_coin")));
        se.Add(E(SeId.UiTap, 1f, 0.03f, 0.05f, OldSe("CardSelectSe")));

        // ---- 攻撃音: 武器タイプ別(剣は既存の3段の振り音) ----
        lib.weaponSe.Add(W(WeaponType.Sword, OldSe("AttackSe3"), 1f, OldSe("AttackSe1"), OldSe("AttackSe2")));
        lib.weaponSe.Add(W(WeaponType.DualBlade, Se("atk_dual_blade_strong"), 0.8f, Se("atk_dual_blade")));
        lib.weaponSe.Add(W(WeaponType.Gun, Se("atk_gun_strong"), 0.7f, Se("atk_gun")));
        lib.weaponSe.Add(W(WeaponType.Bow, Se("atk_bow_strong"), 0.8f, Se("atk_bow")));
        lib.weaponSe.Add(W(WeaponType.Magic, Se("atk_magic_strong"), 0.7f, Se("atk_magic")));
        lib.weaponSe.Add(W(WeaponType.Strike, Se("atk_strike_strong"), 0.9f, Se("atk_strike")));
        lib.weaponSe.Add(W(WeaponType.Special, Se("atk_special_strong"), 0.8f, Se("atk_special")));
        void CW(string id, WeaponType w) => lib.characterWeapons.Add(new CharacterWeapon { characterId = id, weapon = w });
        CW("swordsman", WeaponType.Sword); CW("noble_lady", WeaponType.Sword);
        CW("dual_blade", WeaponType.DualBlade); CW("ninja", WeaponType.DualBlade);
        CW("gunslinger", WeaponType.Gun); CW("archer", WeaponType.Bow);
        CW("mage", WeaponType.Magic); CW("miko", WeaponType.Magic);
        CW("fighter", WeaponType.Strike); CW("dragonkin", WeaponType.Strike);
        CW("dragon_lancer", WeaponType.Special); CW("vampire", WeaponType.Special);
        return lib;
    }

    // 仮音源の取り込み設定: 曲=ストリーミング、環境音/ジングル=圧縮のままメモリ、SE=展開して即再生
    static void ConfigureImports()
    {
        AssetDatabase.Refresh();
        foreach (string dir in new[] { P + "Bgm", P + "Jingle", P + "Ambience", P + "SE" })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir, "*.wav"))
            {
                string path = f.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                bool music = dir.EndsWith("Bgm");
                bool longish = music || dir.EndsWith("Jingle") || (dir.EndsWith("Ambience") && Path.GetFileName(path).StartsWith("amb_") && new FileInfo(path).Length > 400000);
                s.loadType = music ? AudioClipLoadType.Streaming : longish ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = longish ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
                s.quality = 0.55f;
                imp.defaultSampleSettings = s;
                imp.forceToMono = !music;
                imp.loadInBackground = music;
                imp.SaveAndReimport();
            }
        }
    }
}
