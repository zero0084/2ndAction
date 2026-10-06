using System;
using System.Collections.Generic;
using UnityEngine;

// BGM/SE/環境音の割り当て表(2026-09-29)。Resources/Audio/AudioLibrary.asset。
// 音源の差し替えはこのアセットのInspectorでAudioClipを入れ替えるだけ(コードに曲名/パスを書かない)。
//   道中BGM: Stage → Early / Middle / Late(距離で自動切替、境界はここで変更)
//   HOME / ボス(通常・強敵・特殊 + 死神 + 個別の上書き) / BONUS ZONE / RESULT・GAME OVERのジングル
//   SE: SeIdごとの音(複数入れるとランダム)+ 音量/ピッチの揺らぎ + 連打間隔
//   プレイヤーの攻撃音: 武器タイプごと(通常/強)、キャラクター → 武器タイプの対応
[CreateAssetMenu(menuName = "OneMoreMile/Audio Library", fileName = "AudioLibrary")]
public class AudioLibrary : ScriptableObject
{
    [Header("道中BGM: 距離の境界(m)")]
    [Tooltip("この距離から中盤BGM")] public float middleFrom = 10000f;
    [Tooltip("この距離から終盤BGM")] public float lateFrom = 70000f;
    [Tooltip("曲が変わる時のクロスフェード(秒)")] public float crossfadeSeconds = 1.6f;

    [Header("道中BGM(ステージ別 序盤/中盤/終盤)")]
    public List<StageAudio> stages = new List<StageAudio>();

    [Header("HOME")]
    public AudioClip homeBgm;
    public AmbienceSet homeAmbience = new AmbienceSet();

    [Header("ボスBGM(共通3系統 + 死神)")]
    [Tooltip("1000mごとの通常ボス")] public AudioClip bossNormal;
    [Tooltip("5000mごとの強敵ボス")] public AudioClip bossStrong;
    [Tooltip("10000mごとの特殊ボス(神話級など)")] public AudioClip bossSpecial;
    [Tooltip("100,000mの死神(専用)")] public AudioClip bossDeath;
    [Tooltip("ラストダンジョンのボスラッシュ/最終ボス(2026-10-06)")] public AudioClip bossFinal;
    [Tooltip("特定のボスだけ専用曲にする(bossKey例: \"wasteland_road/Hydra\"、ステージを問わないなら \"*/Hydra\")")]
    public List<BossBgmOverride> bossOverrides = new List<BossBgmOverride>();

    [Header("BONUS ZONE(空なら道中BGMを少し速くするだけ)")]
    public AudioClip bonusZoneBgm;

    [Header("闘技場(2026-10-06、空ならボス曲)")]
    public AudioClip arenaBgm;

    [Header("曲の切り替え(秒、2026-10-06)")]
    [Tooltip("道中 → ボス曲")] public float bossInFade = 0.7f;
    [Tooltip("ボス曲 → 道中")] public float bossOutFade = 2.4f;
    [Tooltip("序盤 → 中盤 → 終盤")] public float phaseFade = 3.5f;
    [Tooltip("HOME ↔ ラン")] public float homeFade = 1.2f;

    [Header("曲ごとの音量補正(曲の大きさをそろえる。載っていない曲は1)")]
    public List<ClipGain> musicGains = new List<ClipGain>();

    [Header("高速時の風(2026-10-06、環境音の音量に従う)")]
    public AudioClip speedWind;
    [Tooltip("この速さ(km/h)から鳴り始める")] public float windFromKmh = 70f;
    [Tooltip("この速さで最大")] public float windFullKmh = 300f;
    [Range(0f, 1f)] public float windMaxVolume = 0.55f;
    public Vector2 windPitch = new Vector2(0.85f, 1.25f);

    [Header("音量の優先(2026-10-06)")]
    [Tooltip("重要なSE(FINISH/ボスの撃破/警告)が鳴った時にBGMをどこまで下げるか")] [Range(0f, 1f)] public float importantDuck = 0.55f;
    [Tooltip("下げる時間(秒)")] public float importantDuckSeconds = 0.7f;
    [Tooltip("同時に鳴るSEの数(これを超えると優先度の低い古い音から止める)")] public int seVoices = 20;

    [Header("RESULT / GAME OVER(ループしない短いジングル)")]
    public AudioClip resultJingle;
    public AudioClip gameOverJingle;

    [Header("SE")]
    public List<SeEntry> se = new List<SeEntry>();

