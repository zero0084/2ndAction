using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// シーンに焼き込まれた uGUI の文(SceneBuilder が作る見出し/ボタン/説明)を今の言語へ(2026-10-07)。
//  ・シーンを読んだ時と言語を切り替えた時に、全部の Text を見て、最初に見た文を元の文として覚えて訳す。
//  ・コードが後から書き換えた文(数値の表示など)は、書き換えた側が Loc.Auto を通す。ここでは「最後に訳して入れた文」と違えば
//    それを新しい元の文として扱う(訳せない文は手を付けない)。
//  ・長い訳ではみ出さないよう、訳した Text は元の大きさを上限に自動で縮む(bestFit、下限は元の60%)。
public class LocTextBinder : MonoBehaviour
{
    static LocTextBinder instance;
    class Entry { public string source, applied; public int baseSize; public bool baseBestFit; public int baseMin, baseMax; }
    static readonly Dictionary<Text, Entry> entries = new Dictionary<Text, Entry>();
    public static int Applied { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("[LocTextBinder]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        instance = go.AddComponent<LocTextBinder>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { entries.Clear(); if (instance != null) instance.pending = 2; };
        Loc.Changed += () => ApplyAll();
        instance.pending = 1;
    }

    int pending;
    void LateUpdate()
    {
        if (pending > 0 && --pending == 0) ApplyAll();
    }

    public static void ApplyAll()
    {
        int n = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            var id = t;
            if (!entries.TryGetValue(id, out var e))
            {
                e = new Entry { source = t.text, applied = null, baseSize = t.fontSize, baseBestFit = t.resizeTextForBestFit, baseMin = t.resizeTextMinSize, baseMax = t.resizeTextMaxSize };
                entries[id] = e;
            }
            else if (e.applied != null && t.text != e.applied) e.source = t.text; // コードが書き換えた
            string tr = Loc.Auto(e.source);
            if (tr != e.source || e.applied != null)
            {
                if (t.text != tr) t.text = tr;
                e.applied = tr;
                bool translated = !Loc.IsJapanese;
                if (t.GetComponentInParent<UguiScrollText>(true) != null) { n++; continue; } // 2026-10-08: スクロールする説明文は縮めない(全文を折り返して読む)
                t.resizeTextForBestFit = translated || e.baseBestFit;
                if (translated) { t.resizeTextMaxSize = e.baseSize; t.resizeTextMinSize = Mathf.Max(8, Mathf.RoundToInt(e.baseSize * 0.6f)); }
                else { t.resizeTextMaxSize = e.baseMax; t.resizeTextMinSize = e.baseMin; }
                n++;
            }
        }
        Applied = n;
    }
}
