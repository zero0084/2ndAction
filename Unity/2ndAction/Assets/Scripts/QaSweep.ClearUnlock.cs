#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

// クリア後のご褒美の2人(ルカ / ペンタトゥットゥルー星人)の解放の確認(2026-10-27): -qaClearUnlock <dir> -cuPhase before|after|again
//  専用の保存ファイル(-fileSave)のテスト用データで動かす(通常のデータへクリアの記録を付けない)。間に -ldQa <dir> -ldQaMode stop(三姉妹 → エンドロール → NO → ホーム)
//   before : クリアの記録が無い。2人は解放されておらず、一覧(キャラ選択/闘技場/CPU サポート)に出ない
//   after  : クリアの記録がある(エンディングの始まりで書かれた)。2人とも解放・一覧に出る。ホームで加入のお知らせが1回(2人一緒)出る → 確認
//            → ルカで走り始めて、技の種類(Cat)と体の当たり判定(四足の胴体)を確かめる。宇宙人も同じ
//   again  : 起動し直し。解放は残り、お知らせはもう出ない
public partial class QaSweep
{
    IEnumerator ClearUnlockMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        gm = GameManager.Instance;
        yield return new WaitForSecondsRealtime(3f);
        string phase = Arg("-cuPhase", "before");
        Check(FileSaveStore.Active && SaveProfile.IsTest, $"runs on a dedicated test-data save (file {FileSaveStore.Active}, test data {SaveProfile.IsTest})");
        bool cleared = ProgressStats.GameCleared;
        bool lukaUnlocked = UnlockRules.IsCharacterUnlocked(UnlockRules.Luka), pentaUnlocked = UnlockRules.IsCharacterUnlocked(UnlockRules.Penta);
        L($"[cu] phase {phase}: cleared={cleared} luka={lukaUnlocked} penta={pentaUnlocked} devAll={UnlockRules.DevUnlockAll} notices={NoticeQueue.Pending} current='{NoticeQueue.CurrentId}'");
        if (phase == "before")
        {
            Check(!cleared, "before: no clear record yet");
            Check(!lukaUnlocked && !pentaUnlocked && !UnlockRules.IsCharacterVisible(UnlockRules.Luka) && !UnlockRules.IsCharacterVisible(UnlockRules.Penta), "before: the two are locked and hidden");
            yield break;
        }
        Check(cleared, $"{phase}: the clear record is saved");
        Check(lukaUnlocked && pentaUnlocked && UnlockRules.IsCharacterVisible(UnlockRules.Luka) && UnlockRules.IsCharacterVisible(UnlockRules.Penta), $"{phase}: both are unlocked and shown in the lists");
        // お知らせ(ホームで1回、2人一緒)
        int seen = 0; float w = 0f;
        while (w < 8f)
        {
            if (NoticeQueue.CurrentId == "unlock_clear_rewards")
            {
                seen++;
                Shot($"cu_{phase}_notice");
                yield return null; yield return null;
                NoticeQueue.DebugAnswer();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            else if (NoticeQueue.Open) NoticeQueue.DebugAnswer(); // ほかのお知らせは閉じて先へ
            yield return null; w += Time.unscaledDeltaTime;
        }
        L($"[cu] {phase}: clear-reward notice shown {seen} time(s)");
        if (phase == "after") Check(seen == 1, "after: the join notice is shown once at home (both together)");
        else Check(seen == 0, "again: the join notice is not shown again after a restart");
        if (phase != "after") yield break;
        // 2人で走り始めて、技の種類と体の当たり判定を確かめる
        foreach (var (id, kit) in new[] { (UnlockRules.Luka, CharacterKit.Cat), (UnlockRules.Penta, CharacterKit.Cloud) })
        {
            yield return BeginRun(id, "wasteland_road");
            var col = pc.GetComponent<BoxCollider2D>();
            var def = CharacterDatabase.FindById(id);
            Vector2 want = kit == CharacterKit.Cat ? def.cat.bodySize : def.cloud.bodySize;
            float bottom = col.offset.y - col.size.y * 0.5f;
            L($"[cu] {id}: kit={pc.Kit} body {col.size.x:F2}x{col.size.y:F2} bottom {bottom:F2} lives {gm.Lives}/{gm.maxLives}");
            Check(pc.Kit == kit, $"{id}: plays with its own kit ({kit})");
            Check(Mathf.Abs(col.size.x - want.x) < 0.01f && Mathf.Abs(col.size.y - want.y) < 0.01f && Mathf.Abs(bottom) < 0.01f, $"{id}: body box matches its shape, feet unchanged");
            Shot($"cu_run_{id}");
            yield return null; yield return null;
            yield return EndRun();
        }
        // 元のキャラ(黒剣士)に戻すと体の当たり判定も元に戻る
        yield return BeginRun("swordsman", "wasteland_road");
        var c2 = pc.GetComponent<BoxCollider2D>();
        Check(Mathf.Abs(c2.size.x - 1f) < 0.01f && Mathf.Abs(c2.size.y - 1f) < 0.01f, $"swordsman: body box back to 1x1 ({c2.size.x:F2}x{c2.size.y:F2})");
        yield return EndRun();
    }
}
#endif
