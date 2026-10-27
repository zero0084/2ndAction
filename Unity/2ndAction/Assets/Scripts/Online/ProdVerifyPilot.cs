#if OMM_PROD_VERIFY
using UnityEngine;

// production の最終試験の自動操縦(2026-10-27)。配らない確認用の製品版(BuildWindowsProductSandbox.BuildVerify)だけに入る。配る製品版には入らない。
// 起動引数 -ommVerifyPilot の時だけ: ソロのランをオート(0km/h から)で走らせ、レベルアップ/報酬の3択は1枚目を選び、
// 脱出が使えるようになったら(最初のボスの後)長押しで帰還する。-ommVerifyEscapeAt <m> の時は、その距離を超えてから帰還する(疾走出発の後に自己ベストを超える等)。
// ランキングの対象の判定と送信は普段どおり(オートを使ったランとして載る)
public class ProdVerifyPilot : MonoBehaviour
{
    public static bool On { get; private set; }
    float nextPick;
    static float escapeAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ommVerifyPilot") < 0) return;
        On = true;
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, "-ommVerifyEscapeAt");
        if (i >= 0 && i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out escapeAt);
        var go = new GameObject("[ProdVerifyPilot]");
        DontDestroyOnLoad(go);
        go.AddComponent<ProdVerifyPilot>();
        Debug.Log($"[VerifyPilot] on (auto from 0 km/h, pick the first card, escape when it becomes available and past {escapeAt:F0}m)");
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasStarted || gm.IsGameOver) return;
        if (HighSpeedAssist.Instance != null && HighSpeedAssist.Instance.EngageSettingKmh > 0f) HighSpeedAssist.Instance.ReloadPrefs();
        if (gm.IsRewardSequenceWaitingForSelection)
        {
            if (Time.unscaledTime < nextPick) return;
            nextPick = Time.unscaledTime + 0.4f;
            var seq = FindFirstObjectByType<RewardCardSequence>();
            if (seq != null) seq.OnCardClicked(0);
            return;
        }
        if (gm.EscapeAvailable && gm.MaxDistance >= escapeAt) GameInput.Inject(GameAction.Hold);
    }
}
#endif
