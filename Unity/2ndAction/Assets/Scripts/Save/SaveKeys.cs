using System.Collections.Generic;

// セーブデータの全項目(2026-10-01)。保存先はすべて PlayerPrefs(Android=アプリ専用のXML / Windows=レジストリ)。
// PlayerPrefs はキーの一覧を取れないので、ここに全キーを登録しておく(バックアップ/初期化/検査がこの表を使う)。
// 新しい保存項目を足したら、必ずここへ追加すること(分類を間違えると製品版移行の初期化で消えたり残ったりする)。
//
//  Progress … ゲーム進行。製品版への移行(releaseGeneration)で初期化する。
//  Settings … 設定。製品版への移行でも残す。
//  Dev      … 開発版だけの値(リリース版では読まない)。製品版への移行で消す。
//  Meta     … セーブ自体の管理情報(形式の版/リリース世代)。
public enum SaveCategory { Progress, Settings, Dev, Meta }
public enum SaveType { Int, Float, String }

public static class SaveKeys
{
    public struct Entry
    {
        public string key; public SaveCategory cat; public SaveType type; public string note;
        public Entry(string k, SaveCategory c, SaveType t, string n) { key = k; cat = c; type = t; note = n; }
    }

    // ---- Meta ----
    public const string SchemaVersion = "SaveSchemaVersion";
    public const string ReleaseGeneration = "SaveReleaseGeneration";

    // ---- 今回追加した進行 ----
    public const string LifetimeDistance = "LifetimeDistance";          // ゲーム全体の累計走行距離(m、double文字列)
    public const string ReaperMetPrefix = "ReaperMet_";                // + Eldest / Second / Youngest (int 0/1)
    public const string FinalDungeonUnlocked = "FinalDungeonUnlocked"; // int 0/1(一度1になったら戻さない)
    public const string BossSeen = "BossSeenV1";
    public const string ArenaConfig = "ArenaConfigV1";                 // 闘技場の最後の構成(JSON。練習の設定なので「設定」。デッキ/所持には書かない)
    public const string SprintGates = "SprintGatesV1";                 // 疾走出発の解放: マップごとに実際に倒した 10,000m 刻みの門番(2026-10-05)
    public const string DevSprintUnlockAll = "Dev.SprintUnlockAll";    // 開発版だけ: 疾走出発の行き先を全部選べる                       // 会ったボス(カンマ区切りの "Wild/Wolf" 等)。ラスダンの抽選で製品版が優先する(2026-10-05)
    public const string DevFinalDungeonAlwaysOpen = "Dev.FinalDungeonAlwaysOpen"; // 開発版だけ: ラスダンを常に選べる(既定1)

