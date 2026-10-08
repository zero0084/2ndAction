using UnityEngine;

// ゲームの判定に使う「画面の幅」(2026-10-08、依頼E)。出現位置/画面端/ボスの間合い/ワールドの範囲などはここを使う。
// 見た目のカメラ(縦の横から見る表示で狭くする/斜め上から見る透視カメラ)に関係なく、横画面と同じ値を返す
// = 見せ方を変えても、敵の出現・行動・攻撃の始まる距離は変わらない。
public static class GameView
{
    public static float HalfWidth(Camera cam)
    {
        var cf = CameraFollow.Instance;
        if (cf != null) return cf.LogicalHalfWidth;
        return cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect : 20.8f;
    }
    public static float CenterX(Camera cam)
    {
        var cf = CameraFollow.Instance;
        if (cf != null && cf.HasLogical) return cf.LogicalCenterX;
        return cam != null ? cam.transform.position.x : 0f;
    }
    public static float Right(Camera cam) => CenterX(cam) + HalfWidth(cam);
    public static float Left(Camera cam) => CenterX(cam) - HalfWidth(cam);
}
