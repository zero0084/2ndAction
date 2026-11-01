using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 女性7人の衣装を、作る版で切り替える(2026-11-01 幻想画の立ち絵/選択カード、エステルと制作)。
//  Steam 向け Windows 版: 元の衣装(Assets/Art/UI/Characters/<id>_main.png / <id>_card.png そのもの)
//  Android 版: 布を増やした衣装(Assets/Editor/AndroidCharacterArt/<id>_<main|card>.png.bytes)を、取り込みの時に同じ絵へ書き込む
//  ・キャラのデータ・並びのカード・シーンに焼き込んだ参照は、絵の GUID が1つのまま(版ごとに参照を書き換えない)
//  ・取り込みの結果は機種ごとに別に残る(Unity は機種ごとに取り込み直す/控えを使う)ので、Windows → Android → Windows と切り替えても前の衣装は残らない
//    Android の時だけ .bytes を「元の絵」として登録する → .bytes を差し替えた時も取り込み直される
//  ・Editor フォルダの .bytes はビルドに入らない(Android 版に元の衣装、Windows 版に Android の衣装は入らない)
//  ・Steam/Android の絵は同じ大きさ(4の倍数、カードは透明な余白でそろえた)。大きさが違う時だけ取り込みの大きさへ引き伸ばす
//  確認: Tools/2ndAction/Character Costume: Dump Imported Art(CharacterCostumeCheck)+ Tools/charart/costume_check.py
public sealed class CharacterCostumeImporter : AssetPostprocessor
{
    const string ArtFolder = "Assets/Art/UI/Characters";
    const string AndroidSources = "Assets/Editor/AndroidCharacterArt/";
    public static readonly HashSet<string> Characters = new HashSet<string>
    {
        "dual_blade", "noble_lady", "mage", "gunslinger", "ninja", "miko", "dragonkin"
    };

    void OnPostprocessTexture(Texture2D texture)
    {
        if (!assetPath.StartsWith(ArtFolder + "/", System.StringComparison.Ordinal)) return;
        string name = Path.GetFileNameWithoutExtension(assetPath);
        int split = name.LastIndexOf('_');
        if (split < 0 || !Characters.Contains(name.Substring(0, split))) return;
        string slot = name.Substring(split + 1);
        if (slot != "main" && slot != "card") return;
        if (context.selectedBuildTarget != BuildTarget.Android) return;

        string source = AndroidSources + name + ".png.bytes";
        context.DependsOnSourceAsset(source);
        var replacement = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!replacement.LoadImage(File.ReadAllBytes(source)))
                throw new InvalidDataException("Invalid character texture: " + source);
            if (replacement.width == texture.width && replacement.height == texture.height)
                texture.SetPixels32(replacement.GetPixels32());
            else
            {
                // 取り込みの大きさ(Android の Max Size の上書きを含む)に合わせる
                var pixels = new Color32[texture.width * texture.height];
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                        pixels[y * texture.width + x] = replacement.GetPixelBilinear(
                            (x + 0.5f) / texture.width, (y + 0.5f) / texture.height);
                texture.SetPixels32(pixels);
            }
            texture.Apply();
        }
        finally
        {
            Object.DestroyImmediate(replacement);
        }
    }
}
