using System.Collections.Generic;
using UnityEngine;

// ===== 開発用の闘技場(2026-10-04) =====
// キャラ・カード・敵を好きな条件で戦わせ、同じ条件ですぐ再戦し、計測する試験場。開発版(Editor / Development Build)だけで開ける
// (入口: DEBUG → 闘技場。起動/再戦/退出は ArenaController / EndgameDebug.Arena)。このファイルは通常のコードから呼ばれる入口だけで、
// リリース版では Active が常に false(何も変わらない)。
//  ・試験は DEBUG RUN(BEST / MILE / カード / 通貨 / 累計距離 / 解放 / CONTINUE を保存しない。終わると開始前の値へ戻す)
//  ・距離は「距離条件」のまま止める(敵の強さの計算にだけ使う。ボスの関門/BONUS/死神/解放/EXP は起きない)
//  ・速度: 0 = 自動前進だけを止める(時間/攻撃/ジャンプ/重力/敵は通常どおり) / 基準速度(× カード・キャラの補正) / 実効速度固定
//  ・無敵は闘技場だけの設定(通常の無敵の設定は変えない)。本来受けるはずだったダメージも別に数える
public static class ArenaMode
{
    public static bool Active { get; private set; }
    public static bool Invincible;
    public static bool FixedSpeed => Active && Config.speedMode == 2;

    public static ArenaConfig Config = new ArenaConfig();
    public static ArenaResult Current = new ArenaResult();
    public static ArenaResult Previous;
    public static bool BattleRunning;        // 計測中(敵を出した〜全滅/手動終了/倒れた)

    // 起動の読み直しの前から(ランの開始で CONTINUE 等を書かないように)進行の書き込みを止める
    public static bool PendingStart { get; private set; }
    public static void BeginPending() { PendingStart = true; }
    public static void Begin() { Active = true; PendingStart = false; Invincible = Config.invincible; }
    public static void End() { Active = false; PendingStart = false; Invincible = false; BattleRunning = false; }

    // ---- 計測の入口(通常のコードから) ----
    public static void OnEnemyDamaged(int damage, bool killed)
    {
        if (!Active || !BattleRunning || damage <= 0) return;
        Current.dealt += damage; Current.hits++;
        if (killed) Current.kills++;
    }
    public static void OnPlayerWouldTakeDamage(int amount, string reason)
    {
        if (!Active || !BattleRunning) return;
        Current.wouldTake += Mathf.Max(1, amount); Current.wouldHits++;
    }
    public static void OnPlayerHpLost(int amount)
    {
        if (!Active || !BattleRunning || amount <= 0) return;
        Current.taken += amount;
    }
    public static void OnPlayerDefeated(string reason)
    {
        if (!Active) return;
        Current.defeated = true; Current.defeatReason = reason;
        Defeated?.Invoke();
    }
    public static System.Action Defeated;

    // 闘技場のボタン(画面の左)で始まったタッチは操作にしない(ArenaController が場所を入れる)
    public static readonly List<Rect> BlockRects = new List<Rect>();
    public static bool BlocksPointer(Vector2 screenPosYUp)
    {
        if (!Active || BlockRects.Count == 0) return false;
        var p = new Vector2(screenPosYUp.x, Screen.height - screenPosYUp.y);
        foreach (var r in BlockRects) if (r.Contains(p)) return true;
        return false;
    }
}

[System.Serializable]
public class ArenaConfig
{
    public string character = "swordsman";
    public List<ArenaBuildEntry> build = new List<ArenaBuildEntry>();
    public int speedMode = 1;            // 0 = 停止 / 1 = 基準速度(カード・キャラの補正あり) / 2 = 実効速度固定
    public float kmh = 0f;               // 0 なら停止(自動前進だけ止める)
    public float distance = 3000f;       // 距離条件(敵の強さ)
    public List<ArenaEnemyEntry> enemies = new List<ArenaEnemyEntry> { new ArenaEnemyEntry { kind = ArenaEnemyKind.Dummy, count = 1, ahead = 5f } };
    public bool seedFixed = true;
    public int seed = 12345;
    public int assistMode = 1;           // 0 = OFF / 1 = ON(通常の設定と同じ: 判定速度以上で働く) / 2 = 常時(開発版だけ)
    public float assistEngageKmh = 100f;
    public bool assistBreakObstacles = true, assistEarlyDoubleJump = true;
    public bool invincible = false;
    public bool devAllEnemies;           // 開発版だけ: 未遭遇の敵/ボスも選べる

