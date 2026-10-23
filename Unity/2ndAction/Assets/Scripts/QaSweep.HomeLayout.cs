using System.Collections;
using UnityEngine;

// ホームの重なりの確認(2026-10-23): -qaHomeLayout <dir>(専用の保存 -fileSave で)。起動の画面の大きさで、
//  ガチャ機械の5段階 × 闘技場 未解放/解放 のホームを撮り、右上のボタン(設定/マルチ/ランキング/闘技場)と
//  ガチャ機械(看板を含む絵の範囲 = 押せる範囲)が重ならないこと、ボタンが画面の中にあることを確かめる
public partial class QaSweep
{
    IEnumerator HomeLayoutMode()
    {
        float w0 = 0f;
        while (GameManager.Instance == null && w0 < 10f) { yield return null; w0 += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        gm = GameManager.Instance;
        bool safe = FileSaveStore.Active || MemorySaveStore.Active;
        Check(safe, "runs on a dedicated save only");
        if (!safe) yield break;
        string res = $"{Screen.width}x{Screen.height}";
        float[] bests = { 0f, 5000f, 20000f, 50000f, 100000f };
        for (int unlock = 0; unlock < 2; unlock++)
        {
            UnlockRules.DevUnlockAll = unlock == 1; // 闘技場: 未解放(ボタンなし)/ 解放
            for (int i = 0; i < bests.Length; i++)
            {
                gm.DebugSetBestDistance(bests[i]);
                yield return new WaitForSecondsRealtime(0.6f);
                Rect m = gm.DebugGachaMachineRect;
                var rects = gm.DebugHomeChromeRects();
                int overlaps = 0;
                foreach (var r in rects) if (r.width > 0f && m.width > 0f && r.Overlaps(m)) overlaps++;
                bool inside = true;
                foreach (var r in rects) if (r.width > 0f && (r.xMin < 0f || r.yMin < 0f || r.xMax > Screen.width || r.yMax > Screen.height)) inside = false;
                L($"[home] {res} arena={(unlock == 1 ? "open" : "locked")} stage={gm.CurrentGachaStage} machine={m} buttons={string.Join(" ", rects)} overlaps={overlaps}");
                Check(overlaps == 0, $"{res} stage {gm.CurrentGachaStage} arena {(unlock == 1 ? "open" : "locked")}: the gacha machine and the top-right buttons do not overlap");
                Check(inside, $"{res}: the top-right buttons are inside the screen");
                Shot($"home_{res}_{(unlock == 1 ? "open" : "locked")}_stage{gm.CurrentGachaStage}");
                yield return Frames(3);
            }
        }
        UnlockRules.DevUnlockAll = false;
    }
}
