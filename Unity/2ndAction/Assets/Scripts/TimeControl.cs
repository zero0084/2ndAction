using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 高速走行中のフリーズ/ワープ調査(2026-09-22) - Time.timeScaleへの書き込みが
// HitStop / GameManager(RunLevelUpChoice・RunBossRewardChoice・
// ApplyUpgradeByCardId・PauseMenu)の複数箇所に分散しており、それぞれが
// 「自分が止めた値を自分で元に戻す」という前提で書かれていた。
//
// 具体的に見つかった競合: EnemyController.HitAndDie等はHitStop.Freezeを
// yield returnで待つため単体では問題ないが、"HitStopが有効な間に、別の敵の
// 撃破がLevel Up/Boss Rewardの選択を開始する"ケース(複数の敵がほぼ同時に
// 絡む場合に起こりうる)では、HitStop側が先に止め始め、その後にLevel Up側が
// 「自分も止めたい」と割り込む形になる。従来のHitStopは「自分がFreezeを
// 始める前のTime.timeScale」を記憶して、自分のFreeze時間が終わったら無条件
// にその値へ戻していたため、Level Up側がまだ止めておきたいのに勝手に
// Time.timeScale=1へ戻してしまい、カード選択画面が出ている間にゲーム進行
// (プレイヤー位置・距離・敵)が裏で進んでしまい、カード確定後に画面が
// 一気に「追いつく」=ワープしたように見える、という不具合につながる。
//
// 対策: 「誰が」止めたいかを理由(owner、任意のobject)ごとに登録/解除する
// だけの単純な集合に一本化する。集合が空でない間だけTime.timeScale=0、
// 空になった瞬間だけ1に戻す - 「古い処理の終了処理が、新しく増えた別の
// 停止理由まで巻き込んで解除してしまう」ことが構造的に起こらない。
//
// Time.timeScaleは次の優先順で1箇所(Apply)だけが決める:
//   1. 完全停止の理由(カード選択/ボス報酬/ポーズメニュー/HitStop)が1つでもある → 0
//   2. ボス登場演出のテンポランプ中 → 演出の値
//   3. 中断セーブからの再開直後の慣らし中(既定OFF、2026-10-03) → 慣らしの値
//   4. それ以外 → 1
// (2026-09-27に入れた「高速時の自動スロー」の層は、2026-09-28に高速時の自動操作補助(HighSpeedAssist)へ
//  置き換えて撤去した。時間の流れは速度では変えない。)
public static class TimeControl
{
    static readonly HashSet<object> pauseOwners = new HashSet<object>();

    // BossMilestonePresentationのテンポランプ(開始時の倍率→中間値→0)。演出中はpresentationScale
    // を採用する(ただし完全停止の理由がある間は0が優先)。
    static bool presentationDriving;
    static float presentationScale = 1f;
    static object presentationOwner;


    public static bool IsPaused => pauseOwners.Count > 0;
    public static bool IsPausedBy(object owner) => owner != null && pauseOwners.Contains(owner);
    // 演出の層が続いている実時間(取り残しの検出用、2026-10-01)
    static float presentationSinceRealtime;
    public static float PresentationDriveSeconds => presentationDriving ? Time.realtimeSinceStartup - presentationSinceRealtime : 0f;
    public static bool IsPresentationDriving => presentationDriving;
    public static int ActiveReasonCount => pauseOwners.Count;

    public static void Pause(object owner)
    {
        if (owner == null) return;
        pauseOwners.Add(owner);
        Apply();
    }

    public static void Resume(object owner)
    {
        if (owner == null) return;
        pauseOwners.Remove(owner);
        Apply();
    }

    // ボス登場演出のテンポランプを始める。ランプの開始値(通常の時間の流れ=1)を返す。
    public static float BeginPresentationDrive(object owner)
    {
        presentationDriving = true;
        presentationOwner = owner;
        presentationScale = 1f;
        presentationSinceRealtime = Time.realtimeSinceStartup;
        Apply();
        return 1f;
    }

    public static void SetPresentationScale(object owner, float scale)
    {
        if (!presentationDriving || owner != presentationOwner) return;
        presentationScale = Mathf.Clamp01(scale);
        Apply();
    }

    public static void EndPresentationDrive(object owner)
    {
        if (!presentationDriving || (owner != null && owner != presentationOwner)) return;
        presentationDriving = false;
        presentationOwner = null;
        Apply();
    }

    // 中断セーブからの再開直後の慣らし(2026-10-03、既定OFF): GameManager が開始値→1へ数秒で戻す。
    // 優先順は 停止 > ボス登場演出 > 慣らし > 通常。速度に応じて自動で掛かるものではない(再開の直後だけ)。
    static bool resumeEaseDriving;
    static float resumeEaseScale = 1f;
    public static bool IsResumeEasing => resumeEaseDriving;
    public static float ResumeEaseScale => resumeEaseDriving ? resumeEaseScale : 1f;

    public static void SetResumeEase(float scale)
    {
        resumeEaseDriving = true;
        resumeEaseScale = Mathf.Clamp(scale, 0.05f, 1f);
        Apply();
    }

    public static void EndResumeEase()
    {
        if (!resumeEaseDriving) return;
        resumeEaseDriving = false;
        resumeEaseScale = 1f;
        Apply();
    }

    static void Apply()
    {
        if (pauseOwners.Count > 0) Time.timeScale = 0f;
        else if (presentationDriving) Time.timeScale = presentationScale * DebugTimeScale;
        else if (resumeEaseDriving) Time.timeScale = resumeEaseScale * DebugTimeScale;
        else Time.timeScale = DebugTimeScale;
    }

    // 確認用(2026-09-30): 早送りの倍率(走行時間の計測など)。通常は1。
    public static float DebugTimeScale = 1f;
    public static void SetDebugTimeScale(float s) { DebugTimeScale = Mathf.Max(0.01f, s); Apply(); }

    // リトライ/ホーム帰還/ゲームオーバーなど、「理由がどうあれ必ず通常状態
    // へ戻す」既存の安全ネットから呼ぶ。停止理由の集合ごと空にするので、
    // 次のRun/シーンに古い停止理由が残留しない。
    public static void ResetAll()
    {
        pauseOwners.Clear();
        presentationDriving = false;
        presentationOwner = null;
        resumeEaseDriving = false;
        resumeEaseScale = 1f;
        Time.timeScale = 1f;
    }

    public static string DescribeActiveReasons()
    {
        if (pauseOwners.Count == 0) return presentationDriving ? $"(presentation x{presentationScale:F2})" : resumeEaseDriving ? $"(resume ease x{resumeEaseScale:F2})" : "(none)";
        var sb = new StringBuilder();
        bool first = true;
        foreach (object o in pauseOwners)
        {
            if (!first) sb.Append(", ");
            sb.Append(o is string s ? s : o.GetType().Name);
            first = false;
        }
        return sb.ToString();
    }
}
