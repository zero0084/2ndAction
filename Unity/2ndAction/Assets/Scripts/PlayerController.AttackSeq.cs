using UnityEngine;

// 初撃 / 連撃中 / 締め(First / Combo / Finisher)の定義を全キャラで統一(2026-10-03)。
// 以前は「連撃の何段目か(comboCount)」を命中した瞬間に読んでいたため、次の不揃いがあった。
//   ・連撃の段を数えないキャラ(弓/魔法/忍者/巫女)には一切乗らない
//   ・連撃が1段のお嬢様騎士は毎回 First と Finisher の両方が乗る
//   ・竜騎士は3段突きなのに連撃の長さが2で、2段目と3段目の両方が Finisher。石突きも毎回 First
//   ・上攻撃/急降下/着地などの連撃以外の技が、直前の連撃の段(古い値)のボーナスを受ける
//   ・飛び道具は「当たった時」のプレイヤーの段で決まる(撃った後に別の技を出すと変わる)
// 今の定義:
//   攻撃シーケンス = そのキャラの「主攻撃」(前/後の攻撃)を続けて出したもの。
//     連撃のあるキャラ(黒剣士/双剣士/拳銃士/竜騎士/格闘/吸血鬼/竜人): 連撃の1段目 = First、最後の段 = Finisher、間 = Combo
//     主攻撃が単発のキャラ(お嬢様騎士/弓/魔法/忍者/巫女): 1.2秒以内に続けて出した3回を1つのシーケンスとして数える
//       (1回目 = First、2回目 = Combo、3回目 = Finisher。4回目からは新しいシーケンス)。First と Finisher は同じ攻撃に同時に付かない
//   主攻撃以外の技(上/下/空中の特殊技/急降下/着地/爆発/結界/風刃など)= None(初撃/締めのボーナスは付かない)
//   印は「技を出した時」に判定(近接の箱 / 飛び道具)へ付け、命中した時はその印で決める(PlayerAttackInfo.seqTag)。
// ボーナスの値(FIRST STRIKE/COMBO PLUS 等)は変えていない。
public enum AttackSeqTag : byte { None, First, Combo, Finisher }

public partial class PlayerController
{
    public const int SingleAttackSequenceLength = 3;
    public const float SingleAttackSequenceReset = 1.2f;

    AttackSeqTag currentSeqTag;
    int singleSeqCount;
    float singleSeqLast = -99f;

    // 今出している技の印(判定/飛び道具は作られた時にこれを写す)
    public AttackSeqTag CurrentSeqTag => currentSeqTag;
    public static int SeqTagged; // 確認用

    int currentSeqMoveId;
    public int CurrentSeqMoveId => currentSeqMoveId;
    static int seqMoveCounter;
    void SetSeqTag(AttackSeqTag tag)
    {
        currentSeqTag = tag;
        currentSeqMoveId = tag != AttackSeqTag.None ? ++seqMoveCounter : 0;
        if (tag != AttackSeqTag.None) SeqTagged++;
    }

    // One bonus per press per enemy: a volley (several ofuda/shuriken) or a box that touches the same enemy twice
    // does not stack the First/Finisher bonus on that enemy.
    readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<Component>> seqBonusGiven = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<Component>>();
    public int ConsumeSeqBonus(AttackSeqTag tag, int moveId, Component victim)
    {
        if (tag == AttackSeqTag.None || moveId == 0 || victim == null) return 0;
        if (!seqBonusGiven.TryGetValue(moveId, out var set))
        {
            if (seqBonusGiven.Count > 32) seqBonusGiven.Clear(); // old presses
            set = new System.Collections.Generic.HashSet<Component>();
            seqBonusGiven[moveId] = set;
        }
        if (!set.Add(victim)) return 0;
        return SeqBonus(tag);
    }

    // 連撃の段から(連撃の長さが1以下なら単発のシーケンスとして数える)
    AttackSeqTag ChainTag(int stage, int chainLength)
    {
        if (chainLength <= 1) return NextSingleTag();
        if (stage <= 1) return AttackSeqTag.First;
        return stage >= chainLength ? AttackSeqTag.Finisher : AttackSeqTag.Combo;
    }

    AttackSeqTag NextSingleTag()
    {
        if (Time.time - singleSeqLast > SingleAttackSequenceReset || singleSeqCount >= SingleAttackSequenceLength) singleSeqCount = 0;
        singleSeqCount++;
        singleSeqLast = Time.time;
        if (singleSeqCount == 1) return AttackSeqTag.First;
        return singleSeqCount >= SingleAttackSequenceLength ? AttackSeqTag.Finisher : AttackSeqTag.Combo;
    }

    // 印ごとのボーナス(竜騎士は突きの段の倍率も掛かる = 以前と同じ扱い)
    public int SeqBonus(AttackSeqTag tag)
    {
        int b = tag == AttackSeqTag.First ? FirstHitBonus : tag == AttackSeqTag.Finisher ? ComboFinalStageBonus : 0;
        if (b != 0 && isLancerCharacter && lanceDamageScale != 1f) b = Mathf.RoundToInt(b * lanceDamageScale);
        return b;
    }

    // 判定に今の印を付ける
    void TagHitbox(Component hitbox)
    {
        if (hitbox == null) return;
        var info = hitbox.GetComponent<PlayerAttackInfo>();
        if (info != null) { info.seqTag = currentSeqTag; info.seqMoveId = currentSeqMoveId; }
    }
}
