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
            sel.Invoke(ui, new object[] { i });
            yield return new WaitForSecondsRealtime(1.0f);
            var img = ui.mainVisualImage;
            string sprite = img != null && img.sprite != null ? $"{img.sprite.texture.name} {img.sprite.texture.width}x{img.sprite.texture.height}" : "none";
            L($"[sel] {i} {all[i].characterId}: {sprite}");
            Check(img != null && img.sprite != null && img.sprite.texture.width == 700 && img.sprite.texture.height == 1100, $"{all[i].characterId}: the new 700x1100 portrait is shown (not stretched to a power of two)");
            Shot($"charsel_{Screen.width}x{Screen.height}_{i:00}_{all[i].characterId}");
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }
}
#endif
