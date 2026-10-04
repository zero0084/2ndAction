#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 開発用の闘技場(2026-10-04)から使う GameManager の入口(開発版のみ)
public partial class GameManager
{
    // 距離条件: 敵の強さ(HP/段階)とボスの関門の番号(ボスのHP)に使う距離。ここで止めて、以後は進めない(ReportDistance)
    public void ArenaSetDistance(float d)
    {
        MaxDistance = Mathf.Max(0f, d);
        MaxDistanceExact = MaxDistance;
        if (BossManager.Instance != null) BossManager.Instance.RestoreNextBossDistance(Mathf.Max(0f, d - 1f));
    }

    // キャラの基準値へ戻し(キャラカード/前のビルドを外す)、試験のビルドを通常の取得と同じ処理で掛ける。毎回ここから作り直す
    public string ArenaApplyBuild(List<ArenaBuildEntry> build)
    {
        var def = CharacterDatabase.FindById(activeRunCharacterId);
        ApplyCharacterBaseStats(def);
        var sb = new StringBuilder();
        foreach (var e in build)
        {
            if (e == null || string.IsNullOrEmpty(e.key) || CardDatabase.FindById(e.key) == null) continue;
            int n = Mathf.Clamp(e.times, 1, MaxRunCardLevel);
            for (int i = 0; i < n; i++) ApplyUpgradeByCardId(e.key); // レベルアップで選んだ時と同じ(能力ごとの Lv9 上限もこの中)
            sb.Append(e.key).Append('x').Append(n).Append(' ');
        }
        Lives = maxLives;
        return sb.ToString();
    }

    public void ArenaRefillLives() { Lives = maxLives; }
}
#endif
