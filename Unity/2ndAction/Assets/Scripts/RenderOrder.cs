// Single source of truth for SpriteRenderer.sortingOrder across the whole
// game, so adding a new character/enemy/effect has an obvious, documented
// number to use instead of guessing next to whatever else already exists.
// Every value here is still just a normal int assigned to a normal
// SpriteRenderer field - nothing stops a specific instance from being
// hand-tuned differently afterward (in the Inspector, or in code), this
// only fixes what gets assigned by default when something is first built.
// Values below are exactly what was already scattered across
// GroundFactory/SceneBuilder/PlayerAnimator/AttackSlashVisual/
// OneShotSpriteEffect/BossManager before this file existed - this is a
// pure consolidation, not a re-layering.
//
// IMGUI (GameManager's HUD/menus) and the DeckEdit/RewardCard uGUI
// Canvases aren't part of this list - both render in their own later pass
// on top of every SpriteRenderer regardless of sortingOrder, so they can
// never be "behind" anything here.
public static class RenderOrder
{
    // 荒野街道 地面埋め修整(2026-09-13深夜) - Groundの表面スラブ(0)より
    // 手前に出てはいけないが、Background(-100/-99)よりは確実に手前に
    // 出したい「地面の断面埋め」用。Ground-1という相対値にしているのは
    // Backgroundの-100/-99と衝突しない安全な間隔を確保するため。
    public const int GroundFill = Ground - 1;
    // 空の雲(ForegroundCloudLayer)。背景の絵(-100〜-97)と背景の幕(-50)より手前、地面/障害物/敵より奥(2026-10-02)
    public const int SkyCloud = -45;
    public const int Ground = 0;
    // Ground-type enemies, and the environmental FX that hugs the ground
    // near them (running dust, contact shadow) - the two never actually
    // need strict ordering against each other on screen, so sharing a
    // depth is fine.
    public const int Enemy = 1;
    public const int EnvironmentFx = 1;
    // Player body, and bosses (dragon/majin) - same visual depth as the
    // player they fight.
    public const int Player = 2;
    public const int Boss = 2;
    // Kept as an offset (main - 1) rather than a fixed number, since an
    // outline must always sit directly behind whichever renderer it's
    // outlining - see SpriteOutline.
    public static int OutlineFor(int mainSortingOrder) => mainSortingOrder - 1;
    public const int PlayerOverlay = Player + 1;
    // One-shot combat/movement FX - hit sparks, jump/land dust puffs,
    // ascension smoke (OneShotSpriteEffect's own default).
    public const int CombatFx = 3;
    // The attack slash visual specifically sits one above CombatFx so it
    // never gets hidden behind a simultaneous hit-particle/dust puff.
    public const int SlashFx = 4;
    public const int WorldUi = 5;
}
