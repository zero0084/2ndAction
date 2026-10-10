using System.Collections.Generic;
using UnityEngine;

// 縦画面の配置(2026-10-08、依頼E-1): 横画面で「左 / 中央 / 右」の3列に並んでいる画面(デッキ編集)を、縦画面では
//   上: 右の列(デッキ)   下: 左の列(一覧) + 中央の列(詳細) を横に並べる
// に積み直す。各列の部品(上端中央に留めた物)を列ごとの入れ物へ移し、入れ物ごと縮めて置く(部品の中身や
// 押せる範囲の計算はそのまま、縮めた分も RectTransform のとおりに当たる)。横画面に戻ったら元の親/順番へ戻す
public class PortraitColumns : MonoBehaviour
{
    public float splitX = 250f;     // これより左/右が横の列
    public float topReserve = 110f; // 上の「戻る」の段
    public float MaxTopScale = 1.4f, MaxRowScale = 1.3f;
    public System.Func<RectTransform, bool> Skip;

    RectTransform root;
    readonly RectTransform[] cols = new RectTransform[3];
    readonly List<(RectTransform rt, int idx)> moved = new List<(RectTransform, int)>();
    bool applied, appliedPortrait;
    Vector2 appliedSize;

    public bool NeedsApply
    {
        get
        {
            if (root == null) root = transform as RectTransform;
            return !applied || appliedPortrait != PortraitRunView.IsPortraitScreen || root.rect.size != appliedSize;
        }
    }

    public void Apply()
    {
        if (root == null) root = transform as RectTransform;
        Restore();
        applied = true; appliedPortrait = PortraitRunView.IsPortraitScreen; appliedSize = root.rect.size;
        if (!appliedPortrait) return;

        var groups = new[] { new List<RectTransform>(), new List<RectTransform>(), new List<RectTransform>() };
        var bmin = new Vector2[3]; var bmax = new Vector2[3];
        for (int g = 0; g < 3; g++) { bmin[g] = new Vector2(float.MaxValue, float.MaxValue); bmax[g] = new Vector2(float.MinValue, float.MinValue); }
        int firstIdx = -1;
        var top = new Vector2(0.5f, 1f);
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i) as RectTransform;
            if (c == null || System.Array.IndexOf(cols, c) >= 0) continue;
            if (c.anchorMin != top || c.anchorMax != top) continue;
            if (Skip != null && Skip(c)) continue;
            float x = c.anchoredPosition.x;
            int g = x < -splitX ? 0 : x > splitX ? 2 : 1;
            groups[g].Add(c);
            moved.Add((c, i)); // 入れ物は Restore で一番後ろにあるので、この番号がそのまま元の位置
            if (firstIdx < 0) firstIdx = i;
            if (!c.gameObject.activeSelf) continue;
            Vector2 size = c.sizeDelta;
            float l = x - c.pivot.x * size.x, t = c.anchoredPosition.y + (1f - c.pivot.y) * size.y;
            bmin[g] = Vector2.Min(bmin[g], new Vector2(l, t - size.y));
            bmax[g] = Vector2.Max(bmax[g], new Vector2(l + size.x, t));
        }
        if (firstIdx < 0) return;

        for (int g = 0; g < 3; g++)
        {
            if (cols[g] == null)
            {
                var go = new GameObject("PortraitColumn" + g, typeof(RectTransform));
                go.transform.SetParent(root, false);
                cols[g] = (RectTransform)go.transform;
            }
            var cr = cols[g];
            cr.gameObject.SetActive(true);
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.pivot = top;
            cr.offsetMin = cr.offsetMax = Vector2.zero; cr.localScale = Vector3.one;
        }
        for (int g = 2; g >= 0; g--) cols[g].SetSiblingIndex(firstIdx); // 0,1,2 の順で、最初の部品があった所へ
        // 元の並び順のまま移す(重なりの前後を変えない)
        for (int g = 0; g < 3; g++)
            foreach (var c in groups[g]) c.SetParent(cols[g], false);

        float W = root.rect.width, H = root.rect.height, m = 20f, gap = 20f;
        Vector2 Size(int g) => groups[g].Count == 0 || bmax[g].x < bmin[g].x ? Vector2.zero : bmax[g] - bmin[g];
        Vector2 s0 = Size(0), s1 = Size(1), s2 = Size(2);
        float rowW = s0.x + s1.x + (s0.x > 0f && s1.x > 0f ? gap : 0f);
        // 2026-10-10(全体点検): 縦長の端末では余った高さがあっても1倍のままで、デッキのカード名や一覧が小さかった
        //  → 余裕があれば拡大(下の一覧 1.3倍、上のデッキ 1.4倍まで)。上が小さくなりすぎる(0.75倍未満)なら下を縮めて上へ回す
        float maxRowH = Mathf.Max(1f, Mathf.Max(s0.y, s1.y));
        float k2 = Mathf.Min(MaxRowScale, (W - 2f * m) / Mathf.Max(1f, rowW));
        float TopFit(float kRow) => Mathf.Min(MaxTopScale, (W - 2f * m) / Mathf.Max(1f, s2.x), (H - topReserve - gap - maxRowH * kRow - m) / Mathf.Max(1f, s2.y));
        float k1 = TopFit(k2);
        if (k1 < 0.75f && k2 > 0.5f)
        {
            float k2Room = (H - topReserve - gap - m - s2.y * Mathf.Min(0.75f, (W - 2f * m) / Mathf.Max(1f, s2.x))) / maxRowH;
            k2 = Mathf.Clamp(Mathf.Min(k2, k2Room), Mathf.Min(k2, 0.5f), k2);
            k1 = TopFit(k2);
        }
        // 上: 右の列(中央寄せ)
        Place(2, k1, new Vector2(-s2.x * k1 * 0.5f, -topReserve), bmin[2], bmax[2]);
        // 下: 左の列 + 中央の列
        float rowTop = -topReserve - s2.y * k1 - gap;
        float x0 = -rowW * k2 * 0.5f;
        Place(0, k2, new Vector2(x0, rowTop), bmin[0], bmax[0]);
        Place(1, k2, new Vector2(x0 + (s0.x > 0f ? (s0.x + gap) * k2 : 0f), rowTop), bmin[1], bmax[1]);
        Debug.Log($"[PortraitColumns] {name} {W:F0}x{H:F0} cols {groups[0].Count}/{groups[1].Count}/{groups[2].Count} scale top {k1:F2} row {k2:F2}");
    }

    // 列 g の左上(bmin.x, bmax.y)を target(親の上端中央が原点)へ、倍率 k で
    void Place(int g, float k, Vector2 target, Vector2 bmin, Vector2 bmax)
    {
        var cr = cols[g];
        if (cr == null) return;
        cr.localScale = new Vector3(k, k, 1f);
        cr.anchoredPosition = new Vector2(target.x - k * bmin.x, target.y - k * bmax.y);
    }

    public void Restore()
    {
        if (moved.Count == 0) { foreach (var c in cols) if (c != null) c.gameObject.SetActive(false); return; }
        foreach (var c in cols) if (c != null) c.SetAsLastSibling();
        moved.Sort((a, b) => a.idx.CompareTo(b.idx));
        foreach (var (rt, idx) in moved)
        {
            if (rt == null) continue;
            rt.SetParent(root, false);
            rt.SetSiblingIndex(Mathf.Min(idx, root.childCount - 1));
        }
        moved.Clear();
        foreach (var c in cols) if (c != null) c.gameObject.SetActive(false);
    }
}
