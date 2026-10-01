using System;
using System.Collections.Generic;
using UnityEngine;

// BONUS ZONE(2026-09-29) - 全ステージ共通のボーナス区画の調整データ(Resources/Encounters/BonusZone.asset)。
// 敵の配置はEncounter Directorの共通の仕組み(Formation/Slot/地形の確認)をそのまま使い、
// ここには「Bonusの種類 → 流すFormationの並び」「報酬量」「発生率/長さ」だけを置く。
[CreateAssetMenu(menuName = "OneMoreMile/Bonus Zone Profile", fileName = "BonusZone")]
public class BonusZoneProfile : ScriptableObject
{
    [Header("発生")]
    [Tooltip("ボス撃破(ボス報酬の選択まで終わった後)にBONUS ZONEが起きる確率")]
    [Range(0f, 1f)] public float chanceAfterBoss = 0.5f;
    [Tooltip("ボス区間が終わってから、この距離(m)の安全区間を走った後に始まる")]
    public float delayAfterBoss = 24f;
    [Tooltip("次のボスまでこの距離(m)より近ければ始めない(ボスと重ねない)")]
    public float minDistanceToNextBoss = 420f;
    [Tooltip("マルチ中も自然発生させるか(現状は報酬の同期が未対応なのでfalse)")]
    public bool allowInMultiplayer = false;

    [Header("長さ(どちらか先に来た方で終了)")]
    public float durationSeconds = 24f;
    public float durationDistance = 320f;
    [Tooltip("開始の表示(BONUS ZONE!)の長さ。走行は止めない")]
    public float introSeconds = 1.4f;
    [Tooltip("終了後、通常のEncounterを再開するまでの安全距離(m)")]
    public float postRestDistance = 40f;
    [Tooltip("BONUS RESULTの表示時間(秒)")]
    public float resultSeconds = 2.2f;

    [Tooltip("同時に居てよい報酬Enemyの数(超えている間は次のwaveを待つ。Android向けに大量Activeにしない)")]
    public int maxActiveEnemies = 7;

    [Header("Bonus Encounterの抽選")]
    public List<BonusEncounterType> types = new List<BonusEncounterType>();
    [Tooltip("JACKPOTが選ばれる確率(通常の抽選より先に判定)")]
    [Range(0f, 1f)] public float jackpotChance = 0.06f;
    public string jackpotTypeId = "jackpot";

    [Header("配置(EncounterDirectorの共通のFormation/敵の候補)")]
    public List<EncounterFormation> formations = new List<EncounterFormation>();
    public EncounterDistanceBand band = new EncounterDistanceBand { bandName = "BONUS" };

    [Header("報酬: Treasure Goblin")]
    public int goblinHitMile = 2;
    public int goblinKillMile = 15;
    [Header("報酬: Mimic")]
    public int mimicHitMile = 2;
    [Tooltip("上攻撃(打ち上げ)の倍率")] public float mimicLaunchMultiplier = 1.5f;
    [Tooltip("空中からの攻撃の倍率")] public float mimicAerialMultiplier = 1.25f;
    [Tooltip("下攻撃(叩きつけ)の倍率")] public float mimicSlamMultiplier = 2f;
    [Tooltip("1体から引き出せるMILEの上限(無限稼ぎ防止)")] public int mimicMaxMile = 80;
    [Tooltip("報酬が出るHit数の上限")] public int mimicMaxRewardHits = 45;
    public int mimicKillMile = 10;
    [Tooltip("起きてから逃げ出すまでの秒数")] public float mimicStaySeconds = 8f;
    [Header("報酬: Golden Slime")]
    [Tooltip("撃破1体の基本EXP")] public float goldenSlimeExp = 45f;
    [Tooltip("撃破1体ごとに、今のLvで次のLvまでに必要なEXPのこの割合を足す(どのLvでもEXP FEVERで約1回Level Upを狙える量にするため)")]
    public float goldenSlimeExpPerLevel = 0f;
    [Header("CLEAR / PERFECT(2026-10-01)")]
    [Tooltip("PERFECTの判定で、終了のこの秒数前より後に画面へ入った報酬Enemyは数えない(倒す時間が無いため)")]
    public float perfectGraceSeconds = 1.5f;
    [Tooltip("JACKPOTの開始表示の長さ(通常はintroSeconds)")]
    public float jackpotIntroSeconds = 2.2f;
    [Header("報酬: Card Fairy")]
    [Tooltip("撃破で出る確定Card Choiceの回数")] public int fairyCardChoices = 1;
    [Tooltip("これ以上は1回のBONUS ZONEで出さない")] public int maxFairiesPerZone = 2;
    [Tooltip("捕まらなければこの秒数で逃げ去る")] public float fairyEscapeSeconds = 9f;

    public BonusEncounterType FindType(string id)
    {
        foreach (var t in types) if (t != null && t.id == id) return t;
        return null;
    }

    public EncounterFormation FindFormation(string id)
    {
        foreach (var f in formations) if (f != null && f.formationId == id) return f;
        return null;
    }

    static BonusZoneProfile cached;
    public static BonusZoneProfile Load()
    {
        if (cached == null) cached = Resources.Load<BonusZoneProfile>("Encounters/BonusZone");
        return cached;
    }
}

[Serializable]
public class BonusEncounterType
{
    public string id = "";
    public string displayName = "";
    [Tooltip("通常の抽選の重み(JACKPOTは別枠)")] public float weight = 1f;
    [Tooltip("流すFormation(順番に、最後まで行ったら最初から)")] public List<string> waves = new List<string>();
    [Tooltip("Wave間の距離(m)")] public Vector2 waveGap = new Vector2(8f, 12f);
    [Tooltip("このBonus中のMILE報酬の倍率(敵/CLEAR/PERFECTすべて。JACKPOTの報酬倍率もここ)")] public float mileMultiplier = 1f;
    [Tooltip("このBonus中のEXP報酬の倍率(敵/CLEAR/PERFECTすべて)")] public float expMultiplier = 1f;

    [Header("BONUS CLEAR(最後まで走り切った報酬。敵の報酬とは別)")]
    public int clearMile = 0;
    public float clearExp = 0f;
    [Tooltip("今のLvで次のLvまでに必要なEXPのこの割合を追加(0.5=半レベル分)")] public float clearExpPerLevel = 0f;

    [Header("PERFECT BONUS(条件を満たした時だけ。Card Choiceは出さない)")]
    public int perfectMile = 0;
    public float perfectExp = 0f;
    public float perfectExpPerLevel = 0f;
    [Tooltip("撃破で数える報酬Enemyの種類(空なら撃破の条件なし)")] public List<BonusEnemyKind> perfectKinds = new List<BonusEnemyKind>();
    [Tooltip("画面に現れた対象のうち、倒した割合がこれ以上(1=全撃破)")] [Range(0f, 1f)] public float perfectKillRatio = 1f;
    [Tooltip("最低この数は倒す")] public int perfectMinKills = 0;
    [Tooltip(">0なら: Mimicから引き出したMILEがこれ以上、またはMimicを撃破")] public int perfectMimicMile = 0;
    [Tooltip("条件の短い説明(BONUS中の進行表示/結果に出す)")] public string perfectLabel = "";
    [Tooltip("開始の表示色")] public Color color = new Color(1f, 0.84f, 0.3f);
}
