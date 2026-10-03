using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Creates (once) the CardDefinition assets under Assets/Resources/Cards/,
// one per card in the default spec table below. Adding a new card to that
// table and re-running this (Tools/OneMoreMile/Build Card Database, or just
// SceneBuilder.Build, which calls it automatically) creates its asset -
// nothing else needs code changes as long as the new card only reuses
// existing EffectTypes.
//
// IMPORTANT: this only ever CREATES an asset that doesn't exist yet. If a
// card's .asset already exists, its fields (including any numeric value
// tuned by hand in the Inspector afterwards) are left completely alone -
// rebuilding the scene must never silently revert someone's balance
// tweaks. To intentionally reset a card back to its spec default, delete
// its .asset file (or its effects list) and rebuild.
public static class CardDatabaseBuilder
{
    const string CardsFolder = "Assets/Resources/Cards";
    const string GeneratedIconFolder = "Assets/Art/Icons/Generated";
    // 全カードアイコン統一・カード表示品質改修(2026-09-16) - Visual Style
    // Ver.1準拠の新規アイコン91種をChatGPTで生成し、
    // Assets/Art/Icons/CardIcons/<cardId>.png という命名規則で配置した。
    // 91個のSpecエントリすべてにexistingIconPathを個別に書き込むと巨大な
    // 差分になる上、今後カードを追加するたびに書き忘れるリスクがあるため、
    // 「そのcardId.pngが実在すれば自動的に使う」という規約ベースの解決を
    // ResolveIconPath1箇所に集約した。
    const string UnifiedIconFolder = "Assets/Art/Icons/CardIcons";

    // spec.existingIconPathが明示されていればそちらを優先(将来、特定
    // カードだけ命名規則から外れた場所の素材を使いたくなった場合の抜け道)。
    // 何も指定が無ければUnifiedIconFolder内のcardId.pngを探し、存在すれば
    // そのパスを返す(無ければnullを返し、呼び出し元は従来どおり
    // GenerateIcon()の手続き生成プレースホルダーへフォールバックする)。
    static string ResolveIconPath(Spec spec)
    {
        if (spec.existingIconPath != null) return spec.existingIconPath;
        string candidate = $"{UnifiedIconFolder}/{spec.id}.png";
        return AssetDatabase.LoadAssetAtPath<Texture2D>(candidate) != null ? candidate : null;
    }

    struct Spec
    {
        public string id;
        public string name;
        public int sortOrder;
        // See CardDefinition.recommendPriority. Defaults to 5 (the same
        // default CardDefinition itself uses) for specs below that don't
        // set it explicitly.
        public int recommendPriority;
        public CardCategory category;
        public string description;
        public string existingIconPath; // legacy hand-drawn icon, if any
        public IconGlyph glyph;         // used only when existingIconPath is null
        public Color glyphColor;
        public (EffectType type, float value)[] effects;

        // Card Expansion/Gacha Evolution Ver.1 additions - see
        // CardDefinition's own comments for what each means. rarity
        // defaults to 0 (C# struct default) here and resolves to 1 in
        // Build() below (same "0 = not set" convention EnemyDatabaseBuilder
        // already uses for mileReward/visualScaleMultiplier).
        public int rarity;
        public float unlockDistance;
        public int gachaStage;
        public ElementType element;
    }

    enum IconGlyph { Ring, SpeedLines, WingSword, Shield, ArrowUp, Droplet, CrossSwords, ArmorPlate, Coin }

    [MenuItem("Tools/OneMoreMile/Build Card Database")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(CardsFolder))
        {
            Directory.CreateDirectory(CardsFolder);
            AssetDatabase.Refresh();
        }

