// 終了・中断の入口 / 画面設定 / 文字の大きさ(2026-10-31)の新しい文。ID 10349〜
//  画面は Loc.T(定数) で出す。日本語/英語はエステルのレビュー済み(Docs/LocReview_2026-10-31c、8件+1件を修正)。30言語は Tools/loc の keys_extra37
public static class LocExit
{
    // ---- 終了・中断(AppExit)
    public const string QuitGame = "ゲーム終了";                                         // ID 10349(PC: タイトル/ホーム/一時停止のボタン)
    public const string ToDeviceHome = "端末のホームへ";                                   // ID 10350(Android: タイトル/ホームのボタン。ゲームの中の「ホームへ戻る」とは別)
    public const string SuspendToDeviceHome = "中断して端末のホームへ";                     // ID 10351(Android: 一時停止のボタン)
    public const string SaveSuspend = "中断を保存";                                        // ID 10352(iPhone/iPad: 一時停止のボタン。アプリは閉じない)
    public const string QuitAsk = "ゲームを終了しますか？";                                 // ID 10353(確認の見出し: PC)
    public const string DeviceHomeAsk = "端末のホームへ戻りますか？";                       // ID 10354(確認の見出し: Android)
    public const string BackgroundNote = "アプリを閉じずに端末のホームへ移動します。";             // ID 10355(Android の確認の補足)
    public const string ResumeFrom = "次回、ホームの「続きから」で {0}m のチェックポイントから再開できます。"; // ID 10356 {0}=距離(訳が入るまでの代わり。通常は既存の 10010/10011 の文)
    public const string NotResumable = "このランは続きから再開できません(闘技場・操作の練習など、記録しないモード)。"; // ID 10357
    public const string MultiHost = "ルームを解散して退出します。ほかの参加者のプレイも終わります。このランは途中から再開できません。"; // ID 10358
    public const string MultiJoin = "ルームから退出します。このランには途中から戻れません。";                                       // ID 10359
    public const string MultiNoSave = "マルチプレイのランは中断を保存できません。";           // ID 10360(iPhone/iPad の「中断を保存」)
    public const string SuspendSaved = "中断を保存しました。ホームの「続きから」で {0}m から再開できます。"; // ID 10361 {0}=距離
    public const string Cancel = "キャンセル";                                             // 既存の訳あり

    // ---- 画面設定(PC)
    public const string ModeWindow = "ウィンドウ";                                         // 既存の訳あり
    public const string ModeFull = "全画面";                                               // ID 10362(排他の全画面)
    public const string ModeBorderless = "枠なし全画面";                                    // ID 10363(全画面のウィンドウ)
    public const string ScreenSize = "画面サイズ";                                         // ID 10364(行の名前)
    public const string KeepAsk = "この設定を維持しますか？";                               // ID 10365(変更後の確認の見出し)
    public const string RevertIn = "あと{0}秒で元の設定に戻ります";                            // ID 10366 {0}=秒
    public const string Keep = "維持する";                                                 // ID 10367
    public const string Revert = "元に戻す";                                               // 既存の訳あり
    public const string ScreenNote = "表示モードと画面サイズは「適用」後、15秒以内に「維持する」を押さないと元に戻ります。Alt+Enterでウィンドウと全画面を切り替え、ウィンドウの端をドラッグして大きさを変えられます。"; // ID 10368

    // ---- 画面の向き(スマホ)/ 文字・UI の大きさ
    public const string OrientAuto = "自動";                                               // 既存の訳あり
    public const string OrientPortrait = "縦固定";                                          // ID 10369
    public const string OrientLandscape = "横固定";                                         // ID 10370
    public const string UiSize = "文字・UIの大きさ";                                         // ID 10371(行の名前)
    public const string UiStandard = "標準";                                               // 既存の訳あり
    public const string UiLarge = "大きめ";                                                // ID 10372

    // ---- キー設定の説明(前回の残り: Tab / マウスの戻るボタン)
    public const string KeysHelp = "Esc・マウスの戻るボタン: 戻る/閉じる(ホーム・タイトルでは終了確認)。Tab/Shift+Tab: 項目移動。Enter: 決定。Alt+Enter: 全画面切替。"; // ID 10373

    public static readonly string[] All =
    {
        QuitGame, ToDeviceHome, SuspendToDeviceHome, SaveSuspend, QuitAsk, DeviceHomeAsk, BackgroundNote, ResumeFrom, NotResumable, MultiHost, MultiJoin, MultiNoSave, SuspendSaved,
        ModeFull, ModeBorderless, ScreenSize, KeepAsk, RevertIn, Keep, ScreenNote, OrientPortrait, OrientLandscape, UiSize, UiLarge, KeysHelp,
    };
}