    [Header("プレイヤーの攻撃音(武器タイプ別)")]
    public List<WeaponSeSet> weaponSe = new List<WeaponSeSet>();
    [Tooltip("キャラクター → 武器タイプ(載っていないキャラは剣)")]
    public List<CharacterWeapon> characterWeapons = new List<CharacterWeapon>();

    // ---- 検索 ----
    public StageAudio FindStage(string stageId)
    {
        foreach (var s in stages) if (s != null && s.stageId == stageId) return s;
        return stages.Count > 0 ? stages[0] : null;
    }

    public AudioClip StageBgm(string stageId, float distance)
    {
        var s = FindStage(stageId);
        if (s == null) return null;
        AudioClip c = distance >= lateFrom ? s.late : distance >= middleFrom ? s.middle : s.early;
        // 空きの枠は手前の段の曲を使う(3曲そろっていなくても鳴る)
        if (c == null) c = distance >= lateFrom && s.middle != null ? s.middle : s.early;
        return c;
    }

    public BgmPhase PhaseFor(float distance) => distance >= lateFrom ? BgmPhase.Late : distance >= middleFrom ? BgmPhase.Middle : BgmPhase.Early;

    public AudioClip BossBgm(BossBgmTier tier, string bossKey)
    {
        if (!string.IsNullOrEmpty(bossKey))
        {
            string kind = bossKey.Contains("/") ? bossKey.Substring(bossKey.IndexOf('/') + 1) : bossKey;
            foreach (var o in bossOverrides)
                if (o != null && o.clip != null && (o.bossKey == bossKey || o.bossKey == "*/" + kind)) return o.clip;
        }
        switch (tier)
        {
            case BossBgmTier.Death: return bossDeath != null ? bossDeath : bossSpecial;
            case BossBgmTier.Final: return bossFinal != null ? bossFinal : bossSpecial != null ? bossSpecial : bossStrong;
            case BossBgmTier.Special: return bossSpecial != null ? bossSpecial : bossStrong != null ? bossStrong : bossNormal;
            case BossBgmTier.Strong: return bossStrong != null ? bossStrong : bossNormal;
            default: return bossNormal;
        }
    }

    Dictionary<SeId, SeEntry> seMap;
    public SeEntry FindSe(SeId id)
    {
        if (seMap == null || seMap.Count != se.Count)
        {
            seMap = new Dictionary<SeId, SeEntry>();
            foreach (var e in se) if (e != null) seMap[e.id] = e;
        }
        return seMap.TryGetValue(id, out var r) ? r : null;
    }

    public float MusicGain(AudioClip clip)
    {
        if (clip == null) return 1f;
        foreach (var g in musicGains) if (g != null && g.clip == clip) return g.gain;
        return 1f;
    }

    public WeaponType WeaponFor(string characterId)
    {
        foreach (var c in characterWeapons) if (c != null && c.characterId == characterId) return c.weapon;
        return WeaponType.Sword;
    }

    public WeaponSeSet FindWeapon(WeaponType w)
    {
        foreach (var s in weaponSe) if (s != null && s.weapon == w) return s;
        foreach (var s in weaponSe) if (s != null && s.weapon == WeaponType.Sword) return s;
        return null;
    }

    static AudioLibrary cached;
    public static AudioLibrary Load()
    {
        if (cached == null) cached = Resources.Load<AudioLibrary>("Audio/AudioLibrary");
        return cached;
    }
}

public enum BgmPhase { Early, Middle, Late }
public enum BossBgmTier { Normal, Strong, Special, Death, Final }
// SE の優先度(同時に鳴る数を超えた時、低いものから止める。Critical は BGM を一瞬下げる)
public enum SePriority { Low = 0, Normal = 1, High = 2, Critical = 3 }
// 2026-10-06: ランス/忍者/爪(竜人)/吸血鬼/巫女(霊術)を独立した系統に(数値は保存されるので末尾へ)
public enum WeaponType { Sword, DualBlade, Gun, Bow, Magic, Strike, Special, Lance, Ninja, Claw, Blood, Spirit }

