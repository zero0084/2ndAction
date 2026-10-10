using UnityEngine;

// どのBGM/環境音を流すかを決める係(2026-09-29)。AudioManagerと同じObjectに付く。
// 優先順(上ほど強い):
//   HOME(ラン前)                       → HOME曲 + HOMEの環境音
//   RESULT / GAME OVER                  → ジングル1回(クリア=RESULT、それ以外=GAME OVER)
//   ボス戦(戦闘開始〜撃破まで)          → ボス曲(通常/強敵/特殊、個別の上書き)。警告演出の間と撃破後は道中曲
//   死神(100,000m以降)                 → 死神の専用曲
//   BONUS ZONE(曲が割り当てられていれば) → BONUS曲
//   それ以外                            → ステージの道中曲(距離で序盤/中盤/終盤)
// AudioManager.PlayBgmは同じ曲なら何もしないので、毎回同じ判断をしても再スタートしない。
// 音の再設計(2026-10-06): 切り替えの種類で長さを変える(道中→ボスは短く、ボス→道中は長く、序盤→中盤→終盤はゆっくり)。
//   ボス曲は 通常/強敵/特殊/死神/最終(ラスダンのラッシュ)。闘技場は闘技場の曲(空ならボスの強敵曲)。
public class BgmDirector : MonoBehaviour
{
    AudioManager am;
    float nextCheck;
    bool jinglePlayed;
    public string Reason { get; private set; } = "";

    void Awake() { am = GetComponent<AudioManager>(); }

    public void Refresh() { nextCheck = 0f; }

    // ラストダンジョンのエンディング(2026-09-30): 演出が曲を直接決める間の上書き(ボス戦/死神/ステージ曲より優先)。
    //  OverrideActive=true かつ OverrideClip=null … 曲を止めて環境音だけ(静寂区間)
    //  OverrideAmbience … 環境音の差し替え(null=ステージの環境音のまま)
    public static bool OverrideActive;
    public static AudioClip OverrideClip;
    public static AmbienceSet OverrideAmbience;
    public static string OverrideReason = "";
    public static float OverrideFadeSeconds = 3.5f;
    public static void ClearOverride() { OverrideActive = false; OverrideClip = null; OverrideAmbience = null; OverrideReason = ""; OverrideFadeSeconds = 3.5f; }

    void Update() { using (FrameCost.Scope("Bgm")) UpdateMeasured(); }
    void UpdateMeasured()
    {
        if (am == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.15f;
        var gm = GameManager.Instance;
        var lib = am.Library;
        if (gm == null) return;

        if (!gm.HasStarted)
        {
            jinglePlayed = false;
            Play(lib != null && lib.homeBgm != null ? lib.homeBgm : am.titleBgm, "home", Kind.Home);
            am.SetAmbience(lib != null ? lib.homeAmbience : null);
            return;
        }

        string stage = gm.ActiveRunStageId;
        var stageAudio = lib != null ? lib.FindStage(stage) : null;

        if (gm.IsGameOver)
        {
            if (!jinglePlayed)
            {
                jinglePlayed = true;
                var j = lib == null || gm.QuietFinish ? null : gm.IsWin ? lib.resultJingle : lib.gameOverJingle; // NOで止まった時は静かなまま
                if (j != null) am.PlayJingle(j); else am.StopBgm(0.8f);
                Reason = gm.IsWin ? "result" : "gameover";
            }
            am.SetAmbience(null);
            return;
        }
        jinglePlayed = false;
        if (OverrideActive)
        {
            am.SetAmbience(OverrideAmbience ?? (stageAudio != null ? stageAudio.ambience : null));
            Reason = "override:" + OverrideReason;
            if (OverrideClip == null) { if (am.CurrentBgm != null) am.StopBgm(OverrideFadeSeconds); }
            else am.PlayBgm(OverrideClip, OverrideFadeSeconds);
            return;
        }
        am.SetAmbience(stageAudio != null ? stageAudio.ambience : null);

        var pc = PlayerController.Instance;
        if (pc != null && pc.HasDied) return; // 倒れた: 死亡時のフェードのまま(曲を戻さない)
        am.CancelFadeOut();

        var bm = BossManager.Instance;
        AudioClip clip; string reason; Kind kind;
        if (ArenaMode.Active)
        {
            clip = lib != null ? (lib.arenaBgm != null ? lib.arenaBgm : lib.BossBgm(BossBgmTier.Strong, "")) : null;
            reason = "arena"; kind = Kind.Boss;
        }
        else if (bm != null && bm.IsBossPhase && !string.IsNullOrEmpty(bm.BossMusicKey) && !bm.BossDefeatedThisPhase)
        {
            clip = lib != null ? lib.BossBgm(bm.BossMusicTier, bm.BossMusicKey) : null;
            reason = "boss:" + bm.BossMusicTier + ":" + bm.BossMusicKey; kind = Kind.Boss;
        }
        else if (bm != null && bm.DeathSpawned && lib != null && lib.BossBgm(BossBgmTier.Death, "*/Death") != null)
        {
            clip = lib.BossBgm(BossBgmTier.Death, "*/Death");
            reason = "death"; kind = Kind.Boss;
        }
        else if (lib != null && lib.bonusZoneBgm != null && BonusZone.Instance != null && BonusZone.Instance.BlocksBoss)
        {
            clip = lib.bonusZoneBgm;
            reason = "bonus"; kind = Kind.Bonus;
        }
        else
        {
            clip = lib != null ? lib.StageBgm(stage, gm.MaxDistance) : null;
            if (clip == null) clip = am.gameplayBgm;
            reason = "stage:" + stage + ":" + (lib != null ? lib.PhaseFor(gm.MaxDistance).ToString() : "?"); kind = Kind.Stage;
        }
        Play(clip, reason, kind);
    }

    enum Kind { None, Home, Stage, Boss, Bonus }
    Kind lastKind = Kind.None;
    public float LastFadeSeconds { get; private set; }

    void Play(AudioClip clip, string reason, Kind kind)
    {
        Reason = reason;
        {
            var lib = am.Library;
            float fade = lib != null ? lib.crossfadeSeconds : 1.6f;
            if (lib != null)
            {
                if (kind == Kind.Home || lastKind == Kind.Home || lastKind == Kind.None) fade = lib.homeFade;
                else if (kind == Kind.Boss && lastKind != Kind.Boss) fade = lib.bossInFade;       // ボスは素早く
                else if (lastKind == Kind.Boss && kind != Kind.Boss) fade = lib.bossOutFade;      // 戦いの後はゆっくり戻る
                else if (kind == Kind.Stage && lastKind == Kind.Stage) fade = lib.phaseFade;      // 序盤→中盤→終盤
            }
            if (clip != am.CurrentBgm)
            {
                LastFadeSeconds = fade;
                Debug.Log($"[BGM] {reason} -> {(clip != null ? clip.name : "(none)")} fade {fade:F1}s");
            }
            am.PlayBgm(clip, fade);
        }
        lastKind = kind;
    }
}
