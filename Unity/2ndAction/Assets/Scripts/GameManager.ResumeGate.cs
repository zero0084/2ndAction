using UnityEngine;

// 中断セーブからの再開に準備時間(2026-10-03)。ソロのCONTINUE(BeginContinuedRun)だけが対象。
//   1. Settling  : 復元直後。プレイヤーの操作/前進は止めたまま、画面遷移が開き切ってカメラ・地形・背景が
//                  復元位置へ追いつくまで数フレームだけ時間を流す(Time.timeScale=0の間はCameraFollowが動かない)。
//                  復元直後は敵がいない(中断データは敵/弾を保存しない)ので、この間に進むものは無い。
//   2. Waiting   : ゲーム全体を停止(TimeControlの停止理由)。「準備ができたら再開」ボタンを出す。
//                  HUD(HP/距離/速度)とポーズメニューは操作できる。
//   3. Countdown : ボタンで 3 → 2 → 1(実時間)。この間も停止のまま。GO で停止を解除し、操作と進行が始まる。
//                  途中でアプリが裏へ回った/ポーズメニューや設定を開いた/実時間が大きく飛んだ → Waitingへ戻す。
//   4. (任意)慣らし: GO の後、ゲーム全体の時間の流れ(Time.timeScale)を resumeEaseStartScale から 1 へ
//                  resumeEaseDuration 秒(実時間)かけて戻す。キャラの速度能力やカードは書き換えない。
//                  既定はOFF。調整は GameManager のインスペクター、ON/OFFは開発版のDEBUGパネルでも切り替えられる。
// プレイヤーの操作は Settling〜Countdown の間 PlayerController がカウントダウン中と同じく受け付けない
// (タッチの開始を見ないので、待機中のタッチやボタンのタップが GO の後に攻撃/ジャンプにならない)。
public partial class GameManager
{
    public enum ResumeGatePhase { None, Settling, Waiting, Countdown }

    [Header("中断セーブからの再開(2026-10-03)")]
    [Tooltip("ボタンを押してから再開までの1段の長さ(3→2→1、実時間の秒)")]
    public float resumeCountdownStep = 1f;
    [Tooltip("GO! の文字を出しておく時間(実時間の秒、操作はGO!の瞬間から有効)")]
    public float resumeGoDisplay = 0.6f;
    [Tooltip("再開直後の慣らし(ゲーム全体をゆっくり動かして通常へ戻す)。既定OFF。開発版はDEBUGパネルの切り替えが優先")]
    public bool resumeEaseEnabled = false;
    [Tooltip("慣らしの開始時の時間の流れ(0.2〜1)")]
    [Range(0.2f, 1f)] public float resumeEaseStartScale = 0.5f;
    [Tooltip("慣らしで通常の速さへ戻るまでの実時間の秒")]
    public float resumeEaseDuration = 2.5f;

    [Header("中断再開の足場(2026-10-03)")]
    [Tooltip("再開地点の前方を、穴も坂も無い平地にする(OFF=以前のチャンク数の予約のみ。確認用)")]
    public bool resumeSafeFootingEnabled = true;
    [Tooltip("前方の安全区間 = 保存された速度で通常どおり走った この秒数ぶんの距離")]
    public float resumeSafeSeconds = 2f;
    [Tooltip("前方の安全区間の最低距離(m)。低速でもこれだけは確保する")]
    public float resumeSafeMinAhead = 15f;
    [Tooltip("足元より後ろ側にも確保する距離(m)")]
    public float resumeSafeBehind = 4f;

    // 確認用: 直前のCONTINUEで決めた足場の区間
    public float LastResumeSafeAheadMeters { get; private set; }
    public float LastResumeSafeKmh { get; private set; }
    public int LastResumeFixedPits { get; private set; }

