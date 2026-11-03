using UnityEditor;

// ラン中に同じキャラの体が伸び縮みして見える件(2026-11-03、エステルと確認)。一部の動作の絵だけ、人物そのものが走りの絵より
// 一回り大きく/小さく描かれていた(描いた時の縮尺の差。PPU で合わせきれていなかった)。頭・肩・胴体が走りの絵と同じ大きさになる PPU を
// 絵ごとに決めてここに書く。取り込みのたびにこの値へそろえる(CharacterDatabaseBuilder / SceneBuilder / MotionLinkImporter が
// 元の値を書いても、最後はこの表の値になる。値は倍率ではなく PPU そのものなので、何度取り込み直しても同じ)。
// 足もと(ピボット)は変えないので、地面の位置・攻撃判定(コライダー)・技の時間は変わらない。見た目の大きさだけ。
// 確認: scratchpad の bs/trial.py(走りの絵の隣に倍率を変えて並べる)・-qaMotion の前後の動画。元の値 → 新しい値(倍率)
public class PlayerArtScaleFix : AssetPostprocessor
{
    static readonly (string prefix, float ppu)[] Table =
    {
        ("Assets/Art/PlayerDownAttackLand_v1/downattackland_00", 590f), // 黒剣士 下攻撃の着地: 354 → 590(×0.60、約1.7倍に大きかった)
        ("Assets/Art/NobleLadyHurt_v1/", 236.5f),                       // お嬢様 被弾: 272 → 236.5(×1.15)
        ("Assets/Art/NobleLadyLaunch_v1/", 361f),                       // お嬢様 上へ斬り上げ(ジャンプの始め): 307 → 361(×0.85)
        ("Assets/Art/NobleLadyAir_v1/", 382.6f),                        // お嬢様 空中の薙ぎ払い: 440 → 382.6(×1.15)
        ("Assets/Art/NobleLadyPlunge_v1/", 382.6f),                     // お嬢様 急降下: 440 → 382.6(×1.15)
        ("Assets/Art/GunslingerLand_v1/", 602.5f),                      // 拳銃士 着地: 482 → 602.5(×0.80)
        ("Assets/Art/GunslingerHurt_v1/", 478f),                        // 拳銃士 被弾: 550 → 478(×1.15)
        ("Assets/Art/MotionLink/archer/airdraw_", 486f),                // 弓兵 空中で引く: 583 → 486(×1.20)
        ("Assets/Art/MotionLink/archer/airrel_", 486f),                 // 弓兵 空中で放つ: 583 → 486(×1.20)
        ("Assets/Art/ArcherPoses_v1/", 648f),                           // 弓兵 地上の射撃(引く/放つ/上/下/低い): 583 → 648(×0.90)
        ("Assets/Art/MikoLand_v1/", 507f),                              // 巫女 着地: 583 → 507(×1.15)
        ("Assets/Art/VampireLand_v1/", 530f),                           // 吸血鬼 着地: 583 → 530(×1.10)
        ("Assets/Art/NinjaHurt_v1/", 530f),                             // 忍者 被弾: 583 → 530(×1.10)
        ("Assets/Art/FighterPoses_v1/divekick/", 530f),                 // 格闘家 急降下の蹴り: 583 → 530(×1.10)
        // 第2弾(2026-11-03、二段ジャンプ/技の切り替え/倒れる・起き上がるの確認で見つかった分)
        ("Assets/Art/NobleLadyDoubleJump_v1/", 740f),                   // お嬢様 二段ジャンプ(剣を掲げる): 1220.5 → 740(×1.65、約0.6倍に小さかった)
        ("Assets/Art/NobleLadyDeath_v1/", 236.5f),                      // お嬢様 倒れる: 272 → 236.5(×1.15、被弾と同じ)
        ("Assets/Art/LancerRun_v1/", 463f),                             // 竜騎士 走り: 417 → 463(×0.90、走りだけ他の全部の絵より約1.1倍大きかった)
        ("Assets/Art/ArcherJump_v1/", 486f),                            // 弓兵 ジャンプ: 583 → 486(×1.20)
        ("Assets/Art/ArcherJumpRise_v1/", 486f),                        // 弓兵 ジャンプの始め/二段ジャンプ: 583 → 486(×1.20)
        ("Assets/Art/ArcherLand_v1/", 507f),                            // 弓兵 着地: 583 → 507(×1.15)
        ("Assets/Art/ArcherDeath_v1/", 530f),                           // 弓兵 倒れる: 583 → 530(×1.10)
        ("Assets/Art/NinjaJumpRise_v1/", 507f),                         // 忍者 ジャンプの始め/二段ジャンプ: 583 → 507(×1.15)
        ("Assets/Art/NinjaDeath_v1/", 507f),                            // 忍者 倒れる: 583 → 507(×1.15)
        ("Assets/Art/MikoDeath_v1/", 486f),                             // 巫女 倒れる: 583 → 486(×1.20)
        ("Assets/Art/DragonkinDeath_v1/", 443f),                        // 竜人 倒れる: 510 → 443(×1.15)
    };

    public static bool TryGet(string path, out float ppu)
    {
        foreach (var (prefix, v) in Table)
            if (path.StartsWith(prefix) && path.EndsWith(".png")) { ppu = v; return true; }
        ppu = 0f; return false;
    }

    void OnPreprocessTexture()
    {
        if (!TryGet(assetPath, out float ppu)) return;
        var ti = (TextureImporter)assetImporter;
        if (UnityEngine.Mathf.Abs(ti.spritePixelsPerUnit - ppu) > 0.01f) ti.spritePixelsPerUnit = ppu;
    }
}
