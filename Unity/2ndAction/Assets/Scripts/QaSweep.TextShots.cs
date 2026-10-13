#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Reflection;
using UnityEngine;

// 文字の見切れの点検(2026-10-13)。ラン中の一時表示を実際に出し、出た直後/動きの途中/消える直前を撮る。言語ごとに TextFit の報告を書く。
//   -qaTextShots <dir> [-tsLangs ja,en,de] [-tsShots 1]   画面の大きさは起動の -screen-width/-height で決める(縦画面 480x1040 等)
// 出す物: JACKPOT / BONUS ZONE(開始・帯・結果)/ ボス戦の長い見出し / 必殺技名 / お知らせ / ボス出現 / 撃破 / LEVEL UP / 脱出できる / 結果画面
public partial class QaSweep
{
    static readonly string[] TsBossBanners = { "WARNING!  メカドラゴン接近!", "冷却中! 攻撃のチャンス!", "ドラゴンが低空へ! 反撃のチャンス!", "BOSS RUSH  走りながら戦え!", "ボスを倒さないと先へ進めない!", "八脚の死の舞踏" };
    static readonly string[] TsUltNames = { "最高位魔法グラウンドゼロ", "桜花乱咲薔薇ノ一太刀" };

    IEnumerator TextShotsMode()
    {
        var snap = SaveSystem.Capture();
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(1f);
        bool shots = Arg("-qaShotsOn", "1") == "1";
        string res = $"{Screen.width}x{Screen.height}";
        foreach (var lang in Arg("-tsLangs", "ja").Split(','))
        {
            Loc.Set(lang, false);
            TextFit.Issues.Clear();
            yield return BeginRun("swordsman", "wasteland_road");
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, true);
            GameManager.BlockExpGain = true;
            yield return new WaitForSeconds(1.5f);
            string P(string n) => $"{lang}_{res}_{n}";

            // ---- JACKPOT(開始の膨らみ / 途中 / 消える前 / 帯 / 結果)
            var bz = BonusZone.Instance;
            if (bz != null && bz.Force("jackpot"))
            {
                yield return Frames(2); if (shots) Shot(P("jackpot_0start"));
                yield return new WaitForSecondsRealtime(0.5f); if (shots) Shot(P("jackpot_1mid"));
                yield return new WaitForSecondsRealtime(1.3f); if (shots) Shot(P("jackpot_2end"));
                yield return new WaitForSecondsRealtime(1.5f); if (shots) Shot(P("jackpot_3strip"));
                bz.End();
                yield return new WaitForSecondsRealtime(0.1f); if (shots) Shot(P("jackpot_4clear"));
                yield return new WaitForSecondsRealtime(1.3f); if (shots) Shot(P("jackpot_5result"));
                yield return new WaitForSecondsRealtime(2.5f);
            }
            else L($"[ts {lang}] jackpot could not start");
            if (bz != null && bz.Force(null))
            {
                yield return Frames(2); if (shots) Shot(P("bonus_0start"));
                yield return new WaitForSecondsRealtime(2.5f); if (shots) Shot(P("bonus_1strip"));
                bz.End();
                yield return new WaitForSecondsRealtime(1.4f); if (shots) Shot(P("bonus_2result"));
                yield return new WaitForSecondsRealtime(2.5f);
            }
            // ---- ボス戦の見出し(長い物)
            for (int i = 0; i < TsBossBanners.Length; i++)
            {
                BossBattleHud.Banner(TsBossBanners[i], new Color(1f, 0.85f, 0.4f), 2.4f);
                yield return Frames(2); if (shots) Shot(P($"banner{i}_0start"));
                yield return new WaitForSecondsRealtime(1.2f); if (shots) Shot(P($"banner{i}_1mid"));
                yield return new WaitForSecondsRealtime(1.4f);
            }
            // ---- 必殺技名
            for (int i = 0; i < TsUltNames.Length; i++)
            {
                SetStaticProp(typeof(UltimateFx), "BannerText", TsUltNames[i]);
                SetStaticProp(typeof(UltimateFx), "BannerColor", new Color(1f, 0.6f, 0.9f));
                SetStaticProp(typeof(UltimateFx), "BannerUntil", Time.unscaledTime + 1.6f);
                var ens = typeof(UltimateFxLabel).GetMethod("Ensure", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (ens != null) ens.Invoke(null, null);
                yield return new WaitForSecondsRealtime(0.2f); if (shots) Shot(P($"ult{i}"));
                yield return new WaitForSecondsRealtime(1.6f);
            }
            // ---- お知らせ
            NoticeQueue.Toast("NEW CHARACTER: DRAGON LANCER");
            NoticeQueue.Toast("条件達成! ランを無事に終えると確定します");
            yield return new WaitForSecondsRealtime(0.4f); if (shots) Shot(P("toast0"));
            yield return new WaitForSecondsRealtime(3.2f); if (shots) Shot(P("toast1"));
            yield return new WaitForSecondsRealtime(3.0f);
            // ---- 脱出できる
            var ue = typeof(GameManager).GetMethod("UnlockEscape", NP);
            if (ue != null) { ue.Invoke(gm, null); yield return new WaitForSecondsRealtime(0.3f); if (shots) Shot(P("escape")); yield return new WaitForSecondsRealtime(2.2f); }
            // ---- ボス出現 / 撃破の演出
            if (BossMilestonePresentation.Instance != null)
            {
                BossMilestonePresentation.Instance.Play(gm.MaxDistance + 50f, true, null);
                yield return new WaitForSecondsRealtime(0.5f); if (shots) Shot(P("boss_warning"));
                yield return new WaitForSecondsRealtime(2.0f);
            }
            if (BossDefeatPresentation.Instance != null)
            {
                BossDefeatPresentation.Instance.Play(100000f, true);
                yield return new WaitForSecondsRealtime(0.15f); if (shots) Shot(P("defeat_0clear"));
                yield return new WaitForSecondsRealtime(0.9f); if (shots) Shot(P("defeat_1gameclear"));
                yield return new WaitForSecondsRealtime(1.1f); if (shots) Shot(P("defeat_2keep"));
                yield return new WaitForSecondsRealtime(1.5f);
            }
            // ---- LEVEL UP(3択)
            gm.GrantBonusCardChoice();
            yield return new WaitForSecondsRealtime(0.35f); if (shots) Shot(P("levelup_0"));
            yield return new WaitForSecondsRealtime(1.6f); if (shots) Shot(P("levelup_1choice"));
            float wc = 0f;
            while (gm.IsRewardSequenceWaitingForSelection == false && wc < 4f) { yield return null; wc += Time.unscaledDeltaTime; }
            var seq = Object.FindFirstObjectByType<RewardCardSequence>();
            if (seq != null && gm.IsRewardSequenceWaitingForSelection) seq.OnCardClicked(0);
            yield return new WaitForSecondsRealtime(2.0f);
            // ---- 結果画面(倒れた時)
            typeof(GameManager).GetProperty("InvincibleMode").SetValue(gm, false);
            gm.ReapPlayer("qa text shots");
            yield return new WaitForSecondsRealtime(3.5f); if (shots) Shot(P("result"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"textfit_{lang}_{res}.tsv"), TextFit.Report());
            L($"[ts {lang} {res}] textfit issues {TextFit.Issues.Count} (overflow {Count("OVERFLOW")})");
            yield return EndRun();
            yield return new WaitForSecondsRealtime(1f);
        }
        Loc.Set("ja", false);
        SaveSystem.Restore(snap);
    }

    static int Count(string kind) { int n = 0; foreach (var i in TextFit.Issues.Values) if (i.kind == kind) n++; return n; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static void SetStaticProp(System.Type t, string name, object v)
    {
        var p = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var m = p != null ? p.GetSetMethod(true) : null;
        if (m != null) m.Invoke(null, new[] { v });
    }
}
#endif
