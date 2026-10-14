using UnityEngine;

// CPU サポート(2026-10-14): 本物の PlayerController を「仲間」として動かす。キャラ固有の攻撃/ジャンプ/回避をそのまま使うため、
// 仲間専用の別の操作系は作らない。仲間の間だけ次のことを切り替える(この端末のプレイヤーは今までと同じ):
//  ・Instance を奪わない(PlayerController.Instance は常にこの端末のプレイヤー)
//  ・入力はタッチ/パッドではなく CpuAlly(頭脳)の判断。距離の報告/脱出/画面の表示(OnGUI)をしない
//  ・体のタグは "Ally"(敵/ボス/障害物の「Player に当たったら被弾」に入らない)。被弾は CpuAlly が自分で調べて TakeDamage へ
//  ・攻撃判定のタグは "AllyAttack"(敵側はこのタグを見ない)。AllyAttackRouter が当たりを見て、他の参加者の攻撃の入口へ流す
//  ・HP は GameManager.Lives ではなく CpuAlly の HP。倒れてもランは終わらない(一定時間後に自動で復帰)
//  ・画面全体に効くもの(HitStop/カメラの揺れ/振動/効果音)を出さない
public partial class PlayerController
{
    public const string AllyBodyTag = "Ally";
    public const string AllyAttackTag = "AllyAttack";

    // 仲間を作る間だけ true(Awake で Instance を奪わない)
    public static bool SpawningAlly;
    public bool IsAlly { get; private set; }
    public CpuAlly Ally { get; private set; }
    public void BindAlly(CpuAlly a) { Ally = a; }

    // 仲間が動いている間(Update/LateUpdate)は効果音を鳴らさない(AudioManager.PlayVoice が見る)
    public static int AllyContext;

    // HP(仲間は自分の HP。プレイヤーは GameManager.Lives)。カードの条件(満タン/瀕死)と攻撃力の計算が使う
    public int HpNow => IsAlly ? (Ally != null ? Ally.Hp : 1) : (GameManager.Instance != null ? GameManager.Instance.Lives : 1);
    public int HpMax => IsAlly ? (Ally != null ? Ally.MaxHp : 1) : (GameManager.Instance != null ? GameManager.Instance.maxLives : 1);
    int SealedHeartsNow => IsAlly ? 0 : (GameManager.Instance != null ? GameManager.Instance.SealedHearts : 0);

    // 攻撃の持ち主の番号(他の参加者の攻撃の入口へ渡す)。0 = この端末のプレイヤー
    public int AttackerId => IsAlly && Ally != null ? Ally.AttackerId : 0;

    // 弾の高さ(補助が「浮いたボスの下を弾が素通りするか」を見る)。持ち主ごと
    public float ShotYOffset { get; private set; } = -1f;

    // 攻撃の物(弾/爆風/範囲)に持ち主を付ける。owner が仲間なら "AllyAttack" + AllyAttackRouter、そうでなければ従来の "PlayerAttack"
    public static void TagAttackObject(GameObject go, PlayerController owner)
    {
        if (go == null) return;
        if (owner != null && owner.IsAlly)
        {
            go.tag = AllyAttackTag;
            AllyAttackRouter.Attach(go, owner);
        }
        else go.tag = "PlayerAttack";
    }

    // 仲間の体の中の攻撃判定(元からある子の判定)を仲間用にする
    public void RetagAllyHitboxes()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (t.CompareTag("PlayerAttack")) TagAttackObject(t.gameObject, this);
            else if (t.CompareTag("Player")) t.gameObject.tag = AllyBodyTag;
        }
        gameObject.tag = AllyBodyTag;
    }

    // 復帰/追いつき: 指定の X の地面へ置き直す(攻撃/のけぞり/速度を消す)
    public bool AllyWarpTo(float x, bool upper)
    {
        var tm = TerrainManager.Instance;
        if (tm == null) return false;
        CancelAttacksForReaction();
        velocityY = 0f; knockbackTimer = 0f; lungeVelocityX = 0f; moveSlowTimer = 0f;
        Reaction = ReactionKind.None; reactionTimer = 0f;
        float? sky = upper ? tm.GetSkyHeightAt(x, transform.position.y + 5f) : null;
        float? gy = sky ?? tm.GetHeightAt(x);
        if (!gy.HasValue) return false;
        onSky = sky.HasValue;
        isGrounded = true;
        jumpsUsed = 0;
        transform.position = new Vector3(x, gy.Value + groundOffset, transform.position.z);
        if (sr != null) sr.enabled = true;
        return true;
    }

    public bool AllyOnSky => onSky;
    public SpriteRenderer VisualRenderer => sr; // 体の絵(識別用オーラが写しを作る)
    public bool AllyIsAttacking => isAttacking;

    // 倒れた / 復帰(CO-OP の DOWN と同じ見た目・止まり方)
    public void AllyDown() => NetEnterDown(false);
    public void AllyRevive() { NetRevive(); }

    // 仲間が始めたコルーチンの中も「仲間の処理中」にする(効果音を鳴らさない/演出を控えめに)。
    // PlayerController の中の StartCoroutine はすべてここを通る(名前の隠蔽)。プレイヤーは今までと同じ
    public new Coroutine StartCoroutine(System.Collections.IEnumerator routine)
        => IsAlly ? base.StartCoroutine(AllyWrap(routine)) : base.StartCoroutine(routine);

    System.Collections.IEnumerator AllyWrap(System.Collections.IEnumerator e)
    {
        while (true)
        {
            bool more; object cur = null;
            AllyContext++;
            try { more = e.MoveNext(); if (more) cur = e.Current; }
            finally { AllyContext--; }
            if (!more) yield break;
            yield return cur is System.Collections.IEnumerator sub ? AllyWrap(sub) : cur;
        }
    }

    // ラン開始の基準(自然加速の距離)をプレイヤーにそろえる
    public void AllyMatchStart(PlayerController leader)
    {
        if (leader == null) return;
        startX = leader.startX;
    }
}
