#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 実機の不具合/要望の確認(2026-10-11): -qaFix5 <dir> [-qaFix5Only ABCDEFG] [-qaFix5Video 1]
//  E 読み込み中の表示 / F 障害物の警告 / G ホームの入口の札
//  A 疾走の後などにキャラが細く見える(縦の斜め表示 → 横への回転/表示方式の切り替え/疾走の繰り返し)。直す前の再現も撮る
//  B 持ち帰り予定 MILE(HUD の値 = 帰還で実際に増える額。脱出の長押し/一時停止の内訳、縦横)
//  C 縦画面の斜め表示の自然洞窟(558m/2400m、走行中の動画)と他のマップ
//  D 縦画面のカード選択(枠全体の選択中、長い翻訳、縦長/短め/横)
public partial class QaSweep
{
    bool F5(char c) { string o = Arg("-qaFix5Only", ""); return o == "" || o.IndexOf(c) >= 0; }

    void SetRes(int w, int h) { Screen.SetResolution(w, h, FullScreenMode.Windowed); }
    IEnumerator WaitRes(int w, int h)
    {
        float t = 0f;
        while ((Screen.width != w || Screen.height != h) && t < 4f) { yield return null; t += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(0.8f);
    }

    PlayerAnimator Pa => PlayerController.Instance != null ? PlayerController.Instance.GetComponent<PlayerAnimator>() : null;
    // 絵の向き(Z 以外の回転)。0 = 正面(横から見た元の向き)
    float VisualTilt()
    {
        var pa = Pa; var sr = pa != null ? pa.VisualRenderer : null;
        if (sr == null) return -1f;
        Vector3 e = sr.transform.localEulerAngles;
        return Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(0f, e.x)), Mathf.Abs(Mathf.DeltaAngle(0f, e.y)));
    }
    // 見かけの幅の割合(回転で横につぶれた分。1 = 元のまま)
    float VisualWidthRatio()
    {
        var pa = Pa; var sr = pa != null ? pa.VisualRenderer : null;
        if (sr == null || sr.sprite == null) return -1f;
        float natural = sr.sprite.bounds.size.x * Mathf.Abs(sr.transform.lossyScale.x);
        Vector3 right = sr.transform.rotation * Vector3.right;
        return natural > 0f ? Mathf.Abs(right.x) : -1f;
    }
    string HitboxSize()
    {
        var hb = typeof(PlayerController).GetField("attackHitbox", NP)?.GetValue(pc) as Collider2D;
        return hb != null ? $"{hb.transform.lossyScale.x:F2}x{hb.transform.lossyScale.y:F2} rot={hb.transform.eulerAngles}" : "-";
    }

    IEnumerator Fix5ModeQa()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1.5f);
        SaveProfile.Switch(true, true);
        yield return ReloadHome();
        TutorialProgress.MarkOffered();
        UnlockRules.DevUnlockAll = true; SprintRecords.DevUnlockAll = true;
        StartCoroutine(AutoPickCards());
        bool video = Arg("-qaFix5Video", "0") == "1";

        // ---------------- A: 細く見える ----------------
        if (F5('A'))
        {
            foreach (bool before in new[] { true, false })
            {
                Billboard.QaDisableRestore = before; PlayerAnimator.QaDisableGuard = before;
                string tg = before ? "before" : "after";
                SetRes(540, 960); yield return WaitRes(540, 960);
                PortraitRunView.Set(PortraitRunView.Oblique);
                yield return BeginRun("swordsman", "wasteland_road");
                yield return new WaitForSecondsRealtime(2.5f);
                bool obq = PortraitCameraRig.Instance != null && PortraitCameraRig.Instance.IsActive;
                L($"[A {tg}] portrait oblique={obq} tilt={VisualTilt():F0} {Pa?.VisualPoseReport()} hitbox {HitboxSize()}");
                Shot($"A_{tg}_portrait_oblique");
                SetRes(960, 540);
                // 回転の直後から1.5秒、毎フレームの向き(被弾/落下の復帰の姿勢で向きが入れ直されると、直す前でも元に戻る = 毎回は起きない理由)
                float tilt = 0f, wr = 1f, tw = 0f; bool shotThin = false;
                while (tw < 1.5f)
                {
                    yield return new WaitForEndOfFrame(); tw += Time.unscaledDeltaTime; // 描かれた後の向き(LateUpdate の後)
                    if (Screen.width < Screen.height || (PortraitCameraRig.Instance != null && PortraitCameraRig.Instance.IsActive)) continue;
                    float tl = VisualTilt(), wv = VisualWidthRatio();
                    if (tl > tilt) tilt = tl;
                    if (wv >= 0f && wv < wr) wr = wv;
                    if (before && !shotThin && tl > 30f) { shotThin = true; Shot($"A_{tg}_landscape_thin"); }
                }
                L($"[A {tg}] after rotate to landscape: tilt={tilt:F0} widthRatio={wr:F2} fixes={PlayerAnimator.VisualPoseFixes} {Pa?.VisualPoseReport()} hitbox {HitboxSize()}");
                Shot($"A_{tg}_landscape_after_rotate");
                if (before) Check(tilt > 30f && wr < 0.5f, $"A before: the old behaviour reproduces the thin character (tilt {tilt:F0}, width {wr:F2})");
                else Check(tilt < 1f && wr > 0.9f, $"A after: back to the normal width after portrait oblique -> landscape (tilt {tilt:F0}, width {wr:F2})");
                if (video && !before) yield return RecordUltimate("A_landscape_run", 3f);
                yield return EndRun();
            }
            Billboard.QaDisableRestore = false; PlayerAnimator.QaDisableGuard = false;

            // 縦画面で表示方式を切り替える(横から → 上下2段 → 斜め → 横から)
            SetRes(540, 960); yield return WaitRes(540, 960);
            PortraitRunView.Set(PortraitRunView.Side);
            yield return BeginRun("noble_lady", "natural_cave");
            yield return new WaitForSecondsRealtime(2f);
            foreach (int v in new[] { 1, 2, 0, 2, 1 })
            {
                PortraitRunView.Set(v); yield return new WaitForSecondsRealtime(1.2f);
                float tilt = VisualTilt();
                L($"[A view] -> {v}: tilt={tilt:F0} {Pa?.VisualPoseReport()}");
                if (v != 2) Check(tilt < 1f, $"A: view {v} after switching shows the normal orientation (tilt {tilt:F0})");
            }
            Shot("A_view_cycle_end");
            yield return EndRun();

            // 疾走 → 通常ラン(横、別の到着距離、繰り返し)。縦の斜めで疾走して到着後に横へ
            PortraitRunView.Set(PortraitRunView.Side);
            SetRes(960, 540); yield return WaitRes(960, 540);
            foreach (int dest in new[] { 30000, 90000, 20000 })
            {
                yield return Fix5Sprint("wasteland_road", dest);
                float tilt = VisualTilt(), wr = VisualWidthRatio();
                L($"[A sprint {dest}] arrival tilt={tilt:F0} width={wr:F2} {Pa?.VisualPoseReport()} hitbox {HitboxSize()}");
                Check(tilt < 1f && wr > 0.99f, $"A: after the {dest}m sprint the character has its own width (tilt {tilt:F0}, width {wr:F2})");
                Shot($"A_sprint_{dest / 1000}k_arrival");
                if (video && dest == 30000) yield return RecordUltimate("A_sprint_to_run", 3f);
                yield return EndRun();
            }
            PortraitRunView.Set(PortraitRunView.Oblique);
            SetRes(540, 960); yield return WaitRes(540, 960);
            yield return Fix5Sprint("natural_cave", 20000);
            L($"[A sprint portrait] arrival oblique tilt={VisualTilt():F0} {Pa?.VisualPoseReport()}");
            Shot("A_sprint_portrait_oblique_arrival");
            SetRes(960, 540); yield return WaitRes(960, 540);
            { float tilt = VisualTilt(); Check(tilt < 1f, $"A: sprint in portrait oblique, then rotate to landscape -> normal (tilt {tilt:F0})"); L($"[A sprint portrait->landscape] {Pa?.VisualPoseReport()}"); }
            Shot("A_sprint_portrait_then_landscape");
            yield return EndRun();
            Check(PlayerAnimator.VisualPoseFixes == 0, $"A: the pose guard never had to correct anything after the fix ({PlayerAnimator.VisualPoseFixes}: {PlayerAnimator.LastVisualPoseFix})");
            PortraitRunView.Set(PortraitRunView.Side);
        }

        // ---------------- B: 持ち帰り予定 MILE ----------------
        if (F5('B'))
        {
            foreach (var res in new[] { (960, 540), (540, 960) })
            {
                SetRes(res.Item1, res.Item2); yield return WaitRes(res.Item1, res.Item2);
                string tag = $"{res.Item1}x{res.Item2}";
                yield return BeginRun("swordsman", "wasteland_road");
                yield return new WaitForSecondsRealtime(1f);
                WarpTo(4200f); RunLedger.DevClearDebug();
                yield return new WaitForSecondsRealtime(2.5f);
                typeof(GameManager).GetProperty("RunEnemyMile").GetSetMethod(true).Invoke(gm, new object[] { 37 });
                typeof(GameManager).GetProperty("RunBossMile").GetSetMethod(true).Invoke(gm, new object[] { 50 });
                yield return new WaitForSecondsRealtime(0.3f);
                Shot($"B_{tag}_hud");
                // 脱出の長押しの間の内訳
                var eh = typeof(PlayerController).GetField("escapeHoldTimer", NP);
                for (int i = 0; i < 20; i++) { eh.SetValue(pc, 1.2f); yield return null; }
                Shot($"B_{tag}_escape_hold");
                for (int i = 0; i < 3; i++) { eh.SetValue(pc, 1.2f); yield return null; }
                eh.SetValue(pc, 0f);
                // 一時停止
                var spm = typeof(GameManager).GetField("showPauseMenu", NP);
                object owner = typeof(GameManager).GetField("pauseMenuTimeOwner", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                spm.SetValue(gm, true); TimeControl.Pause(owner);
                yield return new WaitForSecondsRealtime(0.5f); Shot($"B_{tag}_pause");
                yield return new WaitForSecondsRealtime(0.3f);
                spm.SetValue(gm, false); TimeControl.ResetAll();
                // 値の一致: 帰還(成功)で増える額 = 直前の持ち帰り予定(広告の 2倍は含めない)
                int take = gm.TakeHomeMile, owned0 = gm.TotalOwnedMile, after = gm.OwnedMileAfterReturn, pulls = gm.GachaPullsAfterReturn;
                stopKeepAlive = true;
                gm.Win();
                yield return new WaitForSecondsRealtime(0.5f);
                int got = gm.TotalOwnedMile - owned0;
                L($"[B {tag}] take-home {take} (dist {gm.RunDistanceMile} enemy {gm.RunEnemyMile} boss {gm.RunBossMile} bonus {gm.RunBonusMile} ring {gm.RunRingMile}) owned {owned0} -> {gm.TotalOwnedMile} (+{got}), shown after-return {after}, pulls {pulls} (cost {GameManager.GachaCostMile})");
                Check(got == take && gm.RunMile == take, $"B {tag}: the HUD take-home ({take}) equals what the return actually adds ({got}) and the result total ({gm.RunMile})");
                Check(after == gm.TotalOwnedMile && pulls == gm.TotalOwnedMile / GameManager.GachaCostMile, $"B {tag}: after-return total and gacha pulls match ({after}, {pulls})");
                yield return WaitTut(() => gm.ResultShown, 5f); yield return new WaitForSecondsRealtime(0.4f);
                Shot($"B_{tag}_result");
                yield return EndRun();
            }
            // 中断 → 再開: 同じ値(二重に足されない)
            SetRes(960, 540); yield return WaitRes(960, 540);
            yield return BeginRun("swordsman", "wasteland_road");
            WarpTo(3100f); RunLedger.DevClearDebug();
            yield return new WaitForSecondsRealtime(2f);
            typeof(GameManager).GetProperty("RunEnemyMile").GetSetMethod(true).Invoke(gm, new object[] { 21 });
            int beforeSuspend = gm.TakeHomeMile, ownedS = gm.TotalOwnedMile;
            stopKeepAlive = true;
            yield return ResumeGoHome();
            Check(gm.TotalOwnedMile == ownedS, $"B: suspending does not add the run's MILE ({ownedS} -> {gm.TotalOwnedMile})");
            yield return ResumeContinue("B resume");
            stopKeepAlive = false; StartCoroutine(KeepAlive());
            yield return new WaitForSecondsRealtime(1f);
            int afterResume = gm.TakeHomeMile;
            L($"[B resume] take-home before suspend {beforeSuspend}, after CONTINUE {afterResume} (d={gm.MaxDistance:F0})");
            Check(afterResume >= beforeSuspend && afterResume - beforeSuspend <= 2, $"B: after CONTINUE the take-home is the same amount (not doubled) ({beforeSuspend} -> {afterResume})");
            Shot("B_resume_hud");
            yield return EndRun();
        }

        // ---------------- C: 斜め表示の地形 ----------------
        if (F5('C'))
        {
            foreach (var res in new[] { (540, 960), (480, 1040) })
            {
                SetRes(res.Item1, res.Item2); yield return WaitRes(res.Item1, res.Item2);
                string tag = $"{res.Item1}x{res.Item2}";
                foreach (int v in new[] { PortraitRunView.Oblique, PortraitRunView.Side, PortraitRunView.Fold })
                {
                    if (res.Item2 == 1040 && v != PortraitRunView.Oblique) continue;
                    PortraitRunView.Set(v);
                    yield return BeginRun("swordsman", "natural_cave");
                    yield return new WaitForSecondsRealtime(1f);
                    WarpTo(520f); RunLedger.DevClearDebug();
                    yield return WaitTut(() => gm.MaxDistance >= 556f, 20f);
                    Shot($"C_{tag}_cave_view{v}_558m");
                    if (v == PortraitRunView.Oblique)
                    {
                        var tm = TerrainManager.Instance; var cs = tm != null ? tm.cave : null;
                        L($"[C {tag}] cave oblique: ground generated to +{(tm != null ? tm.GeneratedEndX - pc.transform.position.x : 0f):F0}m, ceiling to +{(cs != null ? cs.GeneratedEndX - pc.transform.position.x : 0f):F0}m");
                        Check(tm != null && tm.GeneratedEndX - pc.transform.position.x >= TerrainManager.ObliqueAheadMeters - 10f, "C: in oblique view the ground is generated far enough ahead");
                        Check(cs != null && cs.GeneratedEndX - pc.transform.position.x >= TerrainManager.ObliqueAheadMeters - 20f, "C: in oblique view the cave ceiling is generated far enough ahead");
                        if (video && res.Item2 == 960) yield return RecordUltimate($"C_cave_oblique_run", 5f);
                        WarpTo(2400f); RunLedger.DevClearDebug();
                        yield return new WaitForSecondsRealtime(3f);
                        Shot($"C_{tag}_cave_view{v}_2400m");
                    }
                    yield return EndRun();
                }
            }
            SetRes(540, 960); yield return WaitRes(540, 960);
            PortraitRunView.Set(PortraitRunView.Oblique);
            foreach (var st in new[] { "wasteland_road", "sky_corridor", "last_corridor" })
            {
                yield return BeginRun("swordsman", st);
                yield return new WaitForSecondsRealtime(1f);
                WarpTo(st == "last_corridor" ? 3000f : 1600f); RunLedger.DevClearDebug();
                yield return new WaitForSecondsRealtime(3f);
                Shot($"C_oblique_{st}");
                yield return EndRun();
            }
            PortraitRunView.Set(PortraitRunView.Side);
            SetRes(960, 540); yield return WaitRes(960, 540);
            yield return BeginRun("swordsman", "natural_cave");
            WarpTo(520f); RunLedger.DevClearDebug();
            yield return WaitTut(() => gm.MaxDistance >= 556f, 20f);
            Shot("C_landscape_cave_558m");
            yield return EndRun();
        }

        // ---------------- D: 縦画面のカード選択 ----------------
        if (F5('D'))
        {
            string langKeep = Loc.Current;
            foreach (var res in new[] { (540, 960), (480, 1040), (600, 900), (960, 540) })
                foreach (string lang in res.Item1 == 540 ? new[] { "ja", "de", "ru" } : new[] { "ja" })
                {
                    SetRes(res.Item1, res.Item2); yield return WaitRes(res.Item1, res.Item2);
                    Loc.Set(lang);
                    string tag = $"{res.Item1}x{res.Item2}_{lang}";
                    PortraitRunView.Set(PortraitRunView.Side);
                    yield return BeginRun("swordsman", "wasteland_road");
                    yield return new WaitForSecondsRealtime(1.5f);
                    // 長い説明の候補(合成済み/FINAL EVOLUTION の文が長い)を混ぜるため、何枚か取ってから
                    var apply = typeof(GameManager).GetMethod("ApplyUpgradeByCardId", NP);
                    foreach (var id in new[] { "chain_lightning", "speed_up", "jump_power_up" }) { try { apply.Invoke(gm, new object[] { id }); } catch { } yield return null; }
                    autoPickHold = true;
                    typeof(GameManager).GetMethod("TriggerLevelUpChoice", NP).Invoke(gm, null);
                    yield return WaitTut(() => gm.IsRewardSequenceWaitingForSelection, 6f);
                    yield return new WaitForSecondsRealtime(0.7f);
                    Shot($"D_{tag}_choice");
                    var seq = FindFirstObjectByType<RewardCardSequence>();
                    seq.OnCardClicked(1);
                    yield return new WaitForSecondsRealtime(0.5f);
                    Shot($"D_{tag}_selected_2nd");
                    Check(gm.IsRewardSequenceWaitingForSelection, $"D {tag}: one tap only selects (still waiting)");
                    yield return new WaitForSecondsRealtime(0.3f);
                    seq.OnCardClicked(2);
                    yield return new WaitForSecondsRealtime(0.4f);
                    Check(gm.IsRewardSequenceWaitingForSelection, $"D {tag}: tapping another candidate switches the selection (not a confirm)");
                    Shot($"D_{tag}_selected_3rd");
                    yield return new WaitForSecondsRealtime(0.3f);
                    seq.OnCardClicked(2);
                    yield return WaitTut(() => !gm.IsRewardSequenceWaitingForSelection, 3f);
                    Check(!gm.IsRewardSequenceWaitingForSelection, $"D {tag}: tapping the selected candidate again confirms");
                    autoPickHold = false;
                    yield return WaitTut(() => !gm.IsRewardSequenceRunning, 6f);
                    yield return EndRun();
                }
            Loc.Set(langKeep);
            SetRes(960, 540); yield return WaitRes(960, 540);
        }

        // ---------------- E: 読み込み中の表示 ----------------
        if (F5('E'))
        {
            foreach (var res in new[] { (960, 540), (540, 960) })
            {
                SetRes(res.Item1, res.Item2); yield return WaitRes(res.Item1, res.Item2);
                string tag = $"{res.Item1}x{res.Item2}";
                // ラン → Retry(シーンの読み直し): 読み込みの間に距離が進まない/表示が固まらない
                yield return BeginRun("swordsman", "wasteland_road");
                yield return new WaitForSecondsRealtime(1.5f);
                var old = gm; float d0 = gm.MaxDistance; int loads0 = LoadingScreen.Loads;
                LoadingScreen.QaExtraHold = 1.2f; // 撮影のために表示を少し残す
                stopKeepAlive = true;
                gm.Retry();
                bool sawBusy = false, distMoved = false, blocked = true; float shotAt = -1f;
                float w = 0f;
                while ((GameManager.Instance == null || GameManager.Instance == old || LoadingScreen.Busy) && w < 15f)
                {
                    if (LoadingScreen.Busy) { sawBusy = true; if (!LoadingScreen.BlocksInput || !UiInputGate.ModalOpen) blocked = false; }
                    if (old != null && old.MaxDistance > d0 + 0.5f) distMoved = true;
                    if (LoadingScreen.Visible && shotAt < 0f) shotAt = w;
                    if (shotAt >= 0f && w - shotAt > 0.3f && shotAt < 900f) { shotAt = 999f; Shot($"E_{tag}_loading"); } // 表示しきってから
                    yield return null; w += Time.unscaledDeltaTime;
                }
                gm = GameManager.Instance;
                L($"[E {tag}] reload: loads +{LoadingScreen.Loads - loads0}, wait {LoadingScreen.LastWaitSeconds:F2}s (QA hold 1.2s), shown {LoadingScreen.TimesShown}, worst frame while loading {LoadingScreen.WorstFrameWhileBusy * 1000f:F0}ms, input blocked {blocked}, old run moved {distMoved}");
                Check(sawBusy && blocked && !distMoved, $"E {tag}: during the reload the input is blocked and the old run does not advance");
                Check(LoadingScreen.WorstFrameWhileBusy < 0.5f, $"E {tag}: the loading display keeps drawing (worst frame {LoadingScreen.WorstFrameWhileBusy * 1000f:F0}ms)");
                yield return new WaitForSecondsRealtime(1f);
                // 普通の読み直し(撮影の保持なし): 短ければ表示は出ない
                int shown0 = LoadingScreen.TimesShown;
                yield return BeginRun("swordsman", "wasteland_road");
                yield return new WaitForSecondsRealtime(1f);
                old = gm; stopKeepAlive = true; gm.Retry();
                w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old || LoadingScreen.Busy) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
                gm = GameManager.Instance;
                L($"[E {tag}] plain reload: {LoadingScreen.LastWaitSeconds:F2}s, shown {(LoadingScreen.TimesShown > shown0 ? "yes" : "no")} (threshold {LoadingScreen.ShowAfter:F2}s)");
                Check(LoadingScreen.LastWaitSeconds >= LoadingScreen.ShowAfter || LoadingScreen.TimesShown == shown0, $"E {tag}: a short load does not flash the loading screen");
                // 失敗の表示 → もう一度読み込む
                LoadingScreen.QaForceFail = true;
                yield return BeginRun("swordsman", "wasteland_road");
                yield return new WaitForSecondsRealtime(1f);
                old = gm; stopKeepAlive = true; gm.Retry();
                yield return new WaitForSecondsRealtime(0.8f);
                Shot($"E_{tag}_failed");
                yield return null; yield return null; // 撮影はフレームの終わり(次の読み直しを始める前に撮る)
                Check(LoadingScreen.Busy && LoadingScreen.Visible && LoadingScreen.Failed, $"E {tag}: a failed load shows the message and the retry button (not a dark screen)");
                LoadingScreen.QaForceFail = false;
                LoadingScreen.ReloadActive("qa retry after failure");
                w = 0f; while ((GameManager.Instance == null || GameManager.Instance == old || LoadingScreen.Busy) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
                gm = GameManager.Instance;
                Check(gm != null && gm != old && !LoadingScreen.Busy, $"E {tag}: loading again after a failure reaches the next screen");
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // ---------------- F: 障害物の警告 ----------------
        if (F5('F'))
        {
            foreach (var c in new[] { ("wasteland_road", 3000f, 960, 540, 0), ("wasteland_road", 52000f, 960, 540, 0), ("wasteland_road", 78000f, 960, 540, 0), ("natural_cave", 2400f, 960, 540, 0),
                                      ("sky_corridor", 3000f, 960, 540, 0), ("last_corridor", 3000f, 960, 540, 0), ("wasteland_road", 3000f, 540, 960, 0), ("wasteland_road", 3000f, 540, 960, 2), ("natural_cave", 2400f, 540, 960, 2) })
            {
                SetRes(c.Item3, c.Item4); yield return WaitRes(c.Item3, c.Item4);
                PortraitRunView.Set(c.Item5);
                yield return BeginRun("swordsman", c.Item1);
                typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
                yield return new WaitForSecondsRealtime(1f);
                WarpTo(c.Item2); RunLedger.DevClearDebug();
                string tag = $"{c.Item1}_{c.Item2 / 1000f:0}k_{c.Item3}x{c.Item4}_v{c.Item5}";
                float w = 0f; int maxWarn = 0, frames = 0, warnFrames = 0;
                bool shot = false;
                while (w < 14f)
                {
                    yield return null; w += Time.unscaledDeltaTime; frames++;
                    maxWarn = Mathf.Max(maxWarn, ObstacleWarning.WarnedNow);
                    if (ObstacleWarning.WarnedNow > 0) warnFrames++;
                    if (!shot && ObstacleWarning.WarnedNow > 0 && w > 2f) { yield return new WaitForSecondsRealtime(0.25f); Shot($"F_{tag}"); shot = true; }
                }
                // 壊れた物は警告しない
                int brokenWarned = 0;
                foreach (var oc in ObstacleController.All) if (oc != null && oc.Broken && oc.Visual != null && oc.Visual.transform.Find("HazardRim") is Transform hr && hr.GetComponent<MeshRenderer>().enabled) brokenWarned++;
                L($"[F {tag}] warned max {maxWarn} (cap {ObstacleWarning.MaxWarned}), frames with a warning {warnFrames}/{frames}, total {ObstacleWarning.WarnedTotal}, broken still outlined {brokenWarned}, mode {GameSettings.ObstacleWarn}");
                Check(maxWarn <= ObstacleWarning.MaxWarned && brokenWarned == 0, $"F {tag}: at most {ObstacleWarning.MaxWarned} warnings at once, none on broken obstacles");
                if (c.Item1 == "wasteland_road" && c.Item3 == 960 && c.Item2 < 10000f)
                {
                    GameSettings.SetObstacleWarn(2);
                    float w2 = 0f; while (ObstacleWarning.WarnedNow == 0 && w2 < 8f) { yield return null; w2 += Time.unscaledDeltaTime; }
                    yield return new WaitForSecondsRealtime(0.2f);
                    Shot($"F_{tag}_outline_only");
                    GameSettings.SetObstacleWarn(1);
                }
                yield return EndRun();
            }
            PortraitRunView.Set(PortraitRunView.Side);
        }

        // ---------------- G: ホームの入口の札 ----------------
        if (F5('G'))
        {
            string langKeep = Loc.Current;
            foreach (var res in new[] { (960, 540), (1280, 600), (540, 960), (480, 1040), (600, 900) })
                foreach (string lang in res.Item1 == 960 || res.Item1 == 540 ? new[] { "ja", "de", "ru" } : new[] { "ja" })
                {
                    SetRes(res.Item1, res.Item2); yield return WaitRes(res.Item1, res.Item2);
                    Loc.Set(lang);
                    RunCheckpoint.Clear(); // 中断中のランなし(扉 = 出発)
                    yield return ReloadHome();
                    yield return new WaitForSecondsRealtime(2.5f);
                    gm = GameManager.Instance;
                    string tag = $"{res.Item1}x{res.Item2}_{lang}";
                    Shot($"G_{tag}_home");
                    var rs = gm.LastHomeSignRects;
                    int off = 0, overlap = 0;
                    for (int i = 0; i < rs.Length; i++)
                    {
                        if (rs[i].width <= 0f) continue;
                        if (rs[i].x < 0f || rs[i].y < 0f || rs[i].xMax > Screen.width || rs[i].yMax > Screen.height) off++;
                        for (int j = i + 1; j < rs.Length; j++) if (rs[j].width > 0f && rs[i].Overlaps(rs[j])) overlap++;
                    }
                    L($"[G {tag}] signs {string.Join(" ", rs.Select(r => r.width > 0f ? $"({r.x:F0},{r.y:F0},{r.width:F0}x{r.height:F0})" : "-"))} off-screen {off} overlaps {overlap}");
                    Check(off == 0 && overlap == 0, $"G {tag}: home signs stay on screen and do not overlap each other");
                }
            Loc.Set(langKeep);
            SetRes(960, 540); yield return WaitRes(960, 540);
        }

        UnlockRules.DevUnlockAll = false; SprintRecords.DevUnlockAll = false;
        SaveProfile.Switch(false, true);
        yield return ReloadHome();
    }

    // 疾走出発 → 到着して走り出すまで(リングは自動で通る、3択は最初の候補)
    IEnumerator Fix5Sprint(string stage, int dest)
    {
        yield return WaitHome();
        gm = GameManager.Instance;
        gm.SetSelectedCharacter("swordsman");
        SprintRunner.QaRingPolicy = 1; SprintRunner.QaTimeScale = 6f;
        bool ok = gm.DepartSprint(stage, dest);
        Check(ok, $"sprint departure accepted ({stage} {dest}m)");
        float w = 0f;
        while (SprintRunner.Instance == null && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        w = 0f;
        while ((SprintRunner.Instance != null || gm.ResumeGate != GameManager.ResumeGatePhase.None || !gm.HasStarted) && w < 200f) { yield return null; w += Time.unscaledDeltaTime; }
        SprintRunner.QaRingPolicy = -1; SprintRunner.QaTimeScale = 1f;
        pc = PlayerController.Instance; stopKeepAlive = false; StartCoroutine(KeepAlive());
        yield return new WaitForSecondsRealtime(1.2f);
    }
}
#endif
