#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// キャラ選択のカード規格とカルーセル(2026-11-03)の確認: -qaCarousel <dir> [-crPhase 1|2] [-crVideo 1] [-crWait 0]
//  A カードの外寸が全員同じ / B 枠が共通(全員同じ部品)/ H 中央・隣・外の倍率 / I 吸着の途中の倍率が連続 / J 吸着の時間と確定のタイミング
//  C/D ドラッグ(ゆっくり小さく → 戻る、距離 → 次、払い → 1人、強い払い → 2人まで)/ E 矢印 / F キーボード / G パッド / K 隣のカードを直接タップ
//  O/P 確定で立ち絵・名前が切り替わる(ドラッグ中は切り替わらない)/ M/N ルカ・ペンタ / S 確定したキャラを保存 → -crPhase 2 で開いた時に中央
//  -crWait N: 開いたまま N 秒待つ(エミュレーターで本物のスワイプを adb から入れる確認用。選択の変化をログへ)
public partial class QaSweep
{
    CharacterSelectUI crUi;
    const BindingFlags CrNP = BindingFlags.Instance | BindingFlags.NonPublic;

    IEnumerator CarouselMode()
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        UnlockRules.DevUnlockAll = true;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; // 払いの速さはフレームの時間で測る → 60fps にそろえる
        yield return new WaitForSecondsRealtime(1.5f);
        string phase = Arg("-crPhase", "1");
        if (phase == "2")
        {
            string want = SaveStore.GetString("QaCarouselWant", "");
            gm.OpenCharacterSelect();
            yield return new WaitForSecondsRealtime(1.5f);
            crUi = FindFirstObjectByType<CharacterSelectUI>();
            L($"[S] after restart: selected char {gm.SelectedCharacterId}, centered {crUi.DebugSelectedId}, expected {want}");
            Check(want != "" && gm.SelectedCharacterId == want && crUi.DebugSelectedId == want && Mathf.Abs(crUi.DebugScaleOfPos(crUi.DebugSelected) - CharacterSelectUI.CenterBoost) < 0.01f,
                "S: after a restart the saved character opens in the center");
            Shot($"cr_{Screen.width}x{Screen.height}_restart");
            yield break;
        }
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        crUi = FindFirstObjectByType<CharacterSelectUI>();
        Check(crUi != null && crUi.isActiveAndEnabled, "character select opened");
        if (crUi == null) yield break;
        int n = crUi.VisibleCount;
        L($"[open] {n} characters, selected {crUi.DebugSelectedId} (saved {gm.SelectedCharacterId}), stride {crUi.CardStrideDebug}");
        Check(crUi.DebugSelectedId == gm.SelectedCharacterId, "the currently selected character opens in the center");

