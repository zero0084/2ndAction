#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;

// ガチャのマップ別の見た目(2026-11-06)の確認: -qaGachaSkin <dir>(-memSave / -fileSave で)。起動の画面の大きさで撮る。
//  A 待機: 5 つの土地の台(ホーム)
//  B 引く: 土地ごとに 低いレア度 / ★4以上 のカードで 回している間 → 出た瞬間 → 結果 を撮る。結果のカードの面・★に粒子が重ならない(板の内側には描かない)
//  C 土地の列: 新規(洞窟/天空/ラスダンは鍵)/ 全解放
//  D 例外が出ない・粒子の数が上限以内
public partial class QaSweep
{
    IEnumerator GachaSkinMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        TutorialProgress.MarkOffered();
        string res = $"{Screen.width}x{Screen.height}";
        var lands = new[] { CardMapCategory.Common, CardMapCategory.Wilderness, CardMapCategory.Cave, CardMapCategory.Sky, CardMapCategory.LastDungeon };

        // ---- C(新規のデータ)
        UnlockRules.DevUnlockAll = false;
        gm.DebugGachaSetSkin(CardMapCategory.Common);
        yield return new WaitForSecondsRealtime(2.5f); // 列の数え直し(2秒ごと)
        Shot($"gs_C_new_{res}"); yield return null; yield return null;
        L($"[C] new data: land counts " + string.Join(" ", lands.Select(k => $"{k}={gm.GachaLandCount(k)}")) + $" open " + string.Join(" ", lands.Select(k => $"{k}={GameManager.GachaLandMapOpen(k)}")));
        Check(!GameManager.GachaLandMapOpen(CardMapCategory.LastDungeon) && gm.GachaLandCount(CardMapCategory.LastDungeon) == 0, "[C] new data: Last Dungeon locked and has no cards in the pool");
        Check(Screen.height > Screen.width ? gm.LastGachaLandRowRect.width == 0f : gm.LastGachaLandRowRect.width > 0f, $"[C] land row drawn in landscape only ({gm.LastGachaLandRowRect})");

        UnlockRules.DevUnlockAll = true; GachaStage.DevAllCardsOpen = true;
        yield return new WaitForSecondsRealtime(2.5f);

        // ---- A
        foreach (var k in lands)
        {
            gm.DebugGachaSetSkin(k);
            yield return new WaitForSecondsRealtime(0.5f);
            Shot($"gs_A_idle_{GachaSkinFx.KeyOf(k)}_{res}"); yield return null; yield return null;
        }
        Rect m = gm.DebugGachaMachineRectNow, row = gm.LastGachaLandRowRect;
        L($"[A] machine {m} land row {row}");
        Check(Screen.height > Screen.width || (row.width > 0f && row.yMin >= m.yMax - 1f), "[A] land row sits below the machine (does not cover it)");
        Check(row.width == 0f || row.xMin >= -1f && row.xMax <= Screen.width + 1f && row.yMax <= Screen.height + 1f, "[A] land row inside the screen");

        // ---- B
        int peak = 0;
        foreach (var k in lands)
        {
            var cards = CardDatabase.AllCards.Where(c => c != null && CardMapCategories.Of(c) == k).OrderBy(c => c.rarity).ToList();
            if (cards.Count == 0) { L($"[B] {k}: no cards"); continue; }
            var picks = new[] { cards.First(), cards.Last() }.Distinct();
            foreach (var c in picks)
            {
                gm.DebugGachaSetSkin(CardMapCategory.Common);
                yield return new WaitForSecondsRealtime(0.3f);
                gm.DebugGachaInject(c);
                yield return new WaitForSecondsRealtime(0.3f);
                string tag = $"{GachaSkinFx.KeyOf(k)}_r{c.rarity}_{c.cardId}_{res}";
                Shot($"gs_B1_spin_{tag}"); yield return null; yield return null;
                float t = 0f; while (!gm.GachaResultOpen && t < 3f) { yield return null; t += Time.unscaledDeltaTime; }
                Check(gm.GachaResultOpen, $"[B] {c.cardId}: result opened");
                Check(gm.GachaSkinState.Skin == k, $"[B] {c.cardId}: machine took the card's land look ({gm.GachaSkinState.Skin} / {k})");
                yield return new WaitForSecondsRealtime(0.12f);
                Shot($"gs_B2_reveal_{tag}"); yield return null; yield return null;
                peak = Mathf.Max(peak, gm.GachaSkinState.ParticleCount);
                yield return new WaitForSecondsRealtime(0.9f);
                Shot($"gs_B3_result_{tag}"); yield return null; yield return null;
                Rect b = gm.LastGachaLandBadgeRect;
                L($"[B] {c.cardId} ★{c.rarity} {k}: header {gm.LastGachaLandHeader} spins {gm.GachaSkinState.Spins} reveals {gm.GachaSkinState.Reveals} particles {gm.GachaSkinState.ParticleCount} badge {b}");
                gm.DebugGachaClose();
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Shot($"gs_B4_after_{GachaSkinFx.KeyOf(k)}_{res}"); yield return null; yield return null;
        }
        Check(peak <= GachaSkinFx.MaxParticles && GachaSkinFx.ParticlePeak <= GachaSkinFx.MaxParticles, $"[D] particles within the cap (peak {GachaSkinFx.ParticlePeak} / {GachaSkinFx.MaxParticles})");
        UnlockRules.DevUnlockAll = false; GachaStage.DevAllCardsOpen = false;
    }
}
#endif
