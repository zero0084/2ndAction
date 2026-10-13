#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// エンドロールの確認(2026-10-13 第3弾、名前の追加): -qaCredits <dir> [-crViews 0,1,2]  画面の大きさは起動の大きさ(縦/横)
//  三姉妹は済んだ扱いでエンドロールから始め(開発用の DebugBeginCredits)、区間ごとに撮影。石板は前へ攻撃して壊す。
//  見切れ(名前/説明が画面の外)と、進行(問いかけまで着く・止まらない)を確かめる
public partial class QaSweep
{
    IEnumerator CreditsMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        UnlockRules.DevUnlockAll = true;
        SaveStore.SetInt(SaveKeys.DevFinalDungeonAlwaysOpen, 1);
        TutorialProgress.MarkOffered(); TutorialProgress.MarkEscapeGuideShown();
        bool portrait = Screen.height > Screen.width;
        string[] views = portrait ? Arg("-crViews", "0,1,2").Split(',') : new[] { "-1" };
        foreach (var vs in views)
        {
            int view = int.Parse(vs);
            if (view >= 0) PortraitRunView.Set(view);
            string tag = $"{Screen.width}x{Screen.height}{(view >= 0 ? "_v" + view : "")}";
            L($"== credits {tag} ==");
            yield return BeginRun("swordsman", "last_corridor");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            WarpTo(98900f);
            float w = 0f;
            var flow = LastDungeonFlow.Instance;
            while ((flow == null || gm.MaxDistance < 99030f) && w < 30f) { yield return null; w += Time.deltaTime; flow = LastDungeonFlow.Instance; }
            if (flow == null) { Check(false, "credits: flow running"); yield return EndRun(); continue; }
            flow.DebugBeginCredits();
            yield return new WaitForSeconds(0.5f);
            var cr = flow.Credits;
            Check(cr != null, $"{tag}: the credits road is built");
            if (cr == null) { yield return EndRun(); continue; }
            L($"[{tag}] letters {cr.Letters.Count} plaques {cr.Plaques.Count} subtitles {cr.Subtitles.Count}");
            foreach (var pl in cr.Plaques) L($"[{tag}] plaque '{pl.Text}' width {pl.Width:F1}m text {pl.Label.Width:F1}m");
            Check(cr.Plaques.Count == 3, $"{tag}: three Japanese names are on plaques ({cr.Plaques.Count})");
            Check(cr.Subtitles.Count >= 4, $"{tag}: role captions are shown ({cr.Subtitles.Count})");
            bool allFit = true;
            foreach (var pl in cr.Plaques) if (pl.Label.Width > pl.Width - 0.2f || pl.Label.Width < 0.5f) allFit = false;
            Check(allFit, $"{tag}: each Japanese name fits on its plaque (and is not empty)");
            float lastShot = -9f, t0 = Time.time, lastFlick = 0f; string lastSection = "";
            int offscreen = 0, checkedFrames = 0;
            var cam = Camera.main;
            while (flow.Current == LastDungeonFlow.State.Credits && Time.time - t0 < 150f)
            {
                var pcx = PlayerController.Instance;
                string sec = cr.SectionAt(pcx.transform.position.x);
                if ((sec != lastSection && sec != "") || Time.time - lastShot > 3.5f) { lastShot = Time.time; if (sec != "") lastSection = sec; Shot($"cr_{tag}_{(sec == "" ? "road" : sec.Replace(" ", "_").Replace("/", "_"))}"); }
                // 名前/説明の文字が画面に入っている時、左右/上下にはみ出していないか(プレイヤーの近くの物だけ)
                foreach (var pl in cr.Plaques) offscreen += TextOffscreen(pl.Label, pcx, ref checkedFrames);
                foreach (var st in cr.Subtitles) offscreen += TextOffscreen(st, pcx, ref checkedFrames);
                if (Time.time - lastFlick > 0.3f && WorldPlatforms.WallTouching(pcx.transform.position.x, pcx.transform.position.y, pcx.transform.position.y + 1.5f, 0.35f, 0.15f) != null)
                { lastFlick = Time.time; StartCoroutine(Flick(PlayerController.FlickDirection.Forward)); }
                yield return new WaitForEndOfFrame(); // 上下2段はカメラを描画の直前に段の位置へ置く: 描いた後の位置で測る(次の周の計測)
            }
            L($"[{tag}] credits -> {flow.Current} in {Time.time - t0:F0}s, text cut-off frames {offscreen}/{checkedFrames}");
            Check(flow.Current >= LastDungeonFlow.State.Choice, $"{tag}: the credits reach ONE MORE MILE? ({flow.Current}, {Time.time - t0:F0}s)");
            Check(offscreen == 0, $"{tag}: the names/captions are not cut off when the player passes them ({offscreen} frames)");
            yield return new WaitForSeconds(2.5f);
            Shot($"cr_{tag}_choice");
            yield return EndRun();
        }
    }

    // プレイヤーの前後 6m にある文字が、どのカメラにも全体が映っているか(半分以上が画面の外 = 見切れ)
    int TextOffscreen(CreditText t, PlayerController pcx, ref int n)
    {
        if (t == null) return 0;
        float dx = t.transform.position.x - pcx.transform.position.x;
        if (dx < -2f || dx > 6f) return 0;
        n++;
        float hw = t.Width * 0.5f, hh = t.CapHeight * 0.6f;
        Vector3 c = t.transform.position;
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null || !cam.enabled) continue;
            Vector3 a = cam.WorldToViewportPoint(c + new Vector3(-hw, -hh, 0f)), b = cam.WorldToViewportPoint(c + new Vector3(hw, hh, 0f));
            if (a.z > 0f && Mathf.Min(a.x, b.x) >= -0.02f && Mathf.Max(a.x, b.x) <= 1.02f && Mathf.Min(a.y, b.y) >= -0.02f && Mathf.Max(a.y, b.y) <= 1.02f) return 0;
        }
        return 1;
    }
}
#endif