    // BeginContinuedRun から(プレイヤーを再開地点へ動かした直後、準備画面より前)
    void SetupResumeFooting(float checkpointDistance)
    {
        var tm = TerrainManager.Instance;
        var pc = PlayerController.Instance;
        if (tm == null || pc == null) return;
        if (!resumeSafeFootingEnabled)
        {
            tm.RequestFlatRun(Mathf.CeilToInt(safeZoneLength / Mathf.Max(1f, tm.flatLength)) + 1); // 以前の動き
            return;
        }
        // 速さは実際の移動(PlayerController.Move)と同じ式: 単位/秒(1単位=1m)。距離で決まる自然加速と
        // カード/キャラの速度をすべて含む。慣らしは時間の流れを遅くするだけなので、ここでは使わない(OFFが基準)。
        float speed = pc.CurrentAutoRunSpeed;
        float ahead = Mathf.Max(resumeSafeMinAhead, speed * Mathf.Max(0f, resumeSafeSeconds));
        float px = pc.transform.position.x;
        float lx = FloatingOrigin.ToLogical(px);
        LastResumeSafeAheadMeters = ahead;
        LastResumeSafeKmh = SpeedKmh(speed);
        LastResumeFixedPits = tm.SetResumeFlatZone(lx - Mathf.Max(0f, resumeSafeBehind), lx + ahead);
        // 区間の先(+通常の先読み)まで今すぐ生成する: 準備画面/カウントダウン/GO の間に足場が変わらない
        tm.GenerateNow(px + ahead + tm.generateAheadDistance);
        bool placed = pc.PlaceOnGroundForResume();
        // 敵/障害物/Formation を出さない区間も、足場の区間の終わりまで伸ばす(短い方は以前の safeZoneLength)
        safeZoneEndDistance = Mathf.Max(safeZoneEndDistance, checkpointDistance + ahead);
        Debug.Log($"[ResumeGate] footing: {SpeedKmh(speed):F1} km/h x {resumeSafeSeconds:F1}s -> flat {lx - resumeSafeBehind:F0}..{lx + ahead:F0} ({ahead:F0}m ahead), fixed pits={LastResumeFixedPits}, placed={placed}");
    }

    // 開発版だけ: DEBUGパネルでの上書き(-1=インスペクターの値に従う / 0=OFF / 1=ON)
    public const string ResumeEaseDevKey = "Dev.ResumeEase";

    static readonly object resumeGateTimeOwner = new object();
    // RETURN TO HOME の暗転中の停止(ReturnToHome で掛け、次のシーンの Awake → ClearResumeGate で外す)
    static readonly object returnHomeTimeOwner = new object();
    public static bool IsResumeGatePause(object owner) => owner == resumeGateTimeOwner;

    public ResumeGatePhase ResumeGate { get; private set; } = ResumeGatePhase.None;
    // PlayerController/スポーン側が見る: カウントダウン中と同じく操作/前進/距離加算を止める
    public bool ResumeGateActive => ResumeGate != ResumeGatePhase.None;
    public bool ResumeEaseRunning => resumeEaseActive;
    public string ResumeCountdownLabel => resumeGoTimer > 0f ? "GO!" : resumeCountdownLabel;
    // 確認用: 何回 Waiting へ戻したか(裏へ回った等)
    public int ResumeGateRevertCount { get; private set; }

    float resumeSettleTime;
    int resumeSettleFrames;
    float resumeCountdownElapsed;
    string resumeCountdownLabel = "";
    float resumeGoTimer;
    bool resumeEaseActive;
    float resumeEaseElapsed;

    public bool ResumeEaseSetting
    {
        get
        {
            if (Debug.isDebugBuild && SaveStore.HasKey(ResumeEaseDevKey))
            {
                int v = SaveStore.GetInt(ResumeEaseDevKey, -1);
                if (v >= 0) return v != 0;
            }
            return resumeEaseEnabled;
        }
    }

    // BeginContinuedRun の最後から(ソロのCONTINUEだけ)
    bool resumeGateAuto, resumeGateAccel = true, resumeGateWaitSprint;
    public int AutoResumes { get; private set; } // 確認用

    // autoResume: ボタンを待たずに走り出す / accel: 走り出しを初速から加速する(CONTINUE) / waitSprintOutro: 疾走の絵が消えるまで待つ
    void BeginResumeGate(bool autoResume = false, bool accel = true, bool waitSprintOutro = false)
    {
        if (NetRunLauncher.IsMultiplayerRun) return;
        resumeGateAuto = autoResume;
        resumeGateAccel = accel;
        resumeGateWaitSprint = waitSprintOutro;
        ResumeGate = ResumeGatePhase.Settling;
        resumeSettleTime = 0f;
        resumeSettleFrames = 0;
        resumeCountdownLabel = "";
        resumeGoTimer = 0f;
        StopResumeEase();
    }

    void EnterResumeWaiting()
    {
        ResumeGate = ResumeGatePhase.Waiting;
        resumeCountdownLabel = "";
        resumeCountdownElapsed = 0f;
        TimeControl.Pause(resumeGateTimeOwner);
    }

    // 「準備ができたら再開」ボタン。Waiting の時だけ受け付ける(連打しても1回)
    public bool RequestResumeFromGate()
    {
        if (ResumeGate != ResumeGatePhase.Waiting) return false;
        if (showPauseMenu || showReturnHomeConfirm || UiInputGate.ModalOpen) return false;
        ResumeGate = ResumeGatePhase.Countdown;
        resumeCountdownElapsed = 0f;
        resumeCountdownLabel = "3";
        return true;
    }

