using System.Collections.Generic;
using UnityEngine;

// ラストダンジョンのエンディング(2026-09-30): 地形以外の「乗れる面」と「通り抜けられない壁」の登録簿。
// このゲームのプレイヤーは物理エンジンで地面に立つのではなく、TerrainManagerの高さ(地面/上ルートの面)を
// 毎フレーム問い合わせて着地/落下を決めている。エンドロールの巨大文字やYES/NOの石を「ゲーム世界に存在する物」に
// するため、ここへ登録した物の上面を上ルートの面と同じ扱い(上から落ちてきた時だけ乗れる)にし、
// 壁として登録した物はプレイヤーの横移動を止める(ダメージは無い)。
//  ・登録した物のCollider2D(BoxCollider2D)はプレイヤーの攻撃判定を受けるためのもの(当たりの形=見た目の文字)。
//  ・位置は毎回Transformから読むので、Floating Originの原点移動や、揺れ/崩れる動きにもそのまま追従する。
//  ・何も登録されていない時(通常のラン)は一切コストが掛からない。
public interface IWorldSolid
{
    bool SolidActive { get; }
    // ワールド座標の矩形(xMin, xMax, yBottom, yTop)
    Rect SolidRect { get; }
    bool IsPlatform { get; }   // 上面に乗れる
    bool IsWall { get; }       // 横からぶつかると止まる
}

public static class WorldPlatforms
{
    static readonly List<IWorldSolid> items = new List<IWorldSolid>();
    public static int Count => items.Count;
    public static bool Any => items.Count > 0;

    public static void Register(IWorldSolid s) { if (s != null && !items.Contains(s)) items.Add(s); }
    public static void Unregister(IWorldSolid s) { items.Remove(s); }
    public static void Clear() { items.Clear(); }

    // xの真上にある乗れる面の高さ(複数あれば、yRefより下で一番高い面。yRef=NaNなら一番高い面)。
    public static float? TopAt(float x, float yRef = float.NaN)
    {
        float? best = null;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var s = items[i];
            if (s == null || (s is Object o && o == null)) { items.RemoveAt(i); continue; }
            if (!s.SolidActive || !s.IsPlatform) continue;
            Rect r = s.SolidRect;
            if (x < r.xMin || x > r.xMax) continue;
            if (!float.IsNaN(yRef) && r.yMax > yRef + 0.25f) continue; // プレイヤーの足元より上の面(頭上)は候補にしない
            if (!best.HasValue || r.yMax > best.Value) best = r.yMax;
        }
        return best;
    }

    // プレイヤーの横移動を壁で止める。footY〜headYの高さで重なる壁の手前(左右どちら向きでも)で止める。
    public static float ClampMove(float prevX, float newX, float footY, float headY, float halfWidth)
    {
        if (items.Count == 0 || Mathf.Approximately(prevX, newX)) return newX;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var s = items[i];
            if (s == null || (s is Object o && o == null)) { items.RemoveAt(i); continue; }
            if (!s.SolidActive || !s.IsWall) continue;
            Rect r = s.SolidRect;
            if (footY >= r.yMax - 0.05f || headY <= r.yMin) continue; // 上を越えている/下をくぐっている
            if (newX > prevX)
            {
                float face = r.xMin - halfWidth;
                if (prevX <= face + 0.02f && newX > face) newX = face;
            }
            else
            {
                float face = r.xMax + halfWidth;
                if (prevX >= face - 0.02f && newX < face) newX = face;
            }
        }
        return newX;
    }

    // 今この位置でどれかの壁に接しているか(テスト/演出用)
    public static IWorldSolid WallTouching(float x, float footY, float headY, float halfWidth, float tolerance = 0.08f)
    {
        foreach (var s in items)
        {
            if (s == null || !s.SolidActive || !s.IsWall) continue;
            Rect r = s.SolidRect;
            if (footY >= r.yMax - 0.05f || headY <= r.yMin) continue;
            if (Mathf.Abs(x + halfWidth - r.xMin) <= tolerance || Mathf.Abs(x - halfWidth - r.xMax) <= tolerance) return s;
        }
        return null;
    }
}