// 数値はアセットに保存されるので、追加は必ず末尾へ。
public enum SeId
{
    // プレイヤー
    Jump, DoubleJump, Land, AttackUp, AttackAir, AttackDown, Hit, StrongHit, PlayerDamage, PlayerDeath,
    // 敵/ボス
    EnemyAttack, EnemyHit, EnemyDefeat, BigEnemyAttack, BossAttack, BossHit, BossDefeat, BossFinalHit,
    BossWarning, BossAppear, Milestone, MilestoneClear, LevelUp,
    // UI
    Decide, Cancel, CardSelect, CardGet, CardFusion, FusionSuccess, FusionFail, StageSelect, CharacterSelect,
    CountdownTick, RunStart, ScreenClose, ScreenOpen,
    // HOME
    Door, DeckEdit, Gacha, Coin, UiTap,
    // ---- 2026-10-06 音の再設計(追加は必ず末尾へ) ----
    // 攻撃の手応えの段: Hit < HitLaunch/HitProjectile < StrongHit < Slam < FinishHit < BossFinishHit
    HitLaunch, HitProjectile, SlamImpact, LandHeavy, FinishHit, FinishBurst,
    BossFinishHit, BossCollapse, BossDissolve,
    // 予兆(聞こえること)
    EnemyTelegraph, EnemyShot, BossTelegraph, BossTelegraphHeavy, BossCharge, BossBreak, BossPhase, BossUltimate,
    // カード/COMBO/ULTIMATE/合成
    CardAppear, CardFlip, CardHover, CardRare, MasteryUp, MaxLevel, ComboFormed,
    FinalEvolutionReady, FinalEvolutionActivate, UltimateReady, UltimateActivate, UltimateImpact,
    // 疾走/MILE/速さ
    RingPass, RingBurst, MileGet, SonicBoom,
    // UI
    UiOpen, UiClose, UiTab, UiToggle, UiDeny, UiSlider, Pause, Resume,
    // 闘技場/マルチ
    ArenaStart, ArenaWin, ArenaLose, NetJoin, NetLeave, NetDown, NetRevive,
}

[Serializable]
public class StageAudio
{
    public string stageId = "";
    public string displayName = "";
    [Tooltip("序盤(0m〜)")] public AudioClip early;
    [Tooltip("中盤(middleFrom〜)")] public AudioClip middle;
    [Tooltip("終盤(lateFrom〜)")] public AudioClip late;
    public AmbienceSet ambience = new AmbienceSet();
}

[Serializable]
public class AmbienceSet
{
    [Tooltip("常に小さく流れる環境音(風/洞窟の空気など)")] public AudioClip loop;
    [Range(0f, 1f)] public float loopVolume = 0.5f;
    [Tooltip("ときどき鳴る環境音(鳥/水滴/遠くの岩音など、ランダム)")] public List<AudioClip> oneShots = new List<AudioClip>();
    [Tooltip("ときどき鳴る間隔(秒)")] public Vector2 interval = new Vector2(5f, 11f);
    [Range(0f, 1f)] public float oneShotVolume = 0.45f;
}

[Serializable]
public class BossBgmOverride
{
    public string bossKey = "";
    public AudioClip clip;
}

[Serializable]
public class SeEntry
{
    public SeId id;
    [Tooltip("複数あればランダムに1つ")] public List<AudioClip> clips = new List<AudioClip>();
    [Range(0f, 2f)] public float volume = 1f;
    [Tooltip("ピッチの揺らぎ(±)")] [Range(0f, 0.3f)] public float pitchJitter = 0.04f;
    [Tooltip("音量の揺らぎ(±)")] [Range(0f, 0.5f)] public float volumeJitter = 0.06f;
    [Tooltip("同じSEをこれより短い間隔では重ねない(秒)")] public float minInterval = 0.03f;
    [Tooltip("優先度(同時に鳴る数を超えた時に低いものから止める。Critical は BGM を一瞬下げる)")] public SePriority priority = SePriority.Normal;
    [Tooltip("同じSEが同時に鳴る最大数")] public int maxVoices = 3;
}

[Serializable]
public class ClipGain
{
    public AudioClip clip;
    [Range(0f, 3f)] public float gain = 1f;
}

[Serializable]
public class WeaponSeSet
{
    public WeaponType weapon;
    [Tooltip("通常攻撃(コンボの1段目/2段目…を順に使う)")] public List<AudioClip> normal = new List<AudioClip>();
    [Tooltip("強攻撃(コンボの締め/溜め)")] public AudioClip strong;
    [Range(0f, 2f)] public float volume = 1f;
}

[Serializable]
public class CharacterWeapon
{
    public string characterId = "";
    public WeaponType weapon;
}
