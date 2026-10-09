// 標準のスプライトの表示 + 縦画面「斜め上から見る」のジグザグの道(2026-10-09、依頼G-3)。
// Unity の Sprites/Default と同じ描き方(UnitySprites.cginc)。_ZigOn が 0 の間は Sprites/Default と同じ結果。
Shader "OneMoreMile/SpriteZig"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex ZigSpriteVert
            #pragma fragment SpriteFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "../../Shaders/Zig.cginc"

            v2f ZigSpriteVert(appdata_t IN)
            {
                IN.vertex = ZigObject(IN.vertex);
                return SpriteVert(IN);
            }
            ENDCG
        }
    }
}
