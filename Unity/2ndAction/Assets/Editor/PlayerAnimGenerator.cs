using System.IO;
using UnityEditor;
using UnityEngine;

// Synthesizes the jump-start, double-jump, and landing frame sequences by
// warping the EXISTING (already alpha-keyed) run/jump sprites with simple
// squash/stretch/shear transforms, rather than drawing new art - keeps the
// exact same character, proportions, and colors since it's the same source
// pixels, just displaced.
public static class PlayerAnimGenerator
{
    const float Ppu = 320f;

    [MenuItem("Tools/2ndAction/Generate Player Jump-Land Animations")]
    public static void Run()
    {
        Texture2D run0 = LoadTex("Assets/Art/PlayerRun/run_00.png");
        Texture2D jump0 = LoadTex("Assets/Art/PlayerJump/jump_00.png");
        Texture2D jump4 = LoadTex("Assets/Art/PlayerJump/jump_04.png");

        GetOpaqueBounds(run0, out int runMinX, out int runMaxX, out int runMinY, out _);
        float runFeetY = runMinY;
        float runCenterX = (runMinX + runMaxX) * 0.5f;

        GetOpaqueBounds(jump4, out int j4MinX, out int j4MaxX, out int j4MinY, out int j4MaxY);
        float j4CenterX = (j4MinX + j4MaxX) * 0.5f;
        float j4CenterY = (j4MinY + j4MaxY) * 0.5f;

        // --- Jump start: one new "sink" frame (deepen the run crouch) that
        // hands off into the existing jump_00 launch pose. ---
        Directory.CreateDirectory("Assets/Art/PlayerJumpStart");
        Texture2D sink = SquashStretch(run0, scaleX: 1.14f, scaleY: 0.62f, shearX: 0f, anchorX: runCenterX, anchorY: runFeetY);
        WritePng(sink, "Assets/Art/PlayerJumpStart/jumpstart_00.png");
        WritePng(jump0, "Assets/Art/PlayerJumpStart/jumpstart_01.png"); // existing launch pose, unchanged

        // --- Double jump: squeeze-then-thrust warp of the airborne pose, so
        // it reads differently from a plain single jump hold. Anchored at
        // the sprite's own center since there's no ground contact. ---
        Directory.CreateDirectory("Assets/Art/PlayerDoubleJump");
        Texture2D tuck = SquashStretch(jump4, scaleX: 0.76f, scaleY: 0.76f, shearX: 0f, anchorX: j4CenterX, anchorY: j4CenterY);
        Texture2D thrust = SquashStretch(jump4, scaleX: 1.10f, scaleY: 1.34f, shearX: 40f, anchorX: j4CenterX, anchorY: j4CenterY);
        Texture2D settle = SquashStretch(jump4, scaleX: 1.03f, scaleY: 1.12f, shearX: 14f, anchorX: j4CenterX, anchorY: j4CenterY);
        WritePng(tuck, "Assets/Art/PlayerDoubleJump/doublejump_00.png");
        WritePng(thrust, "Assets/Art/PlayerDoubleJump/doublejump_01.png");
        WritePng(settle, "Assets/Art/PlayerDoubleJump/doublejump_02.png");

        // --- Landing: deep knee-bend impact on the run pose, easing back
        // toward the normal run stance before handing off to the run loop. ---
        Directory.CreateDirectory("Assets/Art/PlayerLand");
        Texture2D impact = SquashStretch(run0, scaleX: 1.20f, scaleY: 0.58f, shearX: 0f, anchorX: runCenterX, anchorY: runFeetY);
        Texture2D recover = SquashStretch(run0, scaleX: 1.08f, scaleY: 0.86f, shearX: 0f, anchorX: runCenterX, anchorY: runFeetY);
        WritePng(impact, "Assets/Art/PlayerLand/land_00.png");
        WritePng(recover, "Assets/Art/PlayerLand/land_01.png");

        Object.DestroyImmediate(run0);
        Object.DestroyImmediate(jump0);
        Object.DestroyImmediate(jump4);
        Object.DestroyImmediate(sink);
        Object.DestroyImmediate(tuck);
        Object.DestroyImmediate(thrust);
        Object.DestroyImmediate(settle);
        Object.DestroyImmediate(impact);
        Object.DestroyImmediate(recover);

        ImportFolder("Assets/Art/PlayerJumpStart");
        ImportFolder("Assets/Art/PlayerDoubleJump");
        ImportFolder("Assets/Art/PlayerLand");

        GenerateAttackVariants();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PlayerAnimGenerator: done");
    }

    // One-off: the source art is dominated by near-black ink (cape, hair),
    // which reads as "a dark shadow" no matter how transparent the whole
    // sprite is made at runtime - alpha-blending black over a midtone
    // background still looks dark. This actually lightens the RGB values
    // themselves (a "screen toward white" blend) in the three base frame
    // sets, so every derived set (jump/land/attack variants, all generated
    // from these same folders) inherits the lighter look too. Deliberately
    // NOT part of Run() - it edits files in place, so re-running it would
    // keep brightening the same frames further each time.
    [MenuItem("Tools/2ndAction/Brighten Player Base Sprites (one-off)")]
    public static void BrightenBaseSprites()
    {
        const float factor = 0.6f;
        BrightenFolderInPlace("Assets/Art/PlayerRun", factor);
        BrightenFolderInPlace("Assets/Art/PlayerJump", factor);
        BrightenFolderInPlace("Assets/Art/PlayerAttack", factor);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PlayerAnimGenerator: brightened base sprites");
    }

