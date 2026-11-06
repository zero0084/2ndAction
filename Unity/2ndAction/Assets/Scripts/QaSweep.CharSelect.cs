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
        CharacterSelectUI.PaperFade = Arg("-csPaperFade", "1") != "0"; // 2026-11-01: 0 = 変更前(紙の四角い縁)の見た目で撮る
        string csLang = Arg("-csLang", ""); // 2026-11-01: Google Play の英語版の撮影
        if (csLang != "" && Loc.Current != csLang) Loc.Set(csLang, false);
        if (Arg("-scClean", "0") == "1") StoreCapture.Clean = true; // ストア用の撮影: 開発用の表示(build の番号/DEV BUILD/TEST FILE)を出さない
        yield return new WaitForSecondsRealtime(1.5f);
        gm.OpenCharacterSelect();
        yield return new WaitForSecondsRealtime(1.5f);
        var ui = FindFirstObjectByType<CharacterSelectUI>();
        Check(ui != null && ui.isActiveAndEnabled, "character select opened");
        if (ui == null) yield break;
        var sel = typeof(CharacterSelectUI).GetMethod("SelectIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        var all = CharacterDatabase.AllCharacters;
        // 2026-11-07(キャラ文言の30言語の確認): -csLangs ja,en,... = 1回の起動で言語を順に切り替えて14人ずつ撮る(切り替えの途中の撮影は省く)
        string csLangs = Arg("-csLangs", "");
        string[] langList = csLangs == "" ? new[] { "" } : csLangs.Split(',');
        foreach (string lang in langList)
        {
        string tag = "";
        if (lang != "")
        {
            Loc.Set(lang, false);
            yield return new WaitForSecondsRealtime(1.2f);
            tag = lang + "_";
            L($"[lang] {lang}");
        }
        for (int i = 0; i < all.Count; i++)
        {
            ui.DebugSelectAndCenter(i); // 2026-10-22: カルーセルの位置も合わせる(以前は選ぶだけで、一覧は開いた時の位置のまま)
            yield return new WaitForSecondsRealtime(1.0f);
            var img = ui.mainVisualImage;
            string sprite = img != null && img.sprite != null ? $"{img.sprite.texture.name} {img.sprite.texture.width}x{img.sprite.texture.height}" : "none";
            L($"[sel] {i} {all[i].characterId}: {sprite}");
            if (CharacterSelectUI.PaperFade && img != null)
                Check(img.material != null && img.material.shader != null && img.material.shader.name == "UI/PaperFade", $"{all[i].characterId}: the portrait fades its paper edge into the background (material {img.material?.shader?.name})");
            Check(img != null && img.sprite != null && img.sprite.texture == all[i].mainVisual && img.preserveAspect && img.sprite.texture.width * 3 == img.sprite.texture.height * 2, $"{all[i].characterId}: the selected 2:3 fantasy portrait is shown with its aspect preserved");
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
            // 2026-11-01: 立ち絵と選んだカードが画面のどこに描かれたか(画面の左下が原点)。Tools/charart/costume_check.py が
            //  この範囲を撮影から切り出し、元の衣装(Steam)/布を増やした衣装(Android)のどちらが出ているかを確かめる
            if (img != null && img.sprite != null) L($"[drawn] {all[i].characterId} main {DrawnRect(img)}");
            // 2026-11-03: カードは共通の規格(枠 + 立ち絵の体の部分 + 名前の板)。焼き込みのカードの絵は使わない
            var face = ui.DebugFace(i);
            var art = face != null ? face.Find("Art")?.GetComponent<UnityEngine.UI.RawImage>() : null;
            Check(art != null && art.texture == all[i].mainVisual, $"{all[i].characterId}: the card shows this character's portrait in the common card frame");
            if (art != null) L($"[card] {all[i].characterId} face {face.rect.size} uv {art.uvRect}");
            LogCharTexts(ui, i, all[i], lang); // 2026-11-06 キャラ文言の更新: 一行/型ラベルの欠け・紹介文の折り返し
            Shot($"charsel_{tag}{Screen.width}x{Screen.height}_{i:00}_{all[i].characterId}");
            yield return new WaitForSecondsRealtime(0.3f);
        }
        }
        if (csLangs != "") yield break;
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

    // 2026-11-06(キャラ文言の更新): 一行/型ラベル(情報パネル・カードの小さな字)が1行に収まるか(OVER = はみ出して欠ける)、
    //  紹介文が実際に何行で描かれたか(| で区切った各行)と、枠を超えてスクロールになるか(SCROLL)
    void LogCharTexts(CharacterSelectUI ui, int i, CharacterDefinition def, string lang = "")
    {
        Canvas.ForceUpdateCanvases();
        string id = (lang != "" ? lang + " " : "") + def.characterId;
        void One(string what, UnityEngine.UI.Text t)
        {
            if (t == null) return;
            float pw = t.preferredWidth, w = t.rectTransform.rect.width;
            L($"[txt] {id} {what} w {pw:F0}/{w:F0} fs {t.fontSize}{(pw > w + 0.5f ? " OVER" : "")} \"{t.text}\"");
        }
        One("name", ui.titleText); // 2026-11-07: 名前(フィリピン語のお嬢様が縦画面で2行目が切れていた)
        One("subtitle", ui.subtitleText);
        One("role", ui.roleBadgeText);
        var face = ui.DebugFace(i);
        One("cardrole", face != null ? face.Find("Role")?.GetComponent<UnityEngine.UI.Text>() : null);
        var ft = ui.flavorText;
        if (ft == null) return;
        var gen = ft.cachedTextGenerator;
        string s = ft.text ?? "";
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < gen.lineCount; k++)
        {
            int a = Mathf.Clamp(gen.lines[k].startCharIdx, 0, s.Length);
            int b = k + 1 < gen.lineCount ? Mathf.Clamp(gen.lines[k + 1].startCharIdx, a, s.Length) : s.Length;
            sb.Append(" | ").Append(s.Substring(a, b - a).Replace("\n", ""));
        }
        var vp = ft.GetComponentInParent<UguiScrollText>();
        float vh = vp != null && vp.Viewport != null ? vp.Viewport.rect.height : ft.rectTransform.rect.height;
        int expect = (Loc.Plain(def.flavorText) ?? "").Split('\n').Length; // 訳の文の行数(右から左の言語は RtlTextWrap が改行を足すので、表示の文ではなく訳で数える)
        L($"[txt] {id} flavor lines {gen.lineCount} (source {expect}){(gen.lineCount > expect ? " WRAP" : "")} fs {ft.fontSize}{(ui.FlavorJoined ? " JOIN" : "")} h {ft.preferredHeight:F0}/{vh:F0} w {ft.rectTransform.rect.width:F0}{(ft.preferredHeight > vh + 1f ? " SCROLL" : "")}{sb}");
    }

    // 縦横比を保って描かれた範囲(画面の座標、x y w h)
    static string DrawnRect(UnityEngine.UI.Image img)
    {
        Rect r = ScreenRectOf(img.rectTransform);
        float sa = img.sprite.rect.width / Mathf.Max(1f, img.sprite.rect.height);
        float w = r.width, h = r.height;
        if (img.preserveAspect) { if (w / Mathf.Max(1f, h) > sa) w = h * sa; else h = w / sa; }
        return $"{r.center.x - w * 0.5f:F1} {r.center.y - h * 0.5f:F1} {w:F1} {h:F1}";
    }
}
#endif
