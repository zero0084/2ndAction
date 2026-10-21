using UnityEngine;

// 2026-10-21 コンボのつながり改修: 1回のフリックがどうなったかを記録する(確認用)。
//  Immediate  = そのフレームで技が出た / 予約された(コンボの次の段など、技の状態が変わった)
//  Reserved   = 技の最中だったので覚えた(先行入力、1つだけ)
//  FiredLater = 覚えていた入力が、動ける瞬間に出た
//  Expired    = 覚えていた入力が、出ないまま時間切れで消えた
//  Replaced   = 覚えていた入力が、新しい入力に置き換えられた(予約は1つだけ)
//  Cleared    = 被弾/停止/カード選択などで捨てた
//  Ignored    = 動ける時だったが、何も起きなかった(地上の↓など)
//  LandBuffered = ↑を着地の予約(地上の打ち上げ)へ回した
//  Merged     = 同じ向きの次の段が既に予約されていたので、重ねなかった(予約は1つだけ)
//  ゲームの動きは変えない。QA が QaFlickFate で受け取って、技が出たか/当たったかと組み合わせる。
public partial class PlayerController
{
    public enum FlickFate { Immediate, Reserved, FiredLater, Expired, Replaced, Cleared, Ignored, LandBuffered, Merged }

    int flickTraceSeq;    // この端末の手動の入力の通し番号
    int bufferedFlickId;  // 覚えている入力の番号(0 = なし)

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static System.Action<int, FlickDirection, FlickFate> QaFlickFate;
#endif
    // 自動テスト用: 1回だけの入力(読んだら消える)。debugInjectFlick(毎フレーム)と違い、本物の1回のフリックと同じ。
    // ヒットストップの間に入れても、本物の指と同じく先行入力として覚える(読むのは開発版だけ)
    [System.NonSerialized] public FlickDirection? debugInjectOnce;

    void TraceFlick(int id, FlickDirection dir, FlickFate fate)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (id > 0 && !IsAlly) QaFlickFate?.Invoke(id, dir, fate);
#endif
    }

    int NextFlickId() => ++flickTraceSeq;
    int fireJumpSerial;   // FireJump の回数(ジャンプ/空中の上攻撃/再打ち上げ。ActSig で「受け付けた」と分かるように)

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 確認用: 技が始まった回数(フレームに1回だけ数える)。QA は毎フレーム読み、増えた時の技を「その入力の技」とする
    int moveStarts, msFrame = -1;
    ActSig msPrev; bool msValid;
    public int DebugMoveStarts
    {
        get
        {
            if (msFrame != Time.frameCount)
            {
                msFrame = Time.frameCount;
                var s = CurrentSig();
                if (msValid && MoveStarted(msPrev, s)) moveStarts++;
                msPrev = s; msValid = true;
            }
            return moveStarts;
        }
    }
    static bool MoveStarted(ActSig a, ActSig b) =>
        b.comboCount > a.comboCount || b.kitGen != a.kitGen || b.lanceGen != a.lanceGen || b.jumpSerial != a.jumpSerial ||
        (b.dive && !a.dive) || (b.kitDive && !a.kitDive) || (b.lanceDive && !a.lanceDive) || (b.hover && !a.hover) ||
        b.hoverShots > a.hoverShots || (b.attacking && !a.attacking) || b.mageLevel != a.mageLevel;
#endif
}
