using UnityEngine;

// 使い回すマテリアル(2026-10-08、メモリの漏れの修正)。
// 線/軌跡/判定の確認表示が、出すたびに new Material(...) を作っていて、物を消してもマテリアルは残り続けていた
// (長いランで数千個に増え、グラフィックのメモリも増え続ける)。色は頂点の色(startColor/endColor/Gradient)で付けているので、
// 1つのマテリアルを全員で共有しても見た目は変わらない。共有の物は個別に書き換えないこと(書き換えると全員に効く)
public static class SharedMaterials
{
    static Material spritesDefault;
    public static Material SpritesDefault
    {
        get
        {
            if (spritesDefault == null) spritesDefault = new Material(Shader.Find("Sprites/Default")) { name = "Shared Sprites/Default" };
            return spritesDefault;
        }
    }
}
