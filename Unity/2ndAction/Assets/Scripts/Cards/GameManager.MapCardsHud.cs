using UnityEngine;

// BLOOD ARMOR の血の盾の HUD(2026-11-06)。HP の枠の上の行(数値/Shield の並び)の右端に「深紅のしずく ×N」。
//  ハート(赤い♥)とも Shield(文字の「Shield N」)とも形と色で見分けられるようにする。0 の時は出さない(枠も伸ばさない)。
//  HP の枠と同じ所に描くので、横/縦・シンプル表示・非表示モード(被弾の直後だけ HP を見せる)は HP の枠の決まりにそのまま従う
public partial class GameManager
{
    static Texture2D bloodDropTex;
    public static readonly Color BloodShieldColor = new Color(0.72f, 0.04f, 0.16f, 1f);
    static readonly Color BloodShieldText = new Color(1f, 0.76f, 0.79f, 1f);   // 2026-11-06(エステルのレビュー): 少し明るく
    static readonly Color BloodShieldRim = new Color(1f, 0.32f, 0.4f, 1f);     // 縁(暗いマップで沈まないように)

    // しずくの形(上が尖った丸)。手続きで1回だけ作る(素材を足さない)
    static Texture2D BloodDropTexture
    {
        get
        {
            if (bloodDropTex != null) return bloodDropTex;
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "BloodShieldDrop" };
            var px = new Color[n * n];
            float cx = n * 0.5f, cy = n * 0.36f, r = n * 0.32f, tipY = n * 0.98f;
            const float rim = 2.8f; // 縁の幅(絵の 48px で約 2.8px = 画面の 18〜20px で約 1px)
            // しずくの形の中にどれだけ入っているか(inset だけ内側へ縮めた形で)
            float Cover(float fx, float fy, float inset)
            {
                float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                float a = Mathf.Clamp01(r - inset + 0.8f - d);
                if (fy > cy && fy < tipY - inset)
                {
                    float half = r * (1f - (fy - cy) / (tipY - cy)) - inset; // 上へ行くほど細く(先が尖る)
                    a = Mathf.Max(a, Mathf.Clamp01(half + 0.8f - Mathf.Abs(fx - cx)));
                }
                return a;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float outer = Cover(fx, fy, 0f), inner = Cover(fx, fy, rim);
                    // 左上の光(暗い背景でもしずくの丸みが分かる)
                    float hl = Mathf.Clamp01(1f - Mathf.Sqrt((fx - (cx - r * 0.35f)) * (fx - (cx - r * 0.35f)) + (fy - (cy + r * 0.25f)) * (fy - (cy + r * 0.25f))) / (r * 0.35f));
                    Color body = Color.Lerp(BloodShieldColor, new Color(1f, 0.75f, 0.78f, 1f), hl * 0.8f);
                    Color c = Color.Lerp(BloodShieldRim, body, inner); // 外側の約1px は明るい深紅の縁
                    px[y * n + x] = new Color(c.r, c.g, c.b, outer);
                }
            t.SetPixels(px);
            t.Apply(false, true);
            bloodDropTex = t;
            return t;
        }
    }

    // 上の行の右端に描いて、使った幅を返す(0 = 描いていない)
    public static int BloodHudDraws; // 確認用(描いたフレーム数)
    float DrawBloodShieldBadge(Rect line, GUIStyle baseStyle)
    {
        int n = BloodShieldHearts;
        if (n <= 0) return 0f;
        BloodHudDraws++;
        var st = new GUIStyle(baseStyle) { alignment = TextAnchor.UpperRight };
        st.normal.textColor = BloodShieldText;
        string label = "×" + n;
        float tw = st.CalcSize(new GUIContent(label)).x;
        float icon = line.height;
        float w = icon + 2f + tw;
        Rect ir = new Rect(line.xMax - w, line.y, icon, icon);
        Color keep = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, keep.a);
        GUI.DrawTexture(new Rect(ir.x - 1f, ir.y - 1f, ir.width + 2f, ir.height + 2f), BloodDropTexture, ScaleMode.ScaleToFit); // 少し大きめ(縁の代わり)
        GUI.color = keep;
        LocGUI.Label(new Rect(line.xMax - tw - 1f, line.y, tw + 2f, line.height), label, st);
        return w + 6f;
    }
}
