// Game Feel Visibility Pass - a temporary debug switch to tell apart two
// very different bugs that look identical from gameplay alone: "the spawn
// call never fired" vs. "it fired, but is too small/brief/faint to notice
// during normal play". When VisibilityBoost is on, every effect that goes
// through OneShotSpriteEffect.CreateTweened/CreateScatterBurst spawns at
// roughly double scale, double lifetime, and full initial alpha regardless
// of its own tuned values (see the boost block in OneShotSpriteEffect) -
// if an effect still never appears with this on, the problem is upstream
// (the event never fired / the sprite is null), not the tuning.
//
// Toggled from GameManager's settings column (gear icon -> "GAMEFEEL FX")
// so it's reachable on-device without a keyboard. Intentionally NOT saved
// to PlayerPrefs - it always starts false on launch, so a shipped build
// can never accidentally leave it on.
public static class GameFeelDebug
{
    public static bool VisibilityBoost = false;
}
