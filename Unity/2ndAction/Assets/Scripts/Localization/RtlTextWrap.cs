using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 右から左の言語(アラビア語/ペルシャ語)の uGUI の文字の折り返し(2026-10-29)。
// 表示用に並べ替えた文を uGUI が左から折り返すと、1行目に文の最後の部分が来て、行の順番が入れ替わって読めなかった。
// 折り返す設定の Text の文が、Loc が並べ替えた文(元の文が分かる物)で幅に入らない時だけ、元の順で幅に合わせて行に分けてから
// 1行ずつ並べ替えた文に置き換える(Loc.RtlWrap)。右から左の言語の時だけ動く。それ以外の言語・入る文は何もしない。
// 置き換えた Text は毎フレーム、文と幅が変わっていないかだけを見る(画面のコードが同じ文を入れ直した時・回転で幅が変わった時に直し直す)。
// 新しい Text は、Loc が並べ替えた文を作った時と10フレームごとに探し、見つけた Text には文が変わった時の知らせを付ける(変わったフレームのうちに直す)。
public class RtlTextWrap : MonoBehaviour
{
    static RtlTextWrap instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("RtlTextWrap");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RtlTextWrap>();
    }

    class State { public string original, shown; public float width; public int fontSize; }
    readonly Dictionary<Text, State> states = new Dictionary<Text, State>();
    readonly List<Text> tracked = new List<Text>();
    readonly List<Text> dead = new List<Text>();
    readonly HashSet<Text> seen = new HashSet<Text>();     // 変わった時の知らせを付けた Text
    readonly HashSet<Text> pending = new HashSet<Text>();  // 文/書式が変わった Text(このフレームで調べる)
    readonly List<Text> work = new List<Text>();
    public static int FixedCount { get; private set; } // 確認用: 置き換えた回数

    void LateUpdate()
    {
        if (!Loc.IsRtl)
        {
            if (states.Count > 0) { states.Clear(); tracked.Clear(); }
            pending.Clear();
            return;
        }
        if (Loc.RtlDirty || Time.frameCount % 10 == 0)
        {
            Loc.RtlDirty = false;
            foreach (var t in FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (seen.Add(t)) { var tt = t; t.RegisterDirtyVerticesCallback(() => pending.Add(tt)); }
                if (!states.ContainsKey(t)) Check(t, null);
            }
        }
        if (pending.Count > 0)
        {
            work.Clear(); work.AddRange(pending); pending.Clear();
            foreach (var t in work) if (t != null && t.isActiveAndEnabled) Check(t, states.TryGetValue(t, out var s0) ? s0 : null);
        }
        dead.Clear();
        for (int i = 0; i < tracked.Count; i++)
        {
            var t = tracked[i];
            if (t == null) { dead.Add(t); continue; }
            if (!t.isActiveAndEnabled) continue;
            Check(t, states[t]);
        }
        foreach (var t in dead) { tracked.Remove(t); states.Remove(t); }
        if (seen.Count > 2000) seen.RemoveWhere(x => x == null);
    }

    void Check(Text t, State st)
    {
        if (t.horizontalOverflow != HorizontalWrapMode.Wrap) return;
        string cur = t.text;
        if (string.IsNullOrEmpty(cur)) return;
        float w = t.rectTransform.rect.width;
        int fs = t.resizeTextForBestFit && t.cachedTextGenerator.fontSizeUsedForBestFit > 0 ? t.cachedTextGenerator.fontSizeUsedForBestFit : t.fontSize;
        if (st != null && cur == st.shown && Mathf.Abs(w - st.width) < 0.5f && fs == st.fontSize) return; // 変わっていない
        string original = st != null && cur == st.shown ? st.original : cur; // 幅だけ変わった時は元の表示から作り直す
        var settings = t.GetGenerationSettings(Vector2.zero);
        settings.horizontalOverflow = HorizontalWrapMode.Overflow;
        settings.resizeTextForBestFit = false;
        settings.fontSize = fs;
        var gen = t.cachedTextGeneratorForLayout;
        float ppu = Mathf.Max(0.0001f, t.pixelsPerUnit);
        string res = Loc.RtlWrap(original, w - 1f, x => gen.GetPreferredWidth(x, settings) / ppu);
        if (st == null)
        {
            if (res == original) return; // 入る文・元の文が分からない文は見張らない
            st = new State();
            states[t] = st; tracked.Add(t);
        }
        st.original = original; st.shown = res; st.width = w; st.fontSize = fs;
        if (res != cur) { t.text = res; FixedCount++; }
    }
}
