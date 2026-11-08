#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 定型メッセージの2台確認(2026-11-08): -netAutoChat
//   -netAutoChatLang <code>   この端末の表示の言語(保存しない)。HOST と JOIN で別の言語にして、同じ ID が各端末の言語で出るかを見る
//   部屋(出発前)とラン中に、決まった順番で文を送る。部屋は「つながった回」ごと(抜けて入り直した後も同じ確認):
//     1回目の部屋: HOST 1,4 / JOIN 2,5
//     2回目の部屋(JOIN が -netAutoLobbyLeaveAt で抜けて入り直した後): HOST 7 / JOIN 8、連打(同じフレームで2回)は2回目が断られる、
//       JOIN は改造した端末の連投(ボタンを通さず 8 通)→ HOST は 4 秒に 5 通までしか受け付けない
//     ラン中: HOST 3,6 / JOIN 9,1(送ってもゲームが止まらない: 距離が伸び続ける・時間の速さが変わらない)
//   受け取った文(相手の番号の分)を順に [QCHAT] 行へ。相手の送った順と1回ずつ一致するかは、両方のログを chatcheck.py で比べる
public partial class NetAutoTest
{
    bool chatTest; string chatLang = "";
    bool chatPresent; int chatRound; float chatT0;
    readonly List<string> chatSchedDone = new List<string>();
    readonly List<string> chatSent = new List<string>();   // "round:id"
    readonly List<string> chatRecv = new List<string>();   // "round:P#:id"
    int chatOwnShows, chatSendOk, chatBurstRefused, chatRawSent, chatBadText, chatRunChecks, chatRunStalls;
    bool chatHooked, chatOpenedPanel;
    float chatRunSendAt = -1f; double chatRunDist0; float chatRunTs0;
    int chatShots;

    int chatRounds = 2; // -netAutoChatRounds N: HOST はこの回数の部屋の確認が終わるまで出発しない
    bool ChatHostReady => !chatTest || role != "HOST" || chatRound > chatRounds || chatRound == chatRounds && chatPresent && Time.unscaledTime - chatT0 > 12f;

    bool ParseChatArg(string a, string next)
    {
        if (a == "-netAutoChatRounds") { int.TryParse(next, out chatRounds); return true; }
        if (a == "-netAutoChat") { chatTest = true; return true; }
        if (a == "-netAutoChatLang") { chatLang = next; return true; }
        return false;
    }

    int chatMeSeen;
    int ChatMe { get { int n = NetPlayer.Local != null ? NetPlayer.Local.PlayerNumber : 0; if (n > 0) chatMeSeen = n; return n; } }
    string ChatPhase => step == Step.Running ? "run" : "lobby" + chatRound;

    void ChatHook()
    {
        if (chatHooked) return;
        chatHooked = true;
        if (chatLang != "" && Loc.Current != chatLang) Loc.Set(chatLang, false);
        QuickChat.OnReceived += (player, id) =>
        {
            string text = Loc.T(LocProfile.ChatText(id));
            bool bad = string.IsNullOrEmpty(text) || text.IndexOf('�') >= 0
                       || (Loc.Current != "ja" && id != 0 && text == LocProfile.ChatText(id)); // 訳されずに日本語のまま
            if (bad) chatBadText++;
            if (player == ChatMe && player != 0) { chatOwnShows++; L($"QCHAT SHOW-OWN {ChatPhase} P{player} id={id} '{text}'"); return; }
            chatRecv.Add($"{ChatPhase}:P{player}:{id}");
            L($"QCHAT RECV {ChatPhase} from=P{player} id={id} lang={Loc.Current} text='{text}'{(bad ? " BAD-TEXT" : "")}");
        };
        L($"QCHAT ready role={role} lang={Loc.Current}");
    }

    bool ChatSend(byte id, string tag)
    {
        bool ok = QuickChat.Send(id);
        if (ok) { chatSendOk++; chatSent.Add($"{ChatPhase}:{id}"); }
        L($"QCHAT SEND {ChatPhase} {tag} id={id} ok={ok} me=P{ChatMe}");
        return ok;
    }

    // 決まった時刻に1回だけ
    bool ChatAt(string key, float now, float at)
    {
        if (now < at || chatSchedDone.Contains(key)) return false;
        chatSchedDone.Add(key);
        return true;
    }

    void ChatShot(string name)
    {
        if (string.IsNullOrEmpty(shotDir)) return;
        System.IO.Directory.CreateDirectory(shotDir);
        string p = System.IO.Path.Combine(shotDir, $"qchat_{role.ToLowerInvariant()}_{++chatShots:00}_{name}.png");
        ScreenCapture.CaptureScreenshot(p);
        L($"QCHAT SHOT {p}");
    }

