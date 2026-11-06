using UnityEngine;

// マップ専用カード(2026-11-06、map_cards_v2.md)の追加の攻撃: SHIELD BURST。
//  Shield(一時シールドを含む)が壊れた時(OnShieldBlock から)、プレイヤーの周り 3m へ「攻撃力の倍率」のダメージ + ボスの BREAK を少し。
//  間隔は Lv で 6 / 4.5 / 3 秒(SHIELD を重ねても 3 秒より短くならない)。ダメージは静かなダメージ(のけぞり/音なし)で、ここから他の追加攻撃は出ない
public static partial class CardProcs
{
    static float shieldBurstAt = -99f;
    public static void ResetMapCards() { shieldBurstAt = -99f; }

    static void MapShieldBurst(PlayerController pc)
    {
        int lv = Lv(EffectType.ShieldBurstLevel);
        if (lv <= 0 || pc == null) return;
        if (NetRunLauncher.IsMultiplayerRun && NetSession.IsActive && !NetCombat.Authority) return; // HP を持つ端末だけ
        lv = Mathf.Min(lv, 9);
        if (Time.time - shieldBurstAt < MapCardRules.ShieldBurstInterval(lv)) return;
        shieldBurstAt = Time.time;
        Vector3 at = pc.transform.position + Vector3.up * 0.8f;
        float dmg = pc.EffectiveAttackPower * MapCardRules.ShieldBurstPower(lv);
        float brk = MapCardRules.ShieldBurstBreak(lv);
        areaBuf.Clear();
        CollectTargets(at, MapCardRules.ShieldBurstRadius, areaBuf);
        foreach (var c in areaBuf)
        {
            if (!TakeProc()) break;
            ElementSystem.DealQuiet(c, Mathf.Max(1, Mathf.RoundToInt(dmg)), ElementType.None);
            if (!(c is EnemyController) && ElementSystem.IsAlive(c)) ElementSystem.AddCardStagger(c, brk); // ボスの BREAK
        }
        if (TakeFx()) OneShotSpriteEffect.CreateTweened(KitProjectile.Burst, at, new Color(0.75f, 0.45f, 1f, 0.95f), 0.26f, 0.8f, MapCardRules.ShieldBurstRadius * 0.95f, -1f, 0f);
        MapCardRules.ShieldBursts++;
    }
}
