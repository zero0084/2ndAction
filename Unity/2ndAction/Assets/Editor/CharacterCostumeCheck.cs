using System.IO;
using UnityEditor;
using UnityEngine;

// 衣装の切り替え(CharacterCostumeImporter)の確認(2026-11-01)。今の機種で取り込まれた立ち絵/カード 28枚を、GPU で描き写して PNG に書き出す。
//  書き出した絵を Tools/charart/costume_check.py が Steam/Android の元の絵と比べて、どちらの衣装かを判定する
//  (圧縮後の絵なので完全には一致しない → 2つの衣装で違う画素だけを比べる)。
//  バッチ: -executeMethod CharacterCostumeCheck.Dump -ccOut <フォルダ>(機種名のフォルダの下へ書く)
public static class CharacterCostumeCheck
{
    [MenuItem("Tools/2ndAction/Character Costume: Dump Imported Art")]
    public static void Dump()
    {
        string root = "Builds/CostumeCheck";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ccOut") root = args[i + 1];
        var target = EditorUserBuildSettings.activeBuildTarget;
        string dir = Path.Combine(root, target.ToString());
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/UI/Characters" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (Path.GetDirectoryName(path).Replace('\\', '/') != "Assets/Art/UI/Characters") continue;
            if (!name.EndsWith("_main") && !name.EndsWith("_card")) continue;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), read.EncodeToPNG());
            Object.DestroyImmediate(read);
            Debug.Log($"[costume] {target} {name} {tex.width}x{tex.height} {tex.format} {(CharacterCostumeImporter.Characters.Contains(name.Substring(0, name.LastIndexOf('_'))) ? "switched" : "shared")}");
            n++;
        }
        Debug.Log($"[costume] dumped {n} textures for {target} -> {dir}");
    }
}
