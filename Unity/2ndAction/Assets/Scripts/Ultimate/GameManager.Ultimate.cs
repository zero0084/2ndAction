using UnityEngine;

// #100 ULTIMATE(2026-10-04)から使う GameManager の入口
public partial class GameManager
{
    // このランのキャラ(CONTINUE も同じ)。開始前は選択中のキャラ
    public string ActiveRunCharacterIdForUltimate => !string.IsNullOrEmpty(activeRunCharacterId) ? activeRunCharacterId : SelectedCharacterId ?? "";

    // カード/報酬の選択が開いている
    public bool UltimateChoiceOpen => levelUpPending || ChoiceUiBusy;

    // ボタンを出してよい(ラン中の HUD が出ていて、停止メニュー/選択が開いていない)
    public bool UltimateHudVisible => HasStarted && !AnyOverlayOpen && !IsGameOver && !showPauseMenu && !levelUpPending && !CountdownActive && !ResumeGateActive && !UiInputGate.ModalOpen;

    // 着地まで(+少し)敵/障害物/Formation を出さない。CONTINUE の安全区間と同じ仕組み(長い方を残す)
    public void UltimateSetSafeUntil(float distance)
    {
        safeZoneEndDistance = Mathf.Max(safeZoneEndDistance, distance);
    }
}
