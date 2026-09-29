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
public class BgmDirector : MonoBehaviour
{
    AudioManager am;
    float nextCheck;
    bool jinglePlayed;
    public string Reason { get; private set; } = "";

    void Awake() { am = GetComponent<AudioManager>(); }

    public void Refresh() { nextCheck = 0f; }

    void Update()
    {
        if (am == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.15f;
        var gm = GameManager.Instance;
        var lib = am.Library;
        if (gm == null) return;

        if (!gm.HasStarted)
        {
            jinglePlayed = false;
            Play(lib != null && lib.homeBgm != null ? lib.homeBgm : am.titleBgm, "home");
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
                var j = lib == null ? null : gm.IsWin ? lib.resultJingle : lib.gameOverJingle;
                if (j != null) am.PlayJingle(j); else am.StopBgm(0.8f);
                Reason = gm.IsWin ? "result" : "gameover";
            }
            am.SetAmbience(null);
            return;
        }
        jinglePlayed = false;
        am.SetAmbience(stageAudio != null ? stageAudio.ambience : null);

        var pc = PlayerController.Instance;
        if (pc != null && pc.HasDied) return; // 倒れた: 死亡時のフェードのまま(曲を戻さない)
        am.CancelFadeOut();

        var bm = BossManager.Instance;
        AudioClip clip; string reason;
        if (bm != null && bm.IsBossPhase && !string.IsNullOrEmpty(bm.BossMusicKey) && !bm.BossDefeatedThisPhase)
        {
            clip = lib != null ? lib.BossBgm(bm.BossMusicTier, bm.BossMusicKey) : null;
            reason = "boss:" + bm.BossMusicTier + ":" + bm.BossMusicKey;
        }
        else if (bm != null && bm.DeathSpawned && lib != null && lib.BossBgm(BossBgmTier.Death, "*/Death") != null)
        {
            clip = lib.BossBgm(BossBgmTier.Death, "*/Death");
            reason = "death";
        }
        else if (lib != null && lib.bonusZoneBgm != null && BonusZone.Instance != null && BonusZone.Instance.BlocksBoss)
        {
            clip = lib.bonusZoneBgm;
            reason = "bonus";
        }
        else
        {
            clip = lib != null ? lib.StageBgm(stage, gm.MaxDistance) : null;
            if (clip == null) clip = am.gameplayBgm;
            reason = "stage:" + stage + ":" + (lib != null ? lib.PhaseFor(gm.MaxDistance).ToString() : "?");
        }
        Play(clip, reason);
    }

    void Play(AudioClip clip, string reason)
    {
        Reason = reason;
        am.PlayBgm(clip);
    }
}