    // 既存のキー(保存しているクラスの定数と同じ文字列)
    public static readonly Entry[] All =
    {
        new Entry(SchemaVersion, SaveCategory.Meta, SaveType.Int, "セーブ形式の版(アプリのversionCodeとは無関係)"),
        new Entry(ReleaseGeneration, SaveCategory.Meta, SaveType.Int, "リリース世代(0=開発版 / 1=製品版)"),

        // ---- 進行 ----
        new Entry("OwnedCardsV1", SaveCategory.Progress, SaveType.String, "所持カード(JSON: cardId(能力一式のv2|キー)/Lv/枚数)"),
        new Entry("NewUnconfirmedCardsV1", SaveCategory.Progress, SaveType.String, "NEW表示の未確認カード(カンマ区切り)"),
        new Entry("CardMasteryV1", SaveCategory.Progress, SaveType.String, "カード長期育成(JSON: カードID/★0〜5/進み/★5後の保管/AWAKENED/Lv9到達の記録)。所持Lvとは別(2026-10-04)"),
        new Entry("CardDataFormat", SaveCategory.Progress, SaveType.Int, "カードデータの旧形式→能力一式形式の変換済み印"),
        new Entry("DeckCardIds", SaveCategory.Progress, SaveType.String, "デッキ(カンマ区切り、最大10)"),
        new Entry("CharacterCardSlots", SaveCategory.Progress, SaveType.String, "(旧)全キャラ共通のキャラカード枠。2026-10-02以降は起動時に選択中のキャラの枠へ移して消す"),
        new Entry("TotalOwnedMile", SaveCategory.Progress, SaveType.Int, "所持MILE(通貨)"),
        new Entry("BestDistance", SaveCategory.Progress, SaveType.Float, "全体の最高距離(ガチャの段階/解放の判定)"),
        new Entry("BestTime", SaveCategory.Progress, SaveType.Float, "最高記録の時間"),
        new Entry("BestDistance_legacyBackup", SaveCategory.Progress, SaveType.Float, "旧・共通BESTの退避(マップ別BEST導入時)"),
        new Entry("UnlockedIds", SaveCategory.Progress, SaveType.String, "距離で解放した敵/カード/エリア(カンマ区切り)"),
        new Entry("ActiveRunCheckpointV1", SaveCategory.Progress, SaveType.String, "中断中のラン(CONTINUE用のJSON)"),
        new Entry("SelectedCharacterId", SaveCategory.Progress, SaveType.String, "選択中のキャラ"),
        new Entry("SelectedStageId", SaveCategory.Progress, SaveType.String, "選択中のステージ"),
        new Entry(LifetimeDistance, SaveCategory.Progress, SaveType.String, "累計走行距離(ラスダン解放の条件)"),
        new Entry(ReaperMetPrefix + "Eldest", SaveCategory.Progress, SaveType.Int, "死神三姉妹 長女と遭遇"),
        new Entry(ReaperMetPrefix + "Second", SaveCategory.Progress, SaveType.Int, "死神三姉妹 次女と遭遇"),
        new Entry(ReaperMetPrefix + "Youngest", SaveCategory.Progress, SaveType.Int, "死神三姉妹 三女と遭遇"),
        new Entry(FinalDungeonUnlocked, SaveCategory.Progress, SaveType.Int, "ラスダン(LAST CORRIDOR)解放"),
        new Entry(BossSeen, SaveCategory.Progress, SaveType.String, "会ったボス(カンマ区切り。ラスダンの節目のボスの抽選に使う)"),
        new Entry(SprintGates, SaveCategory.Progress, SaveType.String, "疾走出発の解放: マップごとに実際に倒した10,000m刻みの門番"),
        new Entry(DevSprintUnlockAll, SaveCategory.Dev, SaveType.Int, "開発版: 疾走出発の行き先を全部選べる"),

        // ---- 設定 ----
        new Entry(ArenaConfig, SaveCategory.Settings, SaveType.String, "闘技場の最後の構成(キャラ/試用のビルド/相手/速度/操作アシスト/無敵)。進行ではない"),
        new Entry("MasterVolume", SaveCategory.Settings, SaveType.Float, "全体音量 0〜1"),
        new Entry("BgmVolume", SaveCategory.Settings, SaveType.Float, "BGM音量 0〜1"),
        new Entry("SfxVolume", SaveCategory.Settings, SaveType.Float, "SE音量 0〜1"),
        new Entry("EnvVolume", SaveCategory.Settings, SaveType.Float, "環境音量 0〜1"),
        new Entry("AudioMuted", SaveCategory.Settings, SaveType.Int, "消音"),
        new Entry("ScreenShakeEnabled", SaveCategory.Settings, SaveType.Int, "画面揺れ"),
        new Entry("GlowIntensity", SaveCategory.Settings, SaveType.Float, "発光演出の強さ 0〜1"),
        new Entry("HighSpeedAssistEnabled", SaveCategory.Settings, SaveType.Int, "高速時の自動操作補助"),
        new Entry("HighSpeedAssistEngageKmh", SaveCategory.Settings, SaveType.Float, "補助が始まる速度"),
        new Entry("PreferredOrientation", SaveCategory.Settings, SaveType.Int, "画面の向き"),
        new Entry("net.lastHostIp", SaveCategory.Settings, SaveType.String, "マルチ: 最後に接続したHOSTのIP"),
        new Entry("net.lastPort", SaveCategory.Settings, SaveType.Int, "マルチ: 最後のポート"),
        new Entry("net.mode", SaveCategory.Settings, SaveType.Int, "マルチ: CO-OP/VERSUS"),
        new Entry("net.roomName", SaveCategory.Settings, SaveType.String, "マルチ: LAN の部屋の名前(空なら「端末名's Room」、2026-10-05)"),

        // ---- 開発版のみ ----
        new Entry("InvincibleMode", SaveCategory.Dev, SaveType.Int, "無敵(開発版のみ)"),
        new Entry("DebugMode", SaveCategory.Dev, SaveType.Int, "DEBUGモード(開発版のみ)"),
        new Entry(BossHpPlan.PrefKey, SaveCategory.Dev, SaveType.Int, "ボスHPの再設計案 0=現行/15/20/25発(開発版のみ)"),
        new Entry(GachaStage.DevAllCardsOpenKey, SaveCategory.Dev, SaveType.Int, "全カード開放(開発版のみ)"),
        new Entry(DevFinalDungeonAlwaysOpen, SaveCategory.Dev, SaveType.Int, "ラスダンを常に選べる(開発版のみ)"),
        new Entry(GameManager.ResumeEaseDevKey, SaveCategory.Dev, SaveType.Int, "中断再開の慣らし 0=OFF/1=ON(開発版のみ、無ければGameManagerの設定)"),
    };

