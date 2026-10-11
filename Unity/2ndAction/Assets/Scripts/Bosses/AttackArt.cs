using System.Collections.Generic;
using UnityEngine;

// ボスの攻撃の本番の絵(2026-10-11、実機: 攻撃の絵が無く色付きの四角だけが見えていた)。
// Assets/Resources/Effects/Atk/<set>_<n>.png(ChatGPT 生成、4コマ。同じ組のコマは同じ大きさ、中心ピボット、PPU 512)。
//  ・MagicPillar … 縦の魔炎の柱(白基調 → 呼び出し側の色で紫/金/黄などに)
//  ・Beam        … 横に流れる光の帯・ブレス(白基調)。SkyBossFx.Beam() もこれを返す
//  ・Burst       … 地面からの噴出(土煙+岩、色付き)。叩きつけ/噴出の攻撃
//  ・RockPillar  … 地面から突き出す岩の柱(色付き)
// 絵が無ければ呼び出し側の従来の絵(手続きの形)のまま。
// 絵の大きさは 1×1 ではない(縦横比がある)ので、「1×1 の絵を当たり判定の大きさへ引き伸ばす」所は Unit() で割ること。
public enum AtkArtSet { MagicPillar, Beam, Burst, RockPillar }

public static class AttackArt
{
    static readonly string[] Names = { "magicpillar", "beam", "burst", "rockpillar" };
    static readonly Sprite[][] cache = new Sprite[Names.Length][];
    static readonly bool[] loaded = new bool[Names.Length];
    static readonly Dictionary<Sprite, AtkArtSet> owner = new Dictionary<Sprite, AtkArtSet>();

    public static Sprite[] Frames(AtkArtSet s)
    {
        int i = (int)s;
        if (!loaded[i])
        {
            loaded[i] = true;
            var list = new List<Sprite>();
            for (int f = 0; f < 12; f++)
            {
                var sp = Resources.Load<Sprite>("Effects/Atk/" + Names[i] + "_" + f);
                if (sp == null) break;
                list.Add(sp);
                owner[sp] = s;
            }
            cache[i] = list.Count > 0 ? list.ToArray() : null;
        }
        return cache[i];
    }

    public static bool Has(AtkArtSet s) => Frames(s) != null;
    public static Sprite First(AtkArtSet s, Sprite fallback) { var f = Frames(s); return f != null ? f[0] : fallback; }
    // 色の付いた絵(着色しない: 呼び出し側の色は白+透明度だけにする)
    public static bool Natural(AtkArtSet s) => s == AtkArtSet.Burst || s == AtkArtSet.RockPillar;

    // この絵が攻撃の絵の組のコマか
    public static bool SetOf(Sprite sp, out AtkArtSet s)
    {
        s = default;
        if (sp == null) return false;
        if (owner.Count == 0) WarmAll();
        return owner.TryGetValue(sp, out s);
    }

    // 1×1 を前提に引き伸ばしている所で使う(手続きの絵は 1×1、攻撃の絵は縦横比がある)
    public static Vector2 Unit(Sprite sp)
    {
        if (sp == null) return Vector2.one;
        Vector2 b = sp.bounds.size;
        return new Vector2(Mathf.Max(0.01f, b.x), Mathf.Max(0.01f, b.y));
    }

    // マルチ: JOIN がミラーの絵を名前で探せるよう、全コマを読み込んでおく
    public static void WarmAll() { for (int i = 0; i < Names.Length; i++) Frames((AtkArtSet)i); }

    // sr の絵が攻撃の絵ならコマ送りを付ける(付いていれば最初から)。once=true は duration で1回だけ再生
    public static AtkArtAnim Animate(SpriteRenderer sr, bool once = false, float duration = 0.5f, float fps = 12f)
    {
        if (sr == null || !SetOf(sr.sprite, out AtkArtSet s)) return null;
        var a = sr.GetComponent<AtkArtAnim>();
        if (a == null) a = sr.gameObject.AddComponent<AtkArtAnim>();
        a.Play(sr, Frames(s), once, duration, fps);
        return a;
    }
}

// 攻撃の絵のコマ送り(同じ SpriteRenderer の絵を差し替えるだけ。大きさ・色・当たり判定は呼び出し側のまま)
public class AtkArtAnim : MonoBehaviour
{
    SpriteRenderer sr;
    Sprite[] frames;
    bool once;
    float fps, t0;

    public void Play(SpriteRenderer target, Sprite[] f, bool playOnce, float duration, float framesPerSecond)
    {
        sr = target; frames = f; once = playOnce;
        fps = playOnce ? f.Length / Mathf.Max(0.05f, duration) : framesPerSecond;
        t0 = Time.time + (playOnce ? 0f : ((gameObject.name.GetHashCode() ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this)) & 7) * 0.04f); // 同時に出る柱どうしのコマをずらす(ゲームの乱数は使わない)
        if (sr != null && frames != null && frames.Length > 0) sr.sprite = frames[0];
    }

    void LateUpdate()
    {
        if (sr == null || frames == null || frames.Length == 0 || !sr.enabled) return;
        // 呼び出し側が別の絵(予告の四角など)に変えたら止める
        if (System.Array.IndexOf(frames, sr.sprite) < 0) { frames = null; return; }
        int n = frames.Length;
        int k = Mathf.FloorToInt(Mathf.Max(0f, Time.time - t0) * fps);
        sr.sprite = frames[once ? Mathf.Min(n - 1, k) : k % n];
    }
}