    static void BrightenFolderInPlace(string dir, float factor)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string file in Directory.GetFiles(dir, "*.png"))
        {
            string path = file.Replace('\\', '/');
            Texture2D src = LoadTex(path);
            Texture2D bright = Brighten(src, factor);
            File.WriteAllBytes(path, bright.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(bright);
        }

        ImportFolder(dir);
    }

    // Screen-blends every pixel toward white by `factor` (0 = unchanged, 1 =
    // fully white), leaving alpha untouched - lightens blacks to grey while
    // keeping highlights from blowing out or the silhouette from vanishing.
    static Texture2D Brighten(Texture2D src, float factor)
    {
        int w = src.width, h = src.height;
        Color[] pixels = src.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            pixels[i] = new Color(
                c.r + (1f - c.r) * factor,
                c.g + (1f - c.g) * factor,
                c.b + (1f - c.b) * factor,
                c.a);
        }

        Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels(pixels);
        outTex.Apply();
        return outTex;
    }

    // The existing 5-frame swing (attack_00..04) is combo stage 2 (medium),
    // unchanged. Stage 1 (first hit) is a tighter, quicker-reading jab; stage
    // 3 (the combo finisher) is a bigger, further-reaching swing - each
    // frame warped independently around its own silhouette center so the
    // pose doesn't drift across the swing.
    static void GenerateAttackVariants()
    {
        const string srcDir = "Assets/Art/PlayerAttack";
        string[] files = Directory.GetFiles(srcDir, "attack_*.png");
        System.Array.Sort(files);

        Directory.CreateDirectory("Assets/Art/PlayerAttackSmall");
        Directory.CreateDirectory("Assets/Art/PlayerAttackLarge");

        int i = 0;
        foreach (string file in files)
        {
            Texture2D src = LoadTex(file.Replace('\\', '/'));
            GetOpaqueBounds(src, out int minX, out int maxX, out int minY, out int maxY);
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;

            Texture2D small = SquashStretch(src, scaleX: 0.86f, scaleY: 0.86f, shearX: 0f, anchorX: cx, anchorY: cy);
            Texture2D large = SquashStretch(src, scaleX: 1.22f, scaleY: 1.22f, shearX: 10f, anchorX: cx, anchorY: cy);

            WritePng(small, $"Assets/Art/PlayerAttackSmall/attacksmall_{i:00}.png");
            WritePng(large, $"Assets/Art/PlayerAttackLarge/attacklarge_{i:00}.png");

            Object.DestroyImmediate(src);
            Object.DestroyImmediate(small);
            Object.DestroyImmediate(large);
            i++;
        }

        ImportFolder("Assets/Art/PlayerAttackSmall");
        ImportFolder("Assets/Art/PlayerAttackLarge");
    }

    static Texture2D LoadTex(string assetPath)
    {
        byte[] bytes = File.ReadAllBytes(assetPath);
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);
        return tex;
    }

    static void WritePng(Texture2D tex, string outPath)
    {
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
    }

    static void ImportFolder(string dir)
    {
        foreach (string path in Directory.GetFiles(dir, "*.png"))
        {
            string assetPath = path.Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }

    // Finds the pixel bounds of non-transparent content, so squash/stretch
    // can anchor on the character's own silhouette (e.g. its feet) instead
    // of the raw canvas edges.
    static void GetOpaqueBounds(Texture2D tex, out int minX, out int maxX, out int minY, out int maxY)
    {
        Color[] pixels = tex.GetPixels();
        int w = tex.width, h = tex.height;
        minX = w; maxX = -1; minY = h; maxY = -1;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x].a > 0.1f)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0) { minX = 0; maxX = w - 1; minY = 0; maxY = h - 1; }
    }

    // Warps a texture by scaling (anchored at anchorX/anchorY) plus an
    // optional horizontal shear proportional to distance from anchorY -
    // squash-and-stretch, the classic hand-drawn-animation technique for
    // conveying weight/impact/thrust, applied here to existing art instead
    // of new frames. Texture2D.GetPixels()/SetPixels() are bottom-left
    // origin (row 0 = bottom), so anchorY in that same space keeps the
    // "ground" row fixed while the rest compresses/stretches around it.
    static Texture2D SquashStretch(Texture2D src, float scaleX, float scaleY, float shearX, float anchorX, float anchorY)
    {
        int w = src.width, h = src.height;
        Color[] srcPixels = src.GetPixels();
        Color[] outPixels = new Color[w * h];

        for (int oy = 0; oy < h; oy++)
        {
            float dyOut = oy - anchorY;
            float sy = dyOut / scaleY;
            float srcY = sy + anchorY;

            for (int ox = 0; ox < w; ox++)
            {
                float dxOut = ox - anchorX;
                float sx = (dxOut - shearX * (dyOut / h)) / scaleX;
                float srcX = sx + anchorX;

                outPixels[oy * w + ox] = SampleBilinear(srcPixels, w, h, srcX, srcY);
            }
        }

        Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels(outPixels);
        outTex.Apply();
        return outTex;
    }

    static Color SampleBilinear(Color[] pixels, int w, int h, float x, float y)
    {
        if (x < 0f || y < 0f || x > w - 1 || y > h - 1) return new Color(0f, 0f, 0f, 0f);

        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);
        float fx = x - x0;
        float fy = y - y0;

        Color c00 = pixels[y0 * w + x0];
        Color c10 = pixels[y0 * w + x1];
        Color c01 = pixels[y1 * w + x0];
        Color c11 = pixels[y1 * w + x1];

        Color top = Color.Lerp(c00, c10, fx);
        Color bot = Color.Lerp(c01, c11, fx);
        return Color.Lerp(top, bot, fy);
    }
}
