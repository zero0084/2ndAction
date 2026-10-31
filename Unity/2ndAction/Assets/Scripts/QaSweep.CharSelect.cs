#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// キャラセレクトの立ち絵(2026-10-05 描き直し)の確認: 12人を順に選んで撮影。 -qaCharSelect <dir>
// (画面比率は起動引数 -screen-width/-screen-height で変えて複数回撮る)
public partial class QaSweep
{
    IEnumerator CharSelectMode()
    {
        float w = 0f;
        while ((GameManager.Instance == null || GameManager.Instance.HasStarted) && w < 15f) { yield return null; w += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        UnlockRules.DevUnlockAll = true; // 2026-10-28: まだ解放していない人も撮る
        yield return new WaitForSecondsRealtime(1.5f);
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = FindFirstObjectByType<CharacterSelectUI>();
        Check(ui != null && ui.isActiveAndEnabled, "character select opened");
        if (ui == null) yield break;
        var sel = typeof(CharacterSelectUI).GetMethod("SelectIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        var all = CharacterDatabase.AllCharacters;
        for (int i = 0; i < all.Count; i++)
        {
            ui.DebugSelectAndCenter(i); // 2026-10-22: カルーセルの位置も合わせる(以前は選ぶだけで、一覧は開いた時の位置のまま)
            yield return new WaitForSecondsRealtime(1.0f);
            var img = ui.mainVisualImage;
            string sprite = img != null && img.sprite != null ? $"{img.sprite.texture.name} {img.sprite.texture.width}x{img.sprite.texture.height}" : "none";
            L($"[sel] {i} {all[i].characterId}: {sprite}");
            Check(img != null && img.sprite != null && img.sprite.texture.width == 700 && img.sprite.texture.height == 1100, $"{all[i].characterId}: the new 700x1100 portrait is shown (not stretched to a power of two)");
            // 2026-10-31: 表示領域(立ち絵の枠)が情報パネル/カルーセルに重ならない・画面の中、キャラごとの倍率
            if (ui.VisualClip != null)
            {
                Rect clip = ScreenRectOf(ui.VisualClip), info = ui.flavorText != null ? ScreenRectOf(ui.flavorText.rectTransform.parent as RectTransform) : default;
                Rect car = ScreenRectOf((RectTransform)ui.carouselScroll.transform);
                Rect scr = new Rect(0, 0, Screen.width, Screen.height);
                bool inside = clip.xMin >= -1 && clip.yMin >= -1 && clip.xMax <= scr.xMax + 1 && clip.yMax <= scr.yMax + 1;
                Check(inside && !clip.Overlaps(info) && !clip.Overlaps(car), $"{all[i].characterId}: the portrait area is on screen and does not overlap the info panel / carousel (clip {clip}, info {info}, carousel {car})");
                L($"[fit] {all[i].characterId} scale {CharacterSelectUI.LastFitScale:F2} image {img.rectTransform.rect.size} clip {clip.size}");
            }
            Shot($"charsel_{Screen.width}x{Screen.height}_{i:00}_{all[i].characterId}");
            yield return new WaitForSecondsRealtime(0.3f);
        }
        // 切り替えの途中(消えかけ/出かけ): 3人ぶん撮る
        foreach (int i in new[] { 4, 8, 12 })
        {
            if (i >= all.Count) continue;
            ui.DebugSelectAndCenter(i - 1); yield return new WaitForSecondsRealtime(0.8f);
            var snap = typeof(CharacterSelectUI).GetMethod("BeginSnap", BindingFlags.Instance | BindingFlags.NonPublic);
            snap.Invoke(ui, new object[] { i });
            yield return new WaitForSecondsRealtime(0.06f); Shot($"charsel_{Screen.width}x{Screen.height}_switch_{i:00}_a");
            yield return new WaitForSecondsRealtime(0.08f); Shot($"charsel_{Screen.width}x{Screen.height}_switch_{i:00}_b");
            yield return new WaitForSecondsRealtime(0.6f);
        }
        // 短い切り替えの動画用の連番(-csVideo 1)。30fps の時間で進める(撮影で遅れても動きは同じ)
        if (Arg("-csVideo", "0") == "1")
        {
            var snap = typeof(CharacterSelectUI).GetMethod("BeginSnap", BindingFlags.Instance | BindingFlags.NonPublic);
            ui.DebugSelectAndCenter(0); yield return new WaitForSecondsRealtime(0.8f);
            Time.captureDeltaTime = 1f / 30f;
            int frame = 0;
            for (int i = 1; i < all.Count; i++)
            {
                snap.Invoke(ui, new object[] { i });
                for (int f = 0; f < 21; f++) { yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"vid_{frame++:0000}.png")); }
            }
            Time.captureDeltaTime = 0f;
        }
    }
}
#endif
