using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    const string BgmVolumeLevelKey = "BgmVolumeLevel";
    const string SfxVolumeLevelKey = "SfxVolumeLevel";

    // 5 steps (0=mute .. 4=max), each a quarter of the base volume below -
    // replaces the old plain on/off toggle with a coarse volume dial.
    public const int MaxVolumeLevel = 4;
    const float BaseBgmVolume = 0.35f;
    // Turned down from the original 0.7 - SFX was too loud overall.
    const float BaseSfxVolume = 0.45f;
    const int DefaultVolumeLevel = MaxVolumeLevel;

    public AudioClip titleBgm;
    public AudioClip gameplayBgm;
    public AudioClip jumpSe;
    public AudioClip doubleJumpSe;
    public AudioClip attackSe1;
    public AudioClip attackSe2;
    public AudioClip attackSe3;
    public AudioClip landSe;

    // Game Feel pass - OneMoreMile_SE_Subtle_Pack. Each of these is its own
    // distinct clip (never reused across roles - see the class comment on
    // PlaySfxAt) and its own restrained starting volume, per the pack's own
    // README suggestion. landSe above is superseded by landingSe (kept
    // assignable too so a scene built before this still has *something*
    // wired, but SceneBuilder now points landingSe at the new clip and
    // PlayLand() prefers it).
    [Header("Game Feel - Restrained SE Pack")]
    public AudioClip attackHitSe;
    public AudioClip playerDamageSe;
    public AudioClip enemyDefeatSe;
    public AudioClip playerDeathSe;
    public AudioClip landingSe;

    // TEMPORARY (Game Feel debug pass, section 16 - "確実に鳴っていること
    // を確認") - found a real reason these read as "barely audible": every
    // one of these plays through AudioSource.PlayOneShot(clip, volumeScale),
    // whose volumeScale multiplies against sfxSource.volume (itself
    // BaseSfxVolume(0.45) * SfxVolumeLevel/Max, so 0.45 at the default full
    // setting) - NOT an independent 0-1 volume on its own. The previous
    // pass's 0.55-0.70 constants were effectively 0.45*0.55=~0.25 etc. of
    // true full volume, compounding two separate "make it quieter" factors
    // that were each individually reasonable-looking in isolation.
    // SfxTestVolumeBoost (1/BaseSfxVolume) cancels the sfxSource.volume
    // side of that multiplication back out, so these 5 constants can be
    // read directly as "fraction of true full volume" while testing -
    // BaseSfxVolume itself is untouched, so this doesn't also affect
    // already-tuned SFX elsewhere (Jump/Attack swing/etc.).
    // PlayOneShot's volumeScale isn't clamped to 1, so values above 1 here
    // are valid and expected.
    const float SfxTestVolumeBoost = 1f / BaseSfxVolume;
    const float AttackHitVolume = 1.0f * SfxTestVolumeBoost;
    const float PlayerDamageVolume = 1.0f * SfxTestVolumeBoost;
    const float EnemyDefeatVolume = 1.0f * SfxTestVolumeBoost;
    const float PlayerDeathVolume = 1.0f * SfxTestVolumeBoost;
    const float LandingVolume = 1.0f * SfxTestVolumeBoost;
    // Attack Hit is the one that fires most often in a row (every combo
    // swing that connects) - a tiny random pitch nudge keeps a fast combo
    // from sounding like the exact same sample looping, without being
    // obviously "randomized" (see PlaySfxAt's own comment on why this is
    // safe to do per-call even though every SFX shares one AudioSource).
    const float AttackHitPitchMin = 0.97f;
    const float AttackHitPitchMax = 1.03f;

    public int BgmVolumeLevel { get; private set; }
    public int SfxVolumeLevel { get; private set; }
    public bool BgmEnabled => BgmVolumeLevel > 0;
    public bool SfxEnabled => SfxVolumeLevel > 0;

    AudioSource bgmSource;
    AudioSource sfxSource;
    AudioClip currentBgm;

    void Awake()
    {
        Instance = this;

        BgmVolumeLevel = Mathf.Clamp(PlayerPrefs.GetInt(BgmVolumeLevelKey, DefaultVolumeLevel), 0, MaxVolumeLevel);
        SfxVolumeLevel = Mathf.Clamp(PlayerPrefs.GetInt(SfxVolumeLevelKey, DefaultVolumeLevel), 0, MaxVolumeLevel);

        // titleBgm/gameplayBgm are real imported audio clips (wired in
        // SceneBuilder) - only fall back to the procedural placeholder if
        // one isn't assigned.
        if (titleBgm == null) titleBgm = AudioFactory.CreateTitleBgm();
        if (gameplayBgm == null) gameplayBgm = AudioFactory.CreateGameplayBgm();
        if (jumpSe == null) jumpSe = AudioFactory.CreateJumpSe();
        if (doubleJumpSe == null) doubleJumpSe = AudioFactory.CreateJumpSe();
        if (attackSe1 == null) attackSe1 = AudioFactory.CreateAttackSe();
        if (attackSe2 == null) attackSe2 = AudioFactory.CreateAttackSe();
        if (attackSe3 == null) attackSe3 = AudioFactory.CreateAttackSe();

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.volume = BaseBgmVolume * BgmVolumeLevel / MaxVolumeLevel;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.volume = BaseSfxVolume * SfxVolumeLevel / MaxVolumeLevel;
    }

    void Start()
    {
        PlayTitleBgm();
    }

    public void PlayTitleBgm() => SwitchBgm(titleBgm);

    public void PlayGameplayBgm() => SwitchBgm(gameplayBgm);

    // BONUS ZONE(2026-09-29): 区画の間だけBGMを少し速く/高くする(1=通常)。
    public void SetBgmPitch(float pitch) { if (bgmSource != null) bgmSource.pitch = pitch; }

    void SwitchBgm(AudioClip clip)
    {
        currentBgm = clip;
        if (clip == null) return;

        bgmSource.clip = clip;
        // Always actually playing - muting/volume is handled purely via
        // bgmSource.volume, so toggling the level doesn't need to restart
        // or re-sync playback position.
        bgmSource.Play();
    }

    public void PlayJump()
    {
        if (SfxEnabled && jumpSe != null) sfxSource.PlayOneShot(jumpSe);
    }

    public void PlayDoubleJump()
    {
        if (SfxEnabled && doubleJumpSe != null) sfxSource.PlayOneShot(doubleJumpSe);
    }

    // landingSe (the new restrained pack) takes over if assigned; landSe
    // stays as a fallback for a scene that was built before this pack
    // existed, so nothing goes silent.
    public void PlayLand()
    {
        if (landingSe != null) PlaySfxScaled(landingSe, LandingVolume, "Landing");
        else if (SfxEnabled && landSe != null) sfxSource.PlayOneShot(landSe);
        else Debug.Log($"[SE] Landing NOT played - landingSe=null={landingSe == null}, landSe=null={landSe == null}, SfxEnabled={SfxEnabled}");
    }

    // Generic one-shot for SFX that don't need their own dedicated method
    // (e.g. the reward card sequence's deck/draw/flip/select/confirm cues).
    public void PlaySfx(AudioClip clip)
    {
        if (SfxEnabled && clip != null) sfxSource.PlayOneShot(clip);
    }

    // 天空回廊ボス追加(2026-09-25) - 音量指定つきの効果音(ログ出力なし、ボス攻撃音は頻繁なため)。
    public void PlaySfxVolume(AudioClip clip, float volumeScale)
    {
        if (SfxEnabled && clip != null) sfxSource.PlayOneShot(clip, volumeScale);
    }

    // volumeScale is per-call (via AudioSource.PlayOneShot's own overload),
    // not a change to sfxSource.volume itself - every other concurrently
    // playing one-shot on this shared source keeps its own separately-
    // requested volume, so a quiet Landing puff and a louder Player Death
    // firing close together don't affect each other.
    //
    // TEMPORARY (Game Feel debug pass) - every call here logs whether it
    // actually played and, if not, exactly which check stopped it
    // (SfxEnabled false / clip null) - remove once SE playback is confirmed
    // on-device via logcat (see the class-level note this pass added).
    void PlaySfxScaled(AudioClip clip, float volumeScale, string label)
    {
        if (SfxEnabled && clip != null)
        {
            sfxSource.PlayOneShot(clip, volumeScale);
            Debug.Log($"[SE] {label} played (volumeScale={volumeScale}, sfxSourceVolume={sfxSource.volume}, mute={sfxSource.mute})");
        }
        else
        {
            Debug.Log($"[SE] {label} NOT played - clip=null={clip == null}, SfxEnabled={SfxEnabled} (SfxVolumeLevel={SfxVolumeLevel})");
        }
    }

    // The moment an attack hitbox actually connects with an enemy (see
    // EnemyController.HitAndDie) - distinct from PlayAttack below, which is
    // the swing itself and fires regardless of whether it lands (see
    // section 13's "role separation" - swing vs hit are never the same SE).
    // A small random pitch nudge (see AttackHitPitchMin/Max) since this is
    // the SE most likely to fire several times in quick succession (a fast
    // combo landing on the same enemy, or several enemies at once).
    public void PlayAttackHit()
    {
        if (!SfxEnabled || attackHitSe == null)
        {
            Debug.Log($"[SE] AttackHit NOT played - clip=null={attackHitSe == null}, SfxEnabled={SfxEnabled}");
            return;
        }
        float prevPitch = sfxSource.pitch;
        // PlayOneShot bakes in the source's pitch AT CALL TIME for that one
        // shot specifically - it does not keep tracking sfxSource.pitch
        // afterward, so restoring it right back below doesn't retroactively
        // affect the sound that was just triggered (safe even though every
        // SFX shares this one AudioSource).
        sfxSource.pitch = Random.Range(AttackHitPitchMin, AttackHitPitchMax);
        sfxSource.PlayOneShot(attackHitSe, AttackHitVolume);
        sfxSource.pitch = prevPitch;
        Debug.Log($"[SE] AttackHit played (volumeScale={AttackHitVolume}, sfxSourceVolume={sfxSource.volume}, mute={sfxSource.mute})");
    }

    public void PlayPlayerDamage() => PlaySfxScaled(playerDamageSe, PlayerDamageVolume, "PlayerDamage");

    public void PlayEnemyDefeat() => PlaySfxScaled(enemyDefeatSe, EnemyDefeatVolume, "EnemyDefeat");

    public void PlayPlayerDeath() => PlaySfxScaled(playerDeathSe, PlayerDeathVolume, "PlayerDeath");

    // A short fade to silence (not a hard stop) on player death, per the
    // "自然であれば追加" brief - purely cosmetic, doesn't touch BgmVolumeLevel
    // or its PlayerPrefs value, so the next run/scene reload comes back at
    // the player's actual chosen volume rather than wherever this fade left
    // bgmSource.volume.
    public void FadeOutBgm(float duration = 1.2f)
    {
        StartCoroutine(FadeOutBgmRoutine(duration));
    }

    System.Collections.IEnumerator FadeOutBgmRoutine(float duration)
    {
        float startVolume = bgmSource.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(t / Mathf.Max(0.001f, duration)));
            yield return null;
        }
        bgmSource.volume = 0f;
    }

    // Boss Milestone Presentation pass - a brief, self-reverting volume dip
    // (never written to BgmVolumeLevel/PlayerPrefs) while the milestone
    // beat plays, same "cosmetic only" reasoning as FadeOutBgm above.
    // duckLevel is a fraction of whatever bgmSource.volume was BEFORE the
    // very first DuckBgm call, not an absolute value, so it still respects
    // whichever BGM volume the player actually has chosen. -1f sentinel
    // (bgmDuckBaseVolume) tracks "not currently ducked" so a second
    // DuckBgm call (e.g. two milestones close together) can't compound the
    // dip on top of an already-ducked volume.
    float bgmDuckBaseVolume = -1f;
    Coroutine bgmDuckRoutine;

    public void DuckBgm(float duckLevel, float duration)
    {
        if (bgmDuckBaseVolume < 0f) bgmDuckBaseVolume = bgmSource.volume;
        if (bgmDuckRoutine != null) StopCoroutine(bgmDuckRoutine);
        bgmDuckRoutine = StartCoroutine(BgmVolumeRoutine(bgmDuckBaseVolume * duckLevel, duration));
    }

    public void UnduckBgm(float duration)
    {
        if (bgmDuckBaseVolume < 0f) return; // wasn't ducked - nothing to restore
        float target = bgmDuckBaseVolume;
        bgmDuckBaseVolume = -1f;
        if (bgmDuckRoutine != null) StopCoroutine(bgmDuckRoutine);
        bgmDuckRoutine = StartCoroutine(BgmVolumeRoutine(target, duration));
    }

    System.Collections.IEnumerator BgmVolumeRoutine(float target, float duration)
    {
        float start = bgmSource.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(start, target, Mathf.Clamp01(t / Mathf.Max(0.001f, duration)));
            yield return null;
        }
        bgmSource.volume = target;
    }

    // stage: 1st/2nd/3rd hit in the combo chain - each has its own SE.
    public void PlayAttack(int stage)
    {
        if (!SfxEnabled) return;
        AudioClip clip = stage switch
        {
            <= 1 => attackSe1,
            2 => attackSe2,
            _ => attackSe3
        };
        if (clip != null) sfxSource.PlayOneShot(clip);
    }

    public void SetBgmVolumeLevel(int level)
    {
        BgmVolumeLevel = Mathf.Clamp(level, 0, MaxVolumeLevel);
        PlayerPrefs.SetInt(BgmVolumeLevelKey, BgmVolumeLevel);
        PlayerPrefs.Save();
        bgmSource.volume = BaseBgmVolume * BgmVolumeLevel / MaxVolumeLevel;
    }

    public void CycleBgmVolume() => SetBgmVolumeLevel((BgmVolumeLevel + 1) % (MaxVolumeLevel + 1));

    public void SetSfxVolumeLevel(int level)
    {
        SfxVolumeLevel = Mathf.Clamp(level, 0, MaxVolumeLevel);
        PlayerPrefs.SetInt(SfxVolumeLevelKey, SfxVolumeLevel);
        PlayerPrefs.Save();
        sfxSource.volume = BaseSfxVolume * SfxVolumeLevel / MaxVolumeLevel;
    }

    public void CycleSfxVolume() => SetSfxVolumeLevel((SfxVolumeLevel + 1) % (MaxVolumeLevel + 1));
}
