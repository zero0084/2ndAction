using System.Collections.Generic;

// CPU サポート(2026-10-14): 仲間のキャラごとのおすすめ構成。
// 元は開発用のビルド検証(Docs/BuildLab/builds_v1.json の *_LD = ラスダンを通す構成)。製品版に入れるためここへ写した。
//  deck      … ラン中のレベルアップ/ボス撃破で取る候補(この12枚の中から priority の順に取る)
//  charCards … ラン開始の時から効くキャラカード(3枠)
//  priority  … 取る順番(Lv9 に届いたカードは飛ばして次へ)
// 仲間のカードの強さ(1回で上がる Lv)は、プレイヤーがそのカードを持っている Lv(読むだけ。消費しない)。持っていなければ Lv1。
public static class CpuBuilds
{
    public sealed class Build
    {
        public readonly string character;
        public readonly string[] deck, charCards, priority;
        public Build(string character, string[] deck, string[] charCards, string[] priority)
        { this.character = character; this.deck = deck; this.charCards = charCards; this.priority = priority; }
    }

    static readonly Build[] All =
    {
        new Build("swordsman", new[] { "attack_up", "boss_killer", "double_attack", "attack_speed_up", "long_blade", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }, new[] { "attack_up", "heart_up", "boss_killer" }, new[] { "attack_up", "boss_killer", "double_attack", "attack_speed_up", "long_blade", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }),
        new Build("dual_blade", new[] { "attack_up", "boss_killer", "double_attack", "rapid_edge", "combo_edge", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "predator" }, new[] { "attack_up", "boss_killer", "heart_up" }, new[] { "attack_up", "boss_killer", "double_attack", "rapid_edge", "combo_edge", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "predator" }),
        new Build("noble_lady", new[] { "attack_up", "boss_killer", "attack_speed_up", "long_blade", "double_attack", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "speed_up" }, new[] { "heart_up", "attack_up", "boss_killer" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "long_blade", "double_attack", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "speed_up" }),
        new Build("gunslinger", new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "rapid_edge", "piercing_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }, new[] { "attack_up", "boss_killer", "attack_speed_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "rapid_edge", "piercing_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }),
        new Build("dragon_lancer", new[] { "attack_up", "boss_killer", "attack_speed_up", "rapid_edge", "first_strike", "double_attack", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }, new[] { "attack_up", "boss_killer", "attack_speed_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "rapid_edge", "first_strike", "double_attack", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }),
        new Build("archer", new[] { "attack_up", "boss_killer", "attack_speed_up", "rapid_edge", "double_attack", "piercing_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "speed_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "rapid_edge", "double_attack", "piercing_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "speed_up" }),
        new Build("mage", new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "long_blade", "heart_up", "long_haul", "shield", "vampire", "second_wind", "phoenix", "iron_will" }, new[] { "heart_up", "attack_up", "boss_killer" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "long_blade", "heart_up", "long_haul", "shield", "vampire", "second_wind", "phoenix", "iron_will" }),
        new Build("fighter", new[] { "attack_up", "boss_killer", "combo_plus", "combo_edge", "double_attack", "rapid_edge", "heart_up", "shield", "vampire", "second_wind", "phoenix", "predator" }, new[] { "attack_up", "boss_killer", "heart_up" }, new[] { "attack_up", "boss_killer", "combo_plus", "combo_edge", "double_attack", "rapid_edge", "heart_up", "shield", "vampire", "second_wind", "phoenix", "predator" }),
        new Build("ninja", new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "first_strike", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }, new[] { "attack_up", "boss_killer", "heart_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "first_strike", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }),
        new Build("miko", new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "long_blade", "piercing_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }, new[] { "attack_up", "boss_killer", "heart_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "long_blade", "piercing_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "iron_will" }),
        new Build("vampire", new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "blood_blade", "predator", "vampire", "heart_up", "shield", "second_wind", "phoenix", "combo_plus" }, new[] { "attack_up", "boss_killer", "vampire" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "blood_blade", "predator", "vampire", "heart_up", "shield", "second_wind", "phoenix", "combo_plus" }),
        new Build("dragonkin", new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "long_blade", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "speed_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up" }, new[] { "attack_up", "boss_killer", "attack_speed_up", "double_attack", "long_blade", "sonic_blade", "heart_up", "shield", "vampire", "second_wind", "phoenix", "speed_up" }),
    };

    static Dictionary<string, Build> byChar;
    public static Build For(string characterId)
    {
        if (byChar == null) { byChar = new Dictionary<string, Build>(); foreach (var b in All) byChar[b.character] = b; }
        if (characterId != null && byChar.TryGetValue(characterId, out var bb)) return bb;
        return byChar.TryGetValue("swordsman", out var sw) ? sw : All[0];
    }
}
