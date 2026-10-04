using System.Collections.Generic;
using UnityEngine;

// ラストダンジョンのボス構成(2026-10-05)。Resources/Bosses/LastDungeonBossTuning(無ければコードの既定値)。
//  ・0〜89km の関門: 3マップ(荒野/洞窟/天空)の既存ボスを「格」ごとのプールから抽選する
//      1,000m格 = 各マップの1,000mのボス / 5,000m格 = 5,000mのボス / 10,000m格 = 各マップの10kmごとの専用ボス
//    製品版は「そのプレイヤーが実際に会った(戦った)ボス」を優先する(ProgressStats.BossSeen。足りない時は全部)。開発版は全部。
//    直近 recentExclude 体は外す(候補が足りない時だけ緩める)。強さは今の距離に合わせる(再戦と同じ距離のHP比+段階)。
//  ・90〜98km: 1,000mごとのボスラッシュ。1つの関門に複数のボス(同時/時間で増援/HPで増援/撃破で次)。
//    同時に出せるボスの数(maxSimultaneous)とタグの組み合わせ規則を超える時は、出す順番を待たせる(破綻する組み合わせを作らない)。
//  ・ラストダンジョンの倍率(HP/攻撃の頻度/移動速度/攻撃の間隔/被弾量)はここで変える。
[CreateAssetMenu(menuName = "OneMoreMile/Last Dungeon Boss Tuning")]
public class LastDungeonBossTuning : ScriptableObject
{
    [System.Flags]
    public enum Tag { None = 0, Ground = 1, Air = 2, Large = 4, Projectile = 8, Chaser = 16, AreaAttack = 32, NoRush = 64 }

    [Header("0〜89km の関門のプール")]
    public List<string> pool1k = new List<string> { "Wild/Wolf", "Cave/Centipede", "Sky/Dragon" };
    public List<string> pool5k = new List<string> { "Wild/GoblinRider", "Cave/Scorpion", "Sky/Majin" };
    public List<string> pool10k = new List<string>
    {
        "Wild/Serpent", "Wild/Cyclops", "Wild/Spider", "Wild/Golem", "Wild/Griffin", "Wild/Hydra", "Wild/Demon", "Wild/Dragon", "Wild/BlackKnight",
        "Cave/Mole", "Cave/Troll", "Cave/Worm", "Cave/CrystalGolem", "Cave/Bat", "Cave/ScorpionKing", "Cave/Basilisk", "Cave/Drake", "Cave/AncientDemon",
        "Sky/Behemoth", "Sky/Titan", "Sky/Jellyfish", "Sky/Leviathan", "Sky/Fenrir", "Sky/SkyGolem", "Sky/Phoenix", "Sky/SkySerpent", "Sky/Guardian",
    };
    [Tooltip("直近何体を抽選から外すか。候補が足りない時だけ緩める")]
    public int recentExclude = 3;
    [Tooltip("製品版: 会ったことのあるボスだけから選ぶ(会ったボスがこの数未満の格は全部から)")]
    public bool releasePreferSeen = true;
    public int minSeenCandidates = 2;
    [Tooltip("0〜89km の関門のボスは、時間内に倒せなければラン再開(BossBattleTuning と同じ)。ボスラッシュは常に倒すまで")]
    public bool milestoneRunResume = true;
    [Tooltip("ラン再開したボスが残ったまま、ボスラッシュの入口のこの距離(m)手前まで来たら、再び足止めする(ボスラッシュを飛ばさない)")]
    public float rushEntranceHoldMeters = 300f;

    [Header("ラストダンジョンの倍率(全関門)")]
    public float hpMul = 1.25f;
    [Tooltip("攻撃の頻度: 特殊攻撃/必殺技の間隔を割る(1.2 = 2割多く使う)")]
    public float attackFrequencyMul = 1.2f;
    [Tooltip("攻撃の間隔: 攻撃と攻撃の間の待ち時間に掛ける(0.85 = 15%短く)。予告(構え)の時間は変えない")]
    [Range(0.5f, 1.5f)] public float attackIntervalMul = 0.85f;
    [Tooltip("移動速度: 接近/間合いの移動/急降下の速さに掛ける")]
    [Range(0.5f, 1.5f)] public float moveSpeedMul = 1.1f;
    [Tooltip("Player への被弾量(再戦の段階の倍率に掛ける)")]
    public float damageMul = 1f;

