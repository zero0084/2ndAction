// 戦闘数値のスケール(2026-10-02)。攻撃力/HP/ダメージ/回復量などの整数の戦闘値を、旧来の10倍の細かさで持つ。
// 旧: 攻撃力2 / HP(ハート)5 / ボスHP 360 → 新: 20 / 50 / 3600。TTK(何発で倒せるか)は変えない。
// 倍率・割合・回数系(移動/ジャンプ/攻撃時間/範囲/EXP/MILE/Drain確率/ジャンプ回数/Shield回数/出現/BREAK倍率)は10倍しない。
// 一覧は Docs/CombatScale.md。
public static class CombatScale
{
    public const int K = 10;
    // ハート1つ = 10HP(HPの表示もハート1つ=10で描く)
    public const int HpPerHeart = 10;
    // 敵/障害物/地形からの通常の被弾(旧1) と 強い一撃(旧2: ボスの必殺技/大技/巨大火球)
    public const int PlayerHit = 10;
    public const int PlayerHeavyHit = 20;
}
