using System.Collections.Generic;
using UnityEngine;

// 音の再生(2026-09-29 全面整理)。
// ・どの音を鳴らすかはAudioLibrary(Resources/Audio/AudioLibrary.asset)に集約し、ここは「鳴らし方」だけを持つ。
//   BGMはどの曲を流すかをBgmDirectorが毎フレーム決め、ここへPlayBgmで渡す(同じ曲なら再スタートしない)。
// ・BGM: 2つのAudioSourceでクロスフェード(時間はunscaled=ポーズ/カード選択中も止まらず、多重にもならない)。
//   ボス演出のDuck、BONUS ZONEのピッチ、死亡時のFadeOutは曲の切り替えとは別の倍率として掛ける。
// ・ジングル(RESULT/GAME OVER): ループしない専用のAudioSource(BGMの音量設定に従う)。
// ・SE: SeIdで指定(複数素材ならランダム、ピッチ/音量を少し揺らす、同じSEの連打間隔)。攻撃音は武器タイプ別。
// ・環境音: ステージ/HOMEごとのループ + ときどき鳴る単発(BGMとは別の音量)。
// ・音量: Master/BGM/SE/環境音の4つ(0〜1の連続値、PlayerPrefsに保存。2026-10-01に0〜4段階から移行)。0なら完全に無音。
//   全体ミュートは音量とは別のフラグ(解除するとミュート前の音量に戻る)。
// ・オンライン: 同期しない(各端末でローカルに鳴らす)。
// 以前からある呼び出し(PlayJump/PlayAttack/PlayAttackHit/PlaySfx等)はそのまま使える。
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    const string MasterVolumeLevelKey = "MasterVolumeLevel";
    const string BgmVolumeLevelKey = "BgmVolumeLevel";
    const string SfxVolumeLevelKey = "SfxVolumeLevel";
    const string EnvVolumeLevelKey = "EnvVolumeLevel";

    // 5段階(0=無音 .. 4=最大)
    public const int MaxVolumeLevel = 4;
    const float BaseBgmVolume = 0.35f;
    const float BaseSfxVolume = 0.45f;
    const float BaseEnvVolume = 0.5f;
    const int DefaultVolumeLevel = MaxVolumeLevel;

    // ---- 以前からの割り当て(AudioLibraryに無い時の予備。SceneBuilderが入れる) ----
    public AudioClip titleBgm;
    public AudioClip gameplayBgm;
    public AudioClip jumpSe;
    public AudioClip doubleJumpSe;
    public AudioClip attackSe1;
    public AudioClip attackSe2;
    public AudioClip attackSe3;
    public AudioClip landSe;
    [Header("Game Feel - Restrained SE Pack")]
    public AudioClip attackHitSe;
    public AudioClip playerDamageSe;
    public AudioClip enemyDefeatSe;
    public AudioClip playerDeathSe;
    public AudioClip landingSe;

    public AudioLibrary Library { get; private set; }

    // 設定画面(2026-10-01): スライダーで細かく変えられるよう0〜1の連続値で持つ(保存もこの値だけ)。
    // 旧い0〜4段階の値(…VolumeLevelキー)は、新しいキーが無い時に一度だけ読み替える。
    const string MasterVolumeKey = "MasterVolume";
    const string BgmVolumeKey = "BgmVolume";
    const string SfxVolumeKey = "SfxVolume";
    const string EnvVolumeKey = "EnvVolume";
    const string MutedKey = "AudioMuted";
    public float MasterVolume { get; private set; } = 1f;
    public float BgmVolume { get; private set; } = 1f;
    public float SfxVolume { get; private set; } = 1f;
    public float EnvVolume { get; private set; } = 1f;
    public bool Muted { get; private set; }
    // 旧API(0〜4段階)。テスト/旧UIの互換用に連続値から計算する。
    public int MasterVolumeLevel => Mathf.RoundToInt(MasterVolume * MaxVolumeLevel);
    public int BgmVolumeLevel => Mathf.RoundToInt(BgmVolume * MaxVolumeLevel);
    public int SfxVolumeLevel => Mathf.RoundToInt(SfxVolume * MaxVolumeLevel);
    public int EnvVolumeLevel => Mathf.RoundToInt(EnvVolume * MaxVolumeLevel);
    public bool BgmEnabled => !Muted && BgmVolume > 0.001f && MasterVolume > 0.001f;
    public bool SfxEnabled => !Muted && SfxVolume > 0.001f && MasterVolume > 0.001f;
    float Master => Muted ? 0f : MasterVolume;

    AudioSource[] bgm = new AudioSource[2];
    float[] bgmWeight = new float[2];
    int bgmCurrent;
    float bgmFadeSpeed = 1f;
    AudioSource jingleSource;
    AudioSource sfxSource;
    AudioSource envLoop, envShot;
    public AudioClip CurrentBgm { get; private set; }
    public AudioClip CurrentJingle { get; private set; }

    void Awake()
    {
        Instance = this;
        Library = AudioLibrary.Load();
        MasterVolume = LoadVolume(MasterVolumeKey, MasterVolumeLevelKey);
        BgmVolume = LoadVolume(BgmVolumeKey, BgmVolumeLevelKey);
        SfxVolume = LoadVolume(SfxVolumeKey, SfxVolumeLevelKey);
        EnvVolume = LoadVolume(EnvVolumeKey, EnvVolumeLevelKey);
        Muted = SaveStore.GetInt(MutedKey, 0) != 0;

        // 素材が無い場合の最後の予備(手続き生成)
        if (titleBgm == null) titleBgm = AudioFactory.CreateTitleBgm();
        if (gameplayBgm == null) gameplayBgm = AudioFactory.CreateGameplayBgm();
        if (jumpSe == null) jumpSe = AudioFactory.CreateJumpSe();
        if (doubleJumpSe == null) doubleJumpSe = AudioFactory.CreateJumpSe();
        if (attackSe1 == null) attackSe1 = AudioFactory.CreateAttackSe();
        if (attackSe2 == null) attackSe2 = AudioFactory.CreateAttackSe();
        if (attackSe3 == null) attackSe3 = AudioFactory.CreateAttackSe();

        for (int i = 0; i < 2; i++) { bgm[i] = gameObject.AddComponent<AudioSource>(); bgm[i].loop = true; bgm[i].playOnAwake = false; bgm[i].volume = 0f; }
        jingleSource = gameObject.AddComponent<AudioSource>(); jingleSource.loop = false; jingleSource.playOnAwake = false;
        sfxSource = gameObject.AddComponent<AudioSource>(); sfxSource.loop = false; sfxSource.playOnAwake = false;
        envLoop = gameObject.AddComponent<AudioSource>(); envLoop.loop = true; envLoop.playOnAwake = false; envLoop.volume = 0f;
        envShot = gameObject.AddComponent<AudioSource>(); envShot.loop = false; envShot.playOnAwake = false;
        ApplyVolumes();

        // どの曲/環境音を流すかを決める係(HOME/ステージの距離/ボス/RESULT)
        if (GetComponent<BgmDirector>() == null) gameObject.AddComponent<BgmDirector>();
    }

    // ===================================================================== //
    // BGM
    // ===================================================================== //
    // 以前の入口(BgmDirectorが状態から曲を決めるので、呼ばれたら判断をやり直すだけ)
    public void PlayTitleBgm() { var d = GetComponent<BgmDirector>(); if (d != null) d.Refresh(); }
    public void PlayGameplayBgm() { var d = GetComponent<BgmDirector>(); if (d != null) d.Refresh(); }

    // 曲を流す。同じ曲が流れていれば何もしない(再スタートしない)。違う曲ならクロスフェード。
    public void PlayBgm(AudioClip clip, float fadeSeconds = -1f)
    {
        if (fadeSeconds < 0f) fadeSeconds = Library != null ? Library.crossfadeSeconds : 1.5f;
        // 同じ曲なら何もしない(FadeOutBgm中でも戻さない: 死亡後に同じ道中曲を要求され続けても静かなまま)
        if (clip == CurrentBgm && (clip == null || bgm[bgmCurrent].isPlaying)) return;
        CurrentBgm = clip;
        fadeOutTarget = 1f; fadeOutMul = 1f;
        StopJingle();
        if (clip == null) { bgmFadeSpeed = 1f / Mathf.Max(0.05f, fadeSeconds); bgmTargetSilent = true; return; }
        bgmTargetSilent = false;
        int next = 1 - bgmCurrent;
        // 次に使う側が別の曲を鳴らしていたら止めてから
        bgm[next].Stop();
        bgm[next].clip = clip;
        bgm[next].pitch = bgmPitch;
        bgm[next].Play();
        bgmWeight[next] = fadeSeconds <= 0.01f ? 1f : 0f;
        if (fadeSeconds <= 0.01f) { bgmWeight[bgmCurrent] = 0f; bgm[bgmCurrent].Stop(); }
        bgmCurrent = next;
        bgmFadeSpeed = 1f / Mathf.Max(0.05f, fadeSeconds);
    }

    public void StopBgm(float fadeSeconds = 0.6f) => PlayBgm(null, fadeSeconds);

    bool bgmTargetSilent;
    float bgmPitch = 1f;
    // BONUS ZONE: 区画の間だけBGMを少し速く/高くする(1=通常)
    public void SetBgmPitch(float pitch)
    {
        bgmPitch = pitch;
        for (int i = 0; i < 2; i++) if (bgm[i] != null) bgm[i].pitch = pitch;
    }

    // 死亡時などに今の曲を静かに消す(次にPlayBgmされたら元に戻る)。音量設定は変えない。
    float fadeOutMul = 1f, fadeOutTarget = 1f, fadeOutSpeed = 1f;
    public void FadeOutBgm(float duration = 1.2f) { fadeOutTarget = 0f; fadeOutSpeed = 1f / Mathf.Max(0.05f, duration); }
    // 倒れた状態から戻った(CO-OPの復活など): 死亡時のフェードを戻す
    public void CancelFadeOut(float duration = 1f) { if (fadeOutTarget < 1f) { fadeOutTarget = 1f; fadeOutSpeed = 1f / Mathf.Max(0.05f, duration); } }

    // ボス演出の一時的な音量の沈み(音量設定は変えない)
    float duck = 1f, duckTarget = 1f, duckSpeed = 1f;
    public void DuckBgm(float duckLevel, float duration) { duckTarget = Mathf.Clamp01(duckLevel); duckSpeed = 1f / Mathf.Max(0.05f, duration); }
    public void UnduckBgm(float duration) { duckTarget = 1f; duckSpeed = 1f / Mathf.Max(0.05f, duration); }

    // ループしない短い曲(RESULT/GAME OVER)。流れているBGMは素早く消す。
    public void PlayJingle(AudioClip clip)
    {
        if (clip == null) return;
        CurrentJingle = clip;
        CurrentBgm = null; bgmTargetSilent = true; bgmFadeSpeed = 1f / 0.35f;
        jingleSource.Stop();
        jingleSource.clip = clip;
        jingleSource.pitch = 1f;
        jingleSource.Play();
    }

    public void StopJingle() { if (jingleSource != null && jingleSource.isPlaying) jingleSource.Stop(); CurrentJingle = null; }
    public bool JinglePlaying => jingleSource != null && jingleSource.isPlaying;

    // 鳴っているBGMの数(テスト用: クロスフェード中以外は1つ)
    public int PlayingBgmSources { get { int n = 0; for (int i = 0; i < 2; i++) if (bgm[i].isPlaying && bgm[i].volume > 0.0001f) n++; return n; } }
    public float CurrentBgmTime => bgm[bgmCurrent] != null ? bgm[bgmCurrent].time : 0f;
    public float CurrentBgmVolume => bgm[bgmCurrent] != null ? bgm[bgmCurrent].volume : 0f;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        // クロスフェード(今の曲へ、それ以外は0へ)
        for (int i = 0; i < 2; i++)
        {
            float target = (i == bgmCurrent && !bgmTargetSilent) ? 1f : 0f;
            bgmWeight[i] = Mathf.MoveTowards(bgmWeight[i], target, bgmFadeSpeed * dt);
            if (bgmWeight[i] <= 0f && bgm[i].isPlaying && (i != bgmCurrent || bgmTargetSilent)) bgm[i].Stop();
        }
        duck = Mathf.MoveTowards(duck, duckTarget, duckSpeed * dt);
        fadeOutMul = Mathf.MoveTowards(fadeOutMul, fadeOutTarget, fadeOutSpeed * dt);
        ApplyVolumes();
        UpdateAmbienceShots(dt);
    }

    void ApplyVolumes()
    {
        float bgmBase = BaseBgmVolume * Master * BgmVolume;
        for (int i = 0; i < 2; i++) if (bgm[i] != null) bgm[i].volume = bgmBase * bgmWeight[i] * duck * fadeOutMul;
        if (jingleSource != null) jingleSource.volume = bgmBase;
        if (sfxSource != null) sfxSource.volume = BaseSfxVolume * Master * SfxVolume;
        float envBase = BaseEnvVolume * Master * EnvVolume;
        if (envLoop != null) envLoop.volume = envBase * (ambience != null ? ambience.loopVolume : 0f) * envWeight;
        if (envShot != null) envShot.volume = envBase;
        envWeight = Mathf.MoveTowards(envWeight, ambience != null && ambience.loop != null ? 1f : 0f, Time.unscaledDeltaTime / 1.2f);
    }

    // ===================================================================== //
    // 環境音
    // ===================================================================== //
    AmbienceSet ambience;
    float envWeight, nextAmbienceShot = -1f;
    public AmbienceSet CurrentAmbience => ambience;
    public AudioClip CurrentAmbienceLoop => envLoop != null && envLoop.isPlaying ? envLoop.clip : null;

    public void SetAmbience(AmbienceSet set)
    {
        if (set == ambience) return;
        ambience = set;
        nextAmbienceShot = Time.unscaledTime + (set != null ? Random.Range(set.interval.x, set.interval.y) : 0f);
        if (set != null && set.loop != null)
        {
            if (envLoop.clip != set.loop) { envLoop.clip = set.loop; envLoop.Play(); envWeight = 0f; }
            else if (!envLoop.isPlaying) envLoop.Play();
        }
    }

    void UpdateAmbienceShots(float dt)
    {
        if (envLoop != null && envWeight <= 0f && envLoop.isPlaying && (ambience == null || ambience.loop == null)) envLoop.Stop();
        if (ambience == null || ambience.oneShots == null || ambience.oneShots.Count == 0) return;
        if (Time.unscaledTime < nextAmbienceShot) return;
        nextAmbienceShot = Time.unscaledTime + Random.Range(ambience.interval.x, Mathf.Max(ambience.interval.x, ambience.interval.y));
        if (EnvVolumeLevel <= 0 || MasterVolumeLevel <= 0) return;
        var clip = ambience.oneShots[Random.Range(0, ambience.oneShots.Count)];
        if (clip == null) return;
        envShot.pitch = Random.Range(0.92f, 1.08f);
        envShot.panStereo = Random.Range(-0.6f, 0.6f);
        envShot.PlayOneShot(clip, ambience.oneShotVolume * Random.Range(0.7f, 1f));
    }

    // ===================================================================== //
    // SE
    // ===================================================================== //
    readonly Dictionary<SeId, float> lastPlayed = new Dictionary<SeId, float>();
    public int SePlayCount { get; private set; }     // テスト用
    public SeId LastSe { get; private set; }

    // SeIdで鳴らす。素材が複数ならランダム、ピッチ/音量を少し揺らす。短い間隔の連打は間引く。
    public bool PlaySe(SeId id, float volumeScale = 1f)
    {
        var e = Library != null ? Library.FindSe(id) : null;
        AudioClip clip = null;
        if (e != null && e.clips != null && e.clips.Count > 0) clip = e.clips[Random.Range(0, e.clips.Count)];
        if (clip == null) clip = LegacyClip(id);
        if (clip == null) return false;
        float now = Time.unscaledTime;
        float minGap = e != null ? e.minInterval : 0.03f;
        if (lastPlayed.TryGetValue(id, out float last) && now - last < minGap) return false;
        lastPlayed[id] = now;
        SePlayCount++; LastSe = id;
        if (!SfxEnabled) return true;
        float vol = (e != null ? e.volume : 1f) * volumeScale;
        float pj = e != null ? e.pitchJitter : 0.03f, vj = e != null ? e.volumeJitter : 0.05f;
        PlayOneShotVaried(clip, vol * Random.Range(1f - vj, 1f + vj), Random.Range(1f - pj, 1f + pj));
        return true;
    }

    // 既存の割り当てがあればそれ、無ければAudioLibraryの音(「既存の音はそのまま」「空きだけ埋める」)
    public static AudioClip Se(SeId id, AudioClip existing = null)
    {
        if (existing != null) return existing;
        return LibraryClip(id);
    }

    public static AudioClip LibraryClip(SeId id)
    {
        var lib = Instance != null ? Instance.Library : AudioLibrary.Load();
        var e = lib != null ? lib.FindSe(id) : null;
        if (e != null && e.clips != null) foreach (var c in e.clips) if (c != null) return c;
        return null;
    }

    public AudioClip ClipFor(SeId id)
    {
        var e = Library != null ? Library.FindSe(id) : null;
        if (e != null && e.clips != null) foreach (var c in e.clips) if (c != null) return c;
        return LegacyClip(id);
    }

    AudioClip LegacyClip(SeId id)
    {
        switch (id)
        {
            case SeId.Jump: return jumpSe;
            case SeId.DoubleJump: return doubleJumpSe;
            case SeId.Land: return landingSe != null ? landingSe : landSe;
            case SeId.Hit: case SeId.EnemyHit: return attackHitSe;
            case SeId.PlayerDamage: return playerDamageSe;
            case SeId.EnemyDefeat: return enemyDefeatSe;
            case SeId.PlayerDeath: return playerDeathSe;
        }
        return null;
    }

    void PlayOneShotVaried(AudioClip clip, float volumeScale, float pitch)
    {
        // PlayOneShotはその時点のpitchで鳴るので、鳴らしてすぐ戻してよい(1つのAudioSourceを共有)
        float prev = sfxSource.pitch;
        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volumeScale);
        sfxSource.pitch = prev;
    }

    // ---- 以前からの入口(そのまま使える) ----
    public void PlayJump() => PlaySe(SeId.Jump);
    public void PlayDoubleJump() => PlaySe(SeId.DoubleJump);
    public void PlayLand() => PlaySe(SeId.Land);
    public void PlayAttackHit() => PlaySe(SeId.Hit);
    public void PlayStrongHit() => PlaySe(SeId.StrongHit);
    public void PlayPlayerDamage() => PlaySe(SeId.PlayerDamage);
    public void PlayEnemyDefeat() => PlaySe(SeId.EnemyDefeat);
    public void PlayPlayerDeath() => PlaySe(SeId.PlayerDeath);

    public void PlaySfx(AudioClip clip)
    {
        if (SfxEnabled && clip != null) sfxSource.PlayOneShot(clip);
    }

    public void PlaySfxVolume(AudioClip clip, float volumeScale)
    {
        if (SfxEnabled && clip != null) sfxSource.PlayOneShot(clip, volumeScale);
    }

    // 攻撃を振った音(当たったかどうかとは別)。stage: コンボの段(3段目以降=強攻撃)。
    // 武器タイプはキャラクターから(AudioLibrary.characterWeapons)。空中の近接攻撃は共通の空中攻撃音。
    float lastAttackSe = -1f;
    public void PlayAttack(int stage)
    {
        float now = Time.unscaledTime;
        if (now - lastAttackSe < 0.03f) return;
        lastAttackSe = now;
        SePlayCount++;
        if (!SfxEnabled) return;
        WeaponType w = CurrentWeapon();
        var pc = PlayerController.Instance;
        bool melee = w == WeaponType.Sword || w == WeaponType.DualBlade || w == WeaponType.Strike || w == WeaponType.Special;
        if (melee && pc != null && !pc.IsGrounded && stage < 3 && PlaySe(SeId.AttackAir)) return;
        var set = Library != null ? Library.FindWeapon(w) : null;
        AudioClip clip = null; float vol = 1f;
        if (set != null)
        {
            vol = set.volume;
            if (stage >= 3 && set.strong != null) clip = set.strong;
            else if (set.normal != null && set.normal.Count > 0) clip = set.normal[Mathf.Clamp(stage - 1, 0, 99) % set.normal.Count];
        }
        if (clip == null) clip = stage <= 1 ? attackSe1 : stage == 2 ? attackSe2 : attackSe3;
        if (clip != null) PlayOneShotVaried(clip, vol * Random.Range(0.94f, 1.04f), Random.Range(0.96f, 1.04f));
    }

    public WeaponType CurrentWeapon()
    {
        if (Library == null) return WeaponType.Sword;
        var gm = GameManager.Instance;
        string id = gm != null ? (!string.IsNullOrEmpty(gm.ActiveRunCharacterId) ? gm.ActiveRunCharacterId : gm.SelectedCharacterId) : null;
        return Library.WeaponFor(id);
    }

    // ===================================================================== //
    // 音量(0〜1の連続値、PlayerPrefsに保存。旧0〜4段階のAPIも残す)
    // ===================================================================== //
    static float LoadVolume(string key, string legacyLevelKey)
    {
        if (SaveStore.HasKey(key)) return Mathf.Clamp01(SaveStore.GetFloat(key, 1f));
        return Mathf.Clamp(SaveStore.GetInt(legacyLevelKey, DefaultVolumeLevel), 0, MaxVolumeLevel) / (float)MaxVolumeLevel; // 旧い段階の値を一度だけ読み替え
    }
    // save=false: スライダーを動かしている間(PlayerPrefsの書き込みは呼び出し側がまとめて行う)
    void Store(string key, float v, bool save) { SaveStore.SetFloat(key, v); if (save) SaveStore.Save(); ApplyVolumes(); }
    public void SetMasterVolume(float v, bool save = true) { MasterVolume = Mathf.Clamp01(v); Store(MasterVolumeKey, MasterVolume, save); }
    public void SetBgmVolume(float v, bool save = true) { BgmVolume = Mathf.Clamp01(v); Store(BgmVolumeKey, BgmVolume, save); }
    public void SetSfxVolume(float v, bool save = true) { SfxVolume = Mathf.Clamp01(v); Store(SfxVolumeKey, SfxVolume, save); }
    public void SetEnvVolume(float v, bool save = true) { EnvVolume = Mathf.Clamp01(v); Store(EnvVolumeKey, EnvVolume, save); }
    // 全体ミュート(音量の値はそのまま=解除すると元の音量に戻る)
    public void SetMuted(bool on) { Muted = on; SaveStore.SetInt(MutedKey, on ? 1 : 0); SaveStore.Save(); ApplyVolumes(); }
    public void SetMasterVolumeLevel(int level) => SetMasterVolume(Mathf.Clamp(level, 0, MaxVolumeLevel) / (float)MaxVolumeLevel);
    public void SetBgmVolumeLevel(int level) => SetBgmVolume(Mathf.Clamp(level, 0, MaxVolumeLevel) / (float)MaxVolumeLevel);
    public void SetSfxVolumeLevel(int level) => SetSfxVolume(Mathf.Clamp(level, 0, MaxVolumeLevel) / (float)MaxVolumeLevel);
    public void SetEnvVolumeLevel(int level) => SetEnvVolume(Mathf.Clamp(level, 0, MaxVolumeLevel) / (float)MaxVolumeLevel);
    public void CycleMasterVolume() => SetMasterVolumeLevel((MasterVolumeLevel + 1) % (MaxVolumeLevel + 1));
    public void CycleBgmVolume() => SetBgmVolumeLevel((BgmVolumeLevel + 1) % (MaxVolumeLevel + 1));
    public void CycleSfxVolume() => SetSfxVolumeLevel((SfxVolumeLevel + 1) % (MaxVolumeLevel + 1));
    public void CycleEnvVolume() => SetEnvVolumeLevel((EnvVolumeLevel + 1) % (MaxVolumeLevel + 1));

    // テスト用: 実際の出力音量
    public float SfxOutputVolume => sfxSource != null ? sfxSource.volume : 0f;
    public float EnvOutputVolume => envLoop != null ? envLoop.volume : 0f;
    public float JingleOutputVolume => jingleSource != null ? jingleSource.volume : 0f;
}
