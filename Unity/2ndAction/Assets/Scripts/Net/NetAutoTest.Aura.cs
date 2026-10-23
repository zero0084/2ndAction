#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 2026-10-23: 開発版(Editor / Development Build)だけ。製品版では起動引数で動かせない(Steam の起動オプションから入れてしまうため)
using System.Text;
using UnityEngine;

// 識別用オーラの確認(2026-10-14): 全端末で同じ人 = 同じ番号/色か、抜けて戻っても番号が変わらないか。
//   -netAutoStartDelay 秒        (HOST)人数がそろっても、この秒数までは出発しない(抜けて戻る試験の時間を取る)
//   -netAutoLobbyLeaveAt 秒      (JOIN)つないでからこの秒数で抜け、-netAutoRejoinAfter 秒後につなぎ直す(同じ端末の鍵)
//   -netAutoAuraLog 秒,秒,...    ラン開始からの秒数で、全員の番号/色と表示中のオーラを [AURA] 行に出す(+画面を撮る: -netAutoShotDir)
public partial class NetAutoTest
{
    float startDelay, lobbyLeaveAt = -1f, rejoinAfter = 2f, rejoinAt = -1f;
    bool lobbyLeft, lobbyRejoined;
    readonly System.Collections.Generic.List<float> auraLogAt = new System.Collections.Generic.List<float>();
    int auraLogIndex;
    string shotDir;
    float spread; // -netAutoSpread m: P1 から番号ごとに m 後ろを走る(負なら前)(重ならない撮影用。走る速さだけを変える)

    bool ParseAuraArg(string a, string next)
    {
        if (a == "-netAutoStartDelay") { float.TryParse(next, out startDelay); return true; }
        if (a == "-netAutoLobbyLeaveAt") { float.TryParse(next, out lobbyLeaveAt); return true; }
        if (a == "-netAutoRejoinAfter") { float.TryParse(next, out rejoinAfter); return true; }
        if (a == "-netAutoAuraLog") { foreach (var s in next.Split(',')) if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t)) auraLogAt.Add(t); return true; }
        if (a == "-netAutoShotDir") { shotDir = next; return true; }
        // 撮影用のコードネーム(メモリの保存 -memSave の時だけ。普段のセーブには書かない)
        if (a == "-netAutoName") { if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-memSave") >= 0) Codename.Set(next); return true; }
        if (a == "-netAutoSpread") { float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out spread); return true; }
        return false;
    }

    // WaitPlayers の間: JOIN が一度抜けて、つなぎ直す
    void LobbyRejoinTick(float stepTime)
    {
        if (role != "JOIN" || lobbyLeaveAt < 0f || lobbyRejoined) return;
        if (!lobbyLeft)
        {
            if (NetSession.IsActive && NetPlayer.Local != null && stepTime >= lobbyLeaveAt)
            {
                L($"[AURA] lobby leave (me=P{NetPlayer.Local.PlayerNumber} key={ShortKey(NetPlayer.LocalParticipantKey())})");
                lobbyLeft = true;
                NetSession.Instance.Leave();
                rejoinAt = Time.unscaledTime + rejoinAfter;
            }
            return;
        }
        if (Time.unscaledTime < rejoinAt) return;
        var m = NetSession.Manager;
        if (m != null && (m.IsClient || m.ShutdownInProgress)) return;
        lobbyRejoined = true;
        L("[AURA] lobby rejoin");
        NetSession.Instance.StartClient(joinIp, NetSession.DefaultPort);
    }

    bool HostMayDepart(float stepTime) => role != "HOST" || stepTime >= startDelay;

    void AuraLogTick(float runTime)
    {
        if (auraLogIndex >= auraLogAt.Count || runTime < auraLogAt[auraLogIndex]) return;
        auraLogIndex++;
        var sb = new StringBuilder();
        sb.Append($"[AURA] t={runTime:F1} me=P{NetCombat.LocalPlayerNumber} players=");
        var list = new System.Collections.Generic.List<NetPlayer>(NetPlayer.All);
        list.Sort((p, q) => p.PlayerNumber.CompareTo(q.PlayerNumber));
        foreach (var p in list)
        {
            int auraPn = 0;
            if (p.IsOwner) { var la = PlayerController.Instance != null ? PlayerController.Instance.GetComponent<ParticipantAura>() : null; auraPn = la != null ? la.Pn : 0; }
            else if (p.Avatar != null) { var ra = p.Avatar.GetComponent<ParticipantAura>(); auraPn = ra != null ? ra.Pn : 0; }
            sb.Append($"{ShortKey(p.ParticipantKey.Value.ToString())}=P{p.PlayerNumber}/aura{auraPn}/{ColorHex(auraPn)}{(p.IsOwner ? "(me)" : "")} ");
        }
        sb.Append($"auras={ParticipantAura.All.Count}");
        L(sb.ToString());
        if (!string.IsNullOrEmpty(shotDir))
        {
            System.IO.Directory.CreateDirectory(shotDir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, $"aura_{role}_P{NetCombat.LocalPlayerNumber}_t{runTime:F0}.png"));
        }
    }

    void SpreadTick()
    {
        if (spread == 0f || role == "HOST") return;
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float hostX = float.MinValue;
        foreach (NetPlayer p in NetPlayer.All)
            if (p != null && !p.IsOwner && p.PlayerNumber == 1 && p.Avatar != null && p.Avatar.HasRecentPosition) hostX = p.Avatar.transform.position.x;
        if (hostX == float.MinValue) { PlayerController.DebugRunOnlyScale = 1f; return; }
        float want = hostX - spread * (NetCombat.LocalPlayerNumber - 1);
        float err = want - pc.transform.position.x;
        PlayerController.DebugRunOnlyScale = Mathf.Clamp(1f + 0.25f * err, 0.6f, 1.5f);
    }

    static string ColorHex(int pn) => pn > 0 ? ColorUtility.ToHtmlStringRGB(ParticipantAura.ColorOf(pn)) : "-";
    // 鍵そのものは出さない(見分けがつく短い要約だけ)
    static string ShortKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "none";
        unchecked { uint h = 2166136261; foreach (char c in key) { h ^= c; h *= 16777619; } return "k" + (h % 100000).ToString("00000"); }
    }
}
#endif