        float waitSec = float.Parse(Arg("-crWait", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (waitSec > 0f)
        {
            // 本物の入力(adb のスワイプ/マウス)を待つ: 選択が変わるたびにログ
            string last = crUi.DebugSelectedId; float t0 = Time.unscaledTime; int frames = 0; float worst = 0f;
            while (Time.unscaledTime - t0 < waitSec)
            {
                yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                if (crUi.DebugSelectedId != last) { last = crUi.DebugSelectedId; L($"[real] t={Time.unscaledTime - t0:F2}s selected -> {last} (pos {crUi.DebugSelected}, refreshes {crUi.DetailRefreshes}, release target {crUi.LastReleaseTarget})"); Shot($"cr_real_{crUi.DebugSelected:00}_{last}"); }
            }
            L($"[real] done: {frames} frames in {waitSec:F0}s ({frames / waitSec:F1} fps, worst frame {worst * 1000f:F0}ms)");
            yield break;
        }

        // ---- A/B: カードの外寸と部品 ----
        var sizes = new HashSet<string>(); var parts = new HashSet<string>();
        for (int p = 0; p < n; p++)
        {
            var f = crUi.DebugFace(p); if (f == null) continue;
            sizes.Add($"{f.rect.size.x:F1}x{f.rect.size.y:F1}");
            var names = new System.Text.StringBuilder(); foreach (Transform c in f) names.Append(c.name).Append(',');
            parts.Add(names.ToString());
            Rect uv = crUi.DebugArtUv(p);
            L($"[card] {p} face {f.rect.size} uv x{uv.x:F3} y{uv.y:F3} w{uv.width:F3} h{uv.height:F3}");
        }
        Check(sizes.Count == 1, $"A: all {n} cards have the same outer size ({string.Join(" / ", sizes)})");
        Check(parts.Count == 1, $"B: all cards are built from the same frame parts ({parts.Count} kind(s))");

        // 左右の端と真ん中で撮る
        foreach (int p in new[] { 0, n / 2, n - 1 })
        {
            crUi.DebugSelectAndCenter(p); yield return null; yield return new WaitForSecondsRealtime(0.4f);
            Shot($"cr_{Screen.width}x{Screen.height}_pos{p:00}_{crUi.DebugSelectedId}"); yield return null; yield return null; // 撮影はフレームの終わり → 次を動かす前に1フレーム
        }

        // 全員を1人ずつ中央へ(カードの規格の比較用)
        if (Arg("-crAll", "0") == "1")
            for (int p = 0; p < n; p++)
            {
                crUi.DebugSelectAndCenter(p); yield return null; yield return new WaitForSecondsRealtime(0.35f);
                Shot($"cra_{Screen.width}x{Screen.height}_{p:00}_{crUi.DebugSelectedId}"); yield return null; yield return null;
            }

        // ---- H: 倍率(同じ位置に来たカードは同じ倍率)----
        int mid = Mathf.Clamp(n / 2, 2, n - 3);
        var centerS = new List<float>(); var neighS = new List<float>(); var outerS = new List<float>();
        foreach (int p in new[] { 2, mid, n - 3 })
        {
            crUi.DebugSelectAndCenter(p); yield return null; yield return null;
            centerS.Add(crUi.DebugScaleOfPos(p)); neighS.Add(crUi.DebugScaleOfPos(p + 1)); neighS.Add(crUi.DebugScaleOfPos(p - 1)); outerS.Add(crUi.DebugScaleOfPos(p + 2)); outerS.Add(crUi.DebugScaleOfPos(p - 2));
        }
        L($"[H] center {string.Join(",", centerS.ConvertAll(x => x.ToString("F3")))} neighbor {string.Join(",", neighS.ConvertAll(x => x.ToString("F3")))} outer {string.Join(",", outerS.ConvertAll(x => x.ToString("F3")))}");
        Check(Spread(centerS) < 0.005f && Spread(neighS) < 0.005f && Spread(outerS) < 0.005f, "H: cards at the same place have the same scale (center / neighbor / outer)");
        Check(Mathf.Abs(centerS[0] / CharacterSelectUI.CenterBoost - 1f) < 0.01f && Mathf.Abs(neighS[0] / centerS[0] - CharacterSelectUI.NeighborScale) < 0.01f && Mathf.Abs(outerS[0] / centerS[0] - CharacterSelectUI.OuterScale) < 0.01f,
            $"H: neighbor {neighS[0] / centerS[0]:F2} / outer {outerS[0] / centerS[0]:F2} of the center");

        // ---- I/J/O/P: 矢印で次へ(吸着の途中は倍率が連続、終わった時に1回だけ確定)----
        crUi.DebugSelectAndCenter(mid); yield return null; yield return null;
        int refresh0 = crUi.DetailRefreshes; string before = crUi.DebugSelectedId;
        var arrowR = crUi.carouselArrowRight;
        typeof(CharacterSelectUI).GetMethod("HandleTap", CrNP).Invoke(crUi, new object[] { (Vector2)RectTransformUtility.WorldToScreenPoint(null, arrowR.TransformPoint(arrowR.rect.center)) });
        float t = 0f; float maxJump = 0f; float prevA = crUi.DebugScaleOfPos(mid), prevB = crUi.DebugScaleOfPos(mid + 1); bool changedEarly = false; int samples = 0;
        bool video = Arg("-crVideo", "0") == "1";
        while (crUi.DebugSnapping && t < 1f)
        {
            if (video) Shot($"cr_snap_{samples:00}");
            yield return null; t += Time.unscaledDeltaTime; samples++;
            float a = crUi.DebugScaleOfPos(mid), b = crUi.DebugScaleOfPos(mid + 1);
            maxJump = Mathf.Max(maxJump, Mathf.Abs(a - prevA), Mathf.Abs(b - prevB)); prevA = a; prevB = b;
            if (crUi.DebugSnapping && crUi.DebugSelectedId != before) changedEarly = true;
        }
        L($"[I/J] arrow: {samples} frames, {t:F2}s, biggest scale step per frame {maxJump:F3}, selected {before} -> {crUi.DebugSelectedId}, refreshes +{crUi.DetailRefreshes - refresh0}");
        Check(!changedEarly && crUi.DebugSelected == mid + 1 && crUi.DetailRefreshes - refresh0 == 1, "E/J: the right arrow slides to the next card and confirms once when the snap ends");
        Check(t >= CharacterSelectUI.SnapMin - 0.02f && t <= CharacterSelectUI.SnapMax + 0.05f && samples >= 5, $"J: the snap takes {t:F2}s (0.16-0.30)");
        Check(maxJump < 0.12f, $"I: the scale changes smoothly during the slide (largest step {maxJump:F3})");
        yield return new WaitForSecondsRealtime(0.3f);
        var def = CharacterDatabase.FindById(crUi.DebugSelectedId);
        Check(crUi.mainVisualImage.sprite != null && crUi.mainVisualImage.sprite.texture == def.mainVisual && crUi.titleText.text == Loc.Auto(def.displayName),
            "O/P: the big portrait and the name follow the confirmed character");

        // ---- C/D: ドラッグと払い(実際の入力と同じ処理: DragBegin/Move/End)----
        yield return DragCase("C1 small slow drag returns", mid + 1, -40f, 10, 0.15f, 0);
        yield return DragCase("C2 drag 0.45 card goes next", mid + 1, -105f, 15, 0.15f, +1);
        yield return DragCase("C3 light flick goes 1", mid + 1, -60f, 3, 0f, +1);
        yield return DragCase("C4 strong flick goes at most 2", 3, -210f, 3, 0f, +2);
        yield return DragCase("C5 flick back", mid, +60f, 3, 0f, -1);
        yield return DragCase("C6 long slow drag keeps the nearest", 2, -crUi.CardStrideDebug * 3f, 40, 0.15f, +3);
        int refreshBefore = crUi.DetailRefreshes;
        crUi.DebugSelectAndCenter(mid); yield return null;
        refreshBefore = crUi.DetailRefreshes;
        crUi.DebugDragBegin();
        for (int k = 0; k < 20; k++) { crUi.DebugDragMove(-12f); yield return null; }
        Check(crUi.DetailRefreshes == refreshBefore, "23: dragging over cards does not switch the portrait / confirm a character");
        crUi.DebugDragEnd(); yield return WaitSnap();

        // ---- K: 隣のカードを直接タップ ----
        int from = crUi.DebugSelected;
        crUi.DebugTapCard(from - 1); bool anim = crUi.DebugSnapping; yield return WaitSnap();
        Check(anim && crUi.DebugSelected == from - 1, "K: tapping the neighbor card slides it to the center and selects it");

        // ---- F/G: キーボード / パッドの左右(同じ吸着)----
        GameInput.DebugMarkDevice(InputDeviceKind.Keyboard);
        from = crUi.DebugSelected;
        GameInput.Inject(GameAction.NavRight); yield return null; yield return null; anim = crUi.DebugSnapping; yield return WaitSnap();
        Check(anim && crUi.DebugSelected == from + 1, "F: keyboard right moves to the next character with the carousel animation");
        GameInput.DebugMarkDevice(InputDeviceKind.Gamepad);
        GameInput.Inject(GameAction.NavLeft); yield return null; yield return null; anim = crUi.DebugSnapping; yield return WaitSnap();
        Check(anim && crUi.DebugSelected == from, "G: pad left moves to the previous character with the carousel animation");
        GameInput.DebugMarkDevice(InputDeviceKind.Touch);

        // ---- M/N: ルカ・ペンタ(外寸は同じ、窓の中の体は小柄なまま)----
        foreach (var id in new[] { "luka", "pentatuttulu", "swordsman", "ninja", "dragon_lancer" })
        {
            int p = -1; for (int k = 0; k < n; k++) { crUi.DebugSelectAndCenter(k); if (crUi.DebugSelectedId == id) { p = k; break; } }
            if (p < 0) continue;
            yield return null; yield return new WaitForSecondsRealtime(0.3f);
            L($"[M/N] {id}: face {crUi.DebugFaceSize(p)} uv {crUi.DebugArtUv(p)}");
            Shot($"cr_{Screen.width}x{Screen.height}_{id}"); yield return null; yield return null;
        }

        // 動画用の連番(-crMovie 1): 30fps の時間で進める(撮影で遅れても動きは同じ)。ドラッグ → 離す → 吸着、矢印、キーの左
        if (Arg("-crMovie", "0") == "1")
        {
            crUi.DebugSelectAndCenter(3); yield return null; yield return null;
            Time.captureDeltaTime = 1f / 30f; int fr = 0;
            for (int k = 0; k < 12; k++) { Shot($"crm_{fr++:0000}"); yield return null; }
            crUi.DebugDragBegin();
            for (int k = 0; k < 24; k++) { crUi.DebugDragMove(-7f); Shot($"crm_{fr++:0000}"); yield return null; } // ゆっくり 0.7 枚ぶん
            crUi.DebugDragEnd();
            for (int k = 0; k < 24; k++) { Shot($"crm_{fr++:0000}"); yield return null; }
            crUi.DebugDragBegin();
            for (int k = 0; k < 4; k++) { crUi.DebugDragMove(-28f); Shot($"crm_{fr++:0000}"); yield return null; } // 払い
            crUi.DebugDragEnd();
            for (int k = 0; k < 24; k++) { Shot($"crm_{fr++:0000}"); yield return null; }
            var ar = crUi.carouselArrowRight;
            typeof(CharacterSelectUI).GetMethod("HandleTap", CrNP).Invoke(crUi, new object[] { (Vector2)RectTransformUtility.WorldToScreenPoint(null, ar.TransformPoint(ar.rect.center)) });
            for (int k = 0; k < 24; k++) { Shot($"crm_{fr++:0000}"); yield return null; }
            GameInput.DebugMarkDevice(InputDeviceKind.Keyboard); GameInput.Inject(GameAction.NavLeft);
            for (int k = 0; k < 24; k++) { Shot($"crm_{fr++:0000}"); yield return null; }
            GameInput.DebugMarkDevice(InputDeviceKind.Touch);
            Time.captureDeltaTime = 0f;
            L($"[movie] {fr} frames");
        }

        // ---- S: 確定して保存(-crPhase 2 で開き直して確かめる)----
        string pick = "vampire";
        for (int k = 0; k < n; k++) { crUi.DebugSelectAndCenter(k); if (crUi.DebugSelectedId == pick) break; }
        yield return null;
        SaveStore.SetString("QaCarouselWant", pick); SaveStore.Save();
        typeof(CharacterSelectUI).GetMethod("Confirm", CrNP).Invoke(crUi, null);
        yield return new WaitForSecondsRealtime(1.0f);
        L($"[S] confirmed {pick}: saved selection {gm.SelectedCharacterId}");
        Check(gm.SelectedCharacterId == pick, "S: confirming saves the selected character");
        SaveStore.Save();
    }

    IEnumerator DragCase(string name, int startPos, float total, int frames, float holdBeforeRelease, int expect)
    {
        crUi.DebugSelectAndCenter(startPos); yield return null; yield return null;
        crUi.DebugDragBegin();
        for (int k = 0; k < frames; k++) { crUi.DebugDragMove(total / frames); yield return null; }
        if (holdBeforeRelease > 0f) yield return new WaitForSecondsRealtime(holdBeforeRelease);
        crUi.DebugDragEnd();
        yield return WaitSnap();
        int got = crUi.DebugSelected - startPos;
        L($"[drag] {name}: moved {total:F0} over {frames} frames, release target {crUi.LastReleaseTarget}, selected {startPos} -> {crUi.DebugSelected} ({got:+0;-0;0})");
        Check(got == expect, $"C/D {name} ({got:+0;-0;0}, expected {expect:+0;-0;0})");
    }

    IEnumerator WaitSnap() { float t = 0f; while (crUi.DebugSnapping && t < 2f) { yield return null; t += Time.unscaledDeltaTime; } yield return null; }

    static float Spread(List<float> v) { float mn = float.MaxValue, mx = float.MinValue; foreach (var x in v) { if (x < 0f) continue; mn = Mathf.Min(mn, x); mx = Mathf.Max(mx, x); } return mx - mn; }
}
#endif