    [Header("ボスラッシュ(90〜98km)")]
    [Tooltip("同時に戦うボスの上限(処理落ち対策。超える分は先のボスが倒れるまで待つ)")]
    [Range(1, 4)] public int maxSimultaneous = 3;
    [Tooltip("ボスラッシュのボス1体のHP(距離に合わせたHP×hpMulに、さらに掛ける。複数体なので1体は軽め)")]
    public float rushHpMulPerBoss = 0.3f;
    [Tooltip("ボスラッシュの被弾量(再戦の段階の代わり。複数体から同時に当たるので控えめ)")]
    public float rushDamageMul = 1.4f;
    [Tooltip("同時に居てよい数(タグごと)")]
    public int maxLarge = 2, maxAir = 2, maxAreaAttack = 1, maxProjectile = 2, maxChaser = 2;

    public enum Trigger { Start, Time, HpBelow, OnKill }
    [System.Serializable]
    public class Slot
    {
        public string key = "Wild/Wolf";
        public Trigger trigger = Trigger.Start;
        [Tooltip("Time: 関門の開始からの秒数 / HpBelow: 出ているボスの合計HPの割合(0〜1)")]
        public float value;
        public Slot() { }
        public Slot(string k, Trigger t = Trigger.Start, float v = 0f) { key = k; trigger = t; value = v; }
    }
    [System.Serializable]
    public class Variant
    {
        public string label = "";
        public List<Slot> slots = new List<Slot>();
    }
    [System.Serializable]
    public class RushGate
    {
        public int km = 90;
        [Tooltip("どれか1つを抽選(HOST/シングル)")]
        public List<Variant> variants = new List<Variant>();
    }
    public List<RushGate> rushGates = DefaultRush();

    [System.Serializable]
    public class TagEntry { public string key; public Tag tags; public TagEntry() { } public TagEntry(string k, Tag t) { key = k; tags = t; } }
    public List<TagEntry> tags = DefaultTags();

    public Tag TagsOf(string key)
    {
        if (tags != null) foreach (var t in tags) if (t != null && t.key == key) return t.tags;
        return Tag.Ground;
    }

    public RushGate RushAt(int km)
    {
        if (rushGates != null) foreach (var g in rushGates) if (g != null && g.km == km) return g;
        return null;
    }

    // ---- 既定値 ----
    static Variant V(string label, params Slot[] s) => new Variant { label = label, slots = new List<Slot>(s) };
    static Slot S(string k) => new Slot(k);
    static Slot T(string k, float sec) => new Slot(k, Trigger.Time, sec);
    static Slot H(string k, float hp) => new Slot(k, Trigger.HpBelow, hp);
    static Slot K(string k) => new Slot(k, Trigger.OnKill);

    // 90km 2体 / 91km 2体 / 92km 2〜3体 / 93km 3体 / 94km 3体 / 95km 3〜4体 / 96km 4体 / 97km 4体 / 98km 5体(最後)
    // 地上+空中の混成、時間/HP/撃破での増援。大型/範囲攻撃が重ならないようタグの規則でも抑える。
    public static List<RushGate> DefaultRush() => new List<RushGate>
    {
        new RushGate { km = 90, variants = { V("地上+空中 同時", S("Wild/Serpent"), S("Sky/Fenrir")), V("連続", S("Cave/Troll"), K("Wild/Griffin")) } },
        new RushGate { km = 91, variants = { V("時間で増援", S("Wild/Cyclops"), T("Cave/Bat", 8f)), V("HPで増援", S("Sky/Behemoth"), H("Cave/Basilisk", 0.6f)) } },
        new RushGate { km = 92, variants = { V("同時+撃破で次", S("Cave/Mole"), S("Wild/Griffin"), K("Sky/Titan")), V("時間で増援", S("Wild/Spider"), T("Sky/Jellyfish", 7f)) } },
        new RushGate { km = 93, variants = { V("ゴーレム", S("Wild/Golem"), H("Cave/CrystalGolem", 0.55f), K("Sky/SkyGolem")), V("混成", S("Cave/Worm"), S("Sky/Fenrir"), T("Wild/Hydra", 10f)) } },
        new RushGate { km = 94, variants = { V("地上2+空中", S("Cave/ScorpionKing"), S("Wild/Demon"), T("Sky/Phoenix", 9f)), V("撃破で次", S("Sky/Leviathan"), S("Cave/Troll"), K("Wild/Cyclops")) } },
        new RushGate { km = 95, variants = { V("4体", S("Wild/Hydra"), S("Cave/Drake"), H("Sky/Jellyfish", 0.6f), K("Wild/Serpent")), V("3体", S("Sky/SkySerpent"), T("Cave/Basilisk", 6f), K("Wild/Golem")) } },
        new RushGate { km = 96, variants = { V("嵐", S("Sky/SkySerpent"), S("Wild/Spider"), T("Wild/Demon", 8f), K("Cave/Bat")), V("巨体", S("Sky/Titan"), S("Cave/Mole"), H("Wild/Griffin", 0.5f), K("Cave/CrystalGolem")) } },
        new RushGate { km = 97, variants = { V("魔の4体", S("Cave/AncientDemon"), S("Sky/Guardian"), H("Wild/Demon", 0.55f), K("Sky/Fenrir")), V("竜と蛇", S("Cave/Drake"), S("Sky/Leviathan"), T("Wild/Hydra", 9f), K("Cave/ScorpionKing")) } },
        new RushGate { km = 98, variants = { V("最後の5連", S("Wild/BlackKnight"), S("Sky/Phoenix"), T("Cave/Basilisk", 8f), H("Sky/Guardian", 0.5f), K("Cave/AncientDemon")),
                                              V("最後の5連(別)", S("Sky/Guardian"), S("Wild/Griffin"), H("Cave/AncientDemon", 0.6f), T("Wild/BlackKnight", 12f), K("Sky/SkySerpent")) } },
    };