    // 旧形式(移行で読み替えた後に消す)
    public static readonly string[] Legacy = { "MasterVolumeLevel", "BgmVolumeLevel", "SfxVolumeLevel", "EnvVolumeLevel" };

    // 可変のキー: マップ別BEST(BestDistance_v2_<stageId>、double文字列)。ステージが増えても拾えるよう既知のIDとStageDatabaseを合わせる
    public const string StageBestPrefix = "BestDistance_v2_";
    static readonly string[] KnownStageIds = { "wasteland_road", "natural_cave", "sky_corridor", "last_corridor", "test_wasteland", "test_wasteland_pit" };
    public static IEnumerable<string> StageBestKeys()
    {
        var ids = new HashSet<string>(KnownStageIds);
        try { if (StageDatabase.AllStages != null) foreach (var s in StageDatabase.AllStages) if (s != null && !string.IsNullOrEmpty(s.stageId)) ids.Add(s.stageId); } catch { }
        foreach (var id in ids) yield return StageBestPrefix + id;
    }

    // 可変のキー: キャラごとのキャラカード枠(CharacterCardSlots.<characterId>、2026-10-02)
    static readonly string[] KnownCharacterIds = { "swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer", "archer", "mage", "fighter", "ninja", "miko", "vampire", "dragonkin" };
    public static IEnumerable<string> CharacterCardKeys()
    {
        var ids = new HashSet<string>(KnownCharacterIds);
        try { foreach (var c in CharacterDatabase.AllCharacters) if (c != null && !string.IsNullOrEmpty(c.characterId)) ids.Add(c.characterId); } catch { }
        foreach (var id in ids) yield return GameManager.CharacterCardSlotsPrefix + id;
    }

    // 開発版のカード調整パネル(CardTest.<key>.<0..2>)はキー数が多く、開発版専用なのでバックアップ/移行の対象外(完全初期化では残る)。

    public static IEnumerable<Entry> Expanded()
    {
        foreach (var e in All) yield return e;
        foreach (var k in StageBestKeys()) yield return new Entry(k, SaveCategory.Progress, SaveType.String, "マップ別BEST(double文字列)");
        foreach (var k in CharacterCardKeys()) yield return new Entry(k, SaveCategory.Progress, SaveType.String, "キャラごとのキャラカード枠(id:Lv,id:Lv,id:Lv)");
    }
}
