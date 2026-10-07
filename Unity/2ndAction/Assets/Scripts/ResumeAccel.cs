using UnityEngine;

// CONTINUE の再開(2026-10-07): 再開ボタンを撤去して、復元が終わったら自動で走り出し、
// 通常のラン開始の初速から「セーブした瞬間の走行速度」までゲーム内時間で5秒かけて滑らかに加速する。
//  ・速さの元: 自然加速は「走った生の距離」で決まる(PlayerController.NaturalMultiplierAt)。ボス戦の間に進んだ分は記録の距離
//    (MaxDistance)から除かれるので、CONTINUE で記録の距離へ戻ると以前は速さが落ちていた。チェックポイントを保存した瞬間の
//    生の距離(speedDistance)を一緒に保存し、再開後は自然加速だけその差(speedDistanceOffset)を足して計算する。
//    距離の記録/報酬/関門には一切使わない。
//  ・加速: 到達先は毎フレームの通常の速さそのもの(= 保存時の速さ。カードの再適用も通常どおり)なので、5秒が終わると継ぎ目なく
//    通常の処理へ戻る。上限を掛けるだけで、カードの効果は二重に掛からない。
//  ・止まっている間(ポーズ/カード選択/ヒットストップ等で timeScale=0)は Time.deltaTime=0 なので5秒の計測も止まる。
//  ・加速の途中で中断しても、保存する値(チェックポイント)はそのまま(加速中の速さは保存しない)ので、次の CONTINUE も同じ所から。
public partial class PlayerController
{
    public const float ResumeAccelSeconds = 5f;
    float resumeAccelElapsed = -1f;          // <0 = 加速していない
    public float speedDistanceOffset;         // 自然加速だけに足す距離(CONTINUE で保存時の速さへ合わせる)
    public bool ResumeAccelActive => resumeAccelElapsed >= 0f;
    public float ResumeAccelElapsed => Mathf.Max(0f, resumeAccelElapsed);
    public float RunStartSpeed => CapSpeed(runSpeed * DebugRunOnlyScale); // 通常のラン開始時の初速(自然加速 x1)
    public float NormalAutoRunSpeed => autoRunEnabled ? CapSpeed(runSpeed * EffectiveSpeedMultiplier() * DebugRunOnlyScale) : 0f;

    public void BeginResumeAccel()
    {
        resumeAccelElapsed = 0f;
        Debug.Log($"[ResumeAccel] start {RunStartSpeed * GameManager.KmhPerMps:F1} -> {NormalAutoRunSpeed * GameManager.KmhPerMps:F1} km/h in {ResumeAccelSeconds}s (game time)");
    }
    public void EndResumeAccel() { resumeAccelElapsed = -1f; }

    float ApplyResumeAccel(float normal)
    {
        if (resumeAccelElapsed < 0f) return normal;
        float start = RunStartSpeed;
        if (normal <= start) return normal;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(resumeAccelElapsed / ResumeAccelSeconds));
        return Mathf.Lerp(start, normal, k);
    }

    void TickResumeAccel()
    {
        if (resumeAccelElapsed < 0f) return;
        var gm = GameManager.Instance;
        if (gm == null || gm.IsGameOver || !gm.HasStarted) { resumeAccelElapsed = -1f; return; }
        resumeAccelElapsed += Time.deltaTime; // 止まっている間は進まない
        if (resumeAccelElapsed >= ResumeAccelSeconds) { resumeAccelElapsed = -1f; Debug.Log($"[ResumeAccel] done at {NormalAutoRunSpeed * GameManager.KmhPerMps:F1} km/h"); }
    }
}