    // カウントダウン中に中断された → 停止した準備画面へ戻す
    void RevertResumeCountdown(string why)
    {
        if (ResumeGate != ResumeGatePhase.Countdown) return;
        ResumeGate = ResumeGatePhase.Waiting;
        resumeCountdownLabel = "";
        resumeCountdownElapsed = 0f;
        ResumeGateRevertCount++;
        TimeControl.Pause(resumeGateTimeOwner); // 念のため(停止のままのはず)
        Debug.Log($"[ResumeGate] countdown cancelled -> waiting ({why})");
    }

    void ReleaseResumeGate()
    {
        ResumeGate = ResumeGatePhase.None;
        resumeCountdownLabel = "";
        resumeGoTimer = resumeGoDisplay;
        TimeControl.Resume(resumeGateTimeOwner);
        if (ResumeEaseSetting && resumeEaseDuration > 0.01f && resumeEaseStartScale < 0.999f)
        {
            resumeEaseActive = true;
            resumeEaseElapsed = 0f;
            TimeControl.SetResumeEase(resumeEaseStartScale);
        }
        Debug.Log($"[ResumeGate] GO (ease={(resumeEaseActive ? $"x{resumeEaseStartScale:F2}->1 in {resumeEaseDuration:F1}s" : "off")})");
    }

    void StopResumeEase()
    {
        resumeEaseActive = false;
        resumeEaseElapsed = 0f;
        TimeControl.EndResumeEase();
    }

    // ラン/シーンが終わる・作り直される時: 停止理由と慣らしを残さない
    void ClearResumeGate()
    {
        ResumeGate = ResumeGatePhase.None;
        resumeCountdownLabel = "";
        resumeGoTimer = 0f;
        TimeControl.Resume(resumeGateTimeOwner);
        TimeControl.Resume(returnHomeTimeOwner);
        StopResumeEase();
    }

    // Update から毎フレーム
    void UpdateResumeGate()
    {
        float dtu = Time.unscaledDeltaTime;
        if (resumeGoTimer > 0f) resumeGoTimer = Mathf.Max(0f, resumeGoTimer - dtu);

        if (IsGameOver)
        {
            if (ResumeGateActive || resumeEaseActive) ClearResumeGate();
            return;
        }

        switch (ResumeGate)
        {
            case ResumeGatePhase.Settling:
                resumeSettleTime += dtu;
                resumeSettleFrames++;
                bool transitioning = ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning;
                // 遷移が開き切って、カメラ/地形が追いつく数フレームが過ぎたら止める(遷移が詰まっても3秒で止める)
                // 疾走の到着: 疾走の絵が実際のキャラへ重なって消えるまで(最長 2秒)待つ = 絵が消えた瞬間に走り出す
                if (resumeGateWaitSprint && SprintRunner.Instance != null && resumeSettleTime < 2f) break;
                if ((!transitioning && resumeSettleFrames >= 6) || resumeSettleTime > 3f)
                {
                    if (resumeGateAuto)
                    {
                        // CONTINUE: ボタンを待たずに走り出す。初速から保存時の速さまで5秒(ゲーム内時間)で加速
                        // 疾走の到着: 加速なし(疾走の勢いのまま、その距離の速さで)
                        AutoResumes++;
                        ReleaseResumeGate();
                        if (resumeGateAccel && PlayerController.Instance != null) PlayerController.Instance.BeginResumeAccel();
                    }
                    else EnterResumeWaiting();
                }
                break;
            case ResumeGatePhase.Waiting:
                // 停止理由が何かの安全装置で外されていたら付け直す(待機中に勝手に進まない)
                if (!TimeControl.IsPausedBy(resumeGateTimeOwner)) TimeControl.Pause(resumeGateTimeOwner);
                break;
            case ResumeGatePhase.Countdown:
                if (!TimeControl.IsPausedBy(resumeGateTimeOwner)) TimeControl.Pause(resumeGateTimeOwner);
                if (showPauseMenu || showReturnHomeConfirm || UiInputGate.ModalOpen) { RevertResumeCountdown("menu opened"); break; }
                // 実時間が大きく飛んだ = 裏へ回っていた(OnApplicationPause が来ない環境の保険)
                if (dtu > 0.5f) { RevertResumeCountdown($"frame gap {dtu:F2}s"); break; }
                resumeCountdownElapsed += dtu;
                float step = Mathf.Max(0.1f, resumeCountdownStep);
                if (resumeCountdownElapsed >= step * 3f) { ReleaseResumeGate(); break; }
                resumeCountdownLabel = (3 - Mathf.FloorToInt(resumeCountdownElapsed / step)).ToString();
                break;
        }

        if (resumeEaseActive)
        {
            // カード選択/ポーズ/ヒットストップ等で止まっている間は慣らしの時間も進めない(再開後に続きから)
            if (!TimeControl.IsPaused)
            {
                resumeEaseElapsed += Mathf.Min(dtu, 0.1f);
                float k = Mathf.Clamp01(resumeEaseElapsed / Mathf.Max(0.01f, resumeEaseDuration));
                if (k >= 1f) StopResumeEase();
                else TimeControl.SetResumeEase(Mathf.Lerp(resumeEaseStartScale, 1f, Mathf.SmoothStep(0f, 1f, k)));
            }
        }
    }

