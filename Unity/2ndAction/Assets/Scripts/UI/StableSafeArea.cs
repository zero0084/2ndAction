using UnityEngine;

// 画面の安全領域(2026-10-10、HUD が時々 120px ほど下/左へずれる不具合)。
// Android では Screen.safeArea に「その時たまたま出ているシステムバー」(端をスワイプした時のステータスバー/ナビゲーションバー、
// 復帰の直後の一瞬など)の余白も入る。全画面のゲームではそのバーは HUD の上に重なって消えるだけなので、HUD の位置を動かす理由にならない。
// → Android では「切り欠き(ノッチ/パンチホール)」だけを避ける余白(Screen.cutouts)を使う。切り欠きの無い端末/PC/エディタは今までどおり。
//    同じ画面サイズ・向き・切り欠きなら、HUD は毎回同じ位置になる。
// 記録: 生の safeArea と使っている値が変わった時に、画面サイズ/向き/安全領域/理由を [SafeArea] で出す(実機で再発した時に追える)
public static class StableSafeArea
{
    static int frame = -1;
    static Rect cached;
    static Rect lastRaw = new Rect(-1, -1, -1, -1), lastStable = new Rect(-1, -1, -1, -1);
    static int lastW, lastH;
    public static string LastReason = "start";   // OrientationWatcher などが書く(復帰/回転/向きの指定)
    public static int Changes { get; private set; }

    // Screen.safeArea と同じ座標(左下が原点、画素)
    public static Rect Rect
    {
        get
        {
            if (frame == Time.frameCount) return cached;
            frame = Time.frameCount;
            cached = Compute();
            return cached;
        }
    }

    public static float Top => Screen.height - Rect.yMax;
    public static float Bottom => Rect.y;
    public static float Left => Rect.x;
    public static float Right => Screen.width - Rect.xMax;

    static Rect Compute()
    {
        Rect raw = Screen.safeArea;
        Rect stable = raw;
#if UNITY_ANDROID && !UNITY_EDITOR
        stable = FromCutouts();
#endif
        if (raw != lastRaw || stable != lastStable || Screen.width != lastW || Screen.height != lastH)
        {
            if (lastW > 0) Changes++;
            Note($"[SafeArea] screen {Screen.width}x{Screen.height} {Screen.orientation} fullScreen={Screen.fullScreen} focus={Application.isFocused} raw={Fmt(raw)} used={Fmt(stable)} cutouts={Screen.cutouts.Length} reason={LastReason}");
            lastRaw = raw; lastStable = stable; lastW = Screen.width; lastH = Screen.height;
        }
        return stable;
    }

    // 切り欠きが触れている端だけ、その分を避ける
    static Rect FromCutouts()
    {
        float W = Screen.width, H = Screen.height;
        float l = 0f, r = 0f, b = 0f, t = 0f;
        const float edge = 4f;
        foreach (var c in Screen.cutouts)
        {
            if (c.width <= 0f || c.height <= 0f) continue;
            if (c.xMin <= edge) l = Mathf.Max(l, c.xMax);
            if (c.xMax >= W - edge) r = Mathf.Max(r, W - c.xMin);
            if (c.yMin <= edge) b = Mathf.Max(b, c.yMax);
            if (c.yMax >= H - edge) t = Mathf.Max(t, H - c.yMin);
        }
        // 切り欠きが情報を返さない端末: 生の値のうち横向きの左右(切り欠き側)だけを使い、上下(システムバー)は使わない
        if (Screen.cutouts.Length == 0)
        {
            Rect raw = Screen.safeArea;
            bool landscape = W > H;
            if (landscape) { l = raw.x; r = W - raw.xMax; }
            else { t = H - raw.yMax; b = 0f; } // 縦: 上の切り欠きの分は生の値(下のナビゲーションバーは使わない)
        }
        return new Rect(l, b, Mathf.Max(1f, W - l - r), Mathf.Max(1f, H - b - t));
    }

    static string Fmt(Rect r) => $"({r.x:F0},{r.y:F0},{r.width:F0},{r.height:F0})";

    // 直近の記録(DEBUG の診断表示で見られる)。ログ(logcat)にも出す
    public static readonly System.Collections.Generic.List<string> Recent = new System.Collections.Generic.List<string>();
    public static void Note(string line)
    {
        string l = $"{System.DateTime.Now:HH:mm:ss} {line}";
        Debug.Log(l);
        Recent.Add(l);
        if (Recent.Count > 20) Recent.RemoveAt(0);
    }
}
