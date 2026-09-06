using System.IO;
using UnityEditor;
using UnityEngine;

// One-off dev tool: turns the user-supplied reference images into game-ready
// sprites (background backdrop, a tileable ground texture cropped from the
// cloud-path artwork, and an alpha-keyed enemy sprite).
public static class EnvironmentAssetProcessor
{
    [MenuItem("Tools/2ndAction/Process Environment Sprites")]
    public static void Run()
    {
        ProcessBackground();
        ProcessGroundTile();
        ProcessEnemy();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("EnvironmentAssetProcessor: done");
    }

    static void ProcessBackground()
    {
        const string src = @"C:\Users\0084k\Downloads\背景2.jpg";
        const string outPath = "Assets/Art/Background/background.png";
        const float lightenAmount = 0.3f; // blend toward white so it reads as a soft backdrop, not too saturated

        Texture2D tex = LoadTexture(src);
        if (tex == null) return;

        Color[] pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.Lerp(pixels[i], Color.white, lightenAmount);
        }
        tex.SetPixels(pixels);
        tex.Apply();

        WritePng(outPath, tex);
        Object.DestroyImmediate(tex);

        ImportSprite(outPath, pixelsPerUnit: 100, wrapRepeat: false, alpha: false);
    }

    // Crops a modest, texture-rich square patch out of the cloud path image
    // (near the bottom, where the path is widest/closest) and imports it as
    // a repeat-wrapped tile for the ground.
    static void ProcessGroundTile()
    {
        const string src = @"C:\Users\0084k\Downloads\雲の道.jpg";
        const string outPath = "Assets/Art/Ground/ground_tile.png";

        Texture2D tex = LoadTexture(src);
        if (tex == null) return;

        int patchSize = Mathf.Min(tex.width, tex.height) / 3;
        int cx = tex.width / 2;
        int cy = Mathf.RoundToInt(tex.height * 0.18f); // near-bottom in image space (Y=0 is bottom in Unity textures)
        int x0 = Mathf.Clamp(cx - patchSize / 2, 0, tex.width - patchSize);
        int y0 = Mathf.Clamp(cy - patchSize / 2, 0, tex.height - patchSize);

        Color[] patch = tex.GetPixels(x0, y0, patchSize, patchSize);
        Texture2D outTex = new Texture2D(patchSize, patchSize, TextureFormat.RGBA32, false);
        outTex.SetPixels(patch);
        outTex.Apply();

        WritePng(outPath, outTex);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(outTex);

        // Tile width in world units = patchSize / pixelsPerUnit.
        ImportSprite(outPath, pixelsPerUnit: 220, wrapRepeat: true, alpha: false);
    }

    static void ProcessEnemy()
    {
        const string src = @"C:\Users\0084k\Downloads\敵2.jpg";
        const string outPath = "Assets/Art/Enemy/enemy.png";
        const float desiredWorldHeight = 1.3f;

        Texture2D tex = LoadTexture(src);
        if (tex == null) return;

        Texture2D keyed = KeyOutWhite(tex);
        WritePng(outPath, keyed);
        int height = tex.height;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(keyed);

        float ppu = height / desiredWorldHeight;
        ImportSprite(outPath, pixelsPerUnit: ppu, wrapRepeat: false, alpha: true, tight: true);
    }

    static Texture2D LoadTexture(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning("EnvironmentAssetProcessor: missing " + path);
            return null;
        }
        byte[] bytes = File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);
        return tex;
    }

    static void WritePng(string outPath, Texture2D tex)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
    }

    static void ImportSprite(string assetPath, float pixelsPerUnit, bool wrapRepeat, bool alpha, bool tight = false)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (importer == null)
        {
            Debug.LogError("EnvironmentAssetProcessor: failed to import " + assetPath);
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.wrapMode = wrapRepeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.alphaIsTransparency = alpha;

        if (tight)
        {
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
        }

        importer.SaveAndReimport();
    }

    static Color SampleBrightestCorner(Texture2D src)
    {
        int w = src.width;
        int h = src.height;
        Color[] corners =
        {
            src.GetPixel(2, 2),
            src.GetPixel(w - 3, 2),
            src.GetPixel(2, h - 3),
            src.GetPixel(w - 3, h - 3)
        };

        Color best = corners[0];
        float bestLuminance = (best.r + best.g + best.b) / 3f;
        for (int i = 1; i < corners.Length; i++)
        {
            float l = (corners[i].r + corners[i].g + corners[i].b) / 3f;
            if (l > bestLuminance)
            {
                bestLuminance = l;
                best = corners[i];
            }
        }
        return best;
    }

    // Same background keying technique used for the player/dragon frames:
    // samples the actual corner background color rather than assuming pure
    // white, so slightly off-white sources still key cleanly.
    static Texture2D KeyOutWhite(Texture2D src)
    {
        Color bg = SampleBrightestCorner(src);
        float bgLuminance = Mathf.Max(0.05f, (bg.r + bg.g + bg.b) / 3f);

        Texture2D outTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        Color[] pixels = src.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            float luminance = (c.r + c.g + c.b) / 3f;
            float rawA = Mathf.Clamp01(1f - luminance / bgLuminance);

            const float clipThreshold = 0.22f;
            float outA = rawA < clipThreshold ? 0f : (rawA - clipThreshold) / (1f - clipThreshold);

            pixels[i] = outA > 0f ? new Color(c.r, c.g, c.b, outA) : new Color(0f, 0f, 0f, 0f);
        }
        outTex.SetPixels(pixels);
        outTex.Apply();
        return outTex;
    }
}