    // アプリが裏へ回った/フォーカスを失った
    void OnResumeGateAppInterrupted(string why)
    {
        if (ResumeGate == ResumeGatePhase.Countdown) RevertResumeCountdown(why);
    }

    // 再開ボタンとカウントダウンの表示(OnGUI、HUDの後)
    void DrawResumeGate()
    {
        if (ResumeGate == ResumeGatePhase.None)
        {
            if (resumeGoTimer > 0f) DrawResumeCountdownLabel("GO!");
            return;
        }
        if (ResumeGate == ResumeGatePhase.Countdown)
        {
            DrawResumeCountdownLabel(resumeCountdownLabel);
            return;
        }
        if (ResumeGate != ResumeGatePhase.Waiting) return;
        if (showPauseMenu || showReturnHomeConfirm || UiInputGate.ModalOpen) return;

        // 画面下の中央(キャラ/前方の敵/障害物/HUDに重ならない位置)。地面の下の帯に置く。
        float bw = Mathf.Min(340f, Screen.width * 0.6f), bh = 62f;
        float bottom = Screen.height - SafeBottom() - UiMargin;
        Rect btn = new Rect(Screen.width * 0.5f - bw * 0.5f, bottom - bh, bw, bh);
        Rect note = new Rect(Screen.width * 0.5f - 260f, btn.y - 30f, 520f, 26f);
        GUIStyle noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        noteStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);
        const string noteText = "一時停止中 ― 周りを確かめてから再開できます";
        DrawCenteredBackdrop(note, noteText, noteStyle);
        LocGUI.Label(note, noteText, noteStyle);
        if (DrawStyledButton(btn, "準備ができたら再開", 20f, primary: true))
        {
            if (RequestResumeFromGate() && AudioManager.Instance != null) AudioManager.Instance.PlaySe(SeId.Decide);
        }
    }

    void DrawResumeCountdownLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        bool isGo = label == "GO!";
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = isGo ? 96 : 120;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = isGo ? new Color(0.65f, 0.9f, 1f) : new Color(0.95f, 0.83f, 0.45f);
        Rect rect = new Rect(0f, Screen.height * 0.5f - 90f, Screen.width, 180f);
        GUIStyle shadowStyle = new GUIStyle(style);
        shadowStyle.normal.textColor = new Color(0.04f, 0.06f, 0.14f, 0.85f);
        LocGUI.Label(new Rect(rect.x + 4f, rect.y + 4f, rect.width, rect.height), label, shadowStyle);
        LocGUI.Label(rect, label, style);
    }

    // カウントダウンの音(新規ランのカウントダウンと同じ音)。表示の文字が変わった時だけ鳴らす。
    string resumeSeLabel = "";
    void UpdateResumeCountdownSe()
    {
        string label = ResumeCountdownLabel;
        if (label == resumeSeLabel) return;
        resumeSeLabel = label;
        if (AudioManager.Instance == null || string.IsNullOrEmpty(label)) return;
        AudioManager.Instance.PlaySe(label == "GO!" ? SeId.RunStart : SeId.CountdownTick);
    }
}

// CONTINUE の速さ(2026-10-07、ResumeAccel.cs)
public partial class GameManager
{
    void FillCheckpointSpeed(RunCheckpoint.Data data)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        data.speedDistance = pc.DistanceExact + pc.speedDistanceOffset;
        data.savedSpeedKmh = pc.NormalAutoRunSpeed * KmhPerMps; // 加速の途中でも、加速を掛けない通常の速さ
    }

    void ApplyCheckpointSpeed(RunCheckpoint.Data data)
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        pc.speedDistanceOffset = data.speedDistance > data.checkpointDistance ? (float)(data.speedDistance - data.checkpointDistance) : 0f;
        Debug.Log($"[ResumeAccel] checkpoint {data.checkpointDistance:F0}m speedDistance {data.speedDistance:F0} (offset {pc.speedDistanceOffset:F0}) saved {data.savedSpeedKmh:F1}km/h -> now {pc.NormalAutoRunSpeed * KmhPerMps:F1}km/h");
    }
}