        foreach (Spec spec in Specs())
        {
            string assetPath = $"{CardsFolder}/{spec.id}.asset";
            CardDefinition existing = AssetDatabase.LoadAssetAtPath<CardDefinition>(assetPath);
            if (existing != null)
            {
                // Never overwrite an existing asset's tuned fields (see
                // class comment) - EXCEPT recommendPriority, which is new
                // and so could never have been hand-tuned yet (every
                // pre-existing asset just has CardDefinition's own field
                // default, 5, from before this spec table gave it a real
                // value). Backfilling it here means the 4 trade-off cards
                // (SPEED DOWN/BERSERKER/HEAVY ARMOR/GREED) actually get
                // their low priority without needing everyone to delete
                // and rebuild 16 existing .asset files by hand.
                if (existing.recommendPriority == 5 && spec.recommendPriority != 0 && spec.recommendPriority != 5)
                {
                    existing.recommendPriority = spec.recommendPriority;
                    EditorUtility.SetDirty(existing);
                }
                // Card Expansion/Gacha Evolution Ver.1 - same backfill
                // reasoning as recommendPriority above: rarity/
                // unlockDistance/gachaStage/element are brand new fields,
                // so any EXISTING asset still sitting at CardDefinition's
                // own bare class defaults (1/0/1/None) couldn't possibly
                // have been hand-tuned away from that yet - safe to
                // backfill this one time. A value that's already anything
                // else (a deliberate Inspector edit made after this rebuild
                // first ran) is left completely alone.
                if (existing.rarity == 1 && existing.unlockDistance == 0f && existing.gachaStage == 1 && existing.element == ElementType.None)
                {
                    bool changed = false;
                    if (spec.rarity != 0 && spec.rarity != 1) { existing.rarity = spec.rarity; changed = true; }
                    if (spec.unlockDistance != 0f) { existing.unlockDistance = spec.unlockDistance; changed = true; }
                    if (spec.gachaStage != 0 && spec.gachaStage != 1) { existing.gachaStage = spec.gachaStage; changed = true; }
                    if (spec.element != ElementType.None) { existing.element = spec.element; changed = true; }
                    if (changed) EditorUtility.SetDirty(existing);
                }

                // 追加カード素材アイコン割り当て(2026-09-11) - マスター
                // 提供の10種アイコンシートのうち、既存カード(LONG BLADE/
                // SHOCKWAVE/MOMENTUM)と名前が重なる3枚は新規カードにせず、
                // 既存カードのアイコンだけを手続き生成(GenerateIcon)から
                // 実アート(existingIconPath)へ差し替える。数値(effects)は
                // 上記backfillと同じ「既存アセットの調整値は絶対に上書き
                // しない」方針を維持するが、アイコンは元々「実アートが
                // 用意でき次第差し替える前提のプレースホルダー」(クラス
                // 冒頭コメント・GenerateIconの解説どおり)なので、spec側で
                // existingIconPathが指定された場合は都度アイコンだけ同期
                // する(誰かがInspectorで別アートに差し替えていない限り、
                // 何度Buildを実行しても同じ結果になる)。
                string resolvedIconPath = ResolveIconPath(spec);
                if (resolvedIconPath != null)
                {
                    Texture2D newIcon = LoadIconTexture(resolvedIconPath);
                    if (newIcon != null && existing.icon != newIcon)
                    {
                        existing.icon = newIcon;
                        EditorUtility.SetDirty(existing);
                    }
                }
                continue;
            }

            var card = ScriptableObject.CreateInstance<CardDefinition>();
            card.cardId = spec.id;
            card.cardName = spec.name;
            card.sortOrder = spec.sortOrder;
            card.recommendPriority = spec.recommendPriority != 0 ? spec.recommendPriority : 5;
            card.category = spec.category;
            card.description = spec.description;
            card.rarity = spec.rarity != 0 ? spec.rarity : 1;
            card.unlockDistance = spec.unlockDistance;
            card.gachaStage = spec.gachaStage != 0 ? spec.gachaStage : 1;
            card.element = spec.element;
            string newCardIconPath = ResolveIconPath(spec);
            card.icon = newCardIconPath != null
                ? LoadIconTexture(newCardIconPath)
                : GenerateIcon(spec.id, spec.glyph, spec.glyphColor);

            card.effects = new List<CardEffect>();
            foreach (var (type, value) in spec.effects)
            {
                card.effects.Add(new CardEffect { type = type, value = value });
            }

            AssetDatabase.CreateAsset(card, assetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CardDatabase.Reset();
    }

    // The 15-card default spec (Ver.0.1) - the 6 legacy cards keep their
    // existing hand-drawn icons and original values; the 9 new ones use
    // placeholder procedural icons (swap CardDefinition.icon in the
    // Inspector any time real art is ready) and placeholder numeric values
    // (tune CardDefinition.effects[].value in the Inspector any time).
    static IEnumerable<Spec> Specs()
    {
        // ===== Existing 16 cards - Card Expansion/Gacha Evolution Ver.1
        // just backfills rarity/unlockDistance/gachaStage per the brief's
        // own "既存カードRarity" table (item 3); everything else about
        // them (id/effects/icon/etc.) is completely unchanged. =====
        yield return new Spec
        {
            id = "speed_up", name = "SPEED UP", sortOrder = 1, category = CardCategory.Movement,
            description = "移動速度が上昇する",
            effects = new[] { (EffectType.MoveSpeed, 0.12f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "speed_down", name = "SPEED DOWN", sortOrder = 2, recommendPriority = 1, category = CardCategory.Movement,
            description = "移動速度が低下する",
            effects = new[] { (EffectType.MoveSpeed, -0.12f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "attack_up", name = "ATTACK UP", sortOrder = 3, category = CardCategory.Attack,
            description = "攻撃力が上昇する",
            effects = new[] { (EffectType.AttackPower, 10f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "jump_power_up", name = "JUMP POWER UP", sortOrder = 4, category = CardCategory.Movement,
            description = "ジャンプ力が上昇する",
            effects = new[] { (EffectType.JumpPower, 0.15f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "jump_count_up", name = "JUMP COUNT UP", sortOrder = 5, category = CardCategory.Movement,
            description = "空中ジャンプ回数が増える",
            effects = new[] { (EffectType.JumpCount, 1f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "heart_up", name = "HEART UP", sortOrder = 6, category = CardCategory.Defense,
            description = "ハート上限が増え、HPが回復する",
            effects = new[] { (EffectType.MaxHp, 10f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "attack_range_up", name = "ATTACK RANGE UP", sortOrder = 7, category = CardCategory.Attack,
            description = "通常攻撃の攻撃判定が広がる",
            glyph = IconGlyph.Ring, glyphColor = new Color(1f, 0.55f, 0.25f),
            effects = new[] { (EffectType.AttackRange, 0.25f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "attack_speed_up", name = "ATTACK SPEED UP", sortOrder = 8, category = CardCategory.Attack,
            description = "3段攻撃のテンポが速くなる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(1f, 0.85f, 0.2f),
            effects = new[] { (EffectType.AttackSpeed, 0.15f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "air_attack_up", name = "AIR ATTACK UP", sortOrder = 9, category = CardCategory.Attack,
            description = "空中にいる間、攻撃力が上昇する",
            glyph = IconGlyph.WingSword, glyphColor = new Color(0.55f, 0.85f, 1f),
            effects = new[] { (EffectType.AirAttackPower, 20f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "shield", name = "SHIELD", sortOrder = 10, category = CardCategory.Defense,
            description = "ダメージを1回だけ無効化する",
            glyph = IconGlyph.Shield, glyphColor = new Color(0.5f, 0.75f, 1f),
            effects = new[] { (EffectType.Shield, 1f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "exp_up", name = "EXP UP", sortOrder = 11, category = CardCategory.Growth,
            description = "獲得できる経験値が増加する",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.55f, 1f, 0.55f),
            effects = new[] { (EffectType.ExpGain, 0.2f) },
            rarity = 1, gachaStage = 1
        };
        yield return new Spec
        {
            id = "vampire", name = "VAMPIRE", sortOrder = 12, category = CardCategory.Heal,
            description = "敵撃破時、確率でHPを回復する",
            glyph = IconGlyph.Droplet, glyphColor = new Color(0.85f, 0.15f, 0.25f),
            effects = new[] { (EffectType.LifestealChance, 0.15f), (EffectType.LifestealAmount, 10f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "berserker", name = "BERSERKER", sortOrder = 13, recommendPriority = 1, category = CardCategory.Risk,
            description = "攻撃力が大幅上昇するが、最大HPが低下する",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.35f, 0.2f),
            effects = new[] { (EffectType.AttackPower, 30f), (EffectType.MaxHp, -10f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "heavy_armor", name = "HEAVY ARMOR", sortOrder = 14, recommendPriority = 1, category = CardCategory.Risk,
            description = "最大HPが大幅増加するが、ジャンプ力が低下する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.6f, 0.65f, 0.75f),
            effects = new[] { (EffectType.MaxHp, 30f), (EffectType.JumpPower, -0.15f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "greed", name = "GREED", sortOrder = 15, recommendPriority = 1, category = CardCategory.Risk,
            description = "移動速度が上昇するが、敵の出現頻度が上がる",
            glyph = IconGlyph.Coin, glyphColor = new Color(1f, 0.82f, 0.25f),
            effects = new[] { (EffectType.MoveSpeed, 0.2f), (EffectType.EnemySpawnRate, 0.3f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };

        // Distance-unlock system Ver.0.1 test content - reuses the existing
        // ArrowUp glyph rather than adding new glyph-drawing code, since
        // this card only exists to verify the unlock pipeline end-to-end.
        // Swap its icon/description/effect any time real content replaces
        // it.
        //
        // Bugfix 2026-09-06, item "Card Unlockシステムを一元化" - this
        // card's 500m gate used to live ONLY in a separate UnlockManager/
        // UnlockDefinition entry (unlock_pathfinder_card_500m,
        // UnlockType.Card), completely independent of unlockDistance below
        // (which sat at 0, i.e. "always eligible") - so this card was
        // already drawable from the Gacha machine at 0m (BuildGachaPool
        // reads unlockDistance/gachaStage directly, never that separate
        // entry) while simultaneously excluded from the starter-deck-fill/
        // corrupted-deck-fallback pool until 500m (those read
        // CardDatabase.UnlockedCards, which DID check the separate entry) -
        // exactly the "two gates can disagree" risk the brief called out.
        // Now unified: unlockDistance carries the real 500m gate directly,
        // and CardDatabase.UnlockedCards reads this same field (see its own
        // comment) instead of the old separate entry. The
        // UnlockDefinition asset itself was deleted (see
        // UnlockDatabaseBuilder.Specs) rather than left as dead
        // configuration.
        yield return new Spec
        {
            id = "pathfinder", name = "PATHFINDER", sortOrder = 16, category = CardCategory.Growth,
            description = "遠くまで到達した証。経験値獲得量が少し増加する",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.3f, 0.85f, 0.75f),
            effects = new[] { (EffectType.ExpGain, 0.1f) },
            rarity = 2, unlockDistance = 500f, gachaStage = 1
        };

        // ===== Card Expansion/Gacha Evolution Ver.1 - new cards ===== //
        // ~68 new cards below. Per the brief's own explicit permission
        // ("最終Balanceを決めません...まず調整可能なParameterとして実装"),
        // every numeric value here is a placeholder, freely re-tunable in
        // the Inspector later. A NUMBER of these (elemental status,
        // true chain-lightning, HP-based conversion flavor, etc.) are
        // DELIBERATELY simplified onto the ~11 new but broadly-reusable
        // EffectTypes (see EffectType.cs's own Ver.1 section) rather than
        // bespoke new mechanics - each such card's comment says so
        // explicitly rather than silently pretending it's the full thing.
        int so = 100; // sortOrder counter for everything below, well past the existing 16

        // --- ★1 (1 new) --- //
        yield return new Spec
        {
            id = "more_enemies", name = "MORE ENEMIES", sortOrder = so++, category = CardCategory.Risk,
            description = "敵の出現頻度が上がる。倒すほど経験値/MILEも増える",
            glyph = IconGlyph.Coin, glyphColor = new Color(0.8f, 0.4f, 0.3f),
            effects = new[] { (EffectType.EnemySpawnRate, 0.15f) },
            rarity = 1, gachaStage = 1
        };

        // --- ★2 (9 new) --- //
        yield return new Spec
        {
            id = "brake_attack", name = "BRAKE ATTACK", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃後わずかに減速する代わりに、威力が上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.9f, 0.6f, 0.3f),
            effects = new[] { (EffectType.AttackPower, 10f), (EffectType.MoveSpeed, -0.05f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "first_strike", name = "FIRST STRIKE", sortOrder = so++, category = CardCategory.Attack,
            description = "コンボの1段目の威力が上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.8f, 0.3f),
            effects = new[] { (EffectType.FirstHitBonus, 20f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "executioner", name = "EXECUTIONER", sortOrder = so++, category = CardCategory.Risk,
            description = "敵撃破で得られるMILEが増加する",
            glyph = IconGlyph.Coin, glyphColor = new Color(0.7f, 0.2f, 0.2f),
            effects = new[] { (EffectType.MileGainMultiplier, 0.15f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "treasure_hunter", name = "TREASURE HUNTER", sortOrder = so++, category = CardCategory.Growth,
            description = "敵撃破で得られるMILEが増加する",
            glyph = IconGlyph.Coin, glyphColor = new Color(1f, 0.85f, 0.35f),
            effects = new[] { (EffectType.MileGainMultiplier, 0.2f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "combo_plus", name = "COMBO PLUS", sortOrder = so++, category = CardCategory.Attack,
            description = "コンボ最終段の威力が上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.6f, 0.5f),
            effects = new[] { (EffectType.ComboFinalStageBonus, 10f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "mob_killer", name = "MOB KILLER", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃力が上がり、敵撃破MILEも少し増加する",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.8f, 0.5f, 0.4f),
            effects = new[] { (EffectType.AttackPower, 10f), (EffectType.MileGainMultiplier, 0.1f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "ground_fighter", name = "GROUND FIGHTER", sortOrder = so++, category = CardCategory.Attack,
            description = "地上にいる間、攻撃力が上昇する",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.6f, 0.75f, 0.4f),
            effects = new[] { (EffectType.GroundAttackPower, 20f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "tough_enemies", name = "TOUGH ENEMIES", sortOrder = so++, category = CardCategory.Risk,
            description = "雑魚敵のHPが上がる代わりに、経験値/MILEが増加する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.6f, 0.4f, 0.4f),
            effects = new[] { (EffectType.EnemyHpMultiplier, 0.3f), (EffectType.MileGainMultiplier, 0.15f) },
            rarity = 2, gachaStage = 1
        };
        yield return new Spec
        {
            id = "fast_enemies", name = "FAST ENEMIES", sortOrder = so++, category = CardCategory.Risk,
            // Simplified - true per-species Enemy movement-speed scaling
            // deferred (EnemySpecialBehavior's speed fields are per-kind,
            // not centrally scalable yet); represented here via a higher
            // spawn tempo instead, which reads similarly as "enemies come
            // at you faster".
            description = "敵の行動が活発になる代わりに、経験値/MILEが増加する",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(0.9f, 0.5f, 0.5f),
            effects = new[] { (EffectType.EnemySpawnRate, 0.1f), (EffectType.MileGainMultiplier, 0.15f) },
            rarity = 2, gachaStage = 1
        };

        // --- ★3 (19 new) --- //
        yield return new Spec
        {
            id = "combo_edge", name = "COMBO EDGE", sortOrder = so++, category = CardCategory.Attack,
            description = "コンボ最終段の威力が大きく上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.5f, 0.3f),
            effects = new[] { (EffectType.ComboFinalStageBonus, 30f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "piercing_blade", name = "PIERCING BLADE", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃の間合いと威力が上がる",
            glyph = IconGlyph.Ring, glyphColor = new Color(0.8f, 0.6f, 1f),
            effects = new[] { (EffectType.AttackRange, 0.3f), (EffectType.AttackPower, 10f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "shockwave", name = "SHOCKWAVE", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃の間合いが大きく上がる",
            // アイコン素材追加(2026-09-11) - マスター提供の実アートへ差し
            // 替え(旧glyph手続き生成アイコンから)。glyph/glyphColorは
            // existingIconPath指定時は使われないが記録として残す。
            glyph = IconGlyph.Ring, glyphColor = new Color(0.9f, 0.7f, 0.3f),
            effects = new[] { (EffectType.AttackRange, 0.4f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "aerial_blade", name = "AERIAL BLADE", sortOrder = so++, category = CardCategory.Attack,
            description = "空中攻撃の威力が大きく上がる",
            glyph = IconGlyph.WingSword, glyphColor = new Color(0.5f, 0.8f, 1f),
            effects = new[] { (EffectType.AirAttackPower, 30f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "counter", name = "COUNTER", sortOrder = so++, category = CardCategory.Defense,
            // Simplified - a true "反撃時のみ強化" trigger is deferred;
            // represented as a flat Shield+Attack bonus for now.
            description = "被弾を1回防ぎ、攻撃力もわずかに上がる",
            glyph = IconGlyph.Shield, glyphColor = new Color(0.6f, 0.8f, 0.9f),
            effects = new[] { (EffectType.Shield, 1f), (EffectType.AttackPower, 10f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "last_stand", name = "LAST STAND", sortOrder = so++, category = CardCategory.Risk,
            description = "HPが減っているほど攻撃力が上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.9f, 0.3f, 0.3f),
            effects = new[] { (EffectType.LowHpAttackBonus, 40f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "overheal", name = "OVERHEAL", sortOrder = so++, category = CardCategory.Heal,
            description = "撃破時の回復量と発動確率が上がる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(0.9f, 0.4f, 0.5f),
            effects = new[] { (EffectType.LifestealAmount, 10f), (EffectType.LifestealChance, 0.1f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "blood_rush", name = "BLOOD RUSH", sortOrder = so++, category = CardCategory.Heal,
            description = "HPが減っているほど攻撃力が上がり、撃破時に回復しやすくなる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(0.8f, 0.2f, 0.3f),
            effects = new[] { (EffectType.LowHpAttackBonus, 20f), (EffectType.LifestealChance, 0.1f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "predator", name = "PREDATOR", sortOrder = so++, category = CardCategory.Heal,
            description = "撃破時に回復する確率が大きく上がる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(0.7f, 0.15f, 0.2f),
            effects = new[] { (EffectType.LifestealChance, 0.2f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "glass_cannon", name = "GLASS CANNON", sortOrder = so++, category = CardCategory.Risk,
            description = "攻撃力が大幅に上がるが、最大HPが下がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.3f, 0.5f),
            effects = new[] { (EffectType.AttackPower, 40f), (EffectType.MaxHp, -20f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "iron_will", name = "IRON WILL", sortOrder = so++, category = CardCategory.Defense,
            description = "最大HPが上がり、HP満タン時は攻撃力も上がる",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.7f, 0.75f, 0.85f),
            effects = new[] { (EffectType.MaxHp, 20f), (EffectType.FullHpAttackBonus, 20f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "long_haul", name = "LONG HAUL", sortOrder = so++, category = CardCategory.Growth,
            description = "経験値獲得量と最大HPがともに上がる",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.5f, 0.85f, 0.7f),
            effects = new[] { (EffectType.ExpGain, 0.15f), (EffectType.MaxHp, 10f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "boss_killer", name = "BOSS KILLER", sortOrder = so++, category = CardCategory.Attack,
            description = "Boss相手への攻撃力が上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.8f, 0.3f, 0.7f),
            effects = new[] { (EffectType.BossDamageBonus, 30f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "adrenaline", name = "ADRENALINE", sortOrder = so++, category = CardCategory.Risk,
            description = "HPが減っているほど攻撃力とテンポが上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(1f, 0.5f, 0.4f),
            effects = new[] { (EffectType.LowHpAttackBonus, 30f), (EffectType.AttackSpeed, 0.1f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "sky_runner", name = "SKY RUNNER", sortOrder = so++, category = CardCategory.Movement,
            description = "ジャンプ力と空中攻撃力がともに上がる",
            glyph = IconGlyph.WingSword, glyphColor = new Color(0.6f, 0.9f, 0.95f),
            effects = new[] { (EffectType.JumpPower, 0.15f), (EffectType.AirAttackPower, 20f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "monster_rush", name = "MONSTER RUSH", sortOrder = so++, category = CardCategory.Risk,
            description = "敵の出現頻度が大きく上がる代わりに、経験値が増加する",
            glyph = IconGlyph.Coin, glyphColor = new Color(0.75f, 0.35f, 0.6f),
            effects = new[] { (EffectType.EnemySpawnRate, 0.25f), (EffectType.ExpGain, 0.15f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "elite_enemies", name = "ELITE ENEMIES", sortOrder = so++, category = CardCategory.Risk,
            // Simplified - a true "確率で強化個体が混ざる" spawn variant is
            // deferred; represented as a flat HP/MILE boost across all
            // grunts instead.
            description = "雑魚敵のHPが大きく上がる代わりに、経験値/MILEが大きく増加する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.7f, 0.3f, 0.5f),
            effects = new[] { (EffectType.EnemyHpMultiplier, 0.5f), (EffectType.MileGainMultiplier, 0.3f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "horde", name = "HORDE", sortOrder = so++, category = CardCategory.Risk,
            // Simplified - true per-Formation enemy-count scaling deferred
            // (Formation SpawnPoints are hand-authored per shape); spawn
            // frequency raised instead, which reads similarly as "more
            // enemies at once" in practice.
            description = "敵の出現頻度が上がる",
            glyph = IconGlyph.Coin, glyphColor = new Color(0.65f, 0.4f, 0.55f),
            effects = new[] { (EffectType.EnemySpawnRate, 0.3f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "boss_challenge", name = "BOSS CHALLENGE", sortOrder = so++, category = CardCategory.Risk,
            description = "Bossが強化される代わりに、Boss討伐MILEが増加する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.55f, 0.3f, 0.65f),
            effects = new[] { (EffectType.BossHpMultiplier, 0.3f), (EffectType.BossMileGainMultiplier, 0.4f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };

        // --- ★4 (32 new) --- //
        yield return new Spec
        {
            id = "momentum", name = "MOMENTUM", sortOrder = so++, category = CardCategory.Movement,
            description = "走り続けて速度が上がるほど、攻撃力も上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(0.4f, 0.9f, 1f),
            effects = new[] { (EffectType.MomentumBonus, 30f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "second_wind", name = "SECOND WIND", sortOrder = so++, category = CardCategory.Heal,
            // Simplified - a true "一度だけ致死ダメージを耐える" mechanic is
            // deferred; represented as a stronger lifesteal proc instead.
            description = "撃破時の回復量が大きく上がる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(0.5f, 0.9f, 0.75f),
            effects = new[] { (EffectType.LifestealAmount, 20f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "chain_explosion", name = "CHAIN EXPLOSION", sortOrder = so++, category = CardCategory.Attack,
            // Simplified - true multi-enemy chain-hit propagation deferred;
            // represented as extra reach + a stronger final-combo hit.
            description = "攻撃の間合いが広がり、コンボ最終段の威力も上がる",
            glyph = IconGlyph.Ring, glyphColor = new Color(1f, 0.6f, 0.2f),
            effects = new[] { (EffectType.AttackRange, 0.3f), (EffectType.ComboFinalStageBonus, 20f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "thunder_strike", name = "THUNDER STRIKE", sortOrder = so++, category = CardCategory.Attack,
            // Element infusion placeholder - see ElementType's own comment;
            // no genuine chain-lightning mechanic yet, just a flat power
            // bonus tagged with the Thunder element for future use.
            description = "攻撃に雷属性を付与し、威力が上がる（属性効果は今後拡張予定）",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.9f, 0.3f),
            effects = new[] { (EffectType.AttackPower, 20f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Thunder
        };
        yield return new Spec
        {
            id = "level_break", name = "LEVEL BREAK", sortOrder = so++, category = CardCategory.Growth,
            description = "経験値獲得量が大きく上がる",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.6f, 0.95f, 0.6f),
            effects = new[] { (EffectType.ExpGain, 0.3f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "double_attack", name = "DOUBLE ATTACK", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃のテンポが大きく上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(1f, 0.75f, 0.3f),
            effects = new[] { (EffectType.AttackSpeed, 0.25f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "sonic_blade", name = "SONIC BLADE", sortOrder = so++, category = CardCategory.Attack,
            // Simplified - "高速時に斬撃波を発生" deferred to a flat
            // First-Hit + tempo bonus for now.
            description = "コンボ1段目の威力とテンポが上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(0.5f, 0.95f, 1f),
            effects = new[] { (EffectType.FirstHitBonus, 20f), (EffectType.AttackSpeed, 0.1f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "giant_slayer", name = "GIANT SLAYER", sortOrder = so++, category = CardCategory.Risk,
            description = "攻撃力が大幅に上がるが、攻撃テンポが下がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.9f, 0.4f, 0.2f),
            effects = new[] { (EffectType.AttackPower, 50f), (EffectType.AttackSpeed, -0.15f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "rapid_edge", name = "RAPID EDGE", sortOrder = so++, category = CardCategory.Attack,
            // Simplified - "連続攻撃で徐々にテンポ上昇" ramp deferred to a
            // flat tempo bonus for now.
            description = "攻撃テンポが上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(1f, 0.7f, 0.5f),
            effects = new[] { (EffectType.AttackSpeed, 0.2f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "long_blade", name = "LONG BLADE", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃の間合いが大幅に上がる",
            glyph = IconGlyph.Ring, glyphColor = new Color(0.85f, 0.65f, 0.35f),
            effects = new[] { (EffectType.AttackRange, 0.5f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "sky_master", name = "SKY MASTER", sortOrder = so++, category = CardCategory.Attack,
            description = "空中攻撃力とジャンプ力がともに上がる",
            glyph = IconGlyph.WingSword, glyphColor = new Color(0.45f, 0.75f, 1f),
            effects = new[] { (EffectType.AirAttackPower, 40f), (EffectType.JumpPower, 0.1f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "fortress", name = "FORTRESS", sortOrder = so++, category = CardCategory.Risk,
            description = "最大HPが大幅に上がるが、移動速度が下がる",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.55f, 0.6f, 0.7f),
            effects = new[] { (EffectType.MaxHp, 40f), (EffectType.MoveSpeed, -0.15f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "perfect_guard", name = "PERFECT GUARD", sortOrder = so++, category = CardCategory.Defense,
            // Simplified - "確率でShieldを消費しない" deferred to a flat
            // extra Shield charge for now.
            description = "Shield耐久回数が増える",
            glyph = IconGlyph.Shield, glyphColor = new Color(0.65f, 0.85f, 1f),
            effects = new[] { (EffectType.Shield, 2f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "experience_burst", name = "EXPERIENCE BURST", sortOrder = so++, category = CardCategory.Growth,
            description = "経験値獲得量が大幅に上がる",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.5f, 1f, 0.75f),
            effects = new[] { (EffectType.ExpGain, 0.35f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "blood_blade", name = "BLOOD BLADE", sortOrder = so++, category = CardCategory.Heal,
            // Simplified - "HP満タン時にVampire余剰回復を攻撃力へ変換"
            // deferred to a flat full-HP attack bonus for now.
            description = "HP満タン時、攻撃力が上がる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(0.8f, 0.25f, 0.35f),
            effects = new[] { (EffectType.FullHpAttackBonus, 30f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "berserk_drive", name = "BERSERK DRIVE", sortOrder = so++, category = CardCategory.Risk,
            description = "HPが減っているほど攻撃力が大きく上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.95f, 0.25f, 0.25f),
            effects = new[] { (EffectType.LowHpAttackBonus, 50f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "heavy_impact", name = "HEAVY IMPACT", sortOrder = so++, category = CardCategory.Attack,
            description = "地上攻撃力が大きく上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.7f, 0.6f, 0.4f),
            effects = new[] { (EffectType.GroundAttackPower, 40f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "air_dominion", name = "AIR DOMINION", sortOrder = so++, category = CardCategory.Attack,
            description = "空中攻撃力が上がり、地上攻撃力も少し上がる",
            glyph = IconGlyph.WingSword, glyphColor = new Color(0.55f, 0.85f, 0.95f),
            effects = new[] { (EffectType.AirAttackPower, 30f), (EffectType.GroundAttackPower, 10f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "combo_master", name = "COMBO MASTER", sortOrder = so++, category = CardCategory.Attack,
            description = "コンボ最終段の威力が大幅に上がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.55f, 0.2f),
            effects = new[] { (EffectType.ComboFinalStageBonus, 50f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "high_voltage", name = "HIGH VOLTAGE", sortOrder = so++, category = CardCategory.Attack,
            // Element infusion placeholder (see thunder_strike's own
            // comment) - "別の敵へChain" deferred.
            description = "雷属性の攻撃力がさらに上がる（属性効果は今後拡張予定）",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.95f, 0.4f),
            effects = new[] { (EffectType.AttackPower, 30f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Thunder
        };
        yield return new Spec
        {
            id = "overdrive", name = "OVERDRIVE", sortOrder = so++, category = CardCategory.Movement,
            description = "走り続けて速度が上がるほど、攻撃力が大きく上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(0.35f, 0.95f, 1f),
            effects = new[] { (EffectType.MomentumBonus, 40f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "reverse_gear", name = "REVERSE GEAR", sortOrder = so++, category = CardCategory.Risk,
            description = "移動速度を犠牲に、攻撃力を大きく高める",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.6f, 0.4f, 0.8f),
            effects = new[] { (EffectType.MoveSpeed, -0.15f), (EffectType.AttackPower, 30f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "heart_breaker", name = "HEART BREAKER", sortOrder = so++, category = CardCategory.Risk,
            description = "最大HPを犠牲に、攻撃力を大きく高める",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.85f, 0.3f, 0.4f),
            effects = new[] { (EffectType.MaxHp, -20f), (EffectType.AttackPower, 40f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "exp_converter", name = "EXP CONVERTER", sortOrder = so++, category = CardCategory.Risk,
            description = "経験値効率を犠牲に、MILE獲得量を高める",
            glyph = IconGlyph.Coin, glyphColor = new Color(0.7f, 0.85f, 0.4f),
            effects = new[] { (EffectType.ExpGain, -0.1f), (EffectType.MileGainMultiplier, 0.3f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "ground_zero", name = "GROUND ZERO", sortOrder = so++, category = CardCategory.Risk,
            description = "空中攻撃力を犠牲に、地上攻撃力を大幅に高める",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.55f, 0.4f, 0.3f),
            effects = new[] { (EffectType.AirAttackPower, -20f), (EffectType.GroundAttackPower, 50f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "skybound", name = "SKYBOUND", sortOrder = so++, category = CardCategory.Risk,
            description = "地上攻撃力を犠牲に、空中攻撃力を大幅に高める",
            glyph = IconGlyph.WingSword, glyphColor = new Color(0.5f, 0.7f, 1f),
            effects = new[] { (EffectType.GroundAttackPower, -20f), (EffectType.AirAttackPower, 50f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3
        };
        yield return new Spec
        {
            id = "flame_blade", name = "FLAME BLADE", sortOrder = so++, category = CardCategory.Attack,
            // Element infusion placeholder - "Hit後Burn継続Damage"
            // deferred, see ElementType's own comment.
            description = "攻撃に炎属性を付与し、威力が上がる（属性効果は今後拡張予定）",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.45f, 0.15f),
            effects = new[] { (EffectType.AttackPower, 20f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Fire
        };
        yield return new Spec
        {
            id = "frost_edge", name = "FROST EDGE", sortOrder = so++, category = CardCategory.Attack,
            // Element infusion placeholder - "Slow/Freeze" deferred.
            description = "攻撃に氷属性を付与する（Slow/Freeze効果は今後拡張予定）",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.55f, 0.85f, 1f),
            effects = new[] { (EffectType.AttackSpeed, 0.1f), (EffectType.AttackPower, 10f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Ice
        };
        yield return new Spec
        {
            id = "wind_cutter", name = "WIND CUTTER", sortOrder = so++, category = CardCategory.Attack,
            // Element infusion placeholder - "前方へWind Slash" deferred.
            description = "攻撃に風属性を付与し、間合いが広がる（属性効果は今後拡張予定）",
            glyph = IconGlyph.Ring, glyphColor = new Color(0.6f, 0.95f, 0.75f),
            effects = new[] { (EffectType.AttackRange, 0.35f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Wind
        };
        yield return new Spec
        {
            id = "hell_mode", name = "HELL MODE", sortOrder = so++, category = CardCategory.Risk,
            description = "敵が大幅に強化される代わりに、経験値/MILEが大幅に増加する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.5f, 0.15f, 0.2f),
            effects = new[]
            {
                (EffectType.EnemyHpMultiplier, 0.6f), (EffectType.EnemySpawnRate, 0.4f),
                (EffectType.MileGainMultiplier, 0.5f), (EffectType.BossMileGainMultiplier, 0.3f)
            },
            rarity = 4, unlockDistance = 50000f, gachaStage = 4
        };
        yield return new Spec
        {
            id = "boss_rush", name = "BOSS RUSH", sortOrder = so++, category = CardCategory.Risk,
            // Simplified - "Boss Gateで追加Bossが出現する可能性" deferred
            // (would need BossManager encounter-composition changes);
            // represented as a Boss HP/reward boost for now.
            description = "Boss戦が強化される代わりに、Boss討伐MILEが大きく増加する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.4f, 0.2f, 0.5f),
            effects = new[] { (EffectType.BossHpMultiplier, 0.4f), (EffectType.BossMileGainMultiplier, 0.5f) },
            rarity = 4, unlockDistance = 50000f, gachaStage = 4
        };
        yield return new Spec
        {
            id = "wanted", name = "WANTED", sortOrder = so++, category = CardCategory.Risk,
            // Simplified - "Chaser/Rusher出現率UP" deferred (would need
            // DistanceTierManager Formation-weight changes); represented as
            // a general Kill MILE boost for now.
            description = "追跡者・急襲者が出やすくなる代わりに、敵撃破MILEが増加する",
            glyph = IconGlyph.Coin, glyphColor = new Color(0.6f, 0.3f, 0.3f),
            effects = new[] { (EffectType.MileGainMultiplier, 0.35f) },
            rarity = 4, unlockDistance = 50000f, gachaStage = 4
        };

        // --- ★5 (7 new) --- //
        yield return new Spec
        {
            id = "phoenix", name = "PHOENIX", sortOrder = so++, category = CardCategory.Heal,
            description = "撃破時の回復確率と回復量がともに大きく上がる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(1f, 0.55f, 0.25f),
            effects = new[] { (EffectType.LifestealChance, 0.3f), (EffectType.LifestealAmount, 20f) },
            rarity = 5, unlockDistance = 50000f, gachaStage = 4
        };
        yield return new Spec
        {
            id = "ultimate", name = "ULTIMATE", sortOrder = so++, category = CardCategory.Special,
            description = "攻撃力・最大HP・移動速度がまとめて上がる",
            glyph = IconGlyph.Ring, glyphColor = new Color(1f, 0.85f, 0.3f),
            effects = new[] { (EffectType.AttackPower, 30f), (EffectType.MaxHp, 20f), (EffectType.MoveSpeed, 0.1f) },
            rarity = 5, unlockDistance = 50000f, gachaStage = 4
        };
        yield return new Spec
        {
            id = "one_more_mile", name = "ONE MORE MILE", sortOrder = so++, category = CardCategory.Special,
            description = "MILE獲得量と経験値獲得量がまとめて大きく上がる",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.5f, 0.75f, 1f),
            effects = new[]
            {
                (EffectType.MileGainMultiplier, 0.5f), (EffectType.BossMileGainMultiplier, 0.5f), (EffectType.ExpGain, 0.2f)
            },
            rarity = 5, unlockDistance = 100000f, gachaStage = 5
        };
        yield return new Spec
        {
            id = "deaths_contract", name = "DEATH'S CONTRACT", sortOrder = so++, category = CardCategory.Risk,
            description = "攻撃力が大幅に上がるが、最大HPが大きく下がる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(0.4f, 0.1f, 0.5f),
            effects = new[] { (EffectType.AttackPower, 60f), (EffectType.MaxHp, -30f) },
            rarity = 5, unlockDistance = 100000f, gachaStage = 5
        };
        yield return new Spec
        {
            id = "no_turning_back", name = "NO TURNING BACK", sortOrder = so++, category = CardCategory.Risk,
            description = "移動速度が大きく上がるが、敵の出現頻度も上がる",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(0.5f, 0.2f, 0.6f),
            effects = new[] { (EffectType.MoveSpeed, 0.25f), (EffectType.EnemySpawnRate, 0.3f) },
            rarity = 5, unlockDistance = 100000f, gachaStage = 5
        };
        yield return new Spec
        {
            id = "the_long_road", name = "THE LONG ROAD", sortOrder = so++, category = CardCategory.Growth,
            description = "経験値獲得量と最大HPがともに大きく上がる",
            glyph = IconGlyph.ArrowUp, glyphColor = new Color(0.6f, 0.5f, 0.9f),
            effects = new[] { (EffectType.ExpGain, 0.4f), (EffectType.MaxHp, 20f) },
            rarity = 5, unlockDistance = 100000f, gachaStage = 5
        };
        yield return new Spec
        {
            id = "pandemonium", name = "PANDEMONIUM", sortOrder = so++, category = CardCategory.Risk,
            // Simplified - "通常のDistance制限を越えて高難度Enemy/Formation
            // が出現可能" deferred (would need DistanceTierManager tier-
            // gating changes); represented as a strong all-around
            // difficulty/reward multiplier instead.
            description = "敵とBossが大幅に強化される代わりに、報酬が大幅に増加する",
            glyph = IconGlyph.ArmorPlate, glyphColor = new Color(0.3f, 0.1f, 0.4f),
            effects = new[]
            {
                (EffectType.EnemyHpMultiplier, 0.8f), (EffectType.EnemySpawnRate, 0.6f),
                (EffectType.MileGainMultiplier, 0.6f), (EffectType.BossMileGainMultiplier, 0.6f), (EffectType.BossHpMultiplier, 0.5f)
            },
            rarity = 5, unlockDistance = 100000f, gachaStage = 5
        };

        // --- 追加カードアイコン素材(2026-09-11)、★3 (7 new) --- //
        // マスターから提供された「OneMoreMile追加カード10種」のアイコン
        // シート(1枚の画像に10個、白い角丸パネル単位で切り出し済み -
        // scratchpad/cardicons以下で処理)のうち、既存カードと名前が重な
        // る3枚(LONG BLADE/SHOCKWAVE/MOMENTUM、いずれも上のSpecsに既存)
        // は新規カードにせず、それぞれのSpecへexistingIconPathを足して
        // アイコンだけ実アートへ差し替える(上記Build()のicon同期ロジック
        // 参照)。残り7枚は、既存EffectTypeの組み合わせだけで表現できる
        // 新規カードとしてここに追加(新しいコードは一切不要 - クラス
        // 冒頭コメントの設計方針どおり)。数値/rarityは同じ帯の既存カード
        // (piercing_blade/shockwave/aerial_blade/counter/last_standなど、
        // ★3=単発または2効果程度)に揃えた。
        yield return new Spec
        {
            id = "ground_breaker", name = "GROUND BREAKER", sortOrder = so++, category = CardCategory.Attack,
            description = "地上での攻撃力が大きく上がる",
            effects = new[] { (EffectType.GroundAttackPower, 40f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "flame_counter", name = "FLAME COUNTER", sortOrder = so++, category = CardCategory.Defense,
            // Simplified - 既存の"counter"と同じく、真の「反撃時のみ強化」
            // トリガーは未実装。被弾防止+反撃火力アップという固定効果で
            // 表現。
            description = "被弾を1回防ぎ、反撃の威力も上がる",
            effects = new[] { (EffectType.Shield, 1f), (EffectType.AttackPower, 20f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "combo_rush", name = "COMBO RUSH", sortOrder = so++, category = CardCategory.Attack,
            description = "攻撃のテンポが上がり、コンボ最終段の威力も上がる",
            effects = new[] { (EffectType.AttackSpeed, 0.15f), (EffectType.ComboFinalStageBonus, 20f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "air_strike", name = "AIR STRIKE", sortOrder = so++, category = CardCategory.Attack,
            description = "空中攻撃の威力が大きく上がる",
            effects = new[] { (EffectType.AirAttackPower, 40f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "last_chance", name = "LAST CHANCE", sortOrder = so++, category = CardCategory.Defense,
            // 天使の羽+光輪のアイコンに合わせ、「もう一度だけ救われる」
            // 守護のイメージをShield(被弾1回無効)+回復量アップで表現。
            description = "被弾を1回防ぎ、撃破時の回復量も少し上がる",
            effects = new[] { (EffectType.Shield, 1f), (EffectType.LifestealAmount, 10f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "close_call", name = "CLOSE CALL", sortOrder = so++, category = CardCategory.Movement,
            // 迫る影から素早く逃げ切るイメージ - 移動速度アップ+被弾1回
            // 無効。LAST CHANCE(Shield+回復)とは違う軸(Shield+速度)で
            // 差別化。
            description = "移動速度が上がり、被弾も1回防ぐ",
            effects = new[] { (EffectType.MoveSpeed, 0.1f), (EffectType.Shield, 1f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };
        yield return new Spec
        {
            id = "hunter", name = "HUNTER", sortOrder = so++, category = CardCategory.Attack,
            // スコープに入った髑髏 = 大物(Boss)を狙い撃つイメージ。既存
            // "boss_killer"(BossDamageBonus+3)と同系統だが、こちらは
            // アート専用の別カードとして少し強めに設定。
            description = "ボスへの攻撃力が大きく上がる",
            effects = new[] { (EffectType.BossDamageBonus, 40f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2
        };

        // --- カードバランス v3(2026-10-03): 属性カード8枚(#92〜#99)。効果の値は Editor/CardBalanceV3.cs が入れる --- //
        yield return new Spec { id = "burning_soul", name = "BURNING SOUL", sortOrder = so++, category = CardCategory.Attack, description = "炎上が長く、強くなる",
            glyph = IconGlyph.Droplet, glyphColor = new Color(1f, 0.45f, 0.15f), effects = new[] { (EffectType.BurnPowerPct, 0.08f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2, element = ElementType.Fire };
        yield return new Spec { id = "inferno", name = "INFERNO", sortOrder = so++, category = CardCategory.Attack, description = "燃えている敵を倒すと、周りへ炎が広がる",
            glyph = IconGlyph.Ring, glyphColor = new Color(1f, 0.3f, 0.1f), effects = new[] { (EffectType.InfernoLevel, 1f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Fire };
        yield return new Spec { id = "ice_prison", name = "ICE PRISON", sortOrder = so++, category = CardCategory.Attack, description = "冷気が溜まりやすくなり、凍結しやすくなる",
            glyph = IconGlyph.Shield, glyphColor = new Color(0.55f, 0.85f, 1f), effects = new[] { (EffectType.FreezeThresholdReduce, 1f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2, element = ElementType.Ice };
        yield return new Spec { id = "absolute_zero", name = "ABSOLUTE ZERO", sortOrder = so++, category = CardCategory.Attack, description = "凍結した敵が砕け、冷えた敵への攻撃が強くなる",
            glyph = IconGlyph.Ring, glyphColor = new Color(0.8f, 0.95f, 1f), effects = new[] { (EffectType.AbsoluteZeroLevel, 1f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Ice };
        yield return new Spec { id = "chain_lightning", name = "CHAIN LIGHTNING", sortOrder = so++, category = CardCategory.Attack, description = "雷が近くの別の敵へ連鎖する",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(1f, 0.95f, 0.35f), effects = new[] { (EffectType.LightningChains, 1f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2, element = ElementType.Thunder };
        yield return new Spec { id = "thunder_lord", name = "THUNDER LORD", sortOrder = so++, category = CardCategory.Attack, description = "雷の連鎖・範囲・威力が強くなる",
            glyph = IconGlyph.CrossSwords, glyphColor = new Color(1f, 0.85f, 0.2f), effects = new[] { (EffectType.LightningDamagePct, 0.06f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Thunder };
        yield return new Spec { id = "gale", name = "GALE", sortOrder = so++, category = CardCategory.Attack, description = "風刃が速く、遠くへ、多くの敵を貫く",
            glyph = IconGlyph.SpeedLines, glyphColor = new Color(0.6f, 1f, 0.8f), effects = new[] { (EffectType.WindSpeedPct, 0.05f) },
            rarity = 3, unlockDistance = 5000f, gachaStage = 2, element = ElementType.Wind };
        yield return new Spec { id = "tornado", name = "TORNADO", sortOrder = so++, category = CardCategory.Attack, description = "風刃が当たった所に小さな竜巻が起きる",
            glyph = IconGlyph.Ring, glyphColor = new Color(0.5f, 0.95f, 0.75f), effects = new[] { (EffectType.TornadoLevel, 1f) },
            rarity = 4, unlockDistance = 20000f, gachaStage = 3, element = ElementType.Wind };
    }

    // アイコン素材追加(2026-09-11) - existingIconPathで指定された実アート
    // PNGを、SceneBuilder.LoadIconTexture(private)と同じ設定
    // (TextureImporterType.Default、mipmap無効、alphaIsTransparency、
    // alphaSource=FromInput)で明示的にimport設定してから読み込む。
    // 以前はAssetDatabase.LoadAssetAtPathを素通しで呼んでいたため、
    // 新規追加したPNGがプロジェクト設定次第でSprite等として誤importされ、
    // Texture2Dとして正しく取得できない/見た目が壊れるおそれがあった。
    static Texture2D LoadIconTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // --- Procedural placeholder icon generation -----------------------
    // A rounded-square backdrop tinted per-glyph, with a simple geometric
    // glyph drawn on top so the 9 new cards are visually distinct from
    // each other (and from the 6 legacy hand-drawn icons) even before real
    // art exists. Swap CardDefinition.icon in the Inspector to replace.

    const int IconSize = 256;

    static Texture2D GenerateIcon(string cardId, IconGlyph glyph, Color glyphColor)
    {
        var px = new Color[IconSize * IconSize];
        Color bg = new Color(glyphColor.r, glyphColor.g, glyphColor.b, 0.22f);
        Vector2 center = new Vector2(IconSize / 2f, IconSize / 2f);

        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                px[y * IconSize + x] = InRoundedRect(p, center, IconSize * 0.42f, IconSize * 0.42f, 36f)
                    ? bg
                    : new Color(0f, 0f, 0f, 0f);
            }
        }

        DrawGlyph(px, glyph, glyphColor, center);

        var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();

        if (!AssetDatabase.IsValidFolder(GeneratedIconFolder))
        {
            Directory.CreateDirectory(GeneratedIconFolder);
            AssetDatabase.Refresh();
        }

        string path = $"{GeneratedIconFolder}/Icon_{cardId}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void DrawGlyph(Color[] px, IconGlyph glyph, Color color, Vector2 c)
    {
        switch (glyph)
        {
            case IconGlyph.Ring:
                FillRing(px, c, 78f, 58f, color);
                break;

            case IconGlyph.SpeedLines:
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 offset = new Vector2(i * 34f, -i * 8f);
                    DrawThickLine(px, c + offset + new Vector2(-55f, 0f), c + offset + new Vector2(55f, 0f), 16f, color);
                }
                break;

            case IconGlyph.WingSword:
                FillCircle(px, c + new Vector2(0f, 45f), 30f, color);
                FillTriangle(px,
                    c + new Vector2(0f, 20f),
                    c + new Vector2(-38f, -60f),
                    c + new Vector2(38f, -60f),
                    color);
                break;

            case IconGlyph.Shield:
                FillRoundedRect(px, c + new Vector2(0f, 15f), 62f, 46f, 18f, color);
                FillTriangle(px,
                    c + new Vector2(-62f, 15f),
                    c + new Vector2(62f, 15f),
                    c + new Vector2(0f, -70f),
                    color);
                break;

            case IconGlyph.ArrowUp:
                DrawThickLine(px, c + new Vector2(0f, 65f), c + new Vector2(0f, -55f), 20f, color);
                DrawThickLine(px, c + new Vector2(0f, 65f), c + new Vector2(-42f, 15f), 20f, color);
                DrawThickLine(px, c + new Vector2(0f, 65f), c + new Vector2(42f, 15f), 20f, color);
                break;

            case IconGlyph.Droplet:
                FillCircle(px, c + new Vector2(0f, -20f), 42f, color);
                FillTriangle(px,
                    c + new Vector2(-34f, -10f),
                    c + new Vector2(34f, -10f),
                    c + new Vector2(0f, 70f),
                    color);
                break;

            case IconGlyph.CrossSwords:
                DrawThickLine(px, c + new Vector2(-55f, -55f), c + new Vector2(55f, 55f), 18f, color);
                DrawThickLine(px, c + new Vector2(-55f, 55f), c + new Vector2(55f, -55f), 18f, color);
                break;

            case IconGlyph.ArmorPlate:
                FillRoundedRect(px, c, 68f, 68f, 14f, color);
                FillRoundedRect(px, c, 30f, 30f, 8f, new Color(0f, 0f, 0f, 0.35f));
                break;

            case IconGlyph.Coin:
                FillCircle(px, c, 68f, color);
                FillRing(px, c, 46f, 34f, new Color(0f, 0f, 0f, 0.3f));
                break;
        }
    }

    static void SetPixelSafe(Color[] px, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= IconSize || y >= IconSize) return;
        // Simple over-blend so overlapping strokes/backgrounds look clean
        // rather than one hard-replacing the other.
        Color existing = px[y * IconSize + x];
        px[y * IconSize + x] = Color.Lerp(existing, color, color.a);
    }

    static void FillCircle(Color[] px, Vector2 c, float radius, Color color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(c.x - radius));
        int maxX = Mathf.Min(IconSize - 1, Mathf.CeilToInt(c.x + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(c.y - radius));
        int maxY = Mathf.Min(IconSize - 1, Mathf.CeilToInt(c.y + radius));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) <= radius) SetPixelSafe(px, x, y, color);
            }
        }
    }

    static void FillRing(Color[] px, Vector2 c, float outerRadius, float innerRadius, Color color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(c.x - outerRadius));
        int maxX = Mathf.Min(IconSize - 1, Mathf.CeilToInt(c.x + outerRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(c.y - outerRadius));
        int maxY = Mathf.Min(IconSize - 1, Mathf.CeilToInt(c.y + outerRadius));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                if (d <= outerRadius && d >= innerRadius) SetPixelSafe(px, x, y, color);
            }
        }
    }

    static void FillTriangle(Color[] px, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        float minXf = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float maxXf = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float minYf = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        float maxYf = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
        int minX = Mathf.Max(0, Mathf.FloorToInt(minXf));
        int maxX = Mathf.Min(IconSize - 1, Mathf.CeilToInt(maxXf));
        int minY = Mathf.Max(0, Mathf.FloorToInt(minYf));
        int maxY = Mathf.Min(IconSize - 1, Mathf.CeilToInt(maxYf));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (PointInTriangle(p, a, b, c)) SetPixelSafe(px, x, y, color);
            }
        }
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b);
        float d2 = Cross(p, b, c);
        float d3 = Cross(p, c, a);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    static float Cross(Vector2 p1, Vector2 p2, Vector2 p3) =>
        (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

    static void DrawThickLine(Color[] px, Vector2 a, Vector2 b, float thickness, Color color)
    {
        float minXf = Mathf.Min(a.x, b.x) - thickness;
        float maxXf = Mathf.Max(a.x, b.x) + thickness;
        float minYf = Mathf.Min(a.y, b.y) - thickness;
        float maxYf = Mathf.Max(a.y, b.y) + thickness;
        int minX = Mathf.Max(0, Mathf.FloorToInt(minXf));
        int maxX = Mathf.Min(IconSize - 1, Mathf.CeilToInt(maxXf));
        int minY = Mathf.Max(0, Mathf.FloorToInt(minYf));
        int maxY = Mathf.Min(IconSize - 1, Mathf.CeilToInt(maxYf));

        Vector2 ab = b - a;
        float abSqr = Mathf.Max(0.0001f, ab.sqrMagnitude);
        float half = thickness * 0.5f;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abSqr);
                Vector2 proj = a + ab * t;
                if (Vector2.Distance(p, proj) <= half) SetPixelSafe(px, x, y, color);
            }
        }
    }

    static void FillRoundedRect(Color[] px, Vector2 c, float halfW, float halfH, float radius, Color color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(c.x - halfW));
        int maxX = Mathf.Min(IconSize - 1, Mathf.CeilToInt(c.x + halfW));
        int minY = Mathf.Max(0, Mathf.FloorToInt(c.y - halfH));
        int maxY = Mathf.Min(IconSize - 1, Mathf.CeilToInt(c.y + halfH));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                if (InRoundedRect(p, c, halfW, halfH, radius)) SetPixelSafe(px, x, y, color);
            }
        }
    }

    // Standard rounded-rect signed-distance test: true when p is inside a
    // halfW x halfH box (centered at c) with corners rounded by `radius`.
    static bool InRoundedRect(Vector2 p, Vector2 c, float halfW, float halfH, float radius)
    {
        Vector2 d = new Vector2(Mathf.Abs(p.x - c.x) - (halfW - radius), Mathf.Abs(p.y - c.y) - (halfH - radius));
        float outsideX = Mathf.Max(d.x, 0f);
        float outsideY = Mathf.Max(d.y, 0f);
        float outsideDist = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
        float insideDist = Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        return outsideDist + insideDist - radius <= 0f;
    }
}
