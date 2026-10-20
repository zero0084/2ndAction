using UnityEditor;
using UnityEngine;

// 画面の共通アイコン(Assets/Resources/UI/Icons/*.png、2026-10-20、エステルさんと相談して ChatGPT で生成・黒の背景から切り出し)の取り込み設定。
// IMGUI の GUI.DrawTexture で 14〜48px に縮めて描くので、ミップマップを付けて縮小のちらつきを防ぐ(元は 128px の正方形)。
// Android の圧縮(ASTC 4x4)は TextureImportRules が付ける
public class UiIconImporter : AssetPostprocessor
{
    const string Dir = "Assets/Resources/UI/Icons/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Dir)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = true;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Trilinear;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.Compressed;
    }
}
