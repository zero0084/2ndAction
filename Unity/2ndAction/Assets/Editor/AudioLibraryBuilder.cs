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
        if (existing != null && !overwrite)
        {
            // 既存のアセットは上書きしない。ただし後から増えたステージの道中BGM枠だけは足す(LAST CORRIDOR等)。
            bool added = false;
            foreach (var st in Create().stages)
            {
                if (existing.stages.Exists(x => x != null && x.stageId == st.stageId)) continue;
                existing.stages.Add(st); added = true;
                Debug.Log("AudioLibraryBuilder: added stage " + st.stageId);
            }
            if (added) { EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets(); }
            Debug.Log("AudioLibraryBuilder: keep " + AssetPath);
            return;
        }
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

        // 音の再設計(2026-10-06): 素材は Tools/audio/gen_audio_v2.py で大きさをそろえた版(*_n)を使う。
        //  段: 重要なSE(FINISH/ボスの撃破/警告/ULTIMATE、Critical)> 戦闘のSE > BGM > 環境音。
        // ---- 道中BGM: Stage → 序盤/中盤/終盤 ----
        lib.stages.Add(new StageAudio { stageId = "sky_corridor", displayName = "Stage00 天空回廊",
            early = Bgm("bgm_sky_early_n"), middle = Bgm("bgm_sky_middle_n"), late = Bgm("bgm_sky_late_n"),
            ambience = Ambience("amb_sky_wind_n", 0.5f, new Vector2(7f, 14f), 0.4f, "amb_wind_whistle_n", "amb_bird_1_n", "amb_hawk", "amb_chime", "amb_far_thunder") });
        lib.stages.Add(new StageAudio { stageId = "wasteland_road", displayName = "Stage01 荒野街道",
            early = C(A + "GameplayBgm.wav"), middle = Bgm("bgm_wasteland_middle_n"), late = Bgm("bgm_wasteland_late_n"),   // 序盤は既存の曲
            ambience = Ambience("amb_wasteland_wind_n", 0.45f, new Vector2(5f, 11f), 0.4f, "amb_bird_0_n", "amb_bird_2_n", "amb_grass_rustle_n", "amb_hawk") });
        lib.stages.Add(new StageAudio { stageId = "natural_cave", displayName = "Stage02 自然洞窟",
            early = Bgm("bgm_cave_early_n"), middle = Bgm("bgm_cave_middle_n"), late = Bgm("bgm_cave_late_n"),
            ambience = Ambience("amb_cave_air_n", 0.6f, new Vector2(4f, 9f), 0.45f, "amb_drip_0_n", "amb_drip_1_n", "amb_rock_far_n", "amb_bat_flutter") });
        lib.stages.Add(new StageAudio { stageId = "last_corridor", displayName = "LAST CORRIDOR(ラストダンジョン)",
            early = Bgm("bgm_last_early_n"), middle = Bgm("bgm_last_middle_n"), late = Bgm("bgm_last_late_n"),
            ambience = Ambience("amb_last_hall_n", 0.55f, new Vector2(6f, 12f), 0.4f, "amb_rock_far_n", "amb_wind_whistle_n", "amb_chain", "amb_drone_swell") });

        // ---- HOME(既存の曲) ----
        lib.homeBgm = C(A + "TitleBgm.wav");
        lib.homeAmbience = Ambience("amb_home_breeze_n", 0.4f, new Vector2(4f, 9f), 0.4f, "amb_bird_0_n", "amb_bird_1_n", "amb_bird_2_n");

        // ---- ボス/BONUS/闘技場/ジングル ----
        lib.bossNormal = Bgm("bgm_boss_normal_n");
        lib.bossStrong = Bgm("bgm_boss_strong_n");
        lib.bossSpecial = Bgm("bgm_boss_special_n");
        lib.bossDeath = Bgm("bgm_boss_death_n");
        lib.bossFinal = Bgm("bgm_boss_final_n");
        lib.bonusZoneBgm = Bgm("bgm_bonus_zone_n");
        lib.arenaBgm = Bgm("bgm_arena_n");
        lib.resultJingle = C(P + "Jingle/jingle_result.wav");
        lib.gameOverJingle = C(P + "Jingle/jingle_game_over.wav");
        lib.bossInFade = 0.7f; lib.bossOutFade = 2.4f; lib.phaseFade = 3.5f; lib.homeFade = 1.2f;
        // 既存の2曲は大きさが違う(測定: TitleBgm -17.2dB / GameplayBgm -19.3dB、仮音源は -16dB にそろえた)
        lib.musicGains.Add(new ClipGain { clip = C(A + "TitleBgm.wav"), gain = 1.15f });
        lib.musicGains.Add(new ClipGain { clip = C(A + "GameplayBgm.wav"), gain = 1.45f });
        lib.musicGains.Add(new ClipGain { clip = C(P + "Jingle/jingle_result.wav"), gain = 0.6f });
        lib.musicGains.Add(new ClipGain { clip = C(P + "Jingle/jingle_game_over.wav"), gain = 0.6f });

        // ---- 高速時の風 / 優先度 ----
        lib.speedWind = Amb("amb_speed_wind");
        lib.windFromKmh = 70f; lib.windFullKmh = 300f; lib.windMaxVolume = 0.55f; lib.windPitch = new Vector2(0.85f, 1.25f);
        lib.importantDuck = 0.55f; lib.importantDuckSeconds = 0.7f; lib.seVoices = 20;

        // ---- SE ----
        const SePriority Lo = SePriority.Low, No = SePriority.Normal, Hi = SePriority.High, Cr = SePriority.Critical;
        var se = lib.se;
        void S(SeId id, float vol, float pj, float gap, SePriority pri, int max, params AudioClip[] clips) { var e = E(id, vol, pj, gap, clips); e.priority = pri; e.maxVoices = max; se.Add(e); }
        // プレイヤー(移動は控えめ、既存の音の性格は残す)
        S(SeId.Jump, 1f, 0.04f, 0.03f, Lo, 2, Se("old_jump_n"));
        S(SeId.DoubleJump, 1f, 0.04f, 0.03f, Lo, 2, Se("old_double_jump_n"));
        S(SeId.Land, 1f, 0.05f, 0.05f, Lo, 2, Se("old_landing_n"), Se("mv_land"));
        S(SeId.LandHeavy, 1f, 0.03f, 0.1f, No, 2, Se("mv_land_heavy"));
        S(SeId.AttackUp, 1f, 0.04f, 0.04f, No, 2, Se("atk_up_n"));
        S(SeId.AttackAir, 1f, 0.06f, 0.03f, No, 3, Se("atk_air_n"));
        S(SeId.AttackDown, 1f, 0.04f, 0.05f, No, 2, Se("atk_down_n"));
        // 手応えの段: Hit < HitProjectile < HitLaunch < StrongHit < SlamImpact < FinishHit < BossFinishHit
        S(SeId.Hit, 1f, 0.05f, 0.02f, No, 4, Se("hit_0"), Se("hit_1"), Se("hit_2"));
        S(SeId.HitProjectile, 1f, 0.06f, 0.02f, No, 4, Se("hit_projectile_0"), Se("hit_projectile_1"));
        S(SeId.HitLaunch, 1f, 0.04f, 0.04f, Hi, 2, Se("hit_launch"));
        S(SeId.StrongHit, 1f, 0.04f, 0.04f, Hi, 3, Se("hit_strong_n"));
        S(SeId.SlamImpact, 1f, 0.03f, 0.06f, Hi, 2, Se("hit_slam"));
        S(SeId.FinishHit, 1f, 0.03f, 0.05f, Hi, 3, Se("finish_hit"));
        S(SeId.FinishBurst, 0.8f, 0.06f, 0.04f, No, 3, Se("finish_burst"));
        S(SeId.BossFinishHit, 1f, 0f, 0.4f, Cr, 1, Se("boss_finish_hit"));
        S(SeId.BossCollapse, 1f, 0.03f, 0.3f, Hi, 2, Se("boss_collapse"));
        S(SeId.BossDissolve, 0.9f, 0.02f, 0.3f, No, 2, Se("boss_dissolve"));
        S(SeId.PlayerDamage, 1f, 0.03f, 0.1f, Cr, 1, Se("old_player_damage_n"));
        S(SeId.PlayerDeath, 1f, 0f, 0.5f, Cr, 1, Se("old_player_death_n"));
        // 敵(画面の位置で鳴らす。予兆は聞こえるように)
        S(SeId.EnemyAttack, 1f, 0.08f, 0.08f, No, 3, Se("enemy_attack_n"));
        S(SeId.BigEnemyAttack, 1f, 0.05f, 0.2f, Hi, 2, Se("enemy_attack_big_n"));
        S(SeId.EnemyHit, 1f, 0.05f, 0.02f, No, 3, Se("hit_0"), Se("hit_2"));
        S(SeId.EnemyDefeat, 1f, 0.06f, 0.03f, No, 4, Se("enemy_defeat_0"), Se("enemy_defeat_1"));
        S(SeId.EnemyTelegraph, 1f, 0.05f, 0.12f, No, 2, Se("enemy_telegraph"));
        S(SeId.EnemyShot, 1f, 0.08f, 0.06f, Lo, 3, Se("enemy_shot"));
        // ボス
        S(SeId.BossAttack, 1f, 0.06f, 0.3f, Hi, 2, Se("boss_attack_n"));
        S(SeId.BossHit, 1f, 0.05f, 0.07f, Hi, 2, Se("boss_hit_n"));
        S(SeId.BossDefeat, 1f, 0f, 0.5f, Hi, 1, Se("boss_defeat_n"));
        S(SeId.BossFinalHit, 1f, 0f, 0.5f, Cr, 1, Se("boss_final_hit_n"));
        S(SeId.BossWarning, 1f, 0f, 0.5f, Cr, 1, Se("boss_warning_n"));
        S(SeId.BossAppear, 1f, 0f, 0.5f, Hi, 1, Se("boss_appear_n"));
        S(SeId.BossTelegraph, 1f, 0.04f, 0.15f, Hi, 2, Se("boss_telegraph"));
        S(SeId.BossTelegraphHeavy, 1f, 0.03f, 0.25f, Cr, 1, Se("boss_telegraph_heavy"));
        S(SeId.BossCharge, 1f, 0.03f, 0.3f, Hi, 1, Se("boss_charge"));
        S(SeId.BossBreak, 1f, 0.03f, 0.3f, Cr, 1, Se("boss_break"));
        S(SeId.BossPhase, 1f, 0f, 0.5f, Cr, 1, Se("boss_phase"));
        S(SeId.BossUltimate, 1f, 0f, 0.5f, Cr, 1, Se("boss_ultimate"));
        // 節目/成長
        S(SeId.Milestone, 1f, 0f, 0.3f, Hi, 1, Se("milestone_n"));
        S(SeId.MilestoneClear, 1f, 0f, 0.3f, Hi, 1, Se("milestone_clear_n"));
        S(SeId.LevelUp, 1f, 0f, 0.3f, Hi, 1, Se("level_up_n"));
        // カード/COMBO/ULTIMATE/合成
        S(SeId.CardSelect, 1f, 0.03f, 0.05f, No, 2, Se("old_CardSelectSe_n"));
        S(SeId.CardGet, 1f, 0f, 0.1f, Hi, 1, Se("card_get"));
        S(SeId.CardAppear, 1f, 0.03f, 0.1f, No, 2, Se("card_appear"));
        S(SeId.CardFlip, 1f, 0.06f, 0.04f, No, 3, Se("card_flip"), Se("old_CardFlipSe_n"));
        S(SeId.CardHover, 1f, 0.04f, 0.04f, Lo, 1, Se("card_hover"));
        S(SeId.CardRare, 1f, 0f, 0.2f, Hi, 1, Se("card_rare"));
        S(SeId.CardFusion, 1f, 0f, 0.1f, No, 1, Se("fusion_charge"));
        S(SeId.FusionSuccess, 1f, 0f, 0.1f, Hi, 1, Se("fusion_success"));
        S(SeId.FusionFail, 1f, 0f, 0.1f, No, 1, Se("fusion_fail"));
        S(SeId.MasteryUp, 1f, 0f, 0.2f, Hi, 1, Se("mastery_up"));
        S(SeId.MaxLevel, 1f, 0f, 0.3f, Hi, 1, Se("max_level"));
        S(SeId.ComboFormed, 1f, 0.02f, 0.2f, Hi, 1, Se("combo_formed"));
        S(SeId.FinalEvolutionReady, 1f, 0f, 0.4f, Hi, 1, Se("fe_ready"));
        S(SeId.FinalEvolutionActivate, 1f, 0f, 0.4f, Cr, 1, Se("fe_activate"));
        S(SeId.UltimateReady, 1f, 0f, 0.4f, Hi, 1, Se("ult_ready"));
        S(SeId.UltimateActivate, 1f, 0f, 0.4f, Cr, 1, Se("ult_activate"));
        S(SeId.UltimateImpact, 1f, 0.03f, 0.12f, Cr, 2, Se("ult_impact"));
        // 疾走/MILE/速さ
        S(SeId.RingPass, 1f, 0.03f, 0.06f, No, 2, Se("ring_pass"));
        S(SeId.RingBurst, 1f, 0.02f, 0.1f, Hi, 1, Se("ring_burst"));
        S(SeId.MileGet, 1f, 0.03f, 0.08f, No, 2, Se("mile_get"));
        S(SeId.SonicBoom, 1f, 0.03f, 0.8f, Hi, 1, Se("sonic_boom"));
        // UI
        S(SeId.Decide, 1f, 0.02f, 0.05f, No, 2, Se("ui_decide_n"));
        S(SeId.Cancel, 1f, 0.02f, 0.05f, No, 2, Se("ui_cancel_n"));
        S(SeId.StageSelect, 1f, 0.02f, 0.05f, No, 1, Se("ui_stage_select_n"));
        S(SeId.CharacterSelect, 1f, 0.02f, 0.05f, No, 1, Se("ui_character_select_n"));
        S(SeId.CountdownTick, 1f, 0f, 0.3f, Hi, 1, Se("ui_countdown_n"));
        S(SeId.RunStart, 1f, 0f, 0.5f, Hi, 1, Se("ui_run_go_n"));
        S(SeId.ScreenClose, 1f, 0.03f, 0.2f, Lo, 1, Se("ui_screen_whoosh_n"));
        S(SeId.ScreenOpen, 0.8f, 0.03f, 0.2f, Lo, 1, Se("ui_screen_whoosh_n"));
        S(SeId.Door, 1f, 0.02f, 0.2f, No, 1, Se("home_door_n"));
        S(SeId.DeckEdit, 1f, 0.03f, 0.1f, No, 1, Se("home_deck_edit_n"));
        S(SeId.Gacha, 1f, 0f, 0.3f, No, 1, Se("home_gacha_n"));
        S(SeId.Coin, 1f, 0.03f, 0.05f, No, 2, Se("home_coin_n"));
        S(SeId.UiTap, 1f, 0.03f, 0.05f, Lo, 1, Se("ui_tap"));
        S(SeId.UiOpen, 1f, 0.02f, 0.08f, No, 1, Se("ui_open"));
        S(SeId.UiClose, 1f, 0.02f, 0.08f, No, 1, Se("ui_close"));
        S(SeId.UiTab, 1f, 0.02f, 0.05f, No, 1, Se("ui_tab"));
        S(SeId.UiToggle, 1f, 0.02f, 0.05f, No, 1, Se("ui_toggle"));
        S(SeId.UiDeny, 1f, 0f, 0.15f, No, 1, Se("ui_deny"));
        S(SeId.UiSlider, 1f, 0.02f, 0.06f, Lo, 1, Se("ui_slider"));
        S(SeId.Pause, 1f, 0f, 0.2f, Hi, 1, Se("ui_pause"));
        S(SeId.Resume, 1f, 0f, 0.2f, Hi, 1, Se("ui_resume"));
        // 闘技場/マルチ
        S(SeId.ArenaStart, 1f, 0f, 0.5f, Cr, 1, Se("arena_start"));
        S(SeId.ArenaWin, 1f, 0f, 0.5f, Cr, 1, Se("arena_win"));
        S(SeId.ArenaLose, 1f, 0f, 0.5f, Hi, 1, Se("arena_lose"));
        S(SeId.NetJoin, 1f, 0f, 0.3f, Hi, 1, Se("net_join"));
        S(SeId.NetLeave, 1f, 0f, 0.3f, Hi, 1, Se("net_leave"));
        S(SeId.NetDown, 1f, 0f, 0.3f, Hi, 1, Se("net_down"));
        S(SeId.NetRevive, 1f, 0f, 0.3f, Hi, 1, Se("net_revive"));

        // ---- 攻撃音: 武器タイプ別(振りは2種類を交互、締めは強) ----
        lib.weaponSe.Add(W(WeaponType.Sword, Se("atk_sword_finisher"), 1f, Se("old_AttackSe1_n"), Se("old_AttackSe2_n"), Se("atk_sword_0")));
        lib.weaponSe.Add(W(WeaponType.DualBlade, Se("atk_dual_blade_strong_n"), 1f, Se("atk_dual_blade_0"), Se("atk_dual_blade_1")));
        lib.weaponSe.Add(W(WeaponType.Gun, Se("atk_gun_strong_n"), 1f, Se("atk_gun_0"), Se("atk_gun_1")));
        lib.weaponSe.Add(W(WeaponType.Bow, Se("atk_bow_strong_n"), 1f, Se("atk_bow_0"), Se("atk_bow_1")));
        lib.weaponSe.Add(W(WeaponType.Magic, Se("atk_magic_strong_n"), 1f, Se("atk_magic_0"), Se("atk_magic_1")));
        lib.weaponSe.Add(W(WeaponType.Strike, Se("atk_strike_strong_n"), 1f, Se("atk_strike_0"), Se("atk_strike_1")));
        lib.weaponSe.Add(W(WeaponType.Special, Se("atk_special_strong_n"), 1f, Se("atk_special_0"), Se("atk_special_1")));
        lib.weaponSe.Add(W(WeaponType.Lance, Se("atk_lance_strong"), 1f, Se("atk_lance_0"), Se("atk_lance_1")));
        lib.weaponSe.Add(W(WeaponType.Ninja, Se("atk_ninja_strong"), 1f, Se("atk_ninja_0"), Se("atk_ninja_1")));
        lib.weaponSe.Add(W(WeaponType.Claw, Se("atk_claw_strong"), 1f, Se("atk_claw_0"), Se("atk_claw_1")));
        lib.weaponSe.Add(W(WeaponType.Blood, Se("atk_blood_strong"), 1f, Se("atk_blood_0"), Se("atk_blood_1")));
        lib.weaponSe.Add(W(WeaponType.Spirit, Se("atk_spirit_strong"), 1f, Se("atk_spirit_0"), Se("atk_spirit_1")));
        void CW(string id, WeaponType w) => lib.characterWeapons.Add(new CharacterWeapon { characterId = id, weapon = w });
        // 12人 → 11系統(黒剣士/お嬢様騎士は剣)
        CW("swordsman", WeaponType.Sword); CW("noble_lady", WeaponType.Sword);
        CW("dual_blade", WeaponType.DualBlade); CW("ninja", WeaponType.Ninja);
        CW("gunslinger", WeaponType.Gun); CW("archer", WeaponType.Bow);
        CW("mage", WeaponType.Magic); CW("miko", WeaponType.Spirit);
        CW("fighter", WeaponType.Strike); CW("dragonkin", WeaponType.Claw);
        CW("dragon_lancer", WeaponType.Lance); CW("vampire", WeaponType.Blood);
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
                // 音の再設計(2026-10-06): 取り込み時の Normalize(ピークを0dBへ)を切る。素材は gen_audio_v2.py で
                // 種類ごとの大きさ(手応えの段)にそろえてあるので、ここで揃え直されると段が消える
                var so = new SerializedObject(imp);
                var np = so.FindProperty("m_Normalize") ?? so.FindProperty("normalize");
                if (np != null) { np.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Debug.LogWarning("AudioLibraryBuilder: normalize property not found");
                imp.SaveAndReimport();
            }
        }
    }
}
