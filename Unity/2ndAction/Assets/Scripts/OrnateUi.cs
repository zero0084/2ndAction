using UnityEngine;

// Visual Style Ver.2 (the design-reference redesign pass) - a decorative
// navy+gold+blue-accent "game-specific" frame (Assets/Art/UI/OrnateFrame.png,
// generated to match the existing CardFrame.png/CardBack.png art direction)
// layered over a plain navy fill, drawn via IMGUI for the TOP screen's hero
// elements (START/DECK/BEST) - see SceneBuilder.CreateOrnatePanel for the
// uGUI equivalent used by the Deck Edit screen's panels/cards.
//
// Deliberately a SEPARATE helper from UiBackdrop rather than a change to
// UiBackdrop.Draw itself: the in-run HUD (distance/Lv/HP) and the settings/
// debug column keep UiBackdrop's plainer thin-edge treatment on purpose -
// busier ornate chrome on every small HUD box during actual gameplay would
// hurt legibility and was never asked for, so only the screens/elements the
// redesign brief actually calls out opt into this.
public static class OrnateUi
{
    // Assigned once from GameManager.Awake() (gameManager.ornateFrame, set
    // by SceneBuilder at scene-build time) - a static field because every
    // OnGUI call site here is itself static-ish (no MonoBehaviour instance
    // handy), same pattern as UiBackdrop's own cached 1x1 texture.
    //
    // This is OrnateFrameSmall.png (144x96), NOT the large OrnateFrame.png
    // the Deck Edit screen's uGUI panels use. IMGUI's GUIStyle.border has
    // no "pixels per unit" concept the way uGUI's Sprite.border does - it's
    // always literal texture pixels measured directly against whatever
    // Rect gets passed to GUI.Box, so the large texture's 280px border
    // (fine for uGUI panels 600-900 units wide, scaled down via
    // spritePixelsPerUnit) completely overwhelmed IMGUI's button-sized
    // Rects here (as small as 52-70px tall) and rendered as a single
    // giant, stretched, broken mess. OrnateFrameSmall.png is a
    // high-quality downscale of the same source art at the same authored
    // proportions, just small enough for its 26px border to actually fit
    // inside a button-sized Rect.
    public static Texture2D FrameTexture;

    // Must match OrnateFrameSmall.png's authored border (26/144 ≈ 18%,
    // 26/96 ≈ 27% - the same proportions as the large texture's 280/1536,
    // 280/1024, just at 1/10.67 scale).
    public const int FrameBorderPx = 26;

    static GUIStyle style;

    static GUIStyle Style()
    {
        if (style == null || style.normal.background != FrameTexture)
        {
            style = new GUIStyle();
            style.border = new RectOffset(FrameBorderPx, FrameBorderPx, FrameBorderPx, FrameBorderPx);
            style.normal.background = FrameTexture;
        }
        return style;
    }

    // Navy fill (same color convention as UiBackdrop's NavyFill) plus the
    // ornate frame texture on top, 9-slice-stretched via GUIStyle.border so
    // it holds up at any rect size without the corners/edges warping.
    // Falls back to a plain fill (no decorative frame) if FrameTexture
    // hasn't been assigned yet, rather than throwing or drawing nothing.
    //
    // fillAlpha is multiplied against the AMBIENT GUI.color.a (not just
    // set outright) so a caller wrapping this in its own
    // `GUI.color = new Color(1,1,1,introFadeAlpha)` (see GameManager's
    // title-screen intro fade) correctly fades the whole panel - the frame
    // draw below already gets this for free since it never reassigns
    // GUI.color itself, only the fill did.
    public static void DrawPanel(Rect rect, float fillAlpha = 0.85f)
    {
        Color prev = GUI.color;
        GUI.color = new Color(0.05f, 0.06f, 0.13f, fillAlpha * prev.a);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;

        if (FrameTexture != null) GUI.Box(rect, GUIContent.none, Style());
    }
}