    void ChatTick(GameManager gm)
    {
        if (!chatTest) return;
        ChatHook();
        bool host = role == "HOST";
        if (step == Step.WaitPlayers || step == Step.WaitRun && (gm == null || !gm.HasStarted))
        {
            int n = 0; foreach (var p in NetPlayer.All) if (p != null) n++;
            bool present = NetSession.IsActive && NetSession.ConnectedPlayerCount >= 2 && n >= 2 && NetPlayer.Local != null;
            if (present && !chatPresent) { chatRound++; chatT0 = Time.unscaledTime; L($"QCHAT round {chatRound} start me=P{ChatMe} players={n}"); }
            if (!present && chatPresent) L($"QCHAT round {chatRound} end (someone left)");
            chatPresent = present;
            if (!host && present && !NetDebugUI.PanelOpen) { NetDebugUI.OpenPanel(); chatOpenedPanel = true; } // 部屋の画面(文のボタン)を出して撮る
            if (!present) return;
            float t = Time.unscaledTime - chatT0;
            string r = "r" + chatRound;
            if (chatRound == 1)
            {
                if (host) { if (ChatAt(r + "a", t, 1.0f)) ChatSend(1, "lobby"); if (ChatAt(r + "b", t, 3.2f)) ChatSend(4, "lobby"); }
                else { if (ChatAt(r + "a", t, 2.1f)) ChatSend(2, "lobby"); if (ChatAt(r + "b", t, 4.3f)) ChatSend(5, "lobby"); }
                if (ChatAt(r + "shot", t, 4.8f)) ChatShot("lobby1");
            }
            else if (chatRound == 2)
            {
                if (host)
                {
                    if (ChatAt(r + "a", t, 1.0f)) ChatSend(7, "lobby-after-rejoin");
                    if (ChatAt(r + "burst", t, 4.3f)) { ChatSend(6, "burst-1st"); if (!ChatSend(6, "burst-2nd")) chatBurstRefused++; }
                }
                else
                {
                    if (ChatAt(r + "a", t, 2.1f)) ChatSend(8, "lobby-after-rejoin");
                    if (ChatAt(r + "burst", t, 3.2f)) { ChatSend(3, "burst-1st"); if (!ChatSend(3, "burst-2nd")) chatBurstRefused++; }
                    if (ChatAt(r + "spam", t, 9.0f)) { QuickChat.DebugSendRaw(9, 8); chatRawSent += 8; L("QCHAT RAWSPAM id=9 x8 (no button limit)"); }
                }
                if (ChatAt(r + "shot", t, 5.0f)) ChatShot("lobby2");
            }
            return;
        }
        if (step != Step.Running) return;
        if (chatOpenedPanel && NetDebugUI.PanelOpen) { NetDebugUI.ClosePanel(); chatOpenedPanel = false; }
        var pc = PlayerController.Instance;
        // 送った 0.6 秒後: 距離が伸び続けている・時間の速さが変わらない
        if (chatRunSendAt >= 0f && runTime - chatRunSendAt >= 0.6f)
        {
            double d1 = pc != null ? pc.DistanceExact : 0;
            bool moving = d1 > chatRunDist0 + 0.5;
            bool ts = Mathf.Approximately(Time.timeScale, chatRunTs0) && Time.timeScale > 0.5f;
            chatRunChecks++; if (!moving || !ts) chatRunStalls++;
            L($"QCHAT RUNCHECK dist {chatRunDist0:F1}->{d1:F1} timeScale {chatRunTs0:F2}->{Time.timeScale:F2} {(moving && ts ? "OK" : "STALL")}");
            chatRunSendAt = -1f;
        }
        byte send = 0;
        if (host) { if (ChatAt("run-a", runTime, 3.0f)) send = 3; else if (ChatAt("run-b", runTime, 5.2f)) send = 6; }
        else { if (ChatAt("run-a", runTime, 4.1f)) send = 9; else if (ChatAt("run-b", runTime, 6.3f)) send = 1; }
        if (send != 0)
        {
            chatRunDist0 = pc != null ? pc.DistanceExact : 0; chatRunTs0 = Time.timeScale; chatRunSendAt = runTime;
            ChatSend(send, "run");
        }
        if (ChatAt("run-shot", runTime, host ? 4.4f : 6.6f)) ChatShot("run");
    }

    void ChatSummary()
    {
        if (!chatTest) return;
        int meNow = ChatMe; L($"QCHAT SUMMARY role={role} me=P{(meNow > 0 ? meNow : chatMeSeen)} lang={Loc.Current} sent=[{string.Join(" ", chatSent)}] recv=[{string.Join(" ", chatRecv)}] ownShows={chatOwnShows} sendOk={chatSendOk} burstRefused={chatBurstRefused} rawSent={chatRawSent} spamDropped={QuickChat.DroppedBySpamGuard} badText={chatBadText} runChecks={chatRunChecks} runStalls={chatRunStalls} rounds={chatRound}");
    }
}
#endif
