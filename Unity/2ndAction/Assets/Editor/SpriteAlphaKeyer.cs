using System.IO;
using UnityEditor;
using UnityEngine;

// One-off dev tool: turns the extracted video frames (white background) into
// sprites with a transparent background, and imports them.
public static class SpriteAlphaKeyer
{
    [MenuItem("Tools/2ndAction/Process Player Sprites")]
    public static void Run()
    {
        ProcessFolder("Assets/Art/PlayerRunSource", "Assets/Art/PlayerRun", "run", 320);
        ProcessFolder("Assets/Art/PlayerJumpSource", "Assets/Art/PlayerJump", "jump", 320);
        ProcessFolder("Assets/Art/PlayerAttackSource", "Assets/Art/PlayerAttack", "attack", 320);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SpriteAlphaKeyer: done");
    }

    // Dust/smoke reference images have a white/light background baked in
    // (checked per-file: none of them carry real alpha), so they go through
    // the same white-removal key as the character sources. The slash VFX
    // source, uniquely, already has real alpha from its GIF - it only needs
    // straight copy-and-import, not re-keying.
    [MenuItem("Tools/2ndAction/Process VFX Sprites")]
    public static void RunVfx()
    {
        ProcessFolder("Assets/Art/LandDustSource", "Assets/Art/LandDust", "land", 430f);
        ProcessFolder("Assets/Art/DoubleJumpDustSource", "Assets/Art/DoubleJumpDust", "doublejumpdust", 430f);
        ProcessFolder("Assets/Art/JumpDustSource", "Assets/Art/JumpDust", "jumpdust", 290f);
        ProcessFolder("Assets/Art/AscensionSmokeSource", "Assets/Art/AscensionSmoke", "smoke", 320f);
        ImportPassthrough("Assets/Art/AttackSlashSource", "Assets/Art/AttackSlashFx", "slash", 240f);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SpriteAlphaKeyer: vfx done");
    }

    static void ImportPassthrough(string sourceDir, string outDir, string prefix, float ppu)
    {
        if (!Directory.Exists(sourceDir))
        {
            Debug.LogWarning("SpriteAlphaKeyer: missing " + sourceDir);
            return;
        }
        Directory.CreateDirectory(outDir);

        string[] files = Directory.GetFiles(sourceDir, "*.png");
        System.Array.Sort(files);

        int outIndex = 0;
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            string outPath = Path.Combine(outDir, $"{prefix}_{outIndex:00}.png");
            File.WriteAllBytes(outPath, bytes);
            outIndex++;
        }

        foreach (string path in Directory.GetFiles(outDir, "*.png"))
        {
            string assetPath = path.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }

    [MenuItem("Tools/2ndAction/Process Dragon Sprites")]
    public static void RunDragon()
    {
        // The three source videos were captured at different resolutions
        // (idle/fire 672x448, charge 1168x784). Importing all three at the
        // same fixed pixels-per-unit made the charge animation render ~1.75x
        // bigger than idle/fire. Instead, derive each set's PPU from its own
        // source frame height so they all resolve to the SAME world height.
        const float dragonWorldHeight = 3.45f; // matches what ppu=130 gave a 448px-tall frame
        ProcessFolderByWorldHeight("Assets/Art/DragonIdleSource", "Assets/Art/DragonIdle", "idle", dragonWorldHeight);
        ProcessFolderByWorldHeight("Assets/Art/DragonChargeSource", "Assets/Art/DragonCharge", "charge", dragonWorldHeight);
        ProcessFolderByWorldHeight("Assets/Art/DragonFireSource", "Assets/Art/DragonFire", "fire", dragonWorldHeight);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SpriteAlphaKeyer: dragon done");
    }

    static void ProcessFolderByWorldHeight(string sourceDir, string outDir, string prefix, float desiredWorldHeight)
    {
        if (!Directory.Exists(sourceDir))
        {
            Debug.LogWarning("SpriteAlphaKeyer: missing " + sourceDir);
            return;
        }

        string[] files = Directory.GetFiles(sourceDir, "*.png");
        System.Array.Sort(files);

        float ppu = 130f;
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            Texture2D probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            probe.LoadImage(bytes);
            bool skip = IsMostlyBlack(probe);
            int height = probe.height;
            Object.DestroyImmediate(probe);

            if (!skip)
            {
                ppu = height / desiredWorldHeight;
                break;
            }
        }

        ProcessFolder(sourceDir, outDir, prefix, ppu);
    }

    static void ProcessFolder(string sourceDir, string outDir, string prefix, float ppu)
    {
        if (!Directory.Exists(sourceDir))
        {
            Debug.LogWarning("SpriteAlphaKeyer: missing " + sourceDir);
            return;
        }
        Directory.CreateDirectory(outDir);

        string[] files = Directory.GetFiles(sourceDir, "*.png");
        System.Array.Sort(files);

        int outIndex = 0;
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            Texture2D src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(bytes);

            if (IsMostlyBlack(src))
            {
                Object.DestroyImmediate(src);
                continue; // skip the garbage frame captured before decode kicked in
            }

            Texture2D outTex = KeyOutWhite(src);
            string outPath = Path.Combine(outDir, $"{prefix}_{outIndex:00}.png");
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(outTex);
            outIndex++;
        }

        foreach (string path in Directory.GetFiles(outDir, "*.png"))
        {
            string assetPath = path.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }

    static bool IsMostlyBlack(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();
        int sampleStep = Mathf.Max(1, pixels.Length / 200);
        long sum = 0;
        int count = 0;
        for (int i = 0; i < pixels.Length; i += sampleStep)
        {
            sum += pixels[i].r + pixels[i].g + pixels[i].b;
            count++;
        }
        float avg = sum / (float)(count * 3);
        return avg < 8f;
    }

    // Some source frames have a faint gradient/vignette in the background
    // rather than flat white, so a single sample point can land on a darker
    // patch. Sampling all four corners and keeping the brightest avoids
    // treating that darker patch as the reference and leaving a haze.
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

    // Treats the source as a dark illustration on a light (not necessarily
    // pure-white) background: samples a corner pixel as the actual
    // background color/brightness, derives alpha relative to that, then
    // un-premultiplies against it to recover clean RGB. Some source videos
    // have an off-white (e.g. ~235/255) backing instead of true white, which
    // a fixed "assume 255" key would leave as a visible dark haze.
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
            float rawAlpha = Mathf.Clamp01(1f - luminance / bgLuminance);

            // Some sources have a soft vignette rather than flat background,
            // which a single reference point can't fully cancel out - clip
            // the residual low-end alpha haze to fully transparent instead
            // of leaving a faint dark tint over background areas.
            const float clipThreshold = 0.22f;
            float outAlpha = rawAlpha < clipThreshold ? 0f : (rawAlpha - clipThreshold) / (1f - clipThreshold);

            // Keep the original RGB as-is rather than un-premultiplying
            // against the sampled background: with a background that isn't
            // perfectly flat, unpremultiplying over-subtracts and darkens
            // the recovered color. Passing the source color through (with
            // just the derived alpha) reads correctly once the low end is
            // clipped away above.
            pixels[i] = outAlpha > 0f ? new Color(c.r, c.g, c.b, outAlpha) : new Color(0f, 0f, 0f, 0f);
        }
        outTex.SetPixels(pixels);
        outTex.Apply();
        return outTex;
    }
}