    // GROUND=地上で戦う / AIR=飛ぶ / LARGE=画面の大きな部分を占める / PROJECTILE=飛び道具が主 / CHASER=追いかけ回す / AREA_ATTACK=画面全体級の範囲攻撃
    // NoRush=ボスラッシュに入れない(竜/魔人は別の仕組みのボス = 撃破の数え方が違う)
    public static List<TagEntry> DefaultTags()
    {
        const Tag G = Tag.Ground, A = Tag.Air, L = Tag.Large, P = Tag.Projectile, C = Tag.Chaser, R = Tag.AreaAttack;
        return new List<TagEntry>
        {
            new TagEntry("Wild/Wolf", G | C), new TagEntry("Wild/GoblinRider", G | C), new TagEntry("Wild/Serpent", G | C),
            new TagEntry("Wild/Cyclops", G | L), new TagEntry("Wild/Spider", G | P), new TagEntry("Wild/Golem", G | L),
            new TagEntry("Wild/Griffin", A | C), new TagEntry("Wild/Hydra", G | L | P), new TagEntry("Wild/Demon", A | P),
            new TagEntry("Wild/Dragon", A | P | Tag.NoRush), new TagEntry("Wild/BlackKnight", G | C),
            new TagEntry("Cave/Centipede", G | C), new TagEntry("Cave/Scorpion", G), new TagEntry("Cave/Mole", G),
            new TagEntry("Cave/Troll", G | L), new TagEntry("Cave/Worm", G | L), new TagEntry("Cave/CrystalGolem", G | L | R),
            new TagEntry("Cave/Bat", A | C), new TagEntry("Cave/ScorpionKing", G | L), new TagEntry("Cave/Basilisk", G | P),
            new TagEntry("Cave/Drake", A | P), new TagEntry("Cave/AncientDemon", A | P | R),
            new TagEntry("Sky/Dragon", A | P | Tag.NoRush), new TagEntry("Sky/Majin", A | P | Tag.NoRush),
            new TagEntry("Sky/Behemoth", G | C | R), new TagEntry("Sky/Titan", L | R), new TagEntry("Sky/Jellyfish", A | R),
            new TagEntry("Sky/Leviathan", L | R), new TagEntry("Sky/Fenrir", A | C), new TagEntry("Sky/SkyGolem", L | P),
            new TagEntry("Sky/Phoenix", A | P), new TagEntry("Sky/SkySerpent", A | L | R), new TagEntry("Sky/Guardian", A | P | R),
        };
    }

    static LastDungeonBossTuning cached;
    static bool loaded;
    public static LastDungeonBossTuning I
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<LastDungeonBossTuning>("Bosses/LastDungeonBossTuning");
                if (cached == null) { cached = CreateInstance<LastDungeonBossTuning>(); cached.hideFlags = HideFlags.DontSave; }
                if (cached.rushGates == null || cached.rushGates.Count == 0) cached.rushGates = DefaultRush();
                if (cached.tags == null || cached.tags.Count == 0) cached.tags = DefaultTags();
            }
            return cached;
        }
    }
}
