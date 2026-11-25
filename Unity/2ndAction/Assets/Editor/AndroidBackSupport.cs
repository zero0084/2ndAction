using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Android の「戻る」が、裏から戻った後にゲームへ届かずアプリが終わる不具合の回避(2026-11-25、最終工程の回帰試験で発見)。
//  条件: Predictive Back Support が有効 + Run In Background が有効(マルチの部屋を作る/入ると NetSession が有効にし、その起動の間は有効のまま)。
//  症状: 裏へ → 戻る → 「戻る」ボタン → ゲームに届かず(一時停止/終了の確認が開かない)アプリが終わる。6000.5.6f1 でも起き、
//        6000.6.10f1 の既知の問題(UUM-159613)と同じ組み合わせ。
//  回避: Predictive Back Support を切る(マニフェストの enableOnBackInvokedCallback=false)。「戻る」は今まで通り Esc としてゲームが受け取り、
//        ゲーム側の動き(一番手前を1段閉じる・ラン中は一時停止・タイトルでは終了の確認)は変わらない。変わるのは端末側の「戻る」の予告のアニメだけ
//        (このゲームは戻るを自分で処理するので、予告のアニメは元々出ない場面が大半)。
//  ProjectSettings.asset はリポジトリに入れない運用なので、ビルドの直前に必ず設定する(どのビルドの入口からでも)。
public class AndroidBackSupport : IPreprocessBuildWithReport
{
    public int callbackOrder => -200;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;
        if (PlayerSettings.Android.predictiveBackSupport)
        {
            PlayerSettings.Android.predictiveBackSupport = false;
            Debug.Log("AndroidBackSupport: Predictive Back Support turned off for this build (back after resuming from the background must reach the game)");
        }
        else Debug.Log("AndroidBackSupport: Predictive Back Support is off");
    }
}
