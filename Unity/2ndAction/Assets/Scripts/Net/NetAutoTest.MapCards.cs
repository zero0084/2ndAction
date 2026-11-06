#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 開発版(Editor / Development Build)だけ(NetAutoTest 本体と同じ)
using UnityEngine;

// マップ専用カード 20 枚(2026-11-06)のマルチの確認: -netAutoMapCards [-netAutoMapHitGap 1.7]
//  -netAutoCards と一緒に(例 shield:3,vampire:9,overflow_ward:9,blood_armor:9,last_bastion:9,second_rise:9,revenge_armor:9,break_feast:9)。
//  HOST/JOIN とも自分のプレイヤーで同じ筋書き: 超過の回復(一時シールド / 血の盾)→ 決まった間隔で自分に被弾(Shield / 一時シールド / 血の盾が先に消える
//  → HP が減る → LAST BASTION / SECOND RISE)。JOIN の被弾は HOST へ申告され、HP は HOST が決める。
//  毎回「この端末の HP」と「HOST の表の HP」を比べる(MAPNET ... match=)。最後に数え(二重に出ていないか)を MAPNET_SUMMARY に出す。
//  ふだんの無敵(KeepAlive)は筋書きの被弾の瞬間だけ外す(敵の攻撃は今までどおり受けない)
public partial class NetAutoTest
{
    bool mapNet; float mapHitGap = 1.7f, mapNextHit = -1f, mapCheckAt = -1f; int mapHits, mapMismatch, mapChecks; bool mapHealed, mapOfferLogged;

    bool ParseMapCardsArg(string a, string next)
    {
        if (a == "-netAutoMapCards") { mapNet = true; return true; }
        if (a == "-netAutoMapHitGap") { float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mapHitGap); return true; }
        return false;
    }

    string MapState(GameManager gm)
    {
        var pc = PlayerController.Instance;
        var me = NetMatch.Get(NetCombat.LocalPlayerNumber);
        return $"lives={gm.Lives}/{gm.MaxLives} table={(me != null ? me.Hp : -1)}/{(me != null ? me.MaxHp : -1)} shield={(pc != null ? pc.ShieldCharges : -1)} temp={(pc != null ? pc.TempShieldCount : -1)} blood={gm.BloodShieldHearts}";
    }

    void MapCardsTick(GameManager gm)
    {
        if (!mapNet || gm == null || !cardsDone) return;
        var pc = PlayerController.Instance;
        if (pc == null) return;
        if (!mapOfferLogged)
        {
            mapOfferLogged = true;
            var omc = CardDatabase.FindBaseById("one_more_chance"); var lg = CardDatabase.FindBaseById("last_gamble");
            L($"MAPNET offer role={role} multiplayer={MapCardRules.Multiplayer} oneMoreChance offerable={gm.CanStillPick(omc)} lastGamble offerable={gm.CanStillPick(lg)}");
            gm.ApplyCardEffectsStacked(omc, 9); gm.ApplyCardEffectsStacked(lg, 9); // 持っていても効かない(SoloOnly)
            L($"MAPNET soloOnly role={role} OneMoreChanceLevel={gm.Card.Get(EffectType.OneMoreChanceLevel)} LastGambleLevel={gm.Card.Get(EffectType.LastGambleLevel)} mileMul={gm.Card.Get(EffectType.MileGainMultiplier):F2} gambleOn={MapCardRules.GambleOn}");
        }
        // 超過の回復: 満タンから吸収(血の盾)と普通の回復(一時シールド)
        if (!mapHealed && runTime > 3f)
        {
            mapHealed = true;
            gm.DrainHeal(5 * CombatScale.HpPerHeart);
            gm.KitHeal(5 * CombatScale.HpPerHeart);
            L($"MAPNET heal role={role} {MapState(gm)}");
            mapNextHit = runTime + 1.5f;
        }
        // 筋書きの被弾(この端末のプレイヤー。JOIN は HOST へ申告)
        if (mapNextHit > 0f && runTime >= mapNextHit && mapHits < 30 && gm.Lives > CombatScale.HpPerHeart) // ハート1つの時は打たない(倒れて DOWN にしない。SECOND RISE はその前の被弾で出る)
        {
            mapNextHit = runTime + mapHitGap;
            mapHits++;
            SetPrivateProperty(gm, "InvincibleMode", false);
            string before = MapState(gm);
            pc.TakeDamage(source: "QaMapNet");
            SetPrivateProperty(gm, "InvincibleMode", true);
            L($"MAPNET hit#{mapHits} role={role} before[{before}] after[{MapState(gm)}]");
            mapCheckAt = runTime + 0.8f;
        }
        // 被弾から少し後(HOST の確定と表が届いた後)に、この端末の HP と HOST の表を比べる
        if (mapCheckAt > 0f && runTime >= mapCheckAt)
        {
            mapCheckAt = -1f;
            var me = NetMatch.Get(NetCombat.LocalPlayerNumber);
            bool match = me == null || me.Hp == gm.Lives;
            mapChecks++;
            if (!match) mapMismatch++;
            L($"MAPNET check role={role} match={match} {MapState(gm)}");
        }
    }

    void MapCardsSummary(GameManager gm)
    {
        if (!mapNet) return;
        L($"MAPNET_SUMMARY role={role} me=P{NetCombat.LocalPlayerNumber} hits={mapHits} checks={mapChecks} mismatch={mapMismatch} " +
          $"tempGiven={MapCardRules.TempShieldsGiven} tempUsed={MapCardRules.TempShieldsUsed} overflow={MapCardRules.OverflowConversions} bloodGained={MapCardRules.BloodShieldGained} bloodUsed={MapCardRules.BloodShieldUsed} joinBloodBlocks={GameManager.JoinBloodBlocks} " +
          $"joinAfterDamage={GameManager.JoinAfterDamageCalls} secondRises={MapCardRules.SecondRises} revenge={MapCardRules.RevengeTriggers} oneMore={MapCardRules.OneMoreChances} breakFeast={MapCardRules.BreakFeastHeals} " +
          $"bossBreaks={MapCardRules.BossBreaksSeen} puppetBreaks={MapCardRules.PuppetBreaksSeen} shieldBlocks={GameManager.ShieldBlocks} lifesteal={GameManager.LifestealProcs} gambleExtra={MapCardRules.GambleExtraHearts} growthKills={(gm != null ? gm.MapGrowthKills : -1)} " +
          $"netFx sent={NetElementFx.Sent} received={NetElementFx.Received} played={NetElementFx.Played} missing={NetElementFx.Missing} elementVfx={ElementVfx.Spawned} " +
          (gm != null ? MapState(gm) : ""));
    }
}
#endif
