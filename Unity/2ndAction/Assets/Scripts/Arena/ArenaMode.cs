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

    public static void Begin() { Active = true; Invincible = Config.invincible; }
    public static void End() { Active = false; Invincible = false; BattleRunning = false; }

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
    public int assistMode = 1;           // 0 = OFF / 1 = 高速時のみ / 2 = 常時
    public float assistEngageKmh = 100f;
    public bool assistBreakObstacles = true, assistEarlyDoubleJump = true;
    public bool invincible = false;
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
    public string label = "";
}