    // 初めての時: 今選んでいるキャラ・カードなし・動かない標的・0km/h・無敵OFF・操作アシストは通常の設定と同じ
    public static bool IsBoss(ArenaEnemyEntry e) => e != null && (e.kind == ArenaEnemyKind.WildBoss || e.kind == ArenaEnemyKind.CaveBoss || e.kind == ArenaEnemyKind.SkyBoss);

    public static ArenaConfig Fresh()
    {
        var c = new ArenaConfig { speedMode = 0, kmh = 0f, invincible = false };
        var gm = GameManager.Instance;
        if (gm != null && !string.IsNullOrEmpty(gm.SelectedCharacterId)) c.character = gm.SelectedCharacterId;
        var a = HighSpeedAssist.Instance;
        if (a != null) { c.assistMode = a.AssistEnabledSetting ? 1 : 0; c.assistEngageKmh = a.EngageKmhSetting; }
        return c;
    }

    // 読み込んだ構成を正す(消えたキャラ/カード/敵、範囲外の値、通常版で選べない物)
    public ArenaConfig Sanitized()
    {
        if (CharacterDatabase.FindById(character) == null || !UnlockRules.IsCharacterVisible(character)) character = GameManager.Instance != null ? GameManager.Instance.SelectedCharacterId : "swordsman";
        if (build == null) build = new List<ArenaBuildEntry>();
        build.RemoveAll(b => b == null || string.IsNullOrEmpty(b.key) || CardDatabase.FindById(b.key) == null);
        foreach (var b in build) b.times = Mathf.Clamp(b.times, 1, GameManager.MaxRunCardLevel);
        if (enemies == null) enemies = new List<ArenaEnemyEntry>();
        if (!Debug.isDebugBuild) devAllEnemies = false;
        enemies.RemoveAll(e => e == null || (e.kind == ArenaEnemyKind.Enemy && EnemyDatabase.FindById(e.enemyId) == null) || !ArenaCatalog.EntryKnown(e, devAllEnemies)); // 保存した構成からも未遭遇の相手は外す
        int bosses = 0; enemies.RemoveAll(e => IsBoss(e) && ++bosses > 1); // ボスは1種類まで
        if (enemies.Count == 0) enemies.Add(new ArenaEnemyEntry { kind = ArenaEnemyKind.Dummy, count = 1, ahead = 5f });
        speedMode = Mathf.Clamp(speedMode, 0, 2);
        kmh = Mathf.Clamp(kmh, 0f, 300f);
        distance = Mathf.Clamp(distance, 0f, 99000f);
        if (!Debug.isDebugBuild) { if (assistMode == 2) assistMode = 1; devAllEnemies = false; }
        assistEngageKmh = Mathf.Clamp(assistEngageKmh, HighSpeedAssist.MinEngageKmh, HighSpeedAssist.MaxEngageKmh);
        return this;
    }
}

[System.Serializable]
public class ArenaBuildEntry
{
    public string key;      // 素のカードID / 所持カードのコピー(v2|… のキー)
    public int times = 1;   // 取得回数(素のカードはラン中のLv。能力ごとに最大9)
    public bool owned;      // 所持カードのコピー(合成Lv・能力一式はキーが持つ。1回の取得で全能力)
}

public enum ArenaEnemyKind { Enemy, WildBoss, CaveBoss, SkyBoss, Dummy }

[System.Serializable]
public class ArenaEnemyEntry
{
    public ArenaEnemyKind kind;
    public string enemyId = "goblin";
    public int bossKind;
    public int count = 1;
    public float ahead = 8f;     // プレイヤーの前方(m)
    public float spacing = 2.5f; // 2体目以降の間隔(m)
    public int tier = 1;         // 雑魚の AI Tier(T0〜T4)
}

public class ArenaResult
{
    public long dealt; public int hits, kills, spawned;
    public int taken, wouldTake, wouldHits;
    public float time, clearTime = -1f;
    public bool defeated, ended; public string defeatReason = "";
    public bool clearedByHand;  // 「敵を全削除」で消した(撃破の時間には数えない。計測はそのまま続く)
    public string label = "";
    public string build = "";
}
