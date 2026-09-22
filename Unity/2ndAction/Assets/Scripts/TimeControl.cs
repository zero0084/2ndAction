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
public static class TimeControl
{
    static readonly HashSet<object> pauseOwners = new HashSet<object>();

    // BossMilestonePresentationは1.0→中間値→0という「演出用の連続的な
    // TimeScaleランプ」を独自に行う(このクラスが持つのは0/1の二値のみ)。
    // そのランプが進行中の間はこのクラスからのTime.timeScale書き込みを
    // 完全に止め、Presentation側の値をそのまま尊重する - 停止理由の集合
    // 自体(診断表示用)は通常どおり追跡する。
    static bool presentationDriving;

    public static bool IsPaused => pauseOwners.Count > 0;
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

    public static void BeginPresentationDrive(object owner)
    {
        presentationDriving = true;
        if (owner != null) pauseOwners.Add(owner);
    }

    public static void EndPresentationDrive(object owner)
    {
        presentationDriving = false;
        if (owner != null) pauseOwners.Remove(owner);
        Apply();
    }

    static void Apply()
    {
        if (presentationDriving) return;
        Time.timeScale = pauseOwners.Count > 0 ? 0f : 1f;
    }

    // リトライ/ホーム帰還/ゲームオーバーなど、「理由がどうあれ必ず通常状態
    // へ戻す」既存の安全ネットから呼ぶ。停止理由の集合ごと空にするので、
    // 次のRun/シーンに古い停止理由が残留しない。
    public static void ResetAll()
    {
        pauseOwners.Clear();
        presentationDriving = false;
        Time.timeScale = 1f;
    }

    public static string DescribeActiveReasons()
    {
        if (pauseOwners.Count == 0) return "(none)";
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
