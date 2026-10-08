// カードバランス v3(2026-10-03): 確認用の代表的な構成(CARD BALANCE TEST の「ビルド」タブと自動テスト QaSweep -qaCardV3 で共通)。
// キャラカード3枠 + デッキ12枚 = 15枚まで(2026-10-07 にデッキ10→12)。"id" は選んだLv、"id:n" は Lv n。
public static class CardBuildPresets
{
    public static readonly (string key, string name, string[] cards)[] All =
    {
        ("mix", "一般的な混合", new[] { "attack_up", "speed_up", "heart_up", "air_attack_up", "shield", "exp_up", "vampire", "first_strike", "attack_speed_up", "jump_count_up" }),
        ("attack", "Attack特化", new[] { "attack_up", "ground_fighter", "combo_plus", "first_strike", "boss_killer", "giant_slayer", "mob_killer", "attack_speed_up", "double_attack", "ultimate", "combo_rush", "heavy_impact", "brake_attack" }),
        ("extreme", "極端なRisk(HP犠牲)", new[] { "heart_up", "fortress", "attack_up", "berserker", "glass_cannon", "heart_breaker", "deaths_contract", "giant_slayer", "reverse_gear", "brake_attack", "ground_zero", "last_stand", "berserk_drive" }),
        ("speed", "Speed特化", new[] { "speed_up", "greed", "no_turning_back", "close_call", "ultimate", "overdrive", "momentum", "sonic_blade", "rapid_edge", "attack_speed_up" }),
        ("defense", "Defense特化", new[] { "heart_up", "fortress", "heavy_armor", "iron_will", "shield", "counter", "perfect_guard", "flame_counter", "last_chance", "second_wind", "close_call" }),
        ("blood", "Blood特化", new[] { "vampire", "predator", "overheal", "blood_rush", "blood_blade", "phoenix", "last_stand", "second_wind", "last_chance", "berserk_drive", "attack_up" }),
        ("fire", "Fire特化", new[] { "flame_blade", "burning_soul", "inferno", "flame_counter", "attack_up", "chain_explosion", "shield", "attack_speed_up" }),
        ("ice", "Ice特化", new[] { "frost_edge", "ice_prison", "absolute_zero", "attack_up", "attack_speed_up", "double_attack", "shield" }),
        ("lightning", "Lightning特化", new[] { "thunder_strike", "high_voltage", "chain_lightning", "thunder_lord", "attack_up", "attack_speed_up", "double_attack" }),
        ("wind", "Wind特化", new[] { "wind_cutter", "gale", "tornado", "piercing_blade", "long_blade", "attack_range_up", "attack_up" }),
        ("boss", "Boss特化", new[] { "boss_killer", "hunter", "attack_up", "first_strike", "combo_plus", "predator", "ground_fighter", "giant_slayer", "attack_speed_up", "double_attack" }),
        ("exp", "EXP特化", new[] { "exp_up", "pathfinder", "level_break", "experience_burst", "long_haul", "the_long_road", "one_more_mile", "monster_rush" }),
        ("mile", "MILE特化", new[] { "executioner", "treasure_hunter", "one_more_mile", "mob_killer", "elite_enemies", "wanted", "exp_converter" }),
        ("challenge", "Challenge大量", new[] { "tough_enemies", "fast_enemies", "elite_enemies", "horde", "more_enemies", "boss_challenge", "boss_rush", "wanted", "hell_mode", "pandemonium", "monster_rush" }),
        ("procs", "追加攻撃 全部(負荷確認)", new[] { "chain_explosion", "chain_lightning", "thunder_strike", "thunder_lord", "inferno", "flame_blade", "tornado", "wind_cutter", "gale", "shockwave", "double_attack", "hell_mode", "pandemonium" }),
    };

    public static string[] Find(string key)
    {
        foreach (var p in All) if (p.key == key) return p.cards;
        return null;
    }
}
