using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public enum DamageResult { Ignored, Hit, GameOver }

    public static GameManager Instance { get; private set; }

    const string GameTitle = "One More Mile";

    const string BestDistanceKey = "BestDistance";
    const string BestTimeKey = "BestTime";
    const string OrientationKey = "PreferredOrientation";
    const string InvincibleKey = "InvincibleMode";
    const string DebugModeKey = "DebugMode";
    const string DeckKey = "DeckCardIds";
    const string TotalMileKey = "TotalOwnedMile";
    const string CharacterCardSlotsKey = "CharacterCardSlots";
    // キャラクター選択画面(2026-09-12) - 「選択キャラクター=次回NEW RUNで
    // 使用するキャラクター」の永続化キー。CharacterCardSlotsKeyと同じ
    // 「単純なPlayerPrefs文字列1つ」パターン(SetSelectedCharacterの
    // コメント参照)。Active Run/Checkpoint(RunCheckpoint.cs)側にはこの
    // 概念自体が存在しない - Continueは常にそのRunが始まった時点の状態を
    // そのまま復元するだけで、ここを勝手に読み書きすることはない。
    const string SelectedCharacterKey = "SelectedCharacterId";
    // ステージ選択導線追加(2026-09-12) - 「選択ステージ=次回NEW RUNで
    // 出発するステージ」の永続化キー。SelectedCharacterKeyと全く同じ
    // パターン。
    const string SelectedStageKey = "SelectedStageId";

    // The deck the player has built out of their (currently: always-owned -
    // there's no unlock/collection system yet) cards - TriggerLevelUpChoice
    // draws its 3-of-N pool from this. Persisted across runs as a
    // comma-separated list of CardDefinition.cardId strings. DeckEditUI's
    // "owned cards" list is just CardDatabase.AllCards.
    public const int DeckCapacity = 10;
    readonly List<string> deckCards = new List<string>();
    public IReadOnlyList<string> DeckCards => deckCards;

    void LoadDeck()
    {
        deckCards.Clear();
        // HasKey (not "did anything actually resolve") is what tells a
        // genuine first run apart from a player who deliberately emptied
        // their deck (DeckEditUI's "全て外す") and had that 0-card deck
        // saved - SaveDeck() always writes the key, even for an empty
        // deck, so a returning player's empty deck must NOT re-trigger the
        // first-run default fill below (0/10 is a valid, persisted choice).
        bool hasSavedDeck = PlayerPrefs.HasKey(DeckKey);
        string saved = PlayerPrefs.GetString(DeckKey, "");
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (string id in saved.Split(','))
            {
                // CardDatabase.FindById filters out any id that no longer
                // exists (e.g. a card removed from the database after this
                // deck was saved), so a stale save can't wedge the deck.
                // カード合成改修(2026-09-26)で見つけた不具合の修正: 同じカードを複数枚
                // デッキへ入れられる仕様(AddToDeck/SetDeck)なのに、読み込み時だけ
                // 重複を捨てていたため、再起動で2枚目以降が消えていた。容量だけで制限する。
                if (!string.IsNullOrEmpty(id) && CardDatabase.FindById(id) != null && deckCards.Count < DeckCapacity)
                {
                    deckCards.Add(id);
                }
            }
        }

        if (!hasSavedDeck)
        {
            // Genuine first run - defaults to the first DeckCapacity
            // unlocked cards by sortOrder. Unlocked-only so a distance-
            // gated card can't end up in a brand new player's starting
            // deck before they've ever earned it.
            //
            // Home Room UI reconstruction pass, item 8 - Deck can no longer
            // hold more copies of a card than the player actually owns (see
            // AddToDeck/SetDeck below), so this one-time starter fill also
            // grants a matching Lv.1 owned copy for each card it seeds the
            // deck with - otherwise a brand new player's own starting deck
            // would immediately violate that invariant.
            foreach (CardDefinition card in CardDatabase.UnlockedCards)
            {
                if (deckCards.Count >= DeckCapacity) break;
                deckCards.Add(card.cardId);
                CardInventory.AddCard(card.cardId, 1, 1);
            }
        }
    }

    void SaveDeck()
    {
        PlayerPrefs.SetString(DeckKey, string.Join(",", deckCards));
        PlayerPrefs.Save();
    }

    // Reward/Card Ownership/Gacha/Fusion System Ver.1, item 6 - the deck
    // used to de-dup by cardId (one copy max per card); that restriction is
    // lifted here ("同じカードを複数回デッキに入れられるようにしてくださ
    // い") so e.g. Speed Up x10 is now a valid deck. Capacity is still the
    // only limit. RemoveFromDeck below already only removed ONE matching
    // entry (List.Remove's own behavior), so it needed no change at all to
    // correctly support duplicates.
    // Home Room UI reconstruction pass, item 8 - "実際に所有しているカー
    // ド数を超えて編成できないようにしてください": capacity is no longer
    // the only limit - a card can't be added past however many copies are
    // actually owned (across every level - Deck itself doesn't track
    // level), counting BOTH current Deck slots and Character Card slots
    // using that same cardId as already "spent" against that total (see
    // GetTotalUsedCount).
    public bool AddToDeck(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || deckCards.Count >= DeckCapacity) return false;
        if (GetTotalUsedCount(cardId) >= CardInventory.GetTotalCount(cardId)) return false;
        deckCards.Add(cardId);
        SaveDeck();
        return true;
    }

    // How many copies of cardId are currently "spent" across Deck and
    // Character Card slots combined - the number AddToDeck/EquipCharacterCard
    // compare against CardInventory.GetTotalCount(cardId) to decide whether
    // one more copy is available.
    public int GetTotalUsedCount(string cardId)
    {
        int count = 0;
        for (int d = 0; d < deckCards.Count; d++) if (deckCards[d] == cardId) count++;
        for (int i = 0; i < CharacterCardSlotCount; i++) if (characterCardIds[i] == cardId) count++;
        return count;
    }

    // Replaces the whole deck in one shot (DeckEditUI's "おすすめ編成" and
    // "全て外す") - a single SaveDeck() call instead of looping
    // AddToDeck/RemoveFromDeck one at a time. Same invariants AddToDeck
    // enforces one at a time (capped at DeckCapacity, ownership-limited,
    // duplicates otherwise allowed); an empty cardIds is exactly how "全て
    // 外す" clears the deck to 0/10.
    public void SetDeck(IEnumerable<string> cardIds)
    {
        deckCards.Clear();
        var perCardCount = new Dictionary<string, int>();
        foreach (string id in cardIds)
        {
            if (string.IsNullOrEmpty(id) || deckCards.Count >= DeckCapacity) continue;
            int owned = CardInventory.GetTotalCount(id);
            int charCardUses = 0;
            for (int i = 0; i < CharacterCardSlotCount; i++) if (characterCardIds[i] == id) charCardUses++;
            perCardCount.TryGetValue(id, out int already);
            if (already + charCardUses >= owned) continue; // no more copies of this card left to spend
            perCardCount[id] = already + 1;
            deckCards.Add(id);
        }
        SaveDeck();
    }

    // A 0-card deck is an allowed, explicit player choice (see "0枚デッキ")
    // - TriggerLevelUpChoice already handles an empty pool safely (no card
    // choice screen, run keeps going), so nothing here needs to keep a
    // minimum count anymore.
    public bool RemoveFromDeck(string cardId)
    {
        bool removed = deckCards.Remove(cardId);
        if (removed) SaveDeck();
        return removed;
    }

    // Home Room UI reconstruction pass, item 11 - the Fusion screen must
    // show EVERY owned stack, including ones partly or fully "spent" on
    // Deck/Character Card slots, with enough info to mark them locked
    // rather than just hiding them. Deck doesn't track level, so its
    // aggregate per-cardId usage is allocated across that cardId's owned
    // stacks in ascending level order (spend the "basic" Lv.1 copies into
    // the Deck first, leaving higher-level copies free for Fusion/Convert/
    // Character Card use) - a simple, deterministic rule rather than exact
    // tracking, since Deck fundamentally doesn't know which specific level
    // it's holding.
    public int GetDeckLockedCountForStack(string cardId, int level)
    {
        int deckCount = 0;
        for (int d = 0; d < deckCards.Count; d++) if (deckCards[d] == cardId) deckCount++;
        if (deckCount <= 0) return 0;

        var levels = new List<int>();
        foreach (CardInventory.Stack s in CardInventory.Stacks) if (s.cardId == cardId) levels.Add(s.level);
        levels.Sort();

        int remaining = deckCount;
        foreach (int lv in levels)
        {
            int stackCount = CardInventory.GetCount(cardId, lv);
            int consume = Mathf.Min(remaining, stackCount);
            if (lv == level) return consume;
            remaining -= consume;
            if (remaining <= 0) break;
        }
        return 0;
    }

    // Character Card slots reference an exact (cardId, level) pair, so this
    // one is a direct count (no allocation needed, unlike Deck above).
    public int GetCharacterCardLockedCountForStack(string cardId, int level)
    {
        int locked = 0;
        for (int i = 0; i < CharacterCardSlotCount; i++)
        {
            if (characterCardIds[i] == cardId && characterCardLevels[i] == level) locked++;
        }
        return locked;
    }

    // How many copies of this exact (cardId, level) stack are free to
    // Fuse/Convert right now - the stack's owned count minus whatever's
    // locked into Deck/Character Card slots. Clamped at 0 (never negative)
    // even if the two locks together exceed the owned count due to some
    // edge-case desync, so a caller can always trust this as a safe cap.
    public int GetAvailableCountForStack(string cardId, int level)
    {
        int owned = CardInventory.GetCount(cardId, level);
        int locked = GetDeckLockedCountForStack(cardId, level) + GetCharacterCardLockedCountForStack(cardId, level);
        return Mathf.Max(0, owned - locked);
    }

    // ===== Reward/Card Ownership/Gacha/Fusion System Ver.1 - MILE wallet ===== //
    // Persisted total, separate from a run's own provisional total below
    // (RunDistanceMile/RunEnemyMile/RunBossMile/RunMile) per the brief's
    // "できれば今のうちにRunMileとTotalOwnedMileを分けておいてください" -
    // FINISH/ONE MORE MILE isn't implemented yet, so every run's MILE is
    // awarded in full and unconditionally at FinishRun (see its own
    // comment), but the split is already in place for when that changes.
    public int TotalOwnedMile { get; private set; }

    void LoadMile()
    {
        TotalOwnedMile = PlayerPrefs.GetInt(TotalMileKey, 0);
    }

    void SaveMile()
    {
        PlayerPrefs.SetInt(TotalMileKey, TotalOwnedMile);
        PlayerPrefs.Save();
    }

    // カード合成の確定処理用 - 値だけ変えてPlayerPrefsへ書き、Save()は呼び出し側が
    // 所持カードと一緒に1回だけ行う(CardFusionLogic.Execute)。
    public void AddMileWithoutFlush(int amount)
    {
        if (amount == 0) return;
        TotalOwnedMile = Mathf.Max(0, TotalOwnedMile + amount);
        PlayerPrefs.SetInt(TotalMileKey, TotalOwnedMile);
    }

    public void AddMile(int amount)
    {
        if (amount == 0) return;
        TotalOwnedMile = Mathf.Max(0, TotalOwnedMile + amount);
        SaveMile();
    }

    public bool TrySpendMile(int amount)
    {
        if (amount <= 0 || TotalOwnedMile < amount) return false;
        TotalOwnedMile -= amount;
        SaveMile();
        return true;
    }

    // This run's provisional MILE breakdown - DISTANCE finalizes at
    // FinishRun (see its own comment), ENEMIES/BOSSES accumulate live as
    // RegisterEnemyKill/RegisterBossDefeat fire. RunMile is their sum -
    // what the Result screen shows as "+N MILE", and what gets banked into
    // TotalOwnedMile once (also at FinishRun).
    public int RunDistanceMile { get; private set; }
    public int RunEnemyMile { get; private set; }
    public int RunBossMile { get; private set; }
    public int RunMile => RunDistanceMile + RunEnemyMile + RunBossMile;

    // ===== Run Continuation/Checkpoint Ver.1 ===== //
    // Item 12 - the furthest distance genuinely reached this Run's whole
    // lifetime, independent of any later CONTINUE-triggered rewalk of
    // already-covered ground. Distance MILE at FinishRun uses
    // Max(MaxDistance, HighestReachedDistance) so a shorter post-continue
    // segment can never reduce it, and re-crossing already-covered ground
    // can never grant it twice (see ReportDistance/FinishRun).
    public float HighestReachedDistance { get; private set; }

    // Item 1/2 - a real Run can only be ended by FINISH (successful 3s
    // escape) or GAME OVER (HP 0). Kept as the Boss Gate's own first
    // milestone distance (BossManager's schedule already starts here
    // independently) - no longer read by EscapeAvailable itself, see
    // escapeUnlocked below.
    public float escapeMinDistance = 1000f;
    // Bugfix 2026-09-06 - "ESCAPE解禁を1000m到達からBoss撃破後へ変更".
    // EscapeAvailable used to be a pure function of MaxDistance (true the
    // instant 1000m was crossed, even mid-fight against the first Boss).
    // escapeUnlocked is now a one-way Run-lifetime flag, set true only once
    // - by GameManager exactly where the FIRST Boss Reward finishes
    // resolving (see the wasBossReward branches in ApplyUpgradeByCardId/
    // RunBossRewardChoice's empty-pool path) - and restored from
    // RunCheckpoint on CONTINUE so an already-earned Escape survives an
    // interruption. Never reset back to false within the same Run; a new
    // Run (GAME OVER/FINISH) always gets a fresh GameManager instance where
    // this defaults to false again.
    bool escapeUnlocked;
    public bool EscapeAvailable => HasStarted && !IsGameOver && escapeUnlocked;

    // Item 3 - one-shot "ESCAPE AVAILABLE" banner the first time
    // EscapeAvailable flips true this Run (see Update()/DrawEscapeUI).
    bool escapeAvailableAnnounced;
    float escapeAvailableBannerTimer;
    public float escapeAvailableBannerDuration = 2.2f;

    // Item 14 - "Checkpoint -> Player出現 -> 短い安全な走行区間 -> 通常生成
    // 開始": suppresses new Enemy/Formation/Wall spawning (NOT HP/
    // invincibility - "ゲーム上有利になる必要はありません") for a short
    // distance right after a CONTINUE, so a reload can't drop the player
    // directly into an unavoidable hit. TerrainManager/EnemyWallManager
    // both check this (see IsInSafeZone).
    public float safeZoneLength = 15f;
    float safeZoneEndDistance = -1f;
    public bool IsInSafeZone => safeZoneEndDistance > 0f && MaxDistance < safeZoneEndDistance;

    // Item 6/7 - Boss Reward reuses the exact same pending-choice machinery
    // as a normal Level Up (pendingChoices/levelUpPending/RewardCardSequence)
    // so only ONE card-choice screen can ever be in flight at a time,
    // rather than risking two independent systems trying to show/pause
    // concurrently. This just tags which one is currently in flight, so
    // ApplyUpgradeByCardId knows whether to also SaveCheckpoint() (Boss
    // Reward only - a normal mid-run Level Up is not a checkpoint moment)
    // and RewardCardSequence knows which announcement text to show.
    // Bugfix 2026-09-08 (Bug #001 診断フェーズ) - widened from private to
    // public purely so BossDiagnostics (a separate class) can read/report
    // this in its Freeze Snapshot without GameManager needing to expose a
    // duplicate string-typed accessor - no other behavior change.
    public enum PendingChoiceKind { LevelUp, BossReward }
    PendingChoiceKind pendingChoiceKind = PendingChoiceKind.LevelUp;

    // Presentation Priority pass, extended - same deferred-until-Boss-
    // Presentation-finishes pattern levelUpDeferredPending already uses
    // (see TriggerLevelUpChoice/UpdateDeferredLevelUp), just for Boss
    // Reward instead. The two also defer to EACH OTHER via levelUpPending
    // (shared field) so they can never show concurrently.
    bool bossRewardDeferredPending;
    float bossRewardDeferredTimer = -1f;

    // Item 9 - small in-Gameplay Pause/Menu (RESUME / RETURN TO HOME).
    bool showPauseMenu;
    bool showReturnHomeConfirm;
    // Item 13 - confirm before discarding an Active Run to start fresh.
    bool showNewRunConfirm;

    // ===== Character Card slots (item 5) ===== //
    // A new, separate-from-Deck slot type: always active from the moment a
    // run starts (see ApplyCharacterCardEffects, called from StartGame),
    // max 3, duplicates across slots allowed. Stores which OWNED (cardId,
    // level) stack is equipped in each slot - level matters here (unlike
    // Deck, which doesn't track levels at all) since a fused, leveled-up
    // card is exactly the kind of thing worth dedicating a Character Card
    // slot to.
    public const int CharacterCardSlotCount = 3;
    readonly string[] characterCardIds = new string[CharacterCardSlotCount];
    readonly int[] characterCardLevels = new int[CharacterCardSlotCount];
    public IReadOnlyList<string> CharacterCardIds => characterCardIds;
    public int GetCharacterCardLevel(int slot) => (slot >= 0 && slot < CharacterCardSlotCount) ? Mathf.Max(1, characterCardLevels[slot]) : 1;

    // ===== キャラクター選択(2026-09-12) ===== //
    // 「どの主人公を使うか」の永続化 - Homeでいつでも変更できる「次回
    // NEW RUNの既定値」。プレイアブル主人公追加(2026-09-12、お嬢様騎士)
    // 以降は実際の戦闘性能にも反映されるが、あくまで"次回"の既定値であり、
    // 既にActiveなRun自身のキャラクター(RunCheckpoint.Data.characterId/
    // activeRunCharacterId)には一切影響しない。
    public string SelectedCharacterId { get; private set; }

    // Run開始時(StartGame)にSelectedCharacterIdのスナップショットとして
    // 記録し、以後そのRunがずっと使い続けるキャラクターID。Continueでは
    // RunCheckpoint.Data.characterId(保存済みのRunが実際に使っていた値)
    // から復元する - SelectedCharacterIdをそのまま使わないのは、Continue
    // より前にHomeでCharacter Selectの選択を変えてしまった場合にRunの
    // キャラクターが差し替わってしまうのを防ぐため(マスターの明示要件)。
    string activeRunCharacterId;

    // ステージ選択導線追加(2026-09-12) - activeRunCharacterIdと全く同じ
    // 理由・同じ役割。Run開始時にSelectedStageIdをスナップショットし、
    // Continueでは必ずRunCheckpoint.Data.stageId(保存済みのRunが実際に
    // 出発したステージ)から復元する - Homeで選択ステージを変えても既に
    // Activeなrunには影響しない。
    string activeRunStageId;
    // ObstacleSpawner等、「今このRunがどのステージなのか」をゲームプレイ
    // ロジック側から参照する必要がある場所向けの読み取り専用アクセサ。
    public string ActiveRunStageId => activeRunStageId;

    // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - Run開始時
    // (StartGame/BeginContinuedRunの両方、ApplyCharacterCardEffectsより
    // 前)に一度だけ呼ばれ、選択中/保存済みキャラクターのベース性能を
    // maxLives/Lives、およびPlayerController側の各種性能へ適用する。
    // defがnull(未知のcharacterId等の異常系)の場合は何もせず、Awake()で
    // 既に設定済みの既定値のまま進む(安全側 - 黒剣士相当のまま)。
    void ApplyCharacterBaseStats(CharacterDefinition def)
    {
        if (def == null) return;
        maxLives = def.baseMaxLives;
        Lives = def.baseLives;
        if (PlayerController.Instance != null)
        {
            PlayerController.Instance.ApplyCharacterBaseStats(def);
            // キャラクター専用アニメーション差し替え(2026-09-13) - 見た目
            // (Sprite)側はPlayerAnimatorが別コンポーネントとして持つため、
            // ここでもう一段委譲する。
            PlayerAnimator animator = PlayerController.Instance.GetComponent<PlayerAnimator>();
            if (animator != null) animator.ApplyCharacterAnimationSet(def);
        }
    }

    void LoadSelectedCharacter()
    {
        string saved = PlayerPrefs.GetString(SelectedCharacterKey, "");
        // 未保存(初回起動)、または保存値がデータベースに存在しない(アセ
        // ットが削除された等)場合は、CharacterDatabaseの先頭(=黒剣士、
        // CharacterDatabaseBuilder.Specsのswordsman、sortOrder=0)へ安全に
        // フォールバックする。現在の黒剣士の戦闘性能・スプライトはこの値
        // に一切影響を受けないため、フォールバックしても実際のプレイには
        // 何の影響もない。
        if (!string.IsNullOrEmpty(saved) && CharacterDatabase.FindById(saved) != null)
        {
            SelectedCharacterId = saved;
            return;
        }
        var all = CharacterDatabase.AllCharacters;
        SelectedCharacterId = all.Count > 0 ? all[0].characterId : null;
    }

    // Character Select画面のSELECTからのみ呼ばれる。「選択キャラクター=
    // 次回NEW RUNで使用するキャラクター」という仕様どおり、Active Run/
    // Checkpoint(RunCheckpoint.cs)には一切触れない - RunCheckpointはそもそ
    // も「どのキャラクターで走っているか」という概念自体を持たないため、
    // Continue中のRunがこの変更で差し替わることは構造的に起こり得ない。
    public void SetSelectedCharacter(string characterId)
    {
        if (string.IsNullOrEmpty(characterId) || CharacterDatabase.FindById(characterId) == null) return;
        SelectedCharacterId = characterId;
        PlayerPrefs.SetString(SelectedCharacterKey, characterId);
        PlayerPrefs.Save();
    }

    // ===== ステージ選択(2026-09-12) ===== //
    // 「どのステージへ出発するか」の永続化 - Homeでいつでも変更できる
    // 「次回NEW RUNの既定値」。SelectedCharacterIdと全く同じ設計、CONTINUE
    // には一切影響しない(RunCheckpoint.Data.stageIdがそのRun自身の値を
    // 別途保持する - activeRunStageIdのコメント参照)。
    public string SelectedStageId { get; private set; }

    void LoadSelectedStage()
    {
        string saved = PlayerPrefs.GetString(SelectedStageKey, "");
        StageDefinition savedDef = StageDatabase.FindById(saved);
        if (!string.IsNullOrEmpty(saved) && savedDef != null && savedDef.unlocked)
        {
            SelectedStageId = saved;
            return;
        }
        // 未保存、保存値が存在しない、または保存値が(その後ロックされた
        // 等で)未開放の場合は、開放済みの先頭ステージへ安全にフォール
        // バックする(sortOrder順、CharacterDatabase.LoadSelectedCharacter
        // と同じ考え方)。
        foreach (StageDefinition def in StageDatabase.AllStages)
        {
            if (def.unlocked) { SelectedStageId = def.stageId; return; }
        }
        SelectedStageId = null;
    }

    // Stage Select画面のDepartFromStageSelectからのみ呼ばれる。「選択
    // ステージ=次回NEW RUNで出発するステージ」という仕様どおり、Active
    // Run/Checkpoint(RunCheckpoint.cs)には一切触れない。
    public void SetSelectedStage(string stageId)
    {
        StageDefinition def = StageDatabase.FindById(stageId);
        if (def == null || !def.unlocked) return;
        SelectedStageId = stageId;
        PlayerPrefs.SetString(SelectedStageKey, stageId);
        PlayerPrefs.Save();
    }

    void LoadCharacterCards()
    {
        string saved = PlayerPrefs.GetString(CharacterCardSlotsKey, "");
        string[] entries = saved.Split(',');
        for (int i = 0; i < CharacterCardSlotCount; i++)
        {
            characterCardIds[i] = null;
            characterCardLevels[i] = 1;
            if (i >= entries.Length || string.IsNullOrEmpty(entries[i])) continue;
            // Each entry is "cardId:level" - see SaveCharacterCards.
            string[] parts = entries[i].Split(':');
            string id = parts[0];
            if (string.IsNullOrEmpty(id) || CardDatabase.FindById(id) == null) continue;
            int level = 1;
            if (parts.Length > 1) int.TryParse(parts[1], out level);
            characterCardIds[i] = id;
            characterCardLevels[i] = Mathf.Max(1, level);
        }
    }

    void SaveCharacterCards()
    {
        string[] entries = new string[CharacterCardSlotCount];
        for (int i = 0; i < CharacterCardSlotCount; i++)
        {
            entries[i] = string.IsNullOrEmpty(characterCardIds[i]) ? "" : $"{characterCardIds[i]}:{characterCardLevels[i]}";
        }
        PlayerPrefs.SetString(CharacterCardSlotsKey, string.Join(",", entries));
        PlayerPrefs.Save();
    }

    // Only allowed to equip a card the player actually owns at least one
    // copy of (any level) - cardId null/empty unequips that slot instead.
    public bool EquipCharacterCard(int slot, string cardId, int level)
    {
        if (slot < 0 || slot >= CharacterCardSlotCount) return false;
        if (string.IsNullOrEmpty(cardId))
        {
            characterCardIds[slot] = null;
            characterCardLevels[slot] = 1;
            SaveCharacterCards();
            return true;
        }
        if (CardDatabase.FindById(cardId) == null) return false;
        // Home Room UI reconstruction pass - re-selecting the exact stack
        // already equipped in this slot is always allowed regardless of
        // availability (it's a no-op, not a new "use" of the pool); any
        // other stack must have at least 1 copy free after Deck/other
        // Character Card slots' own locks (see GetAvailableCountForStack).
        bool sameAsCurrent = characterCardIds[slot] == cardId && characterCardLevels[slot] == level;
        if (!sameAsCurrent && GetAvailableCountForStack(cardId, level) <= 0) return false;
        characterCardIds[slot] = cardId;
        characterCardLevels[slot] = Mathf.Max(1, level);
        SaveCharacterCards();
        return true;
    }

    // Item 14 safety guard - "現在キャラクターカードとして装備中、または
    // デッキに入っているカードは合成/MILE化できない". Deliberately name-
    // based (not owned-copy-based) - Deck's pool is the separate distance-
    // unlock system, not CardInventory, so this is the simple, conservative
    // "is this card name doing anything active right now" check rather than
    // trying to reconcile two independent ownership concepts.
    public bool IsCardInUse(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return false;
        for (int i = 0; i < CharacterCardSlotCount; i++)
        {
            if (characterCardIds[i] == cardId) return true;
        }
        return deckCards.Contains(cardId);
    }

    // "常時有効" - applied once, right as a run actually begins (both
    // StartGame code paths call this immediately after HasStarted flips
    // true), via ApplyCardEffectsStacked below - Card Level Ver.1's
    // "Lv.N = N回取得相当" rule applied verbatim (see that method's own
    // comment). Run-time Level Up picks of the same card (ApplyUpgradeByCardId
    // -> ApplyCardEffects, unchanged) then stack additively on top of
    // whatever this already applied, exactly as specified (item 3).
    void ApplyCharacterCardEffects()
    {
        for (int i = 0; i < CharacterCardSlotCount; i++)
        {
            string id = characterCardIds[i];
            if (string.IsNullOrEmpty(id)) continue;
            CardDefinition card = CardDatabase.FindById(id);
            if (card == null) continue;
            // カード合成改修(2026-09-26) - 合成カード(v2キー)は能力ごとの強化量が
            // 定義(effects)に既に含まれているので1回だけ適用する(合成Lvを掛けると
            // 二重適用になる)。素のカードIDは常にLv.1=1回。
            int stacks = CardVariant.IsVariantKey(id) ? 1 : Mathf.Max(1, characterCardLevels[i]);
            ApplyCardEffectsStacked(card, stacks);
            // Bugfix 2026-09-05, item 4 - "Card Lv表示だけ増えて実Effectが
            // 1回しか適用されていないケースがないか". Code review found the
            // stacking path itself (ApplyCardEffectsStacked -> N calls to
            // ApplyCardEffects, each Add*/+= accumulating on PlayerController/
            // GameManager fields) structurally correct, but this log lets it
            // be confirmed for real on-device rather than by review alone.
            if (DebugMode) Debug.Log($"[CardStack] CharacterCard slot {i}: {card.cardName} equippedLv={stacks} -> ApplyCardEffects called {stacks}x at Run start (runStackSoFar={GetCurrentRunStack(card.cardId)})");
        }
    }

    public float retryDelayAfterGameOver = 3f;

    [Header("Lives")]
    public int startingLives = 3;
    public int maxLives = 5;
    public int maxLivesCap = 10;

    [Header("Leveling")]
    // EXP trickles in from distance covered, plus lump sums from kills -
    // tuned so a level-up happens every few hundred meters early on, sooner
    // if the player's also fighting, and gradually less often as the
    // per-level cost grows.
    public float expPerMeter = 1f;
    public float enemyKillExp = 15f;
    public float bossKillExp = 100f;
    public float expBaseForLevel2 = 150f;
    public float expGrowthPerLevel = 60f;

    // Every card's actual magnitudes now live on its CardDefinition asset
    // (Assets/Resources/Cards/*.asset) instead of fixed fields here - see
    // ApplyCardEffects. Accumulated from EXP UP / VAMPIRE / GREED cards;
    // start at their "no effect" values.
    float expGainMultiplier = 1f;
    float lifestealChance;
    float lifestealAmount;
    public float EnemySpawnRateMultiplier { get; private set; } = 1f;
    // Card Expansion/Gacha Evolution Ver.1 - High Risk card family
    // (Tough/Fast/Elite Enemies, Hell Mode, Boss Challenge/Rush,
    // Pandemonium, etc.) - see DistanceTierManager.EnemyHpFor/
    // BossManager.EffectiveBossMaxHp/RegisterEnemyKill/RegisterBossDefeat
    // for where each of these is actually read.
    public float EnemyHpMultiplier { get; private set; } = 1f;
    public float BossHpMultiplier { get; private set; } = 1f;
    public float MileGainMultiplier { get; private set; } = 1f;
    public float BossMileGainMultiplier { get; private set; } = 1f;

    [Header("Top Screen")]
    public Texture2D titleLogo;
    public Texture2D topBackground;
    public Texture2D topCloud;
    // 環境アニメーション構造修正依頼(2026-09-18) - カーテン単体の透過素材
    // (DrawCurtainSway参照)。topBackground自体は既にカーテンを消した版。
    public Texture2D homeCurtain;
    // Decorative navy+gold+blue-accent frame (Assets/Art/UI/OrnateFrame.png)
    // for START/DECK/BEST - see OrnateUi, assigned to its static field in
    // Awake().
    public Texture2D ornateFrame;

    // TOP screen intro fade - counts up from 0 every time this component
    // wakes up fresh (Awake/scene load), only while still on the title
    // screen (see Update) - no explicit "returned to title" reset needed
    // since the only way back to a fresh title screen is Retry(), which
    // reloads the whole scene. Logo/START+DECK/BEST each read a windowed
    // slice of this in OnGUI to fade in staggered, once, on first showing.
    float titleIntroTimer;
    // A brief brightness flash on START right when it's tapped ("軽い発光" -
    // see DrawStyledButton's ornateFlashAlpha param) - set to
    // startPressFlashDuration the moment StartGame() fires, ticks back down
    // to 0 in Update().
    public float startPressFlashDuration = 0.25f;
    float startPressFlashTimer;

    // ===== Home Room UI reconstruction pass ===== //
    // The exact on-screen Rect the room background was last drawn into
    // (see OnGUI) - every tap hotspot below is defined as a fraction of
    // THIS rect (not raw Screen.width/height) so they stay aligned with
    // the actual painted room objects regardless of device aspect ratio.
    Rect bgRoomRect;

    // Home画面改善依頼⑦(2026-09-16), item 8 - doorRect(下のOnGUI内で
    // FracRect(bgRoomRect, 0.40f, 0.14f, 0.565f, 0.65f)として定義)のx0/x1
    // の中点をそのまま定数化したもの。ロゴ・NEXT STAGEの中心をこの値へ
    // 揃えることで「扉の中心を基準に縦軸を揃える」を実現する。doorRectの
    // フラクションを変える場合は、この値も必ず一緒に更新すること。
    const float DoorCenterFrac = (0.40f + 0.565f) / 2f;

    // "少し光る" tap feedback - each hotspot gets its own brief flash timer,
    // same decay pattern as startPressFlashTimer above (ticks down in
    // Update(), unscaled).
    public float roomHotspotFlashDuration = 0.22f;
    float doorHotspotFlashTimer;
    float bedHotspotFlashTimer;
    float bookHotspotFlashTimer;
    float deskHotspotFlashTimer;
    // キャラクター選択画面(2026-09-12) - Home左上の新規ホットスポット用。
    float characterHotspotFlashTimer;

    // Desk "CARD GACHA" machine prop, drawn directly onto the room scene
    // (not its own screen/canvas) - see SceneBuilder for the import.
    public Texture2D gachaMachineTexture;
    // Home画面 / Stage Select改善依頼(2026-09-16), item2 - Characterの肖像画
    // (壁に飾られた額縁)用のフレーム画像(ChatGPT生成、透明中央窓+木/金の
    // 縁)。DrawCharacterHotspot参照 - フレーム自身のアスペクト比を保った
    // まま表示し、その内側の透明窓(実測、フレーム自身の幅80.2%×高さ
    // 79.5%・x=9.85%~90.06%/y=13.02%~92.51%)へ選択中キャラのポートレート
    // を重ねる。
    public Texture2D portraitFrameTexture;
    // Home画面改善依頼⑨(2026-09-17), item1 - 「壁に長く飾られた装飾画」に
    // 寄せるための紙/キャンバス質感の経年風オーバーレイ(ChatGPT生成予定、
    // 未生成の間はnullのまま安全にスキップ - DrawCharacterHotspot参照)。
    public Texture2D portraitAgingOverlayTexture;
    // Home環境アニメーション強化+肖像画背景追加依頼(2026-09-17) -
    // キャラportraitテクスチャ自身が透明背景の切り抜きなので、額縁の窓
    // いっぱいにこの共通背景(暗い油彩風、ChatGPT生成)を先に敷いてから
    // portraitを重ねる - 「切り抜きを貼った」感を減らし、額縁の中で1枚の
    // 絵として成立させる。全キャラ共通(キャラごとに用意しない)。
    public Texture2D portraitBackdropTexture;

    // Home待機演出(2026-09-21) - HomeIdleFx.cs参照。扉の葉/開口部の奥の光/
    // ベッド上のカード(A,B,C,D,E,G,H)は背景から分離した独立の透過素材。
    // 背景(topBackground)側は、これらを除去して補完済みの版。
    public Texture2D homeDoorLeaf;
    public Texture2D homeDoorBackdrop;
    public Texture2D[] homeIdleCards;
    // 待ち時間/揺れ幅/動作時間/発光量/コイン出現率/粒子数などの調整値。
    public HomeIdleSettings homeIdle = new HomeIdleSettings();
    HomeIdleFx idleFx;

    HomeIdleFx GetIdleFx()
    {
        if (idleFx == null) idleFx = new HomeIdleFx(homeIdle);
        idleFx.S = homeIdle;
        idleFx.doorLeaf = homeDoorLeaf;
        idleFx.doorBackdrop = homeDoorBackdrop;
        idleFx.cards = homeIdleCards;
        return idleFx;
    }
    // Ver.1 finishing pass, item 8 - "短いSE" tap feedback for the room's
    // hotspots (door/bed/book/desk). Reuses the existing Card Select SE
    // (already imported for RewardCardSequence) rather than adding new
    // audio - a plain UI-tap-shaped sound fits this just as well.
    public AudioClip roomTapSe;
    public const int GachaCostMile = 500;
    // Brief shake+glow before the result actually reveals - purely a delay/
    // feedback window, the MILE spend and card grant already happened the
    // instant the machine was tapped (see OnGachaMachineTapped).
    public float gachaMachineShakeDuration = 0.5f;
    float gachaMachineShakeTimer;
    CardDefinition pendingGachaCard;
    bool gachaResultOpen;
    // Card Expansion/Gacha Evolution Ver.1, item 14/15 - keeping the whole
    // CardDefinition (instead of just name/icon strings) lets the Result
    // popup read .rarity/.RarityStars directly for the star display and
    // the stronger ★4/★5 reveal below, with no new duplicate fields.
    CardDefinition gachaResultCard;
    int gachaResultOwnedCount;
    // Item 14 - "★4/★5は少し強めのReveal" - a brief extra glow pulse timer,
    // separate from gachaMachineShakeTimer (which ends before the Result
    // popup even opens) so it can animate independently once the popup is
    // showing, without extending the shake/crank itself.
    float gachaResultRevealTimer;
    const float GachaResultRevealDuration = 0.8f;
    // "NOT ENOUGH MILE" brief toast (item 5) - no screen/dialog needed.
    public float gachaInsufficientMessageDuration = 1.4f;
    float gachaInsufficientMessageTimer;

    [Header("Reward Card Sequence")]
    // The card-draw presentation for level-up choices (deck -> draw -> flip
    // -> select -> confirm) - see RewardCardSequence. Purely presentational;
    // it only ever gets fed data built from MakeCardData and hands back the
    // chosen card's id to ApplyUpgradeByCardId.
    public RewardCardSequence rewardCardSequence;

    [Header("Deck Edit")]
    public DeckEditUI deckEditUI;
    // While true, the TOP screen's own IMGUI (background/START/RESET
    // SCORE/orientation/BGM/SE/etc.) is skipped so it can't be clicked
    // through the Deck Edit canvas sitting on top of it.
    bool deckEditOpen;

    public void OpenDeckEdit()
    {
        if (deckEditUI == null) return;
        // Presentation pass - TOP->DECK now goes through the shared wipe
        // (see ScreenTransitionManager); deckEditOpen/deckEditUI.Open() only
        // fire once the screen is 100% covered, so the DECK canvas building
        // itself is never visible mid-flight. Falls back to the old instant
        // open if a scene was built before this manager existed.
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                deckEditOpen = true;
                deckEditUI.Open();
            });
            return;
        }
        deckEditOpen = true;
        deckEditUI.Open();
    }

    public void CloseDeckEdit()
    {
        deckEditOpen = false;
    }

    // Home Room UI reconstruction pass - renamed from the old combined
    // "CardMenuUI" (Gacha+CharacterCard+Fuse+Convert in one screen) to
    // CardFusionUI, now Fusion-only (item 10, "ここは独立画面でOK") -
    // Gacha moved inline onto the TOP room (see OnGachaMachineTapped
    // below), Character Cards/Convert moved into the Card Edit screen
    // (DeckEditUI). Opened the same way DECK is (shared Gold Slash wipe,
    // same "TOP's own IMGUI must not be clickable through the canvas
    // sitting on top of it" reasoning - see AnyOverlayOpen below).
    public CardFusionUI cardFusionUI;
    bool cardFusionOpen;

    // キャラクター選択画面(2026-09-12) - DeckEdit/CardFusionと全く同じ
    // 「ScreenTransitionManagerのGold Slash Wipeが完全に覆ってから開く」
    // 開閉パターン(OpenDeckEdit/OpenCardFusionのコメント参照)。
    public CharacterSelectUI characterSelectUI;
    bool characterSelectOpen;

    // ステージ選択導線追加(2026-09-12) - CharacterSelectと全く同じ
    // 開閉パターン。
    public StageSelectUI stageSelectUI;
    bool stageSelectOpen;

    // Every OnGUI guard that used to check "!deckEditOpen" alone now also
    // needs to hide while the Card Fusion overlay is open - folded into
    // one helper so those call sites don't need two separate negated
    // conditions each.
    bool AnyOverlayOpen => deckEditOpen || cardFusionOpen || characterSelectOpen || stageSelectOpen;
    public bool IsOverlayOpen => AnyOverlayOpen;

    public void OpenCharacterSelect()
    {
        if (characterSelectUI == null) return;
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                characterSelectOpen = true;
                characterSelectUI.Open();
            });
            return;
        }
        characterSelectOpen = true;
        characterSelectUI.Open();
    }

    public void CloseCharacterSelect()
    {
        characterSelectOpen = false;
    }

    public void OpenStageSelect()
    {
        if (stageSelectUI == null) return;
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                stageSelectOpen = true;
                stageSelectUI.Open();
            });
            return;
        }
        stageSelectOpen = true;
        stageSelectUI.Open();
    }

    public void CloseStageSelect()
    {
        stageSelectOpen = false;
    }

    public void OpenCardFusion()
    {
        if (cardFusionUI == null) return;
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                cardFusionOpen = true;
                cardFusionUI.Open();
            });
            return;
        }
        cardFusionOpen = true;
        cardFusionUI.Open();
    }

    public void CloseCardFusion()
    {
        cardFusionOpen = false;
    }

    // ===== Home Room - inline Gacha (item 4) ===== //
    // Called every frame while !HasStarted (see Update()) - decays every
    // hotspot's flash timer and drives the Gacha machine's brief shake-then-
    // reveal sequence (MILE spend/card grant already happened synchronously
    // the instant the machine was tapped; this is purely the presentation
    // delay before the Result popup actually opens).
    void UpdateHomeRoom()
    {
        if (doorHotspotFlashTimer > 0f) doorHotspotFlashTimer -= Time.unscaledDeltaTime;
        if (bedHotspotFlashTimer > 0f) bedHotspotFlashTimer -= Time.unscaledDeltaTime;
        if (bookHotspotFlashTimer > 0f) bookHotspotFlashTimer -= Time.unscaledDeltaTime;
        if (deskHotspotFlashTimer > 0f) deskHotspotFlashTimer -= Time.unscaledDeltaTime;
        // 不具合修正(2026-09-12、ステージ選択導線追加のついでに発見) -
        // characterHotspotFlashTimerがこの減衰リストに元々含まれておらず、
        // Characterホットスポットをタップした後、金色のタップフラッシュが
        // 消えずに表示され続けたままになる不具合があった(前回パスの
        // 見落とし)。
        if (characterHotspotFlashTimer > 0f) characterHotspotFlashTimer -= Time.unscaledDeltaTime;
        if (gachaInsufficientMessageTimer > 0f) gachaInsufficientMessageTimer -= Time.unscaledDeltaTime;

        if (gachaMachineShakeTimer > 0f)
        {
            gachaMachineShakeTimer -= Time.unscaledDeltaTime;
            if (gachaMachineShakeTimer <= 0f && pendingGachaCard != null)
            {
                gachaResultOpen = true;
                gachaResultCard = pendingGachaCard;
                gachaResultOwnedCount = CardInventory.GetTotalCount(pendingGachaCard.cardId);
                gachaResultRevealTimer = pendingGachaCard.rarity >= 4 ? GachaResultRevealDuration : 0f;
                pendingGachaCard = null;
            }
        }
        if (gachaResultRevealTimer > 0f) gachaResultRevealTimer -= Time.unscaledDeltaTime;
    }

    // Tapping the desk machine directly runs the Gacha (item 4) - no
    // dedicated Gacha screen/button. "AvailableCardPool" - CardDatabase.
    // AllCards uniformly for Ver.1 (no rarity/weighting, no distance gate
    // yet - a future distance-based pool expansion slots in right here
    // without touching anything else).
    // Card Expansion/Gacha Evolution Ver.1, item 10/17 - the Gacha's
    // "which cards can appear at all" filter (GachaStage.IsCardEligible) and
    // "how likely is each Rarity" weighting (gachaRarityWeights) are two
    // separate, independently-tunable steps - see BuildGachaPool/
    // DrawFromGachaPool below.
    public float[] gachaRarityWeights = { 50f, 30f, 15f, 4f, 1f };
    public int CurrentGachaStage => GachaStage.StageForDistance(BestDistance);

    // Item 13 - Gacha Visual Evolution placeholder tint, applied on top of
    // the existing tap-glow lerp in the Home Room's machine drawing code.
    // Stage1 OLD/BATTERED (dull/desaturated) -> Stage2 REPAIRED (neutral
    // white, i.e. the machine's original look) -> Stage3 MECHANICAL (cyan
    // light) -> Stage4 MAGICAL (gold energy) -> StageMAX DEATH-TOUCHED
    // (dark metal + purple). Swap any case to Color.white once real
    // per-stage art exists - nothing else needs to change.
    static Color GachaMachineStageTint(int stage)
    {
        switch (stage)
        {
            case 1: return new Color(0.8f, 0.74f, 0.62f);
            case 2: return Color.white;
            case 3: return new Color(0.72f, 0.95f, 1f);
            case 4: return new Color(1f, 0.88f, 0.5f);
            default: return new Color(0.55f, 0.45f, 0.7f);
        }
    }

    List<CardDefinition> BuildGachaPool()
    {
        var pool = new List<CardDefinition>();
        int stage = CurrentGachaStage;
        foreach (CardDefinition card in CardDatabase.AllCards)
        {
            if (GachaStage.IsCardEligible(card, BestDistance, stage)) pool.Add(card);
        }
        return pool;
    }

    // Item 17 - "UnlockされていないRarity/Cardは抽選対象に入れない". Picks
    // a Rarity bucket first (weighted by gachaRarityWeights, but ONLY
    // among rarities actually present in `pool` - an unreached Rarity
    // simply never gets a bucket), then a uniform pick within that bucket.
    CardDefinition DrawFromGachaPool(List<CardDefinition> pool)
    {
        var buckets = new Dictionary<int, List<CardDefinition>>();
        foreach (CardDefinition card in pool)
        {
            int r = Mathf.Clamp(card.rarity, 1, gachaRarityWeights.Length);
            if (!buckets.TryGetValue(r, out List<CardDefinition> list)) { list = new List<CardDefinition>(); buckets[r] = list; }
            list.Add(card);
        }
        if (buckets.Count == 0) return null;

        float totalWeight = 0f;
        foreach (KeyValuePair<int, List<CardDefinition>> kv in buckets) totalWeight += Mathf.Max(0f, gachaRarityWeights[kv.Key - 1]);
        if (totalWeight <= 0f) return pool[Random.Range(0, pool.Count)]; // every eligible Rarity has a zero/negative weight - fall back to a uniform pick

        float roll = Random.value * totalWeight;
        List<CardDefinition> chosenBucket = null;
        foreach (KeyValuePair<int, List<CardDefinition>> kv in buckets)
        {
            float w = Mathf.Max(0f, gachaRarityWeights[kv.Key - 1]);
            if (roll < w) { chosenBucket = kv.Value; break; }
            roll -= w;
        }
        if (chosenBucket == null) foreach (KeyValuePair<int, List<CardDefinition>> kv in buckets) chosenBucket = kv.Value; // floating-point edge case fallback

        return chosenBucket[Random.Range(0, chosenBucket.Count)];
    }

    void OnGachaMachineTapped()
    {
        if (gachaResultOpen || gachaMachineShakeTimer > 0f) return; // ignore re-taps mid-animation/while Result is up
        if (TotalOwnedMile < GachaCostMile)
        {
            gachaInsufficientMessageTimer = gachaInsufficientMessageDuration;
            return;
        }
        List<CardDefinition> pool = BuildGachaPool();
        if (pool.Count == 0) return;
        CardDefinition drawn = DrawFromGachaPool(pool);
        if (drawn == null) return;

        TrySpendMile(GachaCostMile);
        CardInventory.AddCard(drawn.cardId, 1, 1);
        pendingGachaCard = drawn;
        gachaMachineShakeTimer = gachaMachineShakeDuration;
        deskHotspotFlashTimer = roomHotspotFlashDuration;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe);
    }

    public bool HasStarted { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsWin { get; private set; }
    public float MaxDistance { get; private set; }
    public float BestDistance { get; private set; }

    // ===== マップ別BEST(2026-09-22) =====
    // 保存キーは表示名ではなく安定したステージIDで分ける("BestDistance_v2_<stageId>")。値は倍精度をinvariantな文字列で保存。
    // 旧・共通のBestDistance(float)は「どのマップの記録か」を示す情報が無いので、どのマップにも割り当てず、
    // 解放/ガチャ進行の全体最高距離としてのみ従来どおり使う。旧値は初回起動時に別キーへ退避して保持する。
    const string StageBestKeyPrefix = "BestDistance_v2_";
    const string LegacyBestBackupKey = "BestDistance_legacyBackup";
    readonly System.Collections.Generic.Dictionary<string, double> stageBestCache = new System.Collections.Generic.Dictionary<string, double>();

    public double GetStageBest(string stageId)
    {
        if (string.IsNullOrEmpty(stageId)) return 0.0;
        if (stageBestCache.TryGetValue(stageId, out double v)) return v;
        string s = PlayerPrefs.GetString(StageBestKeyPrefix + stageId, "");
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        stageBestCache[stageId] = v;
        return v;
    }

    void SetStageBest(string stageId, double value)
    {
        if (string.IsNullOrEmpty(stageId)) return;
        stageBestCache[stageId] = value;
        PlayerPrefs.SetString(StageBestKeyPrefix + stageId, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    // HUD/タイトルに出すBEST対象のステージ: ラン中はそのランのステージ、タイトルでは次に出発する選択中ステージ。
    string BestDisplayStageId => HasStarted && !string.IsNullOrEmpty(activeRunStageId) ? activeRunStageId : SelectedStageId;
    double BestDisplayValue => GetStageBest(BestDisplayStageId);
    public float BestTime { get; private set; }
    public float RunTime { get; private set; }
    public bool InvincibleMode { get; private set; }
    public bool DebugMode { get; private set; }
    public int Lives { get; private set; }
    public int MaxLives => maxLives;
    // マルチプレイPhase 2.5: この端末でカード選択(レベルアップ/ボス報酬)を開いているか。
    public bool IsLocalChoiceOpen => levelUpPending;
    public int Level { get; private set; } = 1;
    public float Exp { get; private set; }
    public float ExpToNext { get; private set; }
    public int EnemyKillCount { get; private set; }
    public int BossKillCount { get; private set; }
    public float TotalExpEarned { get; private set; }
    public int UpgradeCount => upgradeHistory.Count;
    public bool IsNewBestDistance { get; private set; }
    public bool IsNewBestTime { get; private set; }

    ScreenOrientation preferredOrientation;
    float gameOverTime;
    float runStartTime;

    bool levelUpPending;
    CardDefinition[] pendingChoices;
    // 高速走行中のフリーズ/ワープ調査(2026-09-22) - Time.timeScaleへの
    // 直接書き込みをTimeControl(理由付き参照カウント)へ一本化する際の
    // owner。Level Up/Boss Reward選択は同じlevelUpPendingフラグで排他制御
    // されている(同時に両方Pendingにはならない)ため、共通の1つでよい。
    static readonly object pendingChoiceTimeOwner = new object();
    static readonly object pauseMenuTimeOwner = new object();
    // Diagnostic only - see TriggerLevelUpChoice.
    string lastLevelUpDiagnostic = "";
    // Every card picked this run, in order - drives the "obtained so far"
    // display on both the level-up choice screen and the results screen.
    readonly List<CardDefinition> upgradeHistory = new List<CardDefinition>();

    // Debug-mode-only live speed readout (see UpdateDebugSpeedTracking),
    // for visually confirming whether the dragon's on-screen movement is
    // actually keeping pace with the player's.
    float lastPlayerX;
    bool havePrevPlayerX;
    float measuredPlayerSpeed;
    DragonController debugDragon;
    float lastDragonX;
    bool havePrevDragonX;
    float measuredDragonSpeed;

    void Awake()
    {
        Instance = this;
        // OrnateUi is a static helper (see its class comment) - this is the
        // one place its shared frame texture gets assigned, from the field
        // SceneBuilder already populated on this component.
        OrnateUi.FrameTexture = ornateFrame;
        BestDistance = PlayerPrefs.GetFloat(BestDistanceKey, 0f);
        if (!PlayerPrefs.HasKey(LegacyBestBackupKey) && PlayerPrefs.HasKey(BestDistanceKey))
            PlayerPrefs.SetFloat(LegacyBestBackupKey, BestDistance); // 元データを保持(マップへの割り当ては行わない)
        BestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
        InvincibleMode = PlayerPrefs.GetInt(InvincibleKey, 0) != 0;
        DebugMode = PlayerPrefs.GetInt(DebugModeKey, 0) != 0;
        Lives = startingLives;
        ExpToNext = expBaseForLevel2;

        preferredOrientation = (ScreenOrientation)PlayerPrefs.GetInt(OrientationKey, (int)ScreenOrientation.LandscapeLeft);
        Screen.orientation = preferredOrientation;

        // Must happen before LoadDeck() - its default-deck fallback (and
        // CardDatabase.AllCards callers in general) needs unlock state to
        // already be correct. Backfills anything BestDistance already
        // qualifies for silently (no announcement - see UnlockManager).
        UnlockManager.Initialize(BestDistance);
        // カード合成改修(2026-09-26) - 旧形式の所持カード/デッキ/キャラカードを
        // 能力一式を持つ新形式へ一度だけ変換(以降は何もしない)。
        CardDataMigration.RunIfNeeded();
        LoadDeck();
        LoadMile();
        LoadCharacterCards();
        LoadSelectedCharacter();
        LoadSelectedStage();

        // Bug #001 診断フェーズ (2026-09-08) - Application.logMessageReceived
        // フックは一度だけ登録すれば十分(static event、二重登録防止は
        // EnsureHooked自身が行う)。
        BossDiagnostics.EnsureHooked();

        // 高速走行中のフリーズ/ワープ調査(2026-09-22) - Unityの既定値
        // (Maximum Allowed Timestep=0.333秒、ProjectSettings/TimeManager.
        // asset)のままだと、GC/アセット読み込み等で実時間0.3秒級のヒッチが
        // 起きた際、その1フレームのTime.deltaTimeがそのまま0.333秒に
        // クランプされて渡ってしまう。高速走行中(基礎速度の倍率が上がって
        // いる状態)はこの1フレームだけでプレイヤーが数十ユニット分まとめて
        // 進んでしまい、「画面が一瞬止まって、再開時に位置が飛んだように
        // 見える」不具合の主要因の1つになっていた(ヒッチ自体をゼロには
        // できないが、1フレームが表せる移動量の上限を下げることで見た目の
        // 飛びを大幅に軽減できる)。これはスローモーション演出の追加では
        // なく、既存の実効速度計算(PlayerController.Move等、Time.deltaTime
        // ベース)に対する上限のクランプのみ - 通常フレーム(1/60秒前後)の
        // 挙動には一切影響しない。
        Time.maximumDeltaTime = 0.1f;
    }

    // Item 11 - "強制終了による逃げ対策": mobile OSes suspend/kill a
    // backgrounded app without any further callbacks, so this is the last
    // reliable chance to persist HP/build/MILE before that happens.
    // OnApplicationQuit covers an explicit in-app exit; OnApplicationPause
    // fires when the app is backgrounded (Home button, app switch, a phone
    // call) - both no-op harmlessly via SaveInterruptState's own
    // !HasStarted/IsGameOver guard while on the Home Room screen or after
    // the run has already ended.
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) SaveInterruptState();
        else FreezeDiagnostics.NoteAppResumed(); // 復帰直後の長いフレームは処理落ちではない
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) FreezeDiagnostics.NoteAppResumed();
    }

    void OnApplicationQuit()
    {
        SaveInterruptState();
    }

    void Update()
    {
        if (!HasStarted)
        {
            // Starting now happens only via the on-screen START button (see
            // OnGUI) - Space is kept as an Editor-testing convenience.
            if (Input.GetKeyDown(KeyCode.Space)) StartGame();

            // Drives the title screen's one-shot intro fade (logo -> START/
            // DECK -> BEST) - unscaled since nothing pauses time here, and
            // simply counts up from 0 every time this component wakes up
            // fresh (scene load), no explicit reset needed (see
            // titleIntroTimer's own comment).
            titleIntroTimer += Time.unscaledDeltaTime;
            if (startPressFlashTimer > 0f) startPressFlashTimer -= Time.unscaledDeltaTime;
            UpdateHomeRoom();
            return;
        }

        bool retryAllowed = IsGameOver && Time.time - gameOverTime >= retryDelayAfterGameOver;
        if (retryAllowed && (Input.GetKeyDown(KeyCode.R) || WasTappedOrClicked()))
        {
            RetryWithTransition();
        }

        if (DebugMode) UpdateDebugSpeedTracking();

        UpdateUnlockAnnouncement();
        EnforceNetChoicePriority();
        UpdateDeferredLevelUp();
        UpdateDeferredBossReward();
        UpdatePendingChoiceWatchdog();

        if (heartDamageFlashTimer > 0f) heartDamageFlashTimer -= Time.deltaTime;
        // Level Up Presentation pass - unscaledDeltaTime (not deltaTime)
        // since this flash is triggered right as Time.timeScale drops to 0
        // for the level-up pause (see RewardCardSequence.
        // PlayLevelUpAnnouncement) - a scaled timer would just freeze
        // mid-flash the instant the pause takes effect.
        if (expBarFlashTimer > 0f) expBarFlashTimer -= Time.unscaledDeltaTime;

        // Item 3 - one-shot "ESCAPE AVAILABLE" banner countdown.
        if (escapeAvailableBannerTimer > 0f) escapeAvailableBannerTimer -= Time.unscaledDeltaTime;

        // Bug #001 診断フェーズ (2026-09-08) - 毎フレーム末尾で呼ぶ(この
        // フレーム中に他の処理が行った状態変化を全て反映した「最終状態」
        // を見るため)。両方とも監視/記録のみで、Gameplayには一切影響しない。
        BossDiagnostics.PollStateTransitions();
        BossDiagnostics.UpdateFreezeWatchdog();
        // 高速走行中のフリーズ/ワープ調査(2026-09-22) - Boss Phase専用の
        // 上2つとは別に、通常時(Level Up/被弾/HitStop絡み)も含めて毎フレーム
        // 記録する。DebugModeの有無に関わらず常時軽量に記録し、異常時だけ
        // 詳細を書き出す(FreezeDiagnostics自身のコメント参照)。
        FreezeDiagnostics.Tick();
    }

    // Distance-unlock system - shows a brief "NEW UNLOCK" toast the first
    // time each UnlockDefinition's condition is met (queued by
    // UnlockManager.CheckUnlocks in ReportDistance), then moves on to the
    // next queued one if several unlocked in quick succession (e.g. a big
    // EXP/distance jump crossing two thresholds at once).
    [Header("Unlock Announcement (tunable)")]
    public float unlockToastDuration = 3f;

    readonly Queue<UnlockDefinition> unlockAnnounceQueue = new Queue<UnlockDefinition>();
    UnlockDefinition currentUnlockAnnouncement;
    float unlockAnnounceTimer;

    void UpdateUnlockAnnouncement()
    {
        List<UnlockDefinition> newlyUnlocked = UnlockManager.DrainNewlyUnlocked();
        if (newlyUnlocked != null)
        {
            foreach (UnlockDefinition def in newlyUnlocked) unlockAnnounceQueue.Enqueue(def);
        }

        if (currentUnlockAnnouncement == null)
        {
            if (unlockAnnounceQueue.Count == 0) return;
            currentUnlockAnnouncement = unlockAnnounceQueue.Dequeue();
            unlockAnnounceTimer = unlockToastDuration;
        }

        unlockAnnounceTimer -= Time.deltaTime;
        if (unlockAnnounceTimer <= 0f) currentUnlockAnnouncement = null;
    }

    void DrawUnlockAnnouncement()
    {
        if (currentUnlockAnnouncement == null) return;

        // Fades in/out over the first and last 0.3s of the hold rather
        // than popping in and cutting off abruptly.
        float fadeWindow = Mathf.Min(0.3f, unlockToastDuration * 0.5f);
        float alpha = Mathf.Clamp01(Mathf.Min(unlockToastDuration - unlockAnnounceTimer, unlockAnnounceTimer) / Mathf.Max(0.001f, fadeWindow));

        string text = "NEW UNLOCK: " + currentUnlockAnnouncement.displayName;
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = new Color(1f, 0.93f, 0.75f, alpha);

        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect rect = new Rect(Screen.width / 2f - size.x / 2f - 20f, Screen.height * 0.22f, size.x + 40f, size.y + 16f);
        UiBackdrop.Draw(rect, 0.85f * alpha);
        GUI.Label(rect, text, style);
    }

    // Measures each one's actual frame-to-frame X movement (not just the
    // "commanded" speed value, which both the player and the dragon derive
    // from the same source and would always match trivially) - this is
    // what can actually reveal a real-world discrepancy between how far
    // the player visibly moves and how far the dragon's rendered position
    // keeps up, e.g. if something is silently holding the dragon back.
    void UpdateDebugSpeedTracking()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (PlayerController.Instance != null)
        {
            float x = PlayerController.Instance.transform.position.x;
            if (havePrevPlayerX) measuredPlayerSpeed = (x - lastPlayerX) / dt;
            lastPlayerX = x;
            havePrevPlayerX = true;
        }

        if (debugDragon == null || debugDragon.IsDead)
        {
            debugDragon = FindFirstObjectByType<DragonController>();
            havePrevDragonX = false;
        }

        if (debugDragon != null)
        {
            float x = debugDragon.transform.position.x;
            if (havePrevDragonX) measuredDragonSpeed = (x - lastDragonX) / dt;
            lastDragonX = x;
            havePrevDragonX = true;
        }
        else
        {
            measuredDragonSpeed = 0f;
        }
    }

    // Polish Pass 1 - short cross-fade-through-navy instead of an instant
    // cut, so pressing START reads as "the adventure began" rather than
    // just swapping which IMGUI block draws. Tunable; total time is
    // fadeOut+fadeIn (defaults to ~0.5s, within the 0.5-1s target - no
    // loading/movie, HasStarted flips (and the player starts running)
    // right at the midpoint, hidden under the fully-opaque overlay.
    [Header("Polish Pass 1 - Start Transition (tunable)")]
    public float startTransitionFadeOutDuration = 0.22f;
    public float startTransitionFadeInDuration = 0.24f;

    bool startTransitioning;
    float startTransitionOverlayAlpha;

    // Run開始の瞬間に確定させる値・副作用をまとめたもの。StartGame()と
    // DepartFromStageSelect()の両方から、それぞれの画面遷移が完全に画面を
    // 覆った瞬間(onFullyCovered/フェード最深部)に一度だけ呼ばれる - 2箇所
    // に全く同じ処理を書いていた重複を解消した共通ヘルパー。
    void ApplyGameStart(string stageIdOverride = null)
    {
        HasStarted = true;
        runStartTime = Time.time;
        activeRunCharacterId = SelectedCharacterId;
        activeRunStageId = stageIdOverride ?? SelectedStageId;
        if (TerrainManager.Instance != null) TerrainManager.Instance.ApplyStageTheme(activeRunStageId);
        ApplyCharacterBaseStats(CharacterDatabase.FindById(activeRunCharacterId));
        ApplyCharacterCardEffects();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayGameplayBgm();
        StartCoroutine(RunStartCountdownRoutine());
    }

    // Stage01地形挙動修整(2026-09-17), item4 - 全ステージ共通のRun開始
    // カウントダウン。HasStarted自体は(Home画面⇔ゲーム画面のOnGUI分岐や
    // HUD表示を従来どおり保つため)このApplyGameStart呼び出し時点で即座に
    // trueへ切り替える - ゲーム画面/HUDはすぐ表示される。プレイヤー操作/
    // 敵の湧き/距離加算だけを、別途のCountdownActiveで止める
    // (PlayerController.Update、ObstacleSpawner、EnemyWallManager、
    // UpperRouteEnemySpawnerの各早期returnに追記)。BossManagerは意図的に
    // 変更しない - Run開始直後(距離0)でBoss開始条件を満たすことはないため
    // 無関係であり、誤ってBoss開始処理にもカウントダウンを適用してしまう
    // リスクを避けるため既存の!HasStartedガードのみで済ませる。
    // 「New Run」経由(StartGame/DepartFromStageSelect)のみが対象 -
    // Continue(BeginContinuedRun)は中断データの続きから即再開する既存
    // 挙動を維持し、対象外とした(再開時に敵が近くに既に存在し得るため、
    // カウントダウン中に凍結しきれない可能性がある - スコープ外として
    // 意図的に見送り)。
    // マルチプレイ(2026-09-28): マルチRunではHOSTが決めたRunStateがRunningになった瞬間に解除する
    // (NetRunLauncher.ReleasedForRun)。各端末のコルーチンの進み具合に左右されない。
    bool countdownActive;
    public bool CountdownActive
    {
        get => countdownActive && !NetRunLauncher.ReleasedForRun;
        private set => countdownActive = value;
    }
    public string CountdownLabel { get; private set; } = "";

    [Header("Stage01地形挙動修整(2026-09-17) - Run開始カウントダウン")]
    public float countdownStepDuration = 0.8f;
    public float countdownGoDuration = 0.6f;

    // マルチプレイ対応Phase 1(2026-09-25) - NetRunLauncherがシーンを読み込み直した直後に
    // 呼ぶRun開始口。ステージはHOSTが選んだものを直接使う(この端末で未解放でも同じ
    // ステージを走れるよう、SetSelectedStageの解放チェックを通さない)。
    public void BeginMultiplayerRun(string stageId)
    {
        if (HasStarted) return;
        stageSelectOpen = false;
        if (stageSelectUI != null) stageSelectUI.gameObject.SetActive(false);
        ApplyGameStart(StageDatabase.FindById(stageId) != null ? stageId : null);
    }

    public string ActiveRunCharacterId => activeRunCharacterId;

    IEnumerator RunStartCountdownRoutine()
    {
        CountdownActive = true;
        // マルチプレイ(2026-09-28): 全員の準備完了→HOSTが決めた共通のGO!の時刻から表示を求める。
        if (NetRunLauncher.IsMultiplayerRun)
        {
            yield return MultiplayerCountdownRoutine();
            yield break;
        }
        CountdownLabel = "3";
        yield return new WaitForSecondsRealtime(countdownStepDuration);
        CountdownLabel = "2";
        yield return new WaitForSecondsRealtime(countdownStepDuration);
        CountdownLabel = "1";
        yield return new WaitForSecondsRealtime(countdownStepDuration);
        CountdownLabel = "GO!";
        yield return new WaitForSecondsRealtime(countdownGoDuration);
        CountdownLabel = "";
        CountdownActive = false;
    }

    // マルチRunのカウントダウン: 待ち時間を積み上げず、毎フレーム「GO!の時刻 - 今のサーバー時刻」から
    // READY/3/2/1を決める。RunStateがRunningになった瞬間(NetRunLauncher)に操作/前進/距離/湧きが
    // 全端末で同時に解放される(CountdownActiveの解除条件)。GO!の文字はその後の表示だけ。
    IEnumerator MultiplayerCountdownRoutine()
    {
        while (NetRunLauncher.RunState < NetRunState.Running && NetRunLauncher.IsMultiplayerRun)
        {
            if (NetRunLauncher.RunState == NetRunState.Countdown)
            {
                double remain = NetRunLauncher.SecondsToGo;
                int step = remain > countdownStepDuration * 3f ? 0 : Mathf.Clamp(Mathf.CeilToInt((float)(remain / countdownStepDuration)), 1, 3);
                CountdownLabel = step == 0 ? "READY" : step.ToString();
            }
            else CountdownLabel = "READY"; // WaitingForPlayers: 全員の準備完了待ち
            yield return null;
        }
        CountdownActive = false;
        CountdownLabel = "GO!";
        yield return new WaitForSecondsRealtime(countdownGoDuration);
        CountdownLabel = "";
    }

    void DrawRunStartCountdown()
    {
        if (string.IsNullOrEmpty(CountdownLabel)) return;

        bool isGo = CountdownLabel == "GO!";
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = isGo ? 96 : 120;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = isGo ? new Color(0.65f, 0.9f, 1f) : new Color(0.95f, 0.83f, 0.45f);

        Rect rect = new Rect(0f, Screen.height * 0.5f - 90f, Screen.width, 180f);

        GUIStyle shadowStyle = new GUIStyle(style);
        shadowStyle.normal.textColor = new Color(0.04f, 0.06f, 0.14f, 0.85f);
        Rect shadowRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width, rect.height);
        GUI.Label(shadowRect, CountdownLabel, shadowStyle);
        GUI.Label(rect, CountdownLabel, style);
    }

    void StartGame()
    {
        if (startTransitioning || HasStarted) return;
        if (NetRunLauncher.InterceptDepart(SelectedStageId)) return; // DepartFromStageSelectと同じ(マルチプレイ時のみ)
        // Presentation pass - TOP->GAME now goes through the shared wipe
        // (see ScreenTransitionManager) instead of this file's own plain
        // navy fade below; HasStarted only flips once the screen is 100%
        // covered. StartGameTransition() (the old plain fade) is kept as a
        // fallback for a scene built before this manager existed.
        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            startTransitioning = true;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                ApplyGameStart();
                startTransitioning = false;
            });
            return;
        }
        StartCoroutine(StartGameTransition());
    }

    // Home画面 / Stage Select改善依頼(2026-09-16), item5/9/10 - Stage
    // Selectの「出発」ボタン専用の入口。従来はStage Select側でステージを
    // 確定してHomeへ戻り、改めて扉をタップしてRunを開始する二段階だった
    // (「Stage Selectは出発時だけの専用画面」という今回の方針とは、Home
    // へ一度戻る一手間がある点で噛み合わない)。ステージ確定→Stage Select
    // を閉じる→Run開始を、ScreenTransitionManagerの1回の被覆(onFullyCovered)
    // の中でまとめて行うことで、二重にPlayTransitionを呼ぶことによる
    // デッドロック(内側のStartGame()がIsTransitioning==trueで即return
    // してしまい、画面が覆われたまま何も始まらない不具合)を避けている。
    public void DepartFromStageSelect(string stageId)
    {
        if (startTransitioning || HasStarted) return;
        // マルチプレイ対応Phase 1(2026-09-25) - セッション接続中はHOSTの出発で全員同時に
        // 開始する(NetRunLauncher)。セッションが無ければ何もせずfalseが返り、従来どおり。
        if (NetRunLauncher.InterceptDepart(stageId)) return;
        StageDefinition def = StageDatabase.FindById(stageId);
        if (def == null || !def.unlocked) return;
        if (ScreenTransitionManager.Instance == null || ScreenTransitionManager.Instance.IsTransitioning) return;

        SetSelectedStage(stageId);
        startTransitioning = true;
        ScreenTransitionManager.Instance.PlayTransition(() =>
        {
            stageSelectOpen = false;
            if (stageSelectUI != null) stageSelectUI.gameObject.SetActive(false);
            ApplyGameStart();
            startTransitioning = false;
        });
    }

    IEnumerator StartGameTransition()
    {
        startTransitioning = true;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, startTransitionFadeOutDuration);
            startTransitionOverlayAlpha = Mathf.Clamp01(t);
            yield return null;
        }

        ApplyGameStart();

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, startTransitionFadeInDuration);
            startTransitionOverlayAlpha = 1f - Mathf.Clamp01(t);
            yield return null;
        }
        startTransitionOverlayAlpha = 0f;
        startTransitioning = false;
    }

    // Clears both persisted best-of records, in the run's memory too -
    // used by the TOP-screen "RESET HIGH SCORE" button.
    void ResetHighScores()
    {
        foreach (string k in new System.Collections.Generic.List<string>(stageBestCache.Keys)) PlayerPrefs.DeleteKey(StageBestKeyPrefix + k);
        stageBestCache.Clear();
        if (StageDatabase.AllStages != null) foreach (var st in StageDatabase.AllStages) PlayerPrefs.DeleteKey(StageBestKeyPrefix + st.stageId);
        BestDistance = 0f;
        BestTime = 0f;
        PlayerPrefs.DeleteKey(BestDistanceKey);
        PlayerPrefs.DeleteKey(BestTimeKey);
        PlayerPrefs.Save();
    }

    // Keeps UI elements inside the device's safe area (avoids notches/rounded
    // corners) with a comfortable margin, instead of hugging the raw screen edge.
    const float UiMargin = 28f;

    float SafeTop()
    {
        Rect safe = Screen.safeArea;
        return Screen.height - (safe.y + safe.height);
    }

    float SafeRight()
    {
        Rect safe = Screen.safeArea;
        return Screen.width - (safe.x + safe.width);
    }

    float SafeLeft() => Screen.safeArea.x;
    float SafeBottom() => Screen.safeArea.y;

    // ===== HUD design system (Visual Style Ver.1) =====
    // One shared strip across the top: BEST/DISTANCE (left), Lv/EXP
    // (center), HP (right) - all the same panel height and top offset, so
    // they read as one aligned HUD instead of separately-placed boxes.
    // Every size/color/font constant any panel below uses lives here, so
    // TOP/DECK/HUD can't quietly drift apart from each other over time.
    const float HudPanelHeight = 54f;
    const float HudPanelGap = 8f;
    const int HudLabelFontSize = 13;
    const int HudValueFontSize = 22;
    static readonly Color HudLabelColor = new Color(0.85f, 0.85f, 0.92f, 0.85f);
    static readonly Color HudValueColor = Color.white;
    static readonly Color HudGoldColor = new Color(1f, 0.85f, 0.35f);

    Rect GetBestPanelRect() => new Rect(SafeLeft() + UiMargin, SafeTop() + UiMargin, DistancePanelWidth(false), HudPanelHeight);
    Rect GetDistancePanelRect() => new Rect(SafeLeft() + UiMargin, GetBestPanelRect().yMax + HudPanelGap, DistancePanelWidth(false), HudPanelHeight);
    // 高速走行の視認性補正(2026-09-22) - 現在のAuto Run速度を基礎速度に対する倍率で常時表示する小さなHUD。
    // 既存の速度値(PlayerController.SpeedRatio)を参照して表示するだけで、移動速度の計算には影響しない。
    Rect GetSpeedPanelRect() => new Rect(SafeLeft() + UiMargin, GetDistancePanelRect().yMax + HudPanelGap, DistancePanelWidth(false), 30f);
    int speedHudStep = -1;
    float speedUpShownAt = -100f;
    float speedUpShownKmh;
    const float SpeedUpNoticeSeconds = 1.6f;

    void DrawSpeedHud()
    {
        var pc = PlayerController.Instance;
        if (pc == null) return;
        float ratio = pc.SpeedRatio; // 通知の段を判定するためだけに使う(表示は km/h)
        float kmh = SpeedKmh(pc.CurrentAutoRunSpeed);
        // 0.25刻みの段を超えた瞬間に短い通知(初回描画では鳴らさない)。
        int step = Mathf.FloorToInt(ratio * 4f + 0.0001f);
        if (Event.current.type == EventType.Repaint)
        {
            if (speedHudStep >= 0 && step > speedHudStep)
            {
                speedUpShownAt = Time.unscaledTime;
                speedUpShownKmh = kmh;
            }
            speedHudStep = step;
        }

        Rect r = GetSpeedPanelRect();
        UiBackdrop.Draw(r, 0.6f);
        var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        labelStyle.normal.textColor = HudLabelColor;
        GUI.Label(new Rect(r.x + 12f, r.y, 70f, r.height), "SPEED", labelStyle);
        var valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        valueStyle.normal.textColor = ratio >= 1.01f ? HudGoldColor : HudValueColor;
        GUI.Label(new Rect(r.x + 60f, r.y, r.width - 72f, r.height), kmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km/h", valueStyle);

        float since = Time.unscaledTime - speedUpShownAt;
        if (since >= 0f && since < SpeedUpNoticeSeconds)
        {
            float a = since < 0.2f ? since / 0.2f : Mathf.Clamp01((SpeedUpNoticeSeconds - since) / 0.5f);
            var nStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            Color c = HudGoldColor; c.a = a;
            nStyle.normal.textColor = c;
            GUI.Label(new Rect(r.x + 4f, r.yMax + 2f, 220f, 22f), "SPEED UP! " + speedUpShownKmh.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km/h", nStyle);
        }
    }

    Rect GetLevelExpPanelRect() => new Rect(Screen.width / 2f - 190f, SafeTop() + UiMargin, 380f, HudPanelHeight);
    Rect GetHeartsPanelRect()
    {
        float width = Mathf.Clamp(70f + maxLives * 34f, 220f, 420f);
        return new Rect(Screen.width - SafeRight() - UiMargin - width, SafeTop() + UiMargin, width, HudPanelHeight);
    }

    // The settings/debug column used to start at the same top-right corner
    // the HP panel now occupies - pushed below it instead, same width.
    float DebugColumnTop() => GetHeartsPanelRect().yMax + HudPanelGap;
    Rect GetGearButtonRect() => new Rect(SafeLeft() + UiMargin, Screen.height - SafeBottom() - UiMargin - 52f, 52f, 52f);
    Rect GetOrientationButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop(), 140f, 40f);
    Rect GetBgmButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop() + 46f, 140f, 40f);
    Rect GetSfxButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop() + 92f, 140f, 40f);
    Rect GetInvincibleButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop() + 138f, 140f, 40f);
    Rect GetDebugButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop() + 184f, 140f, 40f);
    // Game Feel Visibility Pass - reachable from the title screen (before
    // START) so the boost is active for the whole run that follows, since
    // this column itself isn't shown during actual gameplay.
    Rect GetGameFeelFxButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop() + 230f, 140f, 40f);
    Rect GetResetHighScoreButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 140f, DebugColumnTop() + 276f, 140f, 40f);

#if UNITY_EDITOR
    public void DebugSetInvincible(bool on) { InvincibleMode = on; Lives = 999; }
    public void DebugSetLives(int n) { Lives = n; NetMatch.RequestDebugSetHp(n); }
#endif

    void ToggleInvincible()
    {
        InvincibleMode = !InvincibleMode;
        PlayerPrefs.SetInt(InvincibleKey, InvincibleMode ? 1 : 0);
        PlayerPrefs.Save();
    }

    void ToggleDebugMode()
    {
        DebugMode = !DebugMode;
        // DEBUGをOFFにしたら、見えないまま速度倍率が残らないよう必ず等倍へ戻す。
        if (!DebugMode) PlayerController.DebugSpeedScale = 1f;
        PlayerPrefs.SetInt(DebugModeKey, DebugMode ? 1 : 0);
        PlayerPrefs.Save();
    }

    void ToggleOrientation()
    {
        preferredOrientation = preferredOrientation == ScreenOrientation.Portrait
            ? ScreenOrientation.LandscapeLeft
            : ScreenOrientation.Portrait;

        Screen.orientation = preferredOrientation;
        PlayerPrefs.SetInt(OrientationKey, (int)preferredOrientation);
        PlayerPrefs.Save();
    }

    // Any tap/click starts the game or retries, EXCEPT one landing on a
    // settings button (those handle themselves via OnGUI).
    bool WasTappedOrClicked()
    {
        // Input Lock - see ScreenTransitionManager's own class comment;
        // swallows every tap/click while a transition is mid-flight so a
        // hasty double-tap can't fire Retry (or anything else routed
        // through this) a second time.
        if (ScreenTransitionManager.Instance != null && ScreenTransitionManager.Instance.IsTransitioning) return false;

        Vector2 screenPos;
        if (Input.GetMouseButtonDown(0)) screenPos = Input.mousePosition;
        else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) screenPos = Input.GetTouch(0).position;
        else return false;

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        if (GetGearButtonRect().Contains(guiPos)) return false;
        if (GetOrientationButtonRect().Contains(guiPos)) return false;
        if (GetBgmButtonRect().Contains(guiPos)) return false;
        if (GetSfxButtonRect().Contains(guiPos)) return false;
        if (GetInvincibleButtonRect().Contains(guiPos)) return false;
        if (GetDebugButtonRect().Contains(guiPos)) return false;
        if (GetGameFeelFxButtonRect().Contains(guiPos)) return false;
        if (!HasStarted && GetResetHighScoreButtonRect().Contains(guiPos)) return false;
        return true;
    }

    // Bugfix 2026-09-06, item "Boss中Distanceの根本修正". The old design
    // only ever froze MaxDistance's own VALUE while IsBossPhase was true -
    // the raw incoming `distance` (Player.transform.x - startX) kept
    // climbing normally underneath that freeze the whole time (correctly -
    // Player/World must keep moving during a Boss fight), but nothing ever
    // compensated for that climb once the freeze lifted. The very next
    // ReportDistance call after Boss Reward completed would see a `distance`
    // value far ahead of the still-frozen MaxDistance, and the existing
    // "distance > MaxDistance" branch would treat that WHOLE Boss-fight
    // movement as legitimate new progress in one lump sum (a single large
    // GainExp/HighestReachedDistance/MILE jump instead of "resume exactly
    // from the checkpoint"). Fixed by tracking exactly how much raw
    // distance accumulated between Boss Gate lock and Boss Reward
    // completion (`distanceExclusionOffset`, updated by
    // BeginBossDistanceExclusion/EndBossDistanceExclusion below) and
    // permanently subtracting that from every future raw distance before
    // it ever reaches the comparison against MaxDistance - so Distance
    // truly resumes from the checkpoint with no catch-up jump, while the
    // Player's own on-screen position/movement during the fight is
    // completely unaffected (this offset only ever touches the Distance
    // bookkeeping, never transform.position itself).
    float distanceExclusionOffset;
    float lastRawDistanceSeen;
    float bossPhaseEntryRawDistance;
    // cm単位表示用の倍精度の並行トラッキング(2026-09-22)。MaxDistance(float)は距離条件/ボス/報酬にそのまま使い、
    // こちらは表示と記録(BEST)専用。float(100,000m超で刻み0.008m以上)ではcm精度が保てないため。
    double distanceExclusionOffsetExact;
    double lastRawDistanceSeenExact;
    double bossPhaseEntryRawDistanceExact;
    public double MaxDistanceExact { get; private set; }

    // Called once, right when BossManager locks the Gate (immediately
    // after ClampMaxDistanceTo) - captures "what raw distance corresponds
    // to the moment Distance froze", the reference point EndBossDistance
    // Exclusion needs to compute how far the Player travelled during the
    // fight.
    public void BeginBossDistanceExclusion()
    {
        bossPhaseEntryRawDistance = lastRawDistanceSeen;
        bossPhaseEntryRawDistanceExact = lastRawDistanceSeenExact;
    }

    // Called once, right where GameManager already calls BossManager.
    // EndBossPhase() (Boss Reward completion) - folds the whole fight's
    // raw movement into the permanent exclusion offset.
    public void EndBossDistanceExclusion()
    {
        distanceExclusionOffset += (lastRawDistanceSeen - bossPhaseEntryRawDistance);
        distanceExclusionOffsetExact += (lastRawDistanceSeenExact - bossPhaseEntryRawDistanceExact);
    }

    public void ReportDistance(float rawDistance) { ReportDistance(rawDistance, rawDistance); }

    public void ReportDistance(float rawDistance, double rawDistanceExact)
    {
        lastRawDistanceSeenExact = rawDistanceExact;
        // lastRawDistanceSeen updates unconditionally, every call, even
        // while IsBossPhase is freezing everything below this line - it's
        // what lets BeginBossDistanceExclusion/EndBossDistanceExclusion
        // above measure the fight's own raw movement independently of
        // whichever MonoBehaviour's Update() happens to run first this
        // frame.
        lastRawDistanceSeen = rawDistance;
        float distance = rawDistance - distanceExclusionOffset;

        // Distance Level Design Ver.1.1, item 1 - Boss Gate: while a Boss
        // checkpoint is active (BossManager.IsBossPhase - reused directly
        // as the gate flag rather than a second, easy-to-desync bool),
        // Distance itself stops advancing entirely, even though the
        // player's own transform.position.x (what `rawDistance` is computed
        // from) keeps climbing normally - Player/Auto Run/Ground Scroll/
        // Background Scroll/Enemy Battle/Player操作 are all completely
        // untouched by this, since none of them read GameManager.
        // MaxDistance at all. This early-return is the ENTIRE gate.
        if (BossManager.Instance != null && BossManager.Instance.IsBossPhase) return;

        double distanceExact = rawDistanceExact - distanceExclusionOffsetExact;
        if (distanceExact > MaxDistanceExact) MaxDistanceExact = distanceExact;

        if (distance > MaxDistance)
        {
            float delta = distance - MaxDistance;
            MaxDistance = distance;
            GainExp(delta * expPerMeter);
            UnlockManager.CheckUnlocks(MaxDistance);

            // Item 12 - tracked unconditionally (not just past
            // escapeMinDistance), so it stays correct even for the very
            // first Boss Checkpoint.
            if (MaxDistance > HighestReachedDistance) HighestReachedDistance = MaxDistance;

            // Item 11 - "強制終了による逃げ対策": keeps the interrupt-state
            // save (HP/RunMile/build/HighestReachedDistance) reasonably
            // fresh without writing to disk every frame - once per 100m is
            // cheap and frequent enough that an app kill can't lose much.
            if (Mathf.FloorToInt(MaxDistance / 100f) > Mathf.FloorToInt((MaxDistance - delta) / 100f))
            {
                SaveInterruptState();
            }
        }
    }

    // Distance Level Design Ver.1.1, item 1 - called once by BossManager at
    // the exact moment a Boss Gate locks, so the displayed/tracked Distance
    // snaps to EXACTLY the gate's own checkpoint value (e.g. 1000.0) rather
    // than whatever fractional overshoot (e.g. 1000.03) happened to trigger
    // it the same frame. Deliberately bypasses GainExp/UnlockManager -
    // this is a correction to an already-reported distance, not new
    // progress.
    public void ClampMaxDistanceTo(float value)
    {
        MaxDistance = value;
        MaxDistanceExact = value;
    }

    // Accumulates EXP and rolls over into as many level-ups as it covers
    // (in case a big lump sum, e.g. a boss kill, crosses more than one
    // threshold at once). Suspended while a level-up choice is already
    // pending - the game is paused then anyway, so nothing would visibly
    // change, and it avoids queuing up a second choice before the first is
    // resolved.
    void GainExp(float amount)
    {
        // マルチプレイPhase 2.5: マルチでは選択中も世界(=距離/撃破)が進むため、その間のEXPは捨てずに
        // 貯め、レベルアップは選択が終わってから順に出す(pendingLevelUpCountの既存の後回し処理)。
        if (amount <= 0f || (levelUpPending && !NetMatch.Active)) return;

        // "EXP UP" cards raise expGainMultiplier above 1 - applied once
        // here so it covers every EXP source (distance, kills, bosses)
        // uniformly instead of needing a multiplier at each call site.
        amount *= expGainMultiplier;

        TotalExpEarned += amount;
        Exp += amount;
        while (Exp >= ExpToNext)
        {
            Exp -= ExpToNext;
            Level++;
            ExpToNext = expBaseForLevel2 + expGrowthPerLevel * (Level - 1);
            TriggerLevelUpChoice();
        }
    }

    // Presentation Priority pass - Boss Spawn/Defeat outranks Level Up (see
    // the class-level priority list this pass introduced). If a Boss
    // Milestone Presentation is currently playing, the level-up itself
    // (HP refill, card draw, Time.timeScale pause, RewardCardSequence) is
    // deferred rather than started concurrently - see
    // levelUpDeferredPending/Update() for where it actually resumes.
    // Otherwise this is exactly the original TriggerLevelUpChoice, just
    // renamed to RunLevelUpChoice so the deferral wrapper below could reuse
    // its name at the call site (GainExp) without changing that caller.
    // Bugfix 2026-09-08 (Bug #001 - root cause confirmed via diagnostic
    // Freeze Snapshot) - "Boss Phase中はLevel Up Card Choiceを開始しない"。
    // 以前はBoss Presentation実行中/既存choice実行中のみdeferしており、
    // BossManager.IsBossPhase自体は見ていなかった - そのためBoss撃破時の
    // EXP付与(RegisterBossDefeat -> GainExp、Boss自身のFinalHitAndDie死亡
    // コルーチン内の、BossManager.OnDragonDefeated()/CheckEncounterComplete()
    // より前の行で呼ばれる)がLevel Up閾値を跨ぐと、**他のBossがまだ生存/
    // 戦闘中の複数Boss Encounterであっても**Level Up Card Choiceがその場
    // で即座に開始されてしまい、Boss Presentation/Combat/Defeat/Rewardの
    // Stateと衝突していた - 実際に「rewardCardSequence OK, starting
    // sequence...」表示中にBossがまだ生存しているFreeze Snapshotスクリー
    // ンショットで確認された、Bug #001の確定した根本原因の1つ。
    //
    // 同時に見つかったもう1つのバグも修正: levelUpDeferredPendingが単純な
    // boolだったため、GainExpのwhileループが1回のEXP付与で複数Levelを
    // 跨いだ場合(大きなEXPジャンプ、または複数Boss撃破分が積み重なった
    // 場合)、2回目以降のTriggerLevelUpChoice()呼び出しは同じboolを
    // 再度trueにするだけで、**1つ分のLevel Up Choiceしか実際には開始
    // されず、残りは静かに消失していた**。pendingLevelUpCountをカウンタ
    // 化し、呼ばれた回数だけ確実にインクリメント、実際に1つ開始した時だ
    // けデクリメントする形にしたので、N回同時にLevel Upしても必ずN回分の
    // Card Choiceが順番に(1つ解決してから次を開始)処理される。
    int pendingLevelUpCount;

    void TriggerLevelUpChoice()
    {
        pendingLevelUpCount++;
        TryStartNextPendingLevelUp();
    }

    // Boss Phase(Spawn Presentation/Combat/Defeat Presentation/Reward -
    // BossManager.IsBossPhaseがtrueである全期間)が、既存の2つのBoss
    // Presentationと同じ優先順位でLevel Upより優先される。呼ぶたびに
    // pendingLevelUpCountが1つ消化できたかどうかを返す - UpdateDeferredLevelUp
    // からも同じロジックをそのまま再利用する。
    bool TryStartNextPendingLevelUp()
    {
        if (pendingLevelUpCount <= 0) return false;
        if (IsNetChoiceBlocked(out _)) return false; // マルチ: Run終了/脱落/DOWN中は開かない(UpdateDeferredLevelUpが扱う)

        bool bossPhaseActive = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        if (IsBossPresentationActive() || levelUpPending || bossPhaseActive)
        {
            if (bossPhaseActive) LogBoss($"LevelUpDeferred(BossPhase, pending={pendingLevelUpCount})");
            Debug.Log($"[PresentationPriority] Level Up deferred (pending={pendingLevelUpCount}, bossPhaseActive={bossPhaseActive}) - Boss Presentation/Boss Reward/Boss Phase active");
            return false;
        }

        pendingLevelUpCount--;
        RunLevelUpChoice();
        return true;
    }

    // Presentation Priority pass - Boss Spawn/Defeat outranks Level Up.
    // Combines both Boss Presentations (Milestone entrance AND Defeat/
    // Clear) behind one check, used by TriggerLevelUpChoice and
    // UpdateDeferredLevelUp - in practice the two never overlap (a
    // milestone's own entrance presentation always finishes long before
    // that same boss is ever killed), but checking both keeps this correct
    // even if that ever changes.
    bool IsBossPresentationActive()
    {
        bool milestoneActive = BossMilestonePresentation.Instance != null && BossMilestonePresentation.Instance.IsRunning;
        bool defeatActive = BossDefeatPresentation.Instance != null && BossDefeatPresentation.Instance.IsRunning;
        return milestoneActive || defeatActive;
    }

    // Presentation Priority pass - fields backing the deferral above.
    // levelUpDeferredTimer counts down in real time (unscaled) once the
    // Boss Presentation/Boss Phase actually finishes, so "Boss Presentation
    // 終了 -> Gameplayを正常状態へ戻す -> 0.3〜0.5秒待つ -> Pending Level Up
    // があれば開始" holds even though gameplay itself has already resumed
    // at normal Time.timeScale by that point.
    float levelUpDeferredTimer = -1f;
    public float levelUpDeferredResumeDelay = 0.4f;

    // Presentation Priority pass - called every frame from Update()
    // (mid-run only, same as heartDamageFlashTimer/DebugMode above). Not
    // reached at all while pendingLevelUpCount is 0, so this is a no-op
    // the overwhelming majority of the time.
    void UpdateDeferredLevelUp()
    {
        if (pendingLevelUpCount <= 0) return;

        // マルチ(2026-09-28): Run終了/脱落なら残りを捨てる。CO-OPのDOWN中は復活まで出さずに待つ。
        if (IsNetChoiceBlocked(out bool dropQueued))
        {
            if (dropQueued)
            {
                Debug.Log($"[NET][CHOICE] {pendingLevelUpCount} queued Level Up(s) dropped - {NetChoiceBlockReason()}");
                pendingLevelUpCount = 0;
            }
            levelUpDeferredTimer = -1f;
            return;
        }

        // Presentation Priority pass - Player Death/Game Clear outranks
        // Level Up: if the run already ended while this was waiting
        // (e.g. the player died mid-boss-fight, after the Boss Presentation
        // itself finished but before this buffer ran out), drop every
        // remaining deferred level-up outright instead of popping the card
        // UI open on top of an already-finished run.
        if (IsGameOver)
        {
            Debug.Log($"[PresentationPriority] {pendingLevelUpCount} deferred Level Up(s) cancelled - run already ended");
            pendingLevelUpCount = 0;
            levelUpDeferredTimer = -1f;
            return;
        }

        bool bossPhaseActive = BossManager.Instance != null && BossManager.Instance.IsBossPhase;
        if (IsBossPresentationActive() || levelUpPending || bossPhaseActive)
        {
            levelUpDeferredTimer = -1f; // reset the buffer - only starts counting once every one of these actually clears
            return;
        }

        if (levelUpDeferredTimer < 0f) levelUpDeferredTimer = levelUpDeferredResumeDelay;
        levelUpDeferredTimer -= Time.unscaledDeltaTime;
        if (levelUpDeferredTimer <= 0f)
        {
            levelUpDeferredTimer = -1f;
            Debug.Log($"[PresentationPriority] Deferred Level Up starting now (pending before this={pendingLevelUpCount})");
            TryStartNextPendingLevelUp();
        }
    }

    // Item 6/7 - "既存Level Upの3択システムを可能な限り再利用" - triggered
    // by BossManager once a Boss encounter (all its bosses) is fully
    // cleared. Same Presentation-priority deferral as Level Up (waits for
    // the Boss Defeat Presentation banner to finish, then a short buffer).
    // Bugfix 2026-09-06 - "Boss撃破後にゲームが停止する", item 2 (根本原因
    // まで追跡するための状態ログ). Logs the exact set of flags the report
    // asked to track, at every named stage of the Boss Reward pipeline.
    // DebugMode-gated (existing project convention for diagnostic logs) -
    // enable Debug Mode before reproducing to capture the full trail via
    // logcat/Console. Deliberately reads every value fresh each call rather
    // than caching, since the whole point is to see it change (or fail to
    // change) across stages.
    public void LogBossRewardStage(string stage)
    {
        if (!DebugMode) return;
        bool? sequenceRunning = rewardCardSequence != null ? rewardCardSequence.IsRunning : (bool?)null;
        bool? sequenceWaiting = rewardCardSequence != null ? rewardCardSequence.IsWaitingForSelection : (bool?)null;
        Debug.Log($"[BossRewardTrace] {stage}: timeScale={Time.timeScale:F2}  IsBossPhase={(BossManager.Instance != null ? BossManager.Instance.IsBossPhase : (bool?)null)}  levelUpPending={levelUpPending}  pendingChoiceKind={pendingChoiceKind}  bossRewardDeferredPending={bossRewardDeferredPending}  RewardSequence.IsRunning={sequenceRunning}  RewardSequence.IsWaitingForSelection={sequenceWaiting}  HasStarted={HasStarted}  IsGameOver={IsGameOver}  MaxDistance={MaxDistance:F1}");
    }

    // Bugfix 2026-09-07 (Bug #001 - "Boss中/Boss撃破後にGameplayが停止する")
    // - the exact [BOSS] tag/field set the bug report itself asked for,
    // covering the WHOLE encounter flow end-to-end (PhaseStart -> ... ->
    // GameplayResume) verbatim against the report's own flow diagram. Kept
    // deliberately separate from LogBossRewardStage/[BossRewardTrace] above
    // (that one already covers the Boss Reward sub-pipeline in finer detail
    // with its own field set) rather than merging the two - both are
    // DebugMode-gated and harmless to leave in permanently.
    public void LogBoss(string tag)
    {
        // Bugfix 2026-09-08 (Bug #001 診断フェーズ) - fed into BossDiagnostics'
        // Ring Buffer unconditionally (NOT gated on DebugMode) - the whole
        // point of this diagnostic phase is catching an elusive freeze that
        // might happen during an ordinary play session where DebugMode was
        // never turned on, so the buffer that a freeze snapshot dumps must
        // already have real history in it regardless. Only the console
        // Debug.Log below (existing behavior) stays DebugMode-gated, so a
        // normal build's logcat isn't spammed by default.
        BossDiagnostics.LogEvent(tag);
        if (!DebugMode) return;
        Debug.Log($"[BOSS] {tag}  timeScale={Time.timeScale:F2}  IsBossPhase={(BossManager.Instance != null ? BossManager.Instance.IsBossPhase : (bool?)null)}  HasStarted={HasStarted}  levelUpPending={levelUpPending}  pendingChoice={pendingChoiceKind}  InputEnabled={Time.timeScale > 0f}  MaxDistance={MaxDistance:F1}");
    }

    // Bugfix 2026-09-08 (Bug #001 診断フェーズ) - narrow, read-only surface
    // purely for BossDiagnostics (a separate class - see its own comment for
    // why this lives outside GameManager) to read otherwise-private state
    // for its Freeze Snapshot/state-transition polling. None of these add
    // new behavior, they just expose what already exists.
    public bool LevelUpPending => levelUpPending;
    public bool LevelUpDeferredPending => pendingLevelUpCount > 0;
    public int PendingLevelUpCount => pendingLevelUpCount;
    public bool BossRewardDeferredPending => bossRewardDeferredPending;
    public PendingChoiceKind CurrentPendingChoiceKind => pendingChoiceKind;
    public bool IsRewardSequenceRunning => rewardCardSequence != null && rewardCardSequence.IsRunning;
    public bool IsRewardSequenceWaitingForSelection => rewardCardSequence != null && rewardCardSequence.IsWaitingForSelection;
    public bool IsBossPresentationActivePublic => IsBossPresentationActive();

    public void TriggerBossRewardChoice()
    {
        LogBossRewardStage("BossRewardStart (TriggerBossRewardChoice entry)");
        if (IsBossPresentationActive() || levelUpPending)
        {
            bossRewardDeferredPending = true;
            LogBossRewardStage("BossRewardStart -> deferred (Presentation/LevelUp active)");
            return;
        }
        // Bug #001 診断フェーズ (2026-09-08), 項目8 - "DisableBossRewardSequence"
        // 比較Toggle。ONの間はカード選択UI自体を丸ごとスキップし、Boss撃破
        // →Checkpoint→EndBossPhase→Gameplay Resumeだけを即座に行う -
        // RewardCardSequence側がFreeze原因候補かどうかを切り分けるための
        // 診断専用の分岐(本仕様として削除するものではない)。
        if (BossDiagnostics.DisableBossRewardSequence)
        {
            SkipBossRewardChoice();
            return;
        }
        RunBossRewardChoice();
    }

    // Bug #001 診断フェーズ - RunBossRewardChoiceの空プール分岐と全く同じ
    // 「カードは出さないが、Boss Reward処理としては正常完了」の後始末を、
    // DisableBossRewardSequence診断Toggle専用にもう一度呼べる形にしたもの。
    void SkipBossRewardChoice()
    {
        LogBossRewardStage("RunBossRewardChoice: DisableBossRewardSequence -> skip straight to SaveCheckpoint");
        LogBoss("RewardStart (skipped by DisableBossRewardSequence)");
        try
        {
            SaveCheckpoint();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BossReward] SaveCheckpoint threw (DisableBossRewardSequence path) - continuing the resume regardless: " + e);
        }
        LogBoss("CheckpointSaved");
        if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
        EndBossDistanceExclusion();
        LogBoss("EndBossPhase");
        UnlockEscape();
        LogBossRewardStage("GameplayResume/InputResume/DistanceResume (DisableBossRewardSequence path)");
        LogBoss("GameplayResume");
        LogBoss("RewardEnd");
    }

    float bossRewardDeferredResumeDelay = 0.4f;
    // Bugfix 2026-09-06 - "Boss撃破後にゲームが停止する". This deferral wait
    // (for IsBossPresentationActive()/levelUpPending to clear) had NO
    // timeout at all - if either Presentation's IsRunning ever got stuck
    // true (an uncaught exception mid-coroutine leaves it exactly like
    // that; Unity just logs the error and abandons the routine, it doesn't
    // crash), RunBossRewardChoice() would simply never fire. Since
    // IsBossPhase now only clears once Boss Reward actually completes (the
    // Boss戦中Distance停止 fix from the previous pass), that stuck wait
    // ALSO meant Distance/Enemy Spawn stayed frozen forever - a
    // previously-invisible edge case (the old code unfroze Distance at the
    // boss's own death, before this wait even began) turned newly visible
    // by that same fix. bossRewardStuckTimer accumulates ONLY while
    // genuinely stuck waiting on a Presentation (reset the instant it
    // isn't), and forces the reward through after a duration no legitimate
    // Presentation should ever take.
    float bossRewardStuckTimer;
    const float BossRewardStuckTimeoutSeconds = 12f;

    void UpdateDeferredBossReward()
    {
        if (!bossRewardDeferredPending) return;

        if (IsGameOver)
        {
            bossRewardDeferredPending = false;
            bossRewardDeferredTimer = -1f;
            bossRewardStuckTimer = 0f;
            return;
        }

        if (IsBossPresentationActive() || levelUpPending)
        {
            bossRewardDeferredTimer = -1f;
            bossRewardStuckTimer += Time.unscaledDeltaTime;
            // Bug #001 診断フェーズ, 項目11 - DisableSafetyTimersが立って
            // いる間は、この安全弁自体は「詰まった」まま維持する(強制解決
            // しない) - 原因がタイムアウトで隠れてしまうのを防ぐための
            // 診断専用ガード。
            if (!BossDiagnostics.DisableSafetyTimers && bossRewardStuckTimer >= BossRewardStuckTimeoutSeconds)
            {
                Debug.LogWarning("[BossReward] Deferred wait exceeded " + BossRewardStuckTimeoutSeconds + "s (a Presentation's IsRunning is stuck true) - forcing Boss Reward through anyway.");
                bossRewardDeferredPending = false;
                bossRewardStuckTimer = 0f;
                RunBossRewardChoice();
            }
            return;
        }
        bossRewardStuckTimer = 0f;

        if (bossRewardDeferredTimer < 0f) bossRewardDeferredTimer = bossRewardDeferredResumeDelay;
        bossRewardDeferredTimer -= Time.unscaledDeltaTime;
        if (bossRewardDeferredTimer <= 0f)
        {
            bossRewardDeferredPending = false;
            bossRewardDeferredTimer = -1f;
            RunBossRewardChoice();
        }
    }

    // Bugfix 2026-09-06 - "Boss撃破後にゲームが停止する", second half. Even
    // once RunBossRewardChoice/RunLevelUpChoice actually starts the paused
    // card-choice UI, an uncaught exception partway through
    // RewardCardSequence.RunSequence (before it reaches onApply) would
    // leave Time.timeScale/levelUpPending stuck exactly like the deferred-
    // wait case above - and, same as that case, Distance/Enemy Spawn now
    // stay frozen with it (see bossRewardStuckTimer's own comment). This is
    // the outermost safety net: however long levelUpPending has been true,
    // if it's unreasonably long (RewardCardSequence's OWN 20s tap-timeout
    // means a legitimate Level Up/Boss Reward always resolves well under
    // this), force everything back to a playable state - Time.timeScale=1,
    // the pending choice cleared, and (Boss Reward only) the exact same
    // Checkpoint/EndBossPhase/UnlockEscape a normal resolution would have
    // done, so the player is never left worse off than a real pick would
    // have left them.
    float pendingChoiceStuckTimer;
    const float PendingChoiceStuckTimeoutSeconds = 30f;

    void UpdatePendingChoiceWatchdog()
    {
        if (!levelUpPending)
        {
            pendingChoiceStuckTimer = 0f;
            return;
        }

        pendingChoiceStuckTimer += Time.unscaledDeltaTime;
        // Bug #001 診断フェーズ, 項目11 - DisableSafetyTimers中はこの最終
        // 安全弁も発動させず、詰まった状態をそのまま保持する。
        if (BossDiagnostics.DisableSafetyTimers) return;
        if (pendingChoiceStuckTimer < PendingChoiceStuckTimeoutSeconds) return;

        Debug.LogWarning($"[BossReward] Pending choice ({pendingChoiceKind}) stuck for {PendingChoiceStuckTimeoutSeconds}s - forcing recovery so gameplay/distance/spawning don't stay frozen.");
        bool wasBossReward = pendingChoiceKind == PendingChoiceKind.BossReward;

        if (rewardCardSequence != null) rewardCardSequence.ForceReset();
        levelUpPending = false;
        pendingChoices = null;
        FreezeDiagnostics.LogEvent("[Pause] Watchdog force-resolved stuck choice -> TimeControl.Resume(pendingChoice)");
        TimeControl.Resume(pendingChoiceTimeOwner);
        pendingChoiceStuckTimer = 0f;
        // Bugfix 2026-09-08 - lastLevelUpDiagnostic (Debug Mode's
        // "rewardCardSequence OK, starting sequence..." on-screen text) used
        // to never get cleared anywhere - it's a plain string field set once
        // in RunLevelUpChoice and never reset, so it stayed on screen for
        // the rest of the run even after that particular Level Up fully
        // resolved. Master mistook a stale one (left over from an earlier,
        // already-resolved Level Up much earlier in the run) for evidence
        // about a LATER, unrelated freeze in a Freeze Snapshot screenshot -
        // clearing it here (and at every other place a pending choice
        // resolves, see ApplyUpgradeByCardId/FinishRun) so it only ever
        // reflects a genuinely still-in-flight sequence.
        lastLevelUpDiagnostic = "";

        if (wasBossReward)
        {
            SaveCheckpoint();
            if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
            EndBossDistanceExclusion();
            UnlockEscape();
        }
    }

    // Item 6 - same 3-card draw/pause/RewardCardSequence machinery as
    // RunLevelUpChoice, deliberately WITHOUT its "always fully restores HP"
    // line (a Boss Reward is a bonus card, not a heal) and labeled "BOSS
    // REWARD" instead of "LEVEL UP". If the pool is empty (0-card Deck),
    // there's nothing to offer, so the Checkpoint just saves immediately
    // instead of stalling on a card screen that would never appear -
    // "Boss Reward処理まで正常に終了した時点" still holds either way.
    void RunBossRewardChoice()
    {
        LogBossRewardStage("RunBossRewardChoice entry");
        LogBoss("RewardStart");
        // マルチプレイPhase 2.5: ボス報酬は最後のボスのラストヒットを取った本人(RewardRecipient)だけ。
        // 本人がJOINなら、その端末へ選択を渡し、HOSTはボス戦の後始末(ボス戦終了/距離再開)だけ行う。
        if (NetCombat.Authority)
        {
            int recipient = NetCombat.LastBossRewardRecipient;
            NetCombat.Log("BOSS", $"Reward Recipient = P{recipient} (local=P{NetCombat.LocalPlayerNumber})");
            if (recipient > 0 && recipient != NetCombat.LocalPlayerNumber)
            {
                NetMatch.SendBossRewardOffer(recipient);
                if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
                EndBossDistanceExclusion();
                UnlockEscape();
                LogBoss("RewardEnd (handed to remote recipient)");
                return;
            }
        }
        // マルチ(2026-09-28): この端末のプレイヤーがRun終了/脱落/DOWN中なら、ボス報酬の選択は出さない
        // (Resultより後に出ない・脱落後に新しい選択を始めない)。ボス戦の後始末だけは通常どおり行う。
        if (IsNetChoiceBlocked(out _))
        {
            Debug.Log($"[NET][CHOICE] Boss Reward not shown - {NetChoiceBlockReason()}");
            if (!IsGameOver)
            {
                if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
                EndBossDistanceExclusion();
                UnlockEscape();
            }
            LogBoss("RewardEnd (blocked: run finished / eliminated / down)");
            return;
        }

        var pool = new List<CardDefinition>();
        foreach (string id in deckCards)
        {
            CardDefinition card = CardDatabase.FindById(id);
            if (card != null) pool.Add(card);
        }
        if (pool.Count == 0 && deckCards.Count > 0) pool.AddRange(CardDatabase.UnlockedCards);

        if (pool.Count == 0)
        {
            LogBossRewardStage("RunBossRewardChoice: pool empty -> SaveCheckpoint");
            // Bugfix 2026-09-07 (Bug #001, report item 4) - same
            // SaveCheckpoint-failure-must-not-block-resume guard as
            // ApplyUpgradeByCardId's matching try/catch.
            try
            {
                SaveCheckpoint();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BossReward] SaveCheckpoint threw (empty-pool path) - continuing the resume regardless: " + e);
            }
            LogBoss("CheckpointSaved");
            // Bugfix 2026-09-06, item "Boss戦中Distance停止" - this is a
            // Boss Reward that resolved with nothing to actually offer (an
            // empty/corrupted deck), but it's still "Boss Reward処理まで
            // 正常に終了した" - the Distance freeze must lift here too, not
            // just on the normal 3-card path below.
            if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
            EndBossDistanceExclusion();
            LogBossRewardStage("RunBossRewardChoice: pool empty -> EndBossPhase done");
            LogBoss("EndBossPhase");
            UnlockEscape();
            LogBossRewardStage("GameplayResume/InputResume/DistanceResume (empty-pool path)");
            LogBoss("GameplayResume");
            LogBoss("RewardEnd");
            return;
        }

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int pickCount = Mathf.Min(3, pool.Count);
        pendingChoices = new CardDefinition[pickCount];
        for (int i = 0; i < pickCount; i++) pendingChoices[i] = pool[i];

        pendingChoiceKind = PendingChoiceKind.BossReward;
        levelUpPending = true;
        FreezeDiagnostics.LogEvent("[Pause] BossReward choice start");
        // マルチプレイPhase 2.5: マルチでは世界全体を止めない(選んでいる本人の端末にUIが出るだけ)。
        if (!NetMatch.Active) TimeControl.Pause(pendingChoiceTimeOwner);

        if (rewardCardSequence != null && pendingChoices.Length > 0)
        {
            var cards = new RewardCardData[pendingChoices.Length];
            for (int i = 0; i < pendingChoices.Length; i++)
            {
                cards[i] = MakeChoiceCardData(pendingChoices[i]);
            }
            LogBossRewardStage("RewardCardSequence Start (about to call StartSequence)");
            rewardCardSequence.StartSequence(cards, ApplyUpgradeByCardId, "BOSS REWARD");
            LogBossRewardStage("RewardCardSequence Start (StartSequence call returned)");
        }
        else
        {
            ApplyUpgradeByCardId(pendingChoices[0].cardId);
        }
    }

    // Draws 3 cards from the player's deck at random and pauses the game
    // (via Time.timeScale) until the player picks one - nearly everything
    // in this game drives its movement off Time.deltaTime, so this alone
    // freezes gameplay without having to touch every script individually.
    void RunLevelUpChoice()
    {
        // Leveling up always fully restores HP, regardless of whether a
        // card ends up being offered below.
        Lives = maxLives;
        NetMatch.RequestSetMax(maxLives, true);

        // Draws from the player's edited deck rather than every card in the
        // database. The fallback to the full unlocked pool only covers the
        // deck-corruption case (the deck HAD entries, but none of them
        // resolve any more - e.g. every id in it got removed from
        // CardDatabase) - an intentionally empty deck (deckCards.Count == 0,
        // a valid player choice since "0枚デッキ" / DeckEditUI's "全て外す")
        // must NOT fall back to the full pool, or clearing the deck would
        // silently keep offering cards anyway.
        var pool = new List<CardDefinition>();
        foreach (string id in deckCards)
        {
            CardDefinition card = CardDatabase.FindById(id);
            if (card != null) pool.Add(card);
        }
        if (pool.Count == 0 && deckCards.Count > 0) pool.AddRange(CardDatabase.UnlockedCards);

        if (pool.Count == 0)
        {
            // No cards available to draw from at all - level up "quietly"
            // instead of opening the card choice screen. Critically must
            // NOT touch Time.timeScale/levelUpPending, so the run just
            // keeps going exactly as if this method had never paused
            // anything - a 0-card deck must never stall progression.
            return;
        }

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int pickCount = Mathf.Min(3, pool.Count);
        pendingChoices = new CardDefinition[pickCount];
        for (int i = 0; i < pickCount; i++) pendingChoices[i] = pool[i];

        pendingChoiceKind = PendingChoiceKind.LevelUp;
        levelUpPending = true;
        FreezeDiagnostics.LogEvent("[Pause] LevelUp choice start");
        if (!NetMatch.Active) TimeControl.Pause(pendingChoiceTimeOwner);

        // Diagnostic: proves whether rewardCardSequence actually survived
        // into the build, unconditionally (not gated behind anything the
        // sequence itself sets), and stays on screen so it's visible even
        // if everything freezes right here.
        lastLevelUpDiagnostic = rewardCardSequence != null
            ? "rewardCardSequence OK, starting sequence..."
            : "rewardCardSequence is NULL";
        Debug.Log("TriggerLevelUpChoice: " + lastLevelUpDiagnostic);

        // Presentation only - the actual pool/selection/effect logic above
        // and in ApplyUpgradeByCardId is untouched.
        if (rewardCardSequence != null && pendingChoices.Length > 0)
        {
            var cards = new RewardCardData[pendingChoices.Length];
            for (int i = 0; i < pendingChoices.Length; i++)
            {
                cards[i] = MakeChoiceCardData(pendingChoices[i]);
            }
            rewardCardSequence.StartSequence(cards, ApplyUpgradeByCardId);
        }
        else
        {
            // Safety net: whatever the reason the card sequence isn't
            // available, the run must never be stuck permanently paused -
            // resolve the choice immediately with the first option instead.
            // (pendingChoices always has at least 1 entry here - the
            // pool.Count == 0 case already returned above.)
            ApplyUpgradeByCardId(pendingChoices[0].cardId);
        }
    }

    // Card Level Ver.1, item 6 - "Card Lv.3 -> ApplyCardEffect(card,
    // stacks: 3)": the one shared operation every stacking source funnels
    // through (Character Card equip today; a future compound-fusion result
    // could reuse this too, per item 7's "同じCardEffect処理を利用できる
    // 構造にしたい"), instead of each caller writing its own loop. A card
    // "at Lv.N" is defined as having been picked up N times - nothing more;
    // every existing EffectType (including the ones flagged as special -
    // Jump Count, Shield, Vampire, Berserker, Greed, Pathfinder - see the
    // brief's item 5) was already safe to apply repeatedly before this
    // existed, since a player could already pick the same Level Up card
    // multiple times in one run. No new clamping/rebalancing was added
    // here on purpose - the brief explicitly asked to confirm the existing
    // stacking behavior rather than redesign it.
    public void ApplyCardEffectsStacked(CardDefinition card, int stacks)
    {
        for (int i = 0; i < stacks; i++) ApplyCardEffects(card);
    }

    // The single place every CardDefinition's effects get turned into an
    // actual gameplay change - see EffectType for what each case means.
    // Adding a new card that only reuses these EffectTypes needs no changes
    // here at all; only a genuinely new EffectType does.
    void ApplyCardEffects(CardDefinition card)
    {
        PlayerController pc = PlayerController.Instance;
        foreach (CardEffect effect in card.effects)
        {
            switch (effect.type)
            {
                case EffectType.MoveSpeed:
                    if (pc != null) pc.runSpeed *= 1f + effect.value;
                    break;
                case EffectType.AttackPower:
                    if (pc != null) pc.AddAttackPower(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.JumpPower:
                    if (pc != null) pc.jumpForce *= 1f + effect.value;
                    break;
                case EffectType.JumpCount:
                    if (pc != null) pc.maxJumps += Mathf.RoundToInt(effect.value);
                    break;
                case EffectType.MaxHp:
                    // Also immediately heals up to the new cap, matching
                    // the original HEART UP card's behaviour - floored at 1
                    // so a negative-value card (BERSERKER) can never zero
                    // out the heart cap entirely.
                    maxLives = Mathf.Max(1, Mathf.Min(maxLivesCap, maxLives + Mathf.RoundToInt(effect.value)));
                    Lives = maxLives;
                    NetMatch.RequestSetMax(maxLives, true);
                    break;
                case EffectType.AttackRange:
                    if (pc != null) pc.AddAttackRangeBonus(effect.value);
                    break;
                case EffectType.AttackSpeed:
                    if (pc != null) pc.AddAttackSpeedBonus(effect.value);
                    break;
                case EffectType.AirAttackPower:
                    if (pc != null) pc.AddAirAttackPowerBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.Shield:
                    if (pc != null) pc.AddShieldCharges(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.ExpGain:
                    expGainMultiplier += effect.value;
                    break;
                case EffectType.LifestealChance:
                    lifestealChance = Mathf.Clamp01(lifestealChance + effect.value);
                    break;
                case EffectType.LifestealAmount:
                    lifestealAmount += effect.value;
                    break;
                case EffectType.EnemySpawnRate:
                    EnemySpawnRateMultiplier += effect.value;
                    break;

                // ===== Card Expansion/Gacha Evolution Ver.1 additions ===== //
                case EffectType.GroundAttackPower:
                    if (pc != null) pc.AddGroundAttackPowerBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.ComboFinalStageBonus:
                    if (pc != null) pc.AddComboFinalStageBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.FirstHitBonus:
                    if (pc != null) pc.AddFirstHitBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.LowHpAttackBonus:
                    if (pc != null) pc.AddLowHpAttackBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.FullHpAttackBonus:
                    if (pc != null) pc.AddFullHpAttackBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.MomentumBonus:
                    if (pc != null) pc.AddMomentumBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.BossDamageBonus:
                    if (pc != null) pc.AddBossDamageBonus(Mathf.RoundToInt(effect.value));
                    break;
                case EffectType.EnemyHpMultiplier:
                    EnemyHpMultiplier += effect.value;
                    break;
                case EffectType.BossHpMultiplier:
                    BossHpMultiplier += effect.value;
                    break;
                case EffectType.MileGainMultiplier:
                    MileGainMultiplier += effect.value;
                    break;
                case EffectType.BossMileGainMultiplier:
                    BossMileGainMultiplier += effect.value;
                    break;
            }
        }
    }

    // Bugfix 2026-09-07 (Bug #001, root cause) - this method's resume-
    // critical lines (levelUpPending=false/Time.timeScale=1f and, for a
    // Boss Reward, Checkpoint/EndBossPhase/EndBossDistanceExclusion/
    // UnlockEscape) used to run unconditionally AFTER ApplyCardEffects(card)
    // in plain sequence - if ApplyCardEffects ever threw (a malformed
    // CardEffect, an unexpected value, etc.), NONE of the resume logic below
    // it would run at all, leaving Time.timeScale/levelUpPending/IsBossPhase
    // stuck exactly like the report describes (recoverable only via the 30s
    // pendingChoiceStuckTimer watchdog). Now wrapped in try/finally so the
    // resume itself is unconditional. SaveCheckpoint() specifically is ALSO
    // now wrapped in its own try/catch (report's own explicit instruction -
    // "SaveCheckpoint()成功をResume条件にしすぎないよう注意...Save処理が失
    // 敗してもGameplay Stateが永久停止しない設計に") so a save failure can
    // never block EndBossPhase/EndBossDistanceExclusion/UnlockEscape either.
    void ApplyUpgradeByCardId(string cardId)
    {
        LogBossRewardStage($"Reward Selected (cardId={cardId})");
        bool wasBossReward = pendingChoiceKind == PendingChoiceKind.BossReward;
        if (wasBossReward) LogBoss("RewardCardSelected");

        try
        {
            CardDefinition card = CardDatabase.FindById(cardId);
            if (card != null)
            {
                // "そのRun中だけ有効な強化...Owned CardとしてHome Roomへ追加し
                // ないでください" - a Boss Reward pick goes through this EXACT
                // same path as a normal Level Up pick (ApplyCardEffects +
                // upgradeHistory only), which already never touches
                // CardInventory - so that requirement holds for free just by
                // reusing this method verbatim.
                ApplyCardEffects(card);
                upgradeHistory.Add(card);
                // Bugfix 2026-09-05, item 4 - see ApplyCharacterCardEffects's
                // matching log; GetCurrentRunStack already includes the Add
                // above (upgradeHistory was just appended to).
                if (DebugMode) Debug.Log($"[CardStack] Run pick: {card.cardName} -> runStackNow={GetCurrentRunStack(card.cardId)} (kind={pendingChoiceKind})");
            }
        }
        finally
        {
            // Item 7 - a Boss Reward choice resolving (even to nothing, if
            // card==null, or if ApplyCardEffects above threw) is exactly
            // "Boss Reward処理まで正常に終了した時点" - the Checkpoint updates
            // here, AFTER the pick, not before.
            levelUpPending = false;
            pendingChoices = null;
            FreezeDiagnostics.LogEvent("[Pause] Choice resolved -> TimeControl.Resume(pendingChoice)");
            TimeControl.Resume(pendingChoiceTimeOwner);
            // Bugfix 2026-09-08 - see UpdatePendingChoiceWatchdog's matching
            // comment for why this needs clearing on every resolution path,
            // not just left to persist until the next Level Up overwrites it.
            lastLevelUpDiagnostic = "";
            LogBossRewardStage("GameplayResume/InputResume (Time.timeScale=1f, levelUpPending=false)");
            if (wasBossReward) LogBoss("GameplayResume");

            if (wasBossReward)
            {
                try
                {
                    SaveCheckpoint();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[BossReward] SaveCheckpoint threw - continuing the resume regardless (report item 4): " + e);
                }
                LogBossRewardStage("SaveCheckpoint done");
                LogBoss("CheckpointSaved");
                // Bugfix 2026-09-06, item "Boss戦中Distance停止" - Distance (and
                // Enemy Wall spawn/TerrainManager safe-terrain suppression) only
                // resumes here, at actual Boss Reward completion - not at the
                // boss's own death (see CheckEncounterComplete's own comment).
                if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
                EndBossDistanceExclusion();
                LogBossRewardStage("EndBossPhase done -> DistanceResume");
                LogBoss("EndBossPhase");
                UnlockEscape();
                LogBoss("RewardEnd");
            }
        }
    }

    // Bugfix 2026-09-06 - "最初の1000m Boss撃破 + Reward完了" is the Run's
    // one-time Escape unlock condition (see escapeUnlocked's own comment).
    // Called from both Boss Reward completion paths above - idempotent
    // past the very first call (escapeUnlocked stays true, and
    // escapeAvailableAnnounced already guards the banner same as before),
    // so no "is this the first boss" check is needed here.
    void UnlockEscape()
    {
        bool wasAlreadyUnlocked = escapeUnlocked;
        escapeUnlocked = true;
        if (!escapeAvailableAnnounced)
        {
            escapeAvailableAnnounced = true;
            escapeAvailableBannerTimer = escapeAvailableBannerDuration;
        }
        if (DebugMode && !wasAlreadyUnlocked)
        {
            Debug.Log($"[EscapeState] EscapeUnlocked=true at Distance={Mathf.FloorToInt(MaxDistance)}m (BossGate={(BossManager.Instance != null ? BossManager.Instance.IsBossPhase : false)})");
        }
    }

    // Single funnel for every "the player got hurt" source (enemy contact,
    // boss contact while attacking, fireball, falling). Handles the debug
    // invincibility toggle, SHIELD charges, and life count; PlayerController
    // is responsible for the actual respawn/flicker on a non-fatal hit.
    // Bugfix 2026-09-06, item 2 - `reason` is Debug-log only (Debug.Log
    // below, no behavior branches on it) so the actual GAME OVER cause can
    // be confirmed on a real device rather than inferred from review alone.
    public DamageResult TryDamagePlayer(bool bypassInvincibleMode = false, string reason = "Other")
    {
        Vector3 dmgPos = PlayerController.Instance != null ? PlayerController.Instance.transform.position : Vector3.zero;
        if (IsGameOver) return DamageResult.Ignored;
        if (PresentationDamageLock) { FreezeDiagnostics.LogEvent($"[Damage] Ignored(PresentationDamageLock) reason={reason} pos=({dmgPos.x:F2},{dmgPos.y:F2})"); return DamageResult.Ignored; }
        if (!bypassInvincibleMode && InvincibleMode) return DamageResult.Ignored;
        if (PlayerController.Instance != null && PlayerController.Instance.TryConsumeShield()) { FreezeDiagnostics.LogEvent($"[Damage] Shielded reason={reason} pos=({dmgPos.x:F2},{dmgPos.y:F2})"); return DamageResult.Ignored; }

        FreezeDiagnostics.LogEvent($"[Damage] Hit reason={reason} pos=({dmgPos.x:F2},{dmgPos.y:F2}) livesBefore={Lives} timeScale={Time.timeScale:F2}");
        Lives = Mathf.Max(0, Lives - 1);
        heartDamageFlashTimer = heartDamageFlashDuration;
        // マルチプレイPhase 2.5: HOST自身のHPの変化もHOSTの表(全員へ配る正解)へ即反映する。
        if (NetCombat.Authority) NetMatch.HostLocalHpChanged(reason);
        if (Lives <= 0)
        {
            // マルチプレイPhase 3: CO-OP=ダウン / VERSUS=脱落 はRunを終えずにNetMatchが扱う。
            if (NetMatch.HostLocalHpZero(reason)) return DamageResult.GameOver;
            // Bugfix 2026-09-06, item 2 - logged unconditionally (not just
            // under DebugMode) since a GAME OVER is rare enough that this
            // never spams, and this is the one moment the report explicitly
            // asked to always be able to see the cause for.
            if (PlayerController.Instance != null)
            {
                Transform pt = PlayerController.Instance.transform;
                Debug.Log($"[GameOver] Reason={reason}  Pos=({pt.position.x:F2},{pt.position.y:F2})  HP=0  Grounded={PlayerController.Instance.IsGrounded}");
            }
            else
            {
                Debug.Log($"[GameOver] Reason={reason}  HP=0  (PlayerController.Instance was null)");
            }

            // Item 15 - "死亡判定が成立したら、死亡演出より先にActive Run/
            // Continue可能状態を無効化してください": PlayerController only
            // reacts to IsGameOver (set inside FinishRun below) on its
            // OWN next Update(), i.e. strictly after this call returns, so
            // clearing the checkpoint here already happens before the
            // death Presentation with no extra ordering needed.
            if (!NetRunLauncher.IsMultiplayerRun) RunCheckpoint.Clear(); // マルチプレイRunの死亡でシングルのCONTINUEを消さない
            FinishRun();
            return DamageResult.GameOver;
        }
        // Item 11 - HP is the single most important piece of "強制終了に
        // よる逃げ対策" state; saved the instant it actually changes; not
        // gated on !IsGameOver since a fatal hit already returned above.
        SaveInterruptState();
        return DamageResult.Hit;
    }

    // ===== マルチプレイPhase 2.5: JOINのHPはHOSTが決める =====

    // JOIN: TryDamagePlayerと同じ事前チェック(終了済み/演出中ロック/無敵モード/シールド)だけを行う。
    // trueなら「HPを減らす被弾」としてHOSTへ申告してよい(ここではHPを減らさない)。
    public bool NetPrecheckDamage(bool bypassInvincibleMode, string reason)
    {
        if (IsGameOver) return false;
        if (PresentationDamageLock) { FreezeDiagnostics.LogEvent($"[Damage] Ignored(PresentationDamageLock) reason={reason} (net claim)"); return false; }
        if (!bypassInvincibleMode && InvincibleMode) return false;
        if (PlayerController.Instance != null && PlayerController.Instance.TryConsumeShield()) { FreezeDiagnostics.LogEvent($"[Damage] Shielded reason={reason} (net claim)"); return false; }
        return true;
    }

    // Phase 3: HOSTがこの端末のプレイヤーのHPを直接決めた(復活の授受/DOWN)。
    public void NetSetLocalLives(int n)
    {
        if (n > maxLives) maxLives = n;
        Lives = Mathf.Max(0, n);
    }

    float netLocalHpRequestUntil;
    public void NetNoteLocalHpRequest() { netLocalHpRequestUntil = Time.realtimeSinceStartup + 0.5f; }

    // JOIN: HOSTが確定したHP/最大HPを反映する(表示のハートもこの値)。
    public void NetApplyAuthoritativeLives(int hp, int max, bool fromHit = false)
    {
        if (IsGameOver) return;
        // 回復/最大HPの要求を送った直後は、HOSTが処理するまでの古い表で巻き戻さない(被弾の確定は常に反映)。
        if (!fromHit && Time.realtimeSinceStartup < netLocalHpRequestUntil && hp < Lives) return;
        if (hp < Lives) heartDamageFlashTimer = heartDamageFlashDuration;
        maxLives = Mathf.Max(1, max);
        Lives = Mathf.Clamp(hp, 0, Mathf.Max(maxLives, hp));
    }

    // ===== マルチ(2026-09-28): 状態の優先順位 =====
    // Run Finished > Eliminated/Down > ChoosingCard > 通常走行。
    // 上位の状態にある間は、この端末でカード選択(レベルアップ/ボス報酬)を開かない・開いていれば閉じる。
    // dropQueued=true: キュー中の選択も捨てる(Run終了/脱落 = もう選べる機会が来ない)。
    // CO-OPのDOWNだけは復活があり得るので、キューは残して復活後に出す(表示中のものは閉じる)。
    bool IsNetChoiceBlocked(out bool dropQueued)
    {
        dropQueued = false;
        if (!NetMatch.Active || !HasStarted) return false;
        if (IsGameOver || NetRunLauncher.RunState == NetRunState.Finished || (NetMatch.Instance != null && NetMatch.Instance.RunOver))
        {
            dropQueued = true;
            return true;
        }
        NetMatch.Rec me = NetMatch.Get(NetCombat.LocalPlayerNumber);
        if (me != null && me.State != NetMatch.PState.Alive)
        {
            dropQueued = me.State != NetMatch.PState.Down;
            return true;
        }
        return false;
    }

    string NetChoiceBlockReason()
    {
        if (IsGameOver || NetRunLauncher.RunState == NetRunState.Finished || (NetMatch.Instance != null && NetMatch.Instance.RunOver)) return "run finished";
        NetMatch.Rec me = NetMatch.Get(NetCombat.LocalPlayerNumber);
        return me != null ? $"local player {me.State}" : "blocked";
    }

    // 毎フレーム: 選択UIが開いている/開きかけているのに上位の状態になっていたら、すぐ閉じる。
    void EnforceNetChoicePriority()
    {
        if (!NetMatch.Active) return;
        bool uiUp = levelUpPending || (rewardCardSequence != null && rewardCardSequence.IsRunning);
        if (!uiUp && !bossRewardDeferredPending) return;
        if (!IsNetChoiceBlocked(out bool dropQueued)) return;
        NetCloseAllChoices(NetChoiceBlockReason(), dropQueued);
    }

    public int NetChoicesClosedCount { get; private set; } // 自動テスト用

    // 表示中/待機中の選択UIをすべて閉じる(カードは適用しない)。Run終了/脱落/DOWNの時に使う。
    public void NetCloseAllChoices(string reason, bool dropQueued)
    {
        bool uiUp = levelUpPending || (rewardCardSequence != null && rewardCardSequence.IsRunning);
        bool wasBossReward = levelUpPending && pendingChoiceKind == PendingChoiceKind.BossReward;
        bool bossQueued = bossRewardDeferredPending;
        if (rewardCardSequence != null && rewardCardSequence.IsRunning) rewardCardSequence.ForceReset();
        levelUpPending = false;
        pendingChoices = null;
        lastLevelUpDiagnostic = "";
        pendingChoiceStuckTimer = 0f;
        TimeControl.Resume(pendingChoiceTimeOwner);
        int dropped = 0;
        if (dropQueued)
        {
            dropped = pendingLevelUpCount;
            pendingLevelUpCount = 0;
            levelUpDeferredTimer = -1f;
        }
        bossRewardDeferredPending = false;
        bossRewardDeferredTimer = -1f;
        bossRewardStuckTimer = 0f;
        // ボス報酬を出さずに終えた場合も、ボス戦の後始末(距離/湧きの再開)は通常の決定時と同じに行う。
        if ((wasBossReward || bossQueued) && !IsGameOver)
        {
            if (BossManager.Instance != null) BossManager.Instance.EndBossPhase();
            EndBossDistanceExclusion();
            UnlockEscape();
        }
        NetChoicesClosedCount++;
        Debug.Log($"[NET][CHOICE] choice UI closed ({reason}) uiWasOpen={uiUp} bossReward={wasBossReward} bossQueued={bossQueued} droppedLevelUps={dropped} keptLevelUps={pendingLevelUpCount}");
    }

    // JOIN: HOSTの判定でこの端末のRunが終わった(Phase 2.5の既定=HP0)。
    public void NetForceGameOver(string reason)
    {
        if (IsGameOver) return;
        Debug.Log($"[GameOver] Reason={reason} (decided by HOST)");
        Lives = 0;
        FinishRun();
    }

    // JOIN: HOSTから「ボス報酬はあなた」と届いた。既存のボス報酬の流れ(演出待ち→3枚選択)で出す。
    public void NetOfferBossReward()
    {
        if (IsGameOver || !HasStarted) return;
        TriggerBossRewardChoice();
    }

    // Kills no longer restore HP directly on their own (see the HEART UP /
    // VAMPIRE cards instead) - they grant EXP toward the next level-up, and
    // (with VAMPIRE) a chance to proc a small heal.
    public void RegisterEnemyKill(int mileReward = 1)
    {
        EnemyKillCount++;
        GainExp(enemyKillExp);
        TryLifesteal();
        // Card Expansion/Gacha Evolution Ver.1 - Tough/Fast/Elite Enemies,
        // Treasure Hunter, Mob Killer, Executioner, Hell Mode, etc.
        RunEnemyMile += Mathf.Max(0, Mathf.RoundToInt(mileReward * MileGainMultiplier));
    }

    public void RegisterBossDefeat(int mileReward = 50)
    {
        BossKillCount++;
        GainExp(bossKillExp);
        TryLifesteal();
        // Card Expansion/Gacha Evolution Ver.1 - Boss Challenge/Rush, One
        // More Mile, Pandemonium, etc.
        RunBossMile += Mathf.Max(0, Mathf.RoundToInt(mileReward * BossMileGainMultiplier));
        // Item 11 - a Boss kill is rare and meaningful enough to save
        // immediately (unlike every ordinary enemy kill, which would be
        // too frequent to write to disk each time - the periodic 100m save
        // in ReportDistance already keeps RunEnemyMile reasonably fresh).
        SaveInterruptState();
    }

    void TryLifesteal()
    {
        if (lifestealChance <= 0f || lifestealAmount <= 0f) return;
        if (Random.value < lifestealChance) AddLife(Mathf.RoundToInt(lifestealAmount));
    }

    // 10〜12人目(2026-09-28) - 吸血鬼の吸血回復用の公開入口(回復量と頻度の上限は吸血鬼側で管理)。
    public void KitHeal(int amount) { if (amount > 0 && !IsGameOver) AddLife(amount); }

    void AddLife(int amount = 1)
    {
        Lives = Mathf.Min(maxLives, Lives + amount);
        NetMatch.RequestHeal(amount);
    }

    public void Win()
    {
        if (IsGameOver) return;
        IsWin = true;
        FinishRun();
    }

    void FinishRun()
    {
        IsGameOver = true;
        gameOverTime = Time.time;
        // Safety net: Time.timeScale is a global engine setting that would
        // otherwise persist across a scene reload (Retry) - if the run
        // somehow ended while a level-up pause (or any other TimeControl
        // reason, including a leaked HitStop) was still active, this
        // guarantees the next run doesn't start frozen. TimeControl.ResetAll
        // clears every registered pause reason, not just this one, on purpose.
        TimeControl.ResetAll();
        // マルチ(2026-09-28): Resultを最優先 - 表示中のカード選択UIを閉じ、キュー中の選択も捨てる
        // (以前はフラグだけ下ろしてUIが画面に残り、VERSUS RESULTと重なることがあった)。
        if (rewardCardSequence != null && rewardCardSequence.IsRunning) rewardCardSequence.ForceReset();
        pendingLevelUpCount = 0;
        levelUpDeferredTimer = -1f;
        bossRewardDeferredPending = false;
        bossRewardDeferredTimer = -1f;
        NetRunLauncher.MarkFinished(IsWin ? "run finished (win)" : "run finished (game over)");
        levelUpPending = false;
        pendingChoices = null;
        lastLevelUpDiagnostic = ""; // Bugfix 2026-09-08 - see UpdatePendingChoiceWatchdog's matching comment
        // Time.time itself doesn't advance while paused for a level-up
        // choice (Time.timeScale = 0), so this naturally excludes any time
        // spent on those pauses from the recorded run time.
        RunTime = Time.time - runStartTime;

        // マップ別BEST: そのランのステージIDの記録だけを更新する(帰還/ゲームオーバーの確定タイミングは従来どおりここ)。
        string bestStageId = activeRunStageId;
        IsNewBestDistance = MaxDistanceExact > GetStageBest(bestStageId);
        if (IsNewBestDistance) SetStageBest(bestStageId, MaxDistanceExact);
        // 全体の最高距離(解放/ガチャ進行用)は従来どおり。
        if (MaxDistance > BestDistance)
        {
            BestDistance = MaxDistance;
            PlayerPrefs.SetFloat(BestDistanceKey, BestDistance);
        }

        IsNewBestTime = RunTime > BestTime;
        if (IsNewBestTime)
        {
            BestTime = RunTime;
            PlayerPrefs.SetFloat(BestTimeKey, BestTime);
        }

        if (IsNewBestDistance || IsNewBestTime) PlayerPrefs.Save();

        // Reward/MILE System Ver.1, items 1/2 - DISTANCE MILE only finalizes
        // here (MaxDistance stops changing the instant the run ends);
        // ENEMY/BOSS MILE already accumulated live via RegisterEnemyKill/
        // RegisterBossDefeat.
        //
        // Run Continuation/Checkpoint Ver.1, item 12 - uses
        // Max(MaxDistance, HighestReachedDistance) rather than MaxDistance
        // alone, so a shorter post-CONTINUE segment (finishing before
        // re-reaching a previously-interrupted run's old high point) can
        // never REDUCE the Distance MILE the player already effectively
        // earned, while re-crossing already-covered ground can never grant
        // it a second time either (both are just a max, not a sum).
        //
        // Item 1/16 - FINISH (via Win()) awards the FULL RunMile; GAME OVER
        // (this method also runs for that path) LOSES it entirely instead -
        // "そのRunで獲得した未確定MILEは全て失います".
        RunDistanceMile = Mathf.FloorToInt(Mathf.Max(MaxDistance, HighestReachedDistance) / 100f);
        if (IsWin) AddMile(RunMile);

        // Item 15/16 - Active Run/Checkpoint is invalidated on EITHER end
        // condition (idempotent with the earlier RunCheckpoint.Clear() call
        // in TryDamagePlayer's GAME OVER branch, and the only call for the
        // FINISH/Win() path).
        // マルチプレイRunの終了で、シングルの中断データ(CONTINUE)を消さない。
        if (!NetRunLauncher.IsMultiplayerRun) RunCheckpoint.Clear();
    }

    public void Retry()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ===== Run Continuation/Checkpoint Ver.1 ===== //

    // Item 11 - called often enough (100m ticks, HP changes, Boss kills,
    // app pause/quit) that an abrupt app kill can't lose much, but cheap
    // enough (one JSON serialize + PlayerPrefs write) to not matter
    // performance-wise at that frequency. Deliberately does NOT touch
    // checkpointDistance - only SaveCheckpoint (Boss Reward) moves that.
    void SaveInterruptState()
    {
        if (!HasStarted || IsGameOver) return;
        // マルチプレイRunはシングル用の中断データ(CONTINUE)を上書きしない。
        if (NetRunLauncher.IsMultiplayerRun) return;
        RunCheckpoint.Data data = RunCheckpoint.Load();
        data.active = true;
        FillCheckpointSnapshot(data);
        RunCheckpoint.Save(data);
    }

    // Item 7 - called once Boss Reward's card choice has fully resolved
    // (see ApplyUpgradeByCardId). This is the ONLY place checkpointDistance
    // itself advances.
    void SaveCheckpoint()
    {
        if (!HasStarted || IsGameOver) return;
        if (NetRunLauncher.IsMultiplayerRun) return; // SaveInterruptStateと同じ理由
        RunCheckpoint.Data data = RunCheckpoint.Load();
        data.active = true;
        data.checkpointDistance = MaxDistance;
        FillCheckpointSnapshot(data);
        RunCheckpoint.Save(data);
    }

    void FillCheckpointSnapshot(RunCheckpoint.Data data)
    {
        data.characterId = activeRunCharacterId;
        data.stageId = activeRunStageId;
        data.highestReachedDistance = HighestReachedDistance;
        data.lives = Lives;
        data.maxLives = maxLives;
        data.level = Level;
        data.exp = Exp;
        data.expToNext = ExpToNext;
        data.enemyKillCount = EnemyKillCount;
        data.bossKillCount = BossKillCount;
        data.runEnemyMile = RunEnemyMile;
        data.runBossMile = RunBossMile;
        data.escapeUnlocked = escapeUnlocked;
        data.upgradeHistoryCardIds = new List<string>();
        foreach (CardDefinition card in upgradeHistory) data.upgradeHistoryCardIds.Add(card.cardId);
    }

    // Item 9 - "RETURN TO HOME" - NOT a FINISH: Run MILE stays unconfirmed,
    // the run itself doesn't end (IsGameOver/IsWin untouched), and
    // Checkpoint is left exactly where the last Boss Reward set it. Reuses
    // the exact same reload path GAME OVER's "Tap to Retry" already uses
    // (RetryWithTransition) - the freshly reloaded Home Room will simply
    // see RunCheckpoint.HasActiveRun still true and offer CONTINUE.
    public void ReturnToHome()
    {
        if (!HasStarted || IsGameOver) return;
        TimeControl.ResetAll(); // defensive - same reasoning as FinishRun's own reset, in case this is ever reached while still paused
        SaveInterruptState();
        RetryWithTransition();
    }

    // Item 13 - the door's primary action when an Active Run already
    // exists. Physically warps the player to the checkpoint's distance
    // (same mechanism DebugWarpToDistance already uses for a large jump,
    // just without that method's Debug-build-only gate - this is a real,
    // player-facing feature) and reconstructs HP/Level/EXP/MILE/Build by
    // replaying the exact same effect-application sequence a fresh run
    // would have gone through (see BeginContinuedRun).
    public void ContinueActiveRun()
    {
        if (!RunCheckpoint.HasActiveRun) return;
        if (startTransitioning || HasStarted) return;

        if (ScreenTransitionManager.Instance != null)
        {
            if (ScreenTransitionManager.Instance.IsTransitioning) return;
            startTransitioning = true;
            ScreenTransitionManager.Instance.PlayTransition(() =>
            {
                BeginContinuedRun();
                startTransitioning = false;
            });
            return;
        }
        BeginContinuedRun();
    }

    void BeginContinuedRun()
    {
        RunCheckpoint.Data data = RunCheckpoint.Load();

        HasStarted = true;
        runStartTime = Time.time;

        MaxDistance = data.checkpointDistance;
        MaxDistanceExact = data.checkpointDistance;
        HighestReachedDistance = Mathf.Max(data.highestReachedDistance, data.checkpointDistance);
        Level = Mathf.Max(1, data.level);
        Exp = data.exp;
        ExpToNext = data.expToNext > 0f ? data.expToNext : expBaseForLevel2;
        EnemyKillCount = data.enemyKillCount;
        BossKillCount = data.bossKillCount;
        RunEnemyMile = data.runEnemyMile;
        RunBossMile = data.runBossMile;
        escapeUnlocked = data.escapeUnlocked;

        // プレイアブル主人公追加(2026-09-12、お嬢様騎士) - このRunが実際
        // に開始された時のキャラクター(data.characterId)を使う。Homeで
        // Character Selectの選択(SelectedCharacterId)がその後変わって
        // いても、このRun自体のキャラクターは変わらない(マスターの明示
        // 要件)。data.characterIdが空(=この仕組みが入る前に保存された
        // 旧いActive Run)の場合のみ、後方互換としてSelectedCharacterIdへ
        // フォールバックする。
        activeRunCharacterId = !string.IsNullOrEmpty(data.characterId) ? data.characterId : SelectedCharacterId;
        ApplyCharacterBaseStats(CharacterDatabase.FindById(activeRunCharacterId));

        // ステージ選択導線追加(2026-09-12) - 上と全く同じ理由。data.stageId
        // が空(旧いActive Run)の場合のみSelectedStageIdへフォールバック。
        activeRunStageId = !string.IsNullOrEmpty(data.stageId) ? data.stageId : SelectedStageId;
        // ステージ別ビジュアル差し替え(2026-09-13) - 必ずこの下のPlayer
        // ワープ(p.x = data.checkpointDistance)より前に呼ぶこと - ワープ
        // した瞬間にTerrainManager.Update()がx=0からcheckpointDistanceまで
        // 一気にチャンクを生成し直す(既存のDebug Warp機構と同じ)ため、
        // その生成が始まる前にテーマを確定させておく必要がある。
        if (TerrainManager.Instance != null) TerrainManager.Instance.ApplyStageTheme(activeRunStageId);

        // Item 8 - "Run中カード効果/各カードStack/Character Card由来の効
        // 果/Run中の現在能力" are reconstructed by REPLAYING the exact same
        // effect-application sequence a fresh run would have gone through
        // (Character Card baseline, then every Level Up/Boss Reward pick
        // in its original order) rather than separately serializing every
        // individual derived PlayerController stat.
        ApplyCharacterCardEffects();
        upgradeHistory.Clear();
        foreach (string cardId in data.upgradeHistoryCardIds)
        {
            CardDefinition card = CardDatabase.FindById(cardId);
            if (card == null) continue; // a card removed from the database since this save - skip rather than crash
            ApplyCardEffects(card);
            upgradeHistory.Add(card);
        }

        // Item 10/11 - HP restored to the EXACT value at interruption, not
        // full - deliberately AFTER the replay above, which otherwise
        // leaves Lives at the reconstructed maxLives (each MaxHp card's own
        // "heals to new cap" side effect).
        maxLives = data.maxLives > 0 ? data.maxLives : maxLives;
        Lives = Mathf.Clamp(data.lives, 1, maxLives);

        // Item 7 - resumes exactly where the Boss schedule was at.
        if (BossManager.Instance != null) BossManager.Instance.RestoreNextBossDistance(data.checkpointDistance);

        // Item 14 - short safe zone right after resuming (no new Enemy/
        // Formation/Wall spawns until past this - see IsInSafeZone).
        safeZoneEndDistance = data.checkpointDistance + safeZoneLength;
        // Item 14 - best-effort pit avoidance right at the checkpoint
        // (Formation/Wall spawning is reliably suppressed via IsInSafeZone
        // above; a Pit is decided by TerrainManager's own chunk-type roll,
        // independent of that, so this asks it to force flat ground for
        // the safe zone's length too). Shares the same large-distance-jump
        // mechanism as the existing Debug Warp feature (teleporting
        // PlayerController below), so a brief one-frame terrain-catch-up
        // is a known, pre-existing characteristic, not new to this pass.
        if (TerrainManager.Instance != null)
        {
            TerrainManager.Instance.RequestFlatRun(Mathf.CeilToInt(safeZoneLength / Mathf.Max(1f, TerrainManager.Instance.flatLength)) + 1);
        }

        if (PlayerController.Instance != null)
        {
            Vector3 p = PlayerController.Instance.transform.position;
            p.x = data.checkpointDistance;
            FreezeDiagnostics.NoteIntendedMove("CONTINUE (checkpoint)");
            PlayerController.Instance.transform.position = p;
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayGameplayBgm();
    }

    // Presentation pass - RESULT->TOP goes through the shared wipe's Close
    // half, THEN reloads the scene at full navy coverage (see
    // ScreenTransitionManager.PlayCloseThenReload's own comment for how the
    // Open half survives across that reload). Falls back to the old
    // instant reload if a scene was built before this manager existed.
    void RetryWithTransition()
    {
        if (ScreenTransitionManager.Instance != null)
        {
            ScreenTransitionManager.Instance.PlayCloseThenReload(Retry);
            return;
        }
        Retry();
    }

    void OnGUI()
    {
        // Bug #001 診断フェーズ (2026-09-08) - 意図的にAnyOverlayOpen等の
        // 分岐の外(=Level Up/Boss Reward/Pauseで隠れている最中でも見える
        // 位置)に置く。まさにFreeze中こそこのパネルを見たいため。
        // 診断表示(2026-09-27 改修) - Bug#001のBOSS診断パネル/Freeze・Warpログ/ボスSnapshotは、
        // 常時表示・自動で開く方式をやめ、DiagnosticsOverlay(Debug Mode中のRunだけ)の小さな
        // 「診断ログ」「BOSS診断」ボタンから開く。異常検知時は「ログ保存済み」を短く出すだけ。

        // Drawn first (before every other element) so everything else on
        // the top screen layers on top of it, and only while that screen
        // is showing - it must never bleed into gameplay. A single static
        // painted scene (per the "背景画像は、停止で" brief) scaled to
        // always fully cover the screen (like ScaleAndCrop) and centered -
        // it's a specific one-off composition (a particular castle, a
        // particular cliff edge), not a seamless tileable pattern, so
        // unlike the small foreground clouds below, this deliberately does
        // NOT scroll/drift.
        // Home Room UI reconstruction pass - the background draw itself now
        // just fills bgRoomRect (a field, not a local) so the title-screen
        // block further down can reuse the EXACT same on-screen rect to
        // place the room's tap targets (door/bed/book/desk) as fractions of
        // it - guarantees they track the image correctly regardless of
        // device aspect ratio, since bgRoomRect already accounts for the
        // "cover" crop/letterbox math. The old drifting-cloud layer is
        // gone - an enclosed room has no sky to drift clouds across (see
        // SceneBuilder's own comment on topCloud).
        // Home待機演出: Home非表示(ラン中/サブ画面)の間は毎回Resetして、途中の
        // 姿勢・コイン・粒子を残さない(戻った時に多重起動もしない)。
        GetIdleFx().SetVisible(!HasStarted && !AnyOverlayOpen && topBackground != null);

        if (!HasStarted && !AnyOverlayOpen && topBackground != null)
        {
            float coverScale = Mathf.Max((float)Screen.width / topBackground.width, (float)Screen.height / topBackground.height);
            float bgWidth = topBackground.width * coverScale;
            float bgHeight = topBackground.height * coverScale;
            bgRoomRect = new Rect((Screen.width - bgWidth) / 2f, (Screen.height - bgHeight) / 2f, bgWidth, bgHeight);
            GUI.DrawTexture(bgRoomRect, topBackground, ScaleMode.StretchToFill);
        }

        // Always-visible build stamp - offset right of center (not dead
        // center) so it doesn't sit on top of PlayerController's ascend
        // hold/countdown text, which is centered along this same bottom row.
        GUIStyle buildStyle = new GUIStyle(GUI.skin.label);
        buildStyle.fontSize = 12;
        buildStyle.alignment = TextAnchor.LowerLeft;
        buildStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        string buildText = "build " + Application.version;
        Rect buildRect = new Rect(Screen.width * 0.66f, Screen.height - 26f, 240f, 22f);
        UiBackdrop.Draw(buildRect, 0.35f);
        GUI.Label(buildRect, buildText, buildStyle);

        // Distance/level/EXP/BEST/hearts are the in-run HUD - meaningless
        // (and, per feedback, just visual clutter) on the title screen or
        // while the Deck Edit screen is open, so all of it is skipped in
        // both cases. BEST alone gets a small standalone display back on
        // the title screen (see the title block below) since it's still
        // relevant there.
        if (HasStarted && !AnyOverlayOpen)
        {
            // Left: BEST/DISTANCE, same panel shape, stacked. Center: Lv/EXP
            // grouped into one panel. Right: HP. All three share
            // HudPanelHeight/top offset - see the Get*PanelRect getters -
            // so this reads as one aligned strip instead of separately
            // placed boxes.
            DrawStatPanel(GetBestPanelRect(), "BEST", FormatDistanceExact(BestDisplayValue), HudGoldColor);
            DrawStatPanel(GetDistancePanelRect(), "DISTANCE", FormatDistanceExact(MaxDistanceExact), HudValueColor, flashIntensity: DistanceFlashIntensity);
            DrawSpeedHud();

            DrawLevelAndExp();
            DrawHeartsPanel();

            // 開発ビルドではDEBUG TOOLSの中に表示する(リリースビルドのDebug Modeでは従来どおりここに出す)。
            if (DebugMode && !Debug.isDebugBuild) DrawDebugSpeedReadout(SafeLeft() + UiMargin, GetSpeedPanelRect().yMax + HudPanelGap + 4f);
            if (DebugMode && Debug.isDebugBuild) DrawDistanceWarpDebugUI();

            DrawUnlockAnnouncement();
            DrawEscapeAvailableBanner();
            if (CountdownActive) DrawRunStartCountdown();

            // Item 9 - small Pause/Menu button, hidden while a Level Up/
            // Boss Reward card choice is already showing its own pause
            // overlay (avoids stacking two independent pause states).
            // Bugfix 2026-09-07 (Bug #001, contributing factor) - this used
            // to be gated ONLY on levelUpPending, not on
            // IsBossPresentationActive() - meaning during Boss Spawn/Defeat
            // Presentation (BEFORE levelUpPending ever flips true for the
            // Boss Reward choice), this button was still fully visible and
            // tappable, and since OnGUI runs regardless of Time.timeScale, a
            // tap here could set Time.timeScale directly while
            // BossMilestonePresentation's own TempoDown/PlayWarning coroutine
            // was independently animating that SAME value - the two writers
            // could race, and closing Pause mid-Presentation would forcibly
            // resume gameplay out from under whichever Presentation was still
            // expecting to hold it paused. Excluded now too, same as
            // levelUpPending.
            if (!IsGameOver && !levelUpPending && !IsBossPresentationActive())
            {
                if (DrawStyledButton(GetPauseButtonRect(), "II", 22f, primary: showPauseMenu))
                {
                    showPauseMenu = !showPauseMenu;
                    if (showPauseMenu) TimeControl.Pause(pauseMenuTimeOwner);
                    else TimeControl.Resume(pauseMenuTimeOwner);
                }
                if (showPauseMenu) DrawPauseMenu();
            }
        }
        DrawReturnHomeConfirm();

        // The level-up choice UI itself is now the RewardCardSequence
        // (Canvas-based card draw/flip/select presentation, kicked off from
        // TriggerLevelUpChoice) instead of an IMGUI overlay drawn here.
        //
        // Temporary diagnostic: shows exactly which step the sequence is on
        // (see RewardCardSequence.LogStep) so a freeze can be pinned down
        // without needing adb/logcat access - drawn via IMGUI, which is
        // unaffected by Time.timeScale, so it keeps updating even if the
        // sequence itself has actually stalled.
        if (DebugMode && !string.IsNullOrEmpty(lastLevelUpDiagnostic))
        {
            GUIStyle diagStyle = new GUIStyle(GUI.skin.label);
            diagStyle.fontSize = 16;
            diagStyle.alignment = TextAnchor.UpperCenter;
            diagStyle.normal.textColor = Color.cyan;
            Rect diagRect = new Rect(0f, Screen.height * 0.5f - 90f, Screen.width, 26f);
            DrawCenteredBackdrop(diagRect, lastLevelUpDiagnostic, diagStyle);
            GUI.Label(diagRect, lastLevelUpDiagnostic, diagStyle);
        }

        if (DebugMode && levelUpPending && !string.IsNullOrEmpty(RewardCardSequence.DebugStep))
        {
            GUIStyle stepStyle = new GUIStyle(GUI.skin.label);
            stepStyle.fontSize = 16;
            stepStyle.alignment = TextAnchor.UpperCenter;
            stepStyle.normal.textColor = Color.yellow;
            string stepText = "STEP: " + RewardCardSequence.DebugStep;
            Rect stepRect = new Rect(0f, Screen.height * 0.5f - 60f, Screen.width, 26f);
            DrawCenteredBackdrop(stepRect, stepText, stepStyle);
            GUI.Label(stepRect, stepText, stepStyle);
        }

        // ホーム(!HasStarted)では、ギア/設定/DEBUG列を「入力判定は従来どおり最初(ここ)」で行い、
        // 見た目の描画はRepaint時に部屋の演出・ホットスポットより手前(後ろの方)で行う。
        // (IMGUIは呼び出し順=描画順のため、ここで描くと演出画像に隠れていた。)
        if ((!HasStarted || IsGameOver) && !AnyOverlayOpen)
        {
            // A small gear icon (bottom-left, per the reference mockup)
            // toggles the whole settings/debug column below instead of it
            // always being on screen - reads as a much simpler title
            // screen at a glance while keeping every toggle (including
            // DEBUG's own, which must stay reachable somehow or it could
            // never be turned back on) exactly as available as before,
            // just one tap further away.
            if (HasStarted || Event.current.type != EventType.Repaint) DrawSettingsChrome();
        }

        if (!HasStarted && !AnyOverlayOpen)
        {
            // One-shot intro fade, staggered: logo first, then the room's
            // tap targets shortly after - same two-window timing the old
            // START/DECK buttons used, just applied to the room hotspots
            // instead. Both windows are short (0.5s) and don't block input.
            float logoFadeAlpha = Mathf.Clamp01(titleIntroTimer / 0.5f);
            float roomFadeAlpha = Mathf.Clamp01((titleIntroTimer - 0.25f) / 0.5f);

            // Home待機演出(2026-09-21) - 背景側の演出(静止カーテン/扉/カード/本の光/
            // 光の粒子)はロゴより奥・各ホットスポットの絵より奥に描く。粒子はロゴ
            // と肖像画の領域では薄くする(視認性優先)。
            {
                var quiet = new System.Collections.Generic.List<Rect>();
                if (titleLogo != null && bgRoomRect.width > 0f)
                {
                    float lcx = bgRoomRect.x + bgRoomRect.width * DoorCenterFrac;
                    float lw = Mathf.Min(Screen.width * 0.46f, titleLogo.width);
                    float lh = lw * (titleLogo.height / (float)titleLogo.width);
                    quiet.Add(new Rect(lcx - lw / 2f, Screen.height * 0.015f, lw, lh));
                }
                if (bgRoomRect.width > 0f) quiet.Add(FracRect(bgRoomRect, 0.02f, 0.14f, 0.17f, 0.38f));
                DrawHomeAmbientAnimations(roomFadeAlpha, quiet.ToArray());
            }

            if (titleLogo != null)
            {
                // Home Room UI reconstruction pass - new "ONE MORE MILE /
                // To the Next Me" logo, much wider/shorter aspect than the
                // old one; same width-fraction approach, height follows
                // automatically from the new texture's own aspect ratio.
                // Ver.1 finishing pass, item 8 - "少し縮小・上寄せ" (was
                // hiding too much of the door/room below it): 0.6 -> 0.46
                // width fraction, top margin 0.03 -> 0.015 of screen height.
                // Home画面改善依頼⑦(2026-09-16), item 8 - ロゴ・扉・NEXT
                // STAGEの中心をできるだけ同じ縦軸に揃える。基準は扉の中心
                // (DoorCenterFrac、doorRectのx0/x1の中点をそのまま定数化した
                // もの)。以前はScreen.width/2(=bgRoomRectの中心、フラクション
                // 0.5)を使っていたが、扉自体が背景アート上でフラクション
                // 0.4825の位置に描かれているため、画面の見た目の中心からは
                // 常にわずかに左へズレていた。
                float logoCenterX = bgRoomRect.width > 0f ? bgRoomRect.x + bgRoomRect.width * DoorCenterFrac : Screen.width / 2f;
                float logoWidth = Mathf.Min(Screen.width * 0.46f, titleLogo.width);
                float logoHeight = logoWidth * (titleLogo.height / (float)titleLogo.width);
                Rect logoRect = new Rect(logoCenterX - logoWidth / 2f, Screen.height * 0.015f, logoWidth, logoHeight);
                Color prevLogo = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, logoFadeAlpha);
                GUI.DrawTexture(logoRect, titleLogo, ScaleMode.ScaleToFit);
                GUI.color = prevLogo;
            }
            else
            {
                GUIStyle titleTextStyle = new GUIStyle(GUI.skin.label);
                titleTextStyle.fontSize = 48;
                titleTextStyle.fontStyle = FontStyle.Bold;
                titleTextStyle.alignment = TextAnchor.MiddleCenter;
                titleTextStyle.normal.textColor = new Color(1f, 1f, 1f, logoFadeAlpha);
                Rect titleRect = new Rect(0f, Screen.height * 0.03f, Screen.width, 70f);
                DrawCenteredBackdrop(titleRect, GameTitle, titleTextStyle);
                GUI.Label(titleRect, GameTitle, titleTextStyle);
            }

            // Home Room UI reconstruction pass - "部屋に存在する物を触る"
            // instead of a menu of buttons. Every hotspot below is a plain
            // invisible GUI.Button (no visible skin - DrawRoomHotspot draws
            // only a brief tap-flash, never a permanent box) positioned as
            // a fraction of bgRoomRect (the room background's actual
            // on-screen rect, set earlier this same OnGUI call), so they
            // stay aligned with the painted room regardless of aspect
            // ratio. Exact fractions are a first pass against the supplied
            // reference art - nudge them here if they drift from the
            // visible objects once seen on a real device.
            // Home画面改善依頼⑪(2026-09-17), item3-5 - 装備表示を撤去した
            // ぶんの「寂しさ」を、控えめな環境アニメーションで補う。

            if (bgRoomRect.width > 0f)
            {
                Color prevRoom = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, roomFadeAlpha);
                // Every hotspot below skips its own hit-test entirely while
                // the Gacha Result popup is up, so a tap can't be consumed
                // out from under that popup's own OK button (see
                // DrawRoomHotspot's own comment).
                bool roomInteractable = !gachaResultOpen && !showNewRunConfirm && !NetDebugUI.BlocksHomeInput;

                // Door (center) - Run Continuation/Checkpoint Ver.1, item
                // 13 - CONTINUE (if an Active Run exists) or a fresh Run,
                // same as the old START button.
                Rect doorRect = FracRect(bgRoomRect, 0.40f, 0.14f, 0.565f, 0.65f);
                // Home画面改善依頼②(2026-09-15), item 4 - 扉は背景の絵に
                // 完全に溶け込んでおり、タップ可能だと伝わる手がかりが
                // 従来皆無だった(タップ時のフラッシュのみ)。中央の扉は
                // 画面の主役でもあるため、他より少し目立つ枠線にした。
                // Home画面改善依頼⑤(2026-09-16), item 4 - 常時の矩形の縁が
                // 「判定枠」に見え貼り付け感の一因になっていたため撤去し、
                // 扉の形に沿って重ねる柔らかい明滅(DrawAmbientGlow)へ置き
                // 換えた。扉は引き続き画面の主役(item 7)なので、他の
                // hotspotより上限の明るさをわずかに高くしてある。
                // Home環境アニメーション強化依頼(2026-09-17), item5 -
                // 「扉下部/隙間にごく薄い暖色光のゆらぎ」がまだ弱く見えた
                // ため上限を0.11→0.16へ引き上げた(周期はそのまま)。
                // Home画面改善依頼⑦(2026-09-16), item 9 - 扉だけは共通の
                // DrawRoomHotspot(全面が白くフラッシュするだけの汎用反応)
                // ではなく専用のDrawDoorHotspotを使い、「取っ手が少し明るく
                // なる」「縁が一瞬金色に光る」「軽いScale/Glow反応」を扉
                // 専用の見た目で表現する(扉が画面の視覚的な主役であるため)。
                if (DrawDoorHotspot(doorRect, ref doorHotspotFlashTimer, roomInteractable, roomFadeAlpha) && roomFadeAlpha > 0.99f)
                {
                    OnDoorTapped();
                }

                if (RunCheckpoint.HasActiveRun && roomFadeAlpha > 0.5f)
                {
                    RunCheckpoint.Data cp = RunCheckpoint.Load();
                    string continueLabel = $"CONTINUE\n{Mathf.FloorToInt(cp.checkpointDistance)}m CHECKPOINT";
                    GUIStyle continueStyle = new GUIStyle(GUI.skin.label);
                    continueStyle.fontSize = 16;
                    continueStyle.fontStyle = FontStyle.Bold;
                    continueStyle.alignment = TextAnchor.MiddleCenter;
                    continueStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
                    Rect continueLabelRect = new Rect(doorRect.x, doorRect.y - 54f, doorRect.width, 46f);
                    DrawCenteredBackdrop(continueLabelRect, continueLabel, continueStyle);
                    GUI.Label(continueLabelRect, continueLabel, continueStyle);

                    // Item 13 - "Active Runが存在する状態でNEW RUNを開始で
                    // きるようにする場合は確認を必ず入れてください".
                    Rect newRunRect = new Rect(doorRect.x, doorRect.yMax + 6f, doorRect.width, 30f);
                    if (roomInteractable && DrawStyledButton(newRunRect, "NEW RUN", 13f, primary: false))
                    {
                        showNewRunConfirm = true;
                    }
                }

                // キャラクター選択画面(2026-09-12) - 参考画像の「マント+
                // 剣が置かれている装備スペース」に相当する導線。既存の
                // 室内アートにはこれに対応する物が描かれていないため(他の
                // 4つと違い完全に透明なDrawRoomHotspotだけでは押せることが
                // 伝わらない)、マスター指示どおりここだけ簡単なパネル+
                // アイコン+ラベル+淡い発光を明示的に描画する。Bedの真上、
                // Doorとは重ならない領域。
                // Home画面レイアウト調整(2026-09-14) - マスター報告
                // 「パネルが大きい割にカードの右側に大きな空白がある」への
                // 対応。根本原因はパネルの縦横比(幅0.28:高さ0.30≒横長)が
                // 縦長のポートレート画像と噛み合っておらず、DrawCharacter
                // Hotspot内でiconHがavailableHに合わせて縮められた結果
                // iconWがavailableW未満になり、余白が生まれていたこと。
                // 幅を約半分(0.30→0.16)に絞ってポートレート自身の縦横比へ
                // 近づけ、「大きな空箱にカード1枚」に見えないようにした。
                // また、y0を0.05→0.10へ下げ、左上のBEST表示(SafeTop()+
                // UiMargin基準の固定72px矩形)と実際に重なっていた既存の
                // 不具合(「CHARACTER」ラベルの頭が隠れていた)も合わせて
                // 解消した。
                // Home画面改善依頼②(2026-09-15), item 2 - 「BEST表示と
                // CHARACTERパネルの間に少し余白を」に対応し、y0を0.10→
                // 0.14へさらに下げてBESTパネルとの間隔を広げた。パネルを
                // 必要以上に大きくしないよう高さは0.27→0.24へわずかに
                // 縮めた(内部の余白はDrawCharacterHotspot側で確保)。
                Rect characterRect = FracRect(bgRoomRect, 0.02f, 0.14f, 0.17f, 0.38f);
                // ブラッシュアップ点検(2026-09-18)で発覚した不具合の修正:
                // 背景画像(1536x1024、縦横比1.5)よりも横長な画面(最近の
                // スマホの横画面によくある20:9等)では、cover-scaleのcropで
                // bgRoomRect.yが負値になり、上記フラクション計算の結果
                // characterRectが左上の固定BEST表示パネル(titleBestRect、
                // 高さ72px)と重なってしまっていた。フラクション自体は
                // そのままに、最終的なyだけBESTパネルの下端を下回らないよう
                // 安全側にクランプする(通常のアスペクト比では発火しない)。
                float minCharacterTop = SafeTop() + UiMargin + 72f + 16f;
                if (characterRect.y < minCharacterTop) characterRect.y = minCharacterTop;
                DrawCharacterHotspot(characterRect, roomInteractable, roomFadeAlpha);

                // Home画面改善依頼⑪(2026-09-17), item1 - キャラ連動の装備/
                // 持ち物表示(旧DrawCharacterBelongings)はHomeから撤去した。
                // 工数に対して見た目の改善が薄いという判断による方針転換
                // (Character Select自体やCharacterDefinition.belongingsの
                // データ自体は無改造 - 将来また使う可能性を潰さない)。

                // Home画面 / Stage Select改善依頼(2026-09-16), item4/5 -
                // マスター指示「Homeにはマップを常設しない」に対応し、
                // NEXT STAGEホットスポット(旧DrawStageHotspot)自体を撤去
                // した。行き先の選択は出発時(扉タップ→Stage Select)専用の
                // 画面へ完全に分離している - OnDoorTapped参照。

                // Bed/scattered cards (bottom-left) - Card Edit (Owned/
                // Character Cards/Deck/Convert).
                Rect bedRect = FracRect(bgRoomRect, 0.0f, 0.52f, 0.32f, 1.0f);
                if (DrawRoomHotspot(bedRect, ref bedHotspotFlashTimer, roomInteractable) && roomFadeAlpha > 0.99f)
                {
                    OpenDeckEdit();
                }

                // Book stack (bottom-right corner) - Card Fusion.
                Rect bookRect = FracRect(bgRoomRect, 0.78f, 0.78f, 1.0f, 1.0f);
                if (DrawRoomHotspot(bookRect, ref bookHotspotFlashTimer, roomInteractable) && roomFadeAlpha > 0.99f)
                {
                    OpenCardFusion();
                }

                // Desk CARD GACHA machine - drawn as a prop directly onto
                // the scene (not present in the room art itself), tap
                // executes the Gacha inline (item 4) - no dedicated screen.
                if (gachaMachineTexture != null)
                {
                    // Home画面改善依頼②(2026-09-15) - マスター報告「椅子の
                    // 前で宙に浮いている」への対応。前回(0.83/0.38)はまだ
                    // 机の奥行きより低く、椅子の背もたれ付近まで機体の下端
                    // が届いていたと判断し、Y位置を0.38→0.26(机の天板の
                    // 高さ)へさらに引き上げ、幅も0.12→0.10へ縮小して周囲の
                    // 家具(本・カップ・コンパス)との遠近感を合わせた。
                    float machineWidth = bgRoomRect.width * 0.10f;
                    float machineAspect = gachaMachineTexture.height / (float)gachaMachineTexture.width;
                    float machineHeight = machineWidth * machineAspect;
                    float shakeOffset = gachaMachineShakeTimer > 0f
                        ? Mathf.Sin(gachaMachineShakeTimer * 55f) * 4f * (gachaMachineShakeTimer / gachaMachineShakeDuration)
                        : 0f;
                    Rect machineRect = new Rect(
                        bgRoomRect.x + bgRoomRect.width * 0.83f - machineWidth / 2f + shakeOffset,
                        bgRoomRect.y + bgRoomRect.height * 0.26f,
                        machineWidth, machineHeight);

                    // Home画面改善依頼⑤(2026-09-16), item 3 - 「独立した
                    // スタンプのように見えないように」。単一の均一な影
                    // 矩形は縁がくっきりして見え、それ自体が「貼った影
                    // 画像」に見えてしまっていた。外側ほど薄い2枚重ねに
                    // し、縁が滲んだ柔らかい接地影に近づけた(新規テクス
                    // チャなしで済む、このファイル内の他の影と同じ単色
                    // 矩形近似の延長)。
                    Color prevShadow = GUI.color;
                    float shadowWidthOuter = machineWidth * 0.95f;
                    float shadowHeightOuter = machineHeight * 0.16f;
                    Rect shadowRectOuter = new Rect(machineRect.x + (machineWidth - shadowWidthOuter) / 2f, machineRect.yMax - shadowHeightOuter * 0.55f, shadowWidthOuter, shadowHeightOuter);
                    GUI.color = new Color(0f, 0f, 0f, 0.22f * roomFadeAlpha);
                    GUI.DrawTexture(shadowRectOuter, Texture2D.whiteTexture);
                    float shadowWidthInner = machineWidth * 0.68f;
                    float shadowHeightInner = machineHeight * 0.10f;
                    Rect shadowRectInner = new Rect(machineRect.x + (machineWidth - shadowWidthInner) / 2f, machineRect.yMax - shadowHeightInner * 0.55f, shadowWidthInner, shadowHeightInner);
                    GUI.color = new Color(0f, 0f, 0f, 0.32f * roomFadeAlpha);
                    GUI.DrawTexture(shadowRectInner, Texture2D.whiteTexture);
                    GUI.color = prevShadow;

                    Color prevMachine = GUI.color;
                    float glow = Mathf.Max(deskHotspotFlashTimer / roomHotspotFlashDuration, gachaMachineShakeTimer > 0f ? 0.5f : 0f);
                    // Item 13 - Gacha Visual Evolution: since no dedicated
                    // per-stage art exists yet, a simple tint stands in
                    // (real art can just replace GachaMachineStageTint's
                    // per-case color with Color.white once it exists, no
                    // other code changes needed).
                    Color stageTint = GachaMachineStageTint(CurrentGachaStage);
                    // Home画面改善依頼②(2026-09-15) - マスター報告「背景
                    // 光源に対して明るさ・コントラストが浮きすぎる」への
                    // 対応。室内の暖色ランプ光に馴染むよう、タップ時の発光
                    // (glow)が無い通常時はわずかに(12%)減光する - 各ステージ
                    // 色の相対的な違いはそのまま保ちつつ、部屋の照度に近づけた。
                    Color dimmedTint = stageTint * 0.88f;
                    Color baseColor = new Color(dimmedTint.r, dimmedTint.g, dimmedTint.b, roomFadeAlpha);
                    GUI.color = Color.Lerp(baseColor, new Color(1f, 0.92f, 0.6f, roomFadeAlpha), Mathf.Clamp01(glow));
                    // Home待機演出(2026-09-21) - 機械本体だけが接地点(下端中央)を軸にぐらぐら
                    // 揺れて収まる。影と、下のGUI.Button(machineRect)のタップ判定は回転しない。
                    HomeIdleFx idleGacha = GetIdleFx();
                    Matrix4x4 gachaPrevMatrix = GUI.matrix;
                    float gachaAngle = idleGacha.GachaAngle();
                    if (Mathf.Abs(gachaAngle) > 0.0001f) GUIUtility.RotateAroundPivot(gachaAngle, new Vector2(machineRect.center.x, machineRect.yMax));
                    GUI.DrawTexture(machineRect, gachaMachineTexture, ScaleMode.ScaleToFit);
                    GUI.matrix = gachaPrevMatrix;
                    GUI.color = prevMachine;
                    // 揺れに合わせたコイン(装飾のみ。所持金・報酬処理とは無関係)。
                    idleGacha.UpdateCoins(machineRect);
                    idleGacha.DrawCoins(roomFadeAlpha);

                    // Home画面改善依頼⑤(2026-09-16), item 3/4 - 常時の金の
                    // 縁取りを撤去。Gacha機はタップ/振動時に`glow`で暖色
                    // ハイライトへ寄る反応が既にあり、それ自体が「ホバー/
                    // タップ時の軽い発光で示す」という要望を満たしている
                    // ため、常時枠を重ねて貼り付け感を足す必要がなかった。

                    if (roomInteractable && GUI.Button(machineRect, GUIContent.none, GUIStyle.none) && roomFadeAlpha > 0.99f)
                    {
                        OnGachaMachineTapped();
                    }

                    // Bugfix 2026-09-05, item 5 - the always-on "CARD GACHA
                    // Lv.X / NEXT EVOLUTION Ym" label sitting directly on the
                    // Home Room background read as stray debug text over the
                    // painted scene. Removed from here entirely; the same
                    // info now shows inside DrawGachaResultPopup instead (a
                    // small UI surface that only appears after actually
                    // tapping the machine), per the brief's own "Tapした後の
                    // 画面内、または小さな専用UIで表示してください".
                }

                GUI.color = prevRoom;
            }

            // BEST (top-left) and MILE (top-right) - the room's only
            // persistent chrome besides the gear icon, both tucked into
            // corners so they never sit over the door/bed/book/desk.
            Rect titleBestRect = new Rect(SafeLeft() + UiMargin, SafeTop() + UiMargin, DistancePanelWidth(true), 72f);
            DrawStatPanel(titleBestRect, "BEST", FormatDistanceExact(BestDisplayValue), HudGoldColor, ornate: true);

            Rect titleMileRect = new Rect(Screen.width - SafeRight() - UiMargin - 190f, SafeTop() + UiMargin, 190f, 72f);
            DrawStatPanel(titleMileRect, "MILE", TotalOwnedMile.ToString(), HudGoldColor, ornate: true);

            // ギア/設定列/DEBUGなどのボタンは、背景・分離画像・演出・粒子より手前に描く。
            if (Event.current.type == EventType.Repaint) DrawSettingsChrome();

            // Also tucked behind the gear icon (see DrawSettingsColumn) -
            // only actually drawn/reachable while that panel is open, not
            // part of the always-on title screen.
            if (showSettingsPanel && DrawStyledButton(GetResetHighScoreButtonRect(), "RESET SCORE", 15f, primary: false))
            {
                ResetHighScores();
            }

            if (DebugMode && Debug.isDebugBuild) DrawGachaDebugUI(titleBestRect);

            DrawGachaResultPopup();
            DrawInsufficientMileToast();
            DrawNewRunConfirm();

            DrawStartTransitionOverlay();
            return;
        }

        if (!IsGameOver)
        {
            // Boss Milestone Presentation pass - screen darken + WARNING
            // line/text, drawn before the transition overlay slot (the two
            // can't actually co-occur - a boss milestone only ever fires
            // while HasStarted && !IsGameOver, and ScreenTransitionManager
            // only ever plays outside that - but this keeps the layering
            // sane if that ever changes).
            if (BossMilestonePresentation.Instance != null) BossMilestonePresentation.Instance.DrawOverlay();
            // Boss Defeat Presentation pass - drawn in the same slot, right
            // after the Milestone overlay (drawn after, i.e. on top - the
            // two never actually overlap in practice, see
            // IsBossPresentationActive's comment). Being inside this same
            // "!IsGameOver" branch is what makes item 12's "Player死亡と同
            // 時に開始されない" hold for the reverse timing too - the instant
            // IsGameOver flips true, GameManager's OnGUI stops taking this
            // branch at all, so this stops rendering immediately even if
            // its coroutine is still finishing up in the background.
            if (BossDefeatPresentation.Instance != null) BossDefeatPresentation.Instance.DrawOverlay();
            DrawStartTransitionOverlay();
            return;
        }

        DrawResults();
        DrawStartTransitionOverlay();
    }

    // Drawn last (on top of everything else in OnGUI, including every
    // early-return branch above - title/mid-run/results) so it can mask
    // every screen change. Presentation pass - delegates to
    // ScreenTransitionManager's Gold Slash wipe when one exists in the
    // scene; the plain navy fade below (startTransitionOverlayAlpha, driven
    // by the old StartGameTransition coroutine) is kept only as a fallback
    // for a scene built before that manager existed.
    void DrawStartTransitionOverlay()
    {
        if (ScreenTransitionManager.Instance != null)
        {
            ScreenTransitionManager.Instance.DrawOverlay();
            return;
        }
        if (startTransitionOverlayAlpha <= 0.001f) return;
        Color prev = GUI.color;
        GUI.color = new Color(0.04f, 0.05f, 0.1f, startTransitionOverlayAlpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // Full results panel shown once the run ends - distance/time (each
    // starred if it beat the persisted best), kill counts, total EXP
    // earned, and how many level-up choices were taken this run.
    void DrawResults()
    {
        GUIStyle headlineStyle = new GUIStyle(GUI.skin.label);
        headlineStyle.fontSize = 34;
        headlineStyle.fontStyle = FontStyle.Bold;
        headlineStyle.alignment = TextAnchor.MiddleCenter;
        headlineStyle.normal.textColor = Color.white;
        string headline = IsWin ? "GAME CLEAR" : "FAILED";

        float panelWidth = Mathf.Min(560f, Screen.width * 0.8f);
        float panelHeight = Mathf.Min(504f, Screen.height * 0.85f);
        Rect panelRect = new Rect(Screen.width / 2f - panelWidth / 2f, Screen.height / 2f - panelHeight / 2f, panelWidth, panelHeight);
        UiBackdrop.Draw(panelRect, 0.8f);

        float y = panelRect.y + 16f;
        GUI.Label(new Rect(panelRect.x, y, panelRect.width, 46f), headline, headlineStyle);
        y += 52f;

        GUIStyle rowStyle = new GUIStyle(GUI.skin.label);
        rowStyle.fontSize = 20;
        rowStyle.alignment = TextAnchor.MiddleLeft;
        rowStyle.normal.textColor = Color.white;

        void Row(string label, string value, bool star)
        {
            string text = star ? $"{label}: {value}  ★" : $"{label}: {value}";
            Color prevColor = rowStyle.normal.textColor;
            rowStyle.normal.textColor = star ? new Color(1f, 0.85f, 0.3f) : Color.white;
            GUI.Label(new Rect(panelRect.x + 30f, y, panelRect.width - 60f, 30f), text, rowStyle);
            rowStyle.normal.textColor = prevColor;
            y += 34f;
        }

        // Reward/MILE System Ver.1, item 3 - "DISTANCE 23,400m +234 MILE"
        // style: the MILE each category earned is folded directly into its
        // existing row instead of adding 3 more rows, matching the brief's
        // own example layout.
        Row("DISTANCE", $"{Mathf.FloorToInt(MaxDistance)}m  +{RunDistanceMile} MILE", IsNewBestDistance);
        Row("TIME", FormatTime(RunTime), IsNewBestTime);
        Row("ENEMIES DEFEATED", $"{EnemyKillCount}  +{RunEnemyMile} MILE", false);
        Row("BOSSES DEFEATED", $"{BossKillCount}  +{RunBossMile} MILE", false);
        Row("TOTAL EXP", Mathf.FloorToInt(TotalExpEarned).ToString(), false);
        Row("UPGRADES OBTAINED", UpgradeCount.ToString(), false);
        Row("TOTAL MILE", $"+{RunMile}  (WALLET {TotalOwnedMile})", false);

        if (upgradeHistory.Count > 0)
        {
            y += 6f;
            DrawUpgradeHistoryRow(new Rect(panelRect.x + 20f, y, panelRect.width - 40f, 48f));
            y += 56f;
        }

        bool retryAllowed = Time.time - gameOverTime >= retryDelayAfterGameOver;
        if (retryAllowed)
        {
            GUIStyle retryStyle = new GUIStyle(GUI.skin.label);
            retryStyle.fontSize = 20;
            retryStyle.alignment = TextAnchor.MiddleCenter;
            retryStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(panelRect.x, panelRect.yMax - 40f, panelRect.width, 30f), "Tap to Retry", retryStyle);
        }
    }

    // Drifts a single cloud shape from fully off-screen left to fully
    // off-screen right at `speed` pixels/second, then loops - phaseOffset
    // staggers multiple layers so they don't all start at the same spot.
    void DrawScrollingCloud(Texture2D tex, float yFrac, float widthFrac, float speed, float phaseOffset, float alpha)
    {
        float cloudWidth = Screen.width * widthFrac;
        float cloudHeight = cloudWidth * (tex.height / (float)tex.width);
        float y = Screen.height * yFrac;
        float wrapWidth = Screen.width + cloudWidth;
        float x = ((Time.time * speed + phaseOffset) % wrapWidth) - cloudWidth;

        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(x, y, cloudWidth, cloudHeight), tex, ScaleMode.ScaleToFit);
        GUI.color = prev;
    }

    // Renders a volume level (0..AudioManager.MaxVolumeLevel) as a simple
    // filled/empty block bar, e.g. level 2 of 4 -> "[##--]".
    static string VolumeBar(int level)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < AudioManager.MaxVolumeLevel; i++) sb.Append(i < level ? '#' : '-');
        sb.Append(']');
        return sb.ToString();
    }

    static string FormatTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    // "23,034m" - the trailing unit rendered a size smaller via rich text
    // (the caller's GUIStyle must have richText=true - see DrawStatPanel).
    static string FormatDistance(float meters)
    {
        int m = Mathf.FloorToInt(meters);
        return $"{m:N0}<size={HudValueFontSize - 6}>m</size>";
    }

    // cm単位(小数2桁)のHUD表記: "1,234.56 m"。桁区切りはコンマ、小数点はドット(CultureInfo固定)。
    // 四捨五入ではなく切り捨て(まだ届いていないcmを先取りしない)。単位は少し小さく。
    static string FormatDistanceExact(double meters)
    {
        double v = System.Math.Floor(System.Math.Max(0.0, meters) * 100.0 + 1e-6) / 100.0;
        return v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + $"<size={HudValueFontSize - 6}> m</size>";
    }

    // HUD左上パネルの幅: 最長想定("9,999,999.99 m")を実測した固定幅。数値が変わっても枠/文字位置が揺れない。
    float distancePanelWidthCache;
    float distancePanelWidthScreen = -1f;
    float DistancePanelWidth(bool ornate)
    {
        if (distancePanelWidthScreen != Screen.width)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = HudValueFontSize, fontStyle = FontStyle.Bold, richText = true };
            float w = st.CalcSize(new GUIContent(FormatDistanceExact(9999999.99))).x;
            // 中央のLv/EXPパネル(画面中央-190から)に重ならない上限。狭い画面(縦画面の小さい解像度)では文字側を縮小して収める(DrawStatPanel)。
            float roomForLeftPanels = Screen.width * 0.5f - 190f - 14f - UiMargin;
            distancePanelWidthCache = Mathf.Max(168f, Mathf.Min(w + 26f, roomForLeftPanels));
            distancePanelWidthScreen = Screen.width;
        }
        return distancePanelWidthCache + (ornate ? 30f : 0f);
    }

    // 速度表示(km/h): ゲーム内メートル(=距離表示と同じ単位)/秒 × 3.6。走行速度(基本のAuto Run速度、カード効果・速度上昇込み)を参照し、
    // カメラ/背景のスクロール速度や攻撃の踏み込み・ノックバック・ジャンプ/落下・復帰時の位置補正は含めない。
    public const float KmhPerMps = 3.6f;
    public static float SpeedKmh(float metersPerSecond) => metersPerSecond * KmhPerMps;

    // Shared "small info panel" for BEST/DISTANCE: a small dim label on
    // top, a bigger bold value below, both left-aligned inside one navy+
    // gold panel - the label/value split every HUD panel here uses.
    // flashIntensity (0-1, Boss Milestone Presentation pass) - "Distance表
    // 示を一度だけ強調" (Scale 1.0->1.25->1.0, Color White->Gold->White).
    // Only the DISTANCE panel ever passes non-zero; every other caller's
    // default 0f leaves this identical to before.
    void DrawStatPanel(Rect rect, string label, string valueText, Color valueColor, bool ornate = false, float flashIntensity = 0f)
    {
        if (ornate) OrnateUi.DrawPanel(rect, 0.85f);
        else UiBackdrop.Draw(rect, 0.85f);

        // The ornate frame's painted border (OrnateUi.FrameBorderPx, 26px)
        // is thicker than UiBackdrop's plain thin edge, so its inset needs
        // to clear more space or the label/value text sits under the
        // frame's corner art instead of the panel's dark interior.
        float pad = ornate ? 20f : 12f;
        float topPad = ornate ? 14f : 4f;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = HudLabelFontSize;
        labelStyle.alignment = TextAnchor.UpperLeft;
        labelStyle.normal.textColor = HudLabelColor;
        GUI.Label(new Rect(rect.x + pad, rect.y + topPad, rect.width - pad * 2f, 18f), label, labelStyle);

        GUIStyle valueStyle = new GUIStyle(GUI.skin.label);
        valueStyle.fontSize = flashIntensity > 0f ? Mathf.RoundToInt(HudValueFontSize * Mathf.Lerp(1f, 1.25f, flashIntensity)) : HudValueFontSize;
        valueStyle.fontStyle = FontStyle.Bold;
        valueStyle.alignment = TextAnchor.UpperLeft;
        valueStyle.richText = true;
        // 枠に収まらないほど長い値/狭い画面では、文字サイズを縮めて欠けを防ぐ(桁が増えても枠は動かさない)。
        {
            float availW = rect.width - pad * 2f;
            Vector2 vs = valueStyle.CalcSize(new GUIContent(valueText));
            if (vs.x > availW && vs.x > 1f) valueStyle.fontSize = Mathf.Max(11, Mathf.FloorToInt(valueStyle.fontSize * availW / vs.x));
        }
        valueStyle.normal.textColor = flashIntensity > 0f ? Color.Lerp(valueColor, new Color(1f, 0.85f, 0.4f), flashIntensity) : valueColor;
        GUI.Label(new Rect(rect.x + pad, rect.y + topPad + 16f, rect.width - pad * 2f, rect.height - topPad - 18f), valueText, valueStyle);
    }

    void DrawLevelAndExp()
    {
        Rect panelRect = GetLevelExpPanelRect();
        UiBackdrop.Draw(panelRect, 0.85f);

        GUIStyle lvStyle = new GUIStyle(GUI.skin.label);
        lvStyle.fontSize = HudValueFontSize;
        lvStyle.fontStyle = FontStyle.Bold;
        lvStyle.alignment = TextAnchor.MiddleLeft;
        lvStyle.normal.textColor = HudGoldColor;
        string lvText = $"Lv.{Level}";
        Rect lvRect = new Rect(panelRect.x + 14f, panelRect.y, 66f, panelRect.height);
        GUI.Label(lvRect, lvText, lvStyle);

        // Dark fill + thin gold frame (same UiBackdrop treatment as every
        // other panel) with a blue-cyan progress fill inset inside it.
        Rect barRect = new Rect(lvRect.xMax + 4f, panelRect.y + panelRect.height / 2f - 11f, panelRect.xMax - 14f - (lvRect.xMax + 4f), 22f);
        UiBackdrop.Draw(barRect, 0.7f);
        float frac = ExpToNext > 0f ? Mathf.Clamp01(Exp / ExpToNext) : 0f;
        if (frac > 0f)
        {
            // Level Up Presentation pass - "EXP Barを Blue/Cyan -> Gold寄
            // りに一瞬発光" (see FlashExpBar/expBarFlashTimer). Blends the
            // fill color from its normal blue-cyan toward gold and briefly
            // brightens, easing back to normal as the timer runs out - a
            // color-only cue, no shape/size change, so it can't be
            // mistaken for the bar's actual progress.
            float flash = expBarFlashDuration > 0f ? Mathf.Clamp01(expBarFlashTimer / expBarFlashDuration) : 0f;
            Color baseColor = new Color(0.35f, 0.75f, 1f);
            Color flashColor = new Color(1f, 0.85f, 0.4f);
            Color prev = GUI.color;
            GUI.color = Color.Lerp(baseColor, flashColor, flash);
            Rect fillRect = new Rect(barRect.x + 3f, barRect.y + 3f, (barRect.width - 6f) * frac, barRect.height - 6f);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    // Polish - a brief flash-toward-white + slight shrink pulse on the
    // hearts row when a hit actually lands (see TryDamagePlayer), so
    // taking damage reads as more than just a heart silently changing
    // state. Purely cosmetic - HP/Lives math is entirely untouched.
    public float heartDamageFlashDuration = 0.3f;
    float heartDamageFlashTimer;

    // Level Up Presentation pass - "EXP Barを Blue/Cyan -> Gold寄りに一瞬発
    // 光". A public one-way trigger (not a subscription/event) so
    // RewardCardSequence can kick this off without reaching into any HUD
    // internals - same "GameManager exposes a dumb trigger, Presentation
    // calls it" pattern as everything else this HUD reads (BestDistance,
    // Lives, etc.), just in the other direction for once.
    public float expBarFlashDuration = 0.35f;
    float expBarFlashTimer;
    public void FlashExpBar() => expBarFlashTimer = expBarFlashDuration;

    // Boss Milestone Presentation pass - "Distance表示を一度だけ強調"
    // (Scale 1.0->1.25->1.0, Color White->Gold->White). Driven directly
    // (not a decaying timer like expBarFlashTimer above) since
    // BossMilestonePresentation authors the whole pop shape itself frame by
    // frame; DrawStatPanel just renders whatever value it's given.
    public float DistanceFlashIntensity;

    // Boss Milestone Presentation pass - "Player安全処理": unconditional
    // damage immunity (unlike InvincibleMode below, which falls
    // deliberately bypass via bypassInvincibleMode) while a boss milestone
    // presentation is playing, so an enemy/hole/projectile still active
    // during that brief window can't land an "unintended" hit. Always
    // restored by BossMilestonePresentation's own try/finally, so it can
    // never get stuck true.
    public bool PresentationDamageLock { get; private set; }
    public void SetPresentationDamageLock(bool locked) => PresentationDamageLock = locked;

    void DrawHeartsPanel()
    {
        Rect rect = GetHeartsPanelRect();
        UiBackdrop.Draw(rect, 0.85f);

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = HudLabelFontSize;
        labelStyle.alignment = TextAnchor.UpperLeft;
        labelStyle.normal.textColor = HudLabelColor;
        GUI.Label(new Rect(rect.x + 12f, rect.y + 4f, rect.width - 16f, 18f), "HP", labelStyle);

        float flash = heartDamageFlashDuration > 0f ? Mathf.Clamp01(heartDamageFlashTimer / heartDamageFlashDuration) : 0f;
        float pulse = 1f - flash * 0.08f;

        GUIStyle heartStyle = new GUIStyle(GUI.skin.label);
        heartStyle.fontSize = Mathf.RoundToInt(24f * pulse);
        heartStyle.alignment = TextAnchor.UpperLeft;
        heartStyle.normal.textColor = Color.Lerp(new Color(1f, 0.25f, 0.35f), Color.white, flash);

        var hearts = new System.Text.StringBuilder();
        for (int i = 0; i < maxLives; i++)
        {
            hearts.Append(i < Lives ? "♥" : "♡");
            if (i < maxLives - 1) hearts.Append(' ');
        }
        GUI.Label(new Rect(rect.x + 12f, rect.y + 20f, rect.width - 16f, rect.height - 22f), hearts.ToString(), heartStyle);
    }

    // Groups the run's upgrade history by type and draws each as its icon
    // with an "xN" count, left-aligned within the given area - shared by
    // the level-up choice screen and the results screen so the player can
    // see everything they've collected so far in both places. Icons are
    // shown in a plain square (unlike the tall 784x1168 cards on the choice
    // screen) with no backdrop box behind them - only the small count badge
    // gets one, so there's no visible frame around the icon art itself.
    void DrawUpgradeHistoryRow(Rect area)
    {
        var counts = new Dictionary<CardDefinition, int>();
        foreach (CardDefinition card in upgradeHistory)
        {
            counts.TryGetValue(card, out int c);
            counts[card] = c + 1;
        }
        if (counts.Count == 0) return;

        int distinctCount = counts.Count;
        float spacing = 10f;
        float iconSize = Mathf.Min(area.height, (area.width - spacing * (distinctCount - 1)) / distinctCount);
        float x = area.x;

        GUIStyle countStyle = new GUIStyle(GUI.skin.label);
        countStyle.fontSize = 13;
        countStyle.fontStyle = FontStyle.Bold;
        countStyle.alignment = TextAnchor.MiddleCenter;
        countStyle.normal.textColor = Color.white;

        foreach (CardDefinition card in CardDatabase.AllCards)
        {
            if (!counts.TryGetValue(card, out int count)) continue;

            Rect iconRect = new Rect(x, area.y, iconSize, iconSize);
            Texture2D icon = card.icon;
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            else UiBackdrop.Draw(iconRect, 0.5f);

            string countText = "x" + count;
            Vector2 countSize = countStyle.CalcSize(new GUIContent(countText));
            Rect countRect = new Rect(iconRect.xMax - countSize.x - 2f, iconRect.yMax - countSize.y - 2f, countSize.x + 8f, countSize.y + 2f);
            UiBackdrop.Draw(countRect, 0.75f);
            GUI.Label(countRect, countText, countStyle);

            x += iconSize + spacing;
        }
    }

    // Public so DeckEditUI (and anything else that wants to show a card
    // without duplicating field lookups) can build the exact same
    // RewardCardData TriggerLevelUpChoice uses, straight from the asset.
    // Public so DeckEditUI's Deck-slot preview can build the same data
    // TriggerLevelUpChoice used to, straight from the asset. Card UI /
    // Rarity Frame pass, item 18 ("Deck -> Lv.3") - Deck itself stores only
    // a cardId (see GameManager.deckCards, unchanged - item 19 forbids
    // touching that), so LevelLine here is a pure display lookup of the
    // HIGHEST level this card happens to be owned at, not anything Deck
    // itself tracks; empty (hidden) if this card isn't owned at all (a
    // corrupted-deck fallback entry from CardDatabase.UnlockedCards, see
    // RunLevelUpChoice's own comment on that same fallback).
    public RewardCardData MakeCardData(CardDefinition card)
    {
        int highest = CardInventory.GetHighestLevel(card.cardId);
        return new RewardCardData
        {
            CardId = card.cardId,
            Icon = card.icon,
            Title = card.cardName,
            Description = card.description,
            Rarity = card.rarity,
            LevelLine = highest > 0 ? $"Lv.{highest}" : "",
            Category = card.category
        };
    }

    // Bugfix 2026-09-05, item 3 / Card UI / Rarity Frame pass, item 3 -
    // "同名カードが複数候補に出た場合、現在何Lv相当か/選んだら何Lvになる
    // か分からない". Used ONLY by the Level Up/Boss Reward 3-card choice
    // (RunLevelUpChoice/RunBossRewardChoice) - MakeCardData itself is left
    // untouched since it's also used by the Deck Edit screen's plain
    // deck-slot preview outside of any Run, where a "current Run stack"
    // number wouldn't mean anything.
    RewardCardData MakeChoiceCardData(CardDefinition card)
    {
        int currentStack = GetCurrentRunStack(card.cardId);
        // カードVisual最終調整依頼(2026-09-18), item1 - 候補として表示
        // されているだけの段階では「NEW」を出さない(実際に選んで初めて
        // 取得した時だけがNEW - CardInventory.AddCard/MakeOwnedCardData
        // 参照)。以前はここで"NEW  Lv.1"と表示しており、「候補に出た＝
        // NEW」という誤った意味になっていた。
        string stackLabel = currentStack > 0 ? $"Lv.{currentStack} -> Lv.{currentStack + 1}" : "Lv.1";
        // 合成カード: 取得すると主能力と全サブ能力が各強化量ぶん適用される(説明文に全能力)。
        CardVariant variant = CardVariant.IsVariantKey(card.cardId) ? CardVariant.Parse(card.cardId) : null;
        if (variant != null) stackLabel = $"合成Lv.{variant.level}  能力{variant.AbilityCount}種";
        return new RewardCardData
        {
            CardId = card.cardId,
            Icon = card.icon,
            Title = card.cardName,
            Description = card.description,
            Rarity = card.rarity,
            LevelLine = stackLabel,
            Category = card.category,
            // レベルアップ選択UI改修(2026-09-11) - 横長3択UI右端の「主要な
            // 強化数値」。CardEffectFormat参照。
            ValueLine = CardEffectFormat.FormatPrimaryValue(card)
        };
    }

    // Character Card equip (Lv.N = N stacks, see ApplyCharacterCardEffects)
    // plus how many times this cardId already appears in upgradeHistory
    // (each entry = exactly one ApplyCardEffects call, see
    // ApplyUpgradeByCardId) - the same two sources RunCheckpoint's Build-
    // reconstruction replay already treats as the complete stack count for
    // a card this Run, just read back out instead of replayed.
    int GetCurrentRunStack(string cardId)
    {
        int stack = 0;
        for (int i = 0; i < CharacterCardSlotCount; i++)
        {
            if (characterCardIds[i] == cardId) stack += characterCardLevels[i];
        }
        foreach (CardDefinition c in upgradeHistory)
        {
            if (c.cardId == cardId) stack++;
        }
        return stack;
    }

    // Reward/Card Ownership/Gacha/Fusion System Ver.1 - same card data, for
    // the Card Edit screen's owned-cards grid, Character Card slots, and
    // CardFusionUI's owned-cards list. Card Level Ver.1, item 8 - Lv is
    // always shown now (even Lv.1, not just once above 1), with an explicit
    // "MAX" tag at CardInventory.MaxCardLevel. Card UI / Rarity Frame pass,
    // item 5/18 - `equipped` distinguishes Collection ("Lv.3 x2", no badge)
    // from Character Card ("Lv.3" - no x-count, since a slot only ever
    // holds one equipped copy - plus the EQUIPPED badge).
    public RewardCardData MakeOwnedCardData(CardDefinition card, int level, int count, bool equipped = false)
    {
        string levelLabel = level >= CardInventory.MaxCardLevel ? $"Lv.{level} MAX" : $"Lv.{level}";
        // Card UI改修(2026-09-08) - 所持枚数はもうLevelLine文字列へ埋め込
        // まず、RewardCardData.Count(タイトル帯右端固定表示)へ分離した。
        // equipped(Character Card装備中)はスロットに1枚しか入らない概念
        // なのでCountは常に0(非表示)のまま。
        return new RewardCardData
        {
            CardId = card.cardId,
            Icon = card.icon,
            Title = card.cardName,
            Description = card.description,
            Rarity = card.rarity,
            LevelLine = levelLabel,
            Count = equipped ? 0 : count,
            ShowEquippedBadge = equipped,
            Category = card.category,
            // カードVisual最終調整依頼(2026-09-18), item1 - Collection/
            // Character Cardスロットで「実際に新規取得済みだが未確認」の
            // カードにだけNEWを出す。EQUIPPED状態の方が情報として優先度が
            // 高いため、RewardCardUI側でEQUIPPEDと同時にはならないよう
            // 一本化して扱う(両方trueでもEQUIPPED表示が勝つ)。
            ShowNewBadge = !equipped && CardInventory.IsNewUnconfirmed(card.cardId)
        };
    }

    // 2026-09-27: 位置を受け取り、描いた下端のYを返す(DEBUG TOOLSを開いた時だけ出す)。
    float DrawDebugSpeedReadout(float x, float y)
    {
        bool autoRun = PlayerController.Instance != null && PlayerController.Instance.autoRunEnabled;
        // 通常のSPEED表示と同じ換算(m/s×3.6=km/h)。括弧内は実測(dx/dt)のm/s。
        float shownKmh = PlayerController.Instance != null ? SpeedKmh(PlayerController.Instance.CurrentAutoRunSpeed) : 0f;
        string text = $"P-speed {shownKmh:F1} km/h (measured {measuredPlayerSpeed:F2} m/s)   AutoRun {(autoRun ? "ON" : "OFF")}";

        if (debugDragon != null && PlayerController.Instance != null)
        {
            float gap = debugDragon.transform.position.x - PlayerController.Instance.transform.position.x;
            text += $"\nD-speed {measuredDragonSpeed:F2}   Gap {gap:F2}";
        }
        else
        {
            text += "\n(no dragon)";
        }

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 12;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.yellow;

        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect rect = new Rect(x, y, size.x + 10f, size.y + 6f);
        UiBackdrop.Draw(rect, 0.55f);
        GUI.Label(rect, text, style);
        return rect.yMax;
    }

    // Distance Level Design Ver.1, item 10/11 - Development Build / Editor
    // only (Debug.isDebugBuild is false in a Release build - the actual
    // hard gate; DebugMode is this project's existing on/off toggle for
    // debug displays in general, so this only shows when BOTH are true,
    // same pattern as DrawDebugSpeedReadout). Jumping sets MaxDistance
    // directly and teleports the player's own X (distance == player.x in
    // this project - see PlayerController.Update's ReportDistance call) -
    // deliberately NOT routed through ReportDistance/GainExp, which would
    // otherwise award a huge EXP lump sum and cascade into dozens of Level
    // Up screens for a single warp.
    // デバッグ列の配置(2026-09-27 改修) - 以前は左上のBEST/DISTANCE/SPEEDのHUDに重なっていた。
    // SPEEDパネルの下から始め、常に出すのは自動スローの確認に使う「SPD」「SLOW」の2行と
    // 「DEBUG TOOLS」の開閉ボタンだけ。距離ワープ/MILE/CARD/状態表示は開いた時だけ出す
    // (プレイ画面を広く見渡せるように)。
    static bool debugToolsOpen;

    void DrawDistanceWarpDebugUI()
    {
        float bw = 62f, bh = 26f, gap = 4f;
        float x0 = SafeLeft() + UiMargin;
        float y = GetSpeedPanelRect().yMax + HudPanelGap + 4f;

        // ---- SPD(走行速度のデバッグ倍率)----
        float scale = PlayerController.DebugSpeedScale;
        (string label, System.Action action)[] speedButtons =
        {
            ("SPD -", () => PlayerController.DebugSpeedScale = StepDebugSpeed(scale, -1)),
            ("SPD +", () => PlayerController.DebugSpeedScale = StepDebugSpeed(scale, +1)),
            ("SPD x1", () => PlayerController.DebugSpeedScale = 1f),
        };
        for (int i = 0; i < speedButtons.Length; i++)
        {
            Rect r = new Rect(x0 + i * (bw + gap), y, bw, bh);
            if (DrawStyledButton(r, speedButtons[i].label, 11f, primary: i == 2 && Mathf.Abs(scale - 1f) > 0.001f))
            {
                speedButtons[i].action();
            }
        }
        float kmh = PlayerController.Instance != null ? SpeedKmh(PlayerController.Instance.CurrentAutoRunSpeed) : 0f;
        string speedText = $"x{PlayerController.DebugSpeedScale:0.##} ({kmh:F0}km/h)";
        GUIStyle speedStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        speedStyle.normal.textColor = Mathf.Abs(PlayerController.DebugSpeedScale - 1f) > 0.001f ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 1f, 0.7f);
        Rect speedRect = new Rect(x0 + speedButtons.Length * (bw + gap), y, speedStyle.CalcSize(new GUIContent(speedText)).x + 14f, bh);
        UiBackdrop.Draw(speedRect, 0.55f);
        GUI.Label(new Rect(speedRect.x + 6f, speedRect.y, speedRect.width - 6f, speedRect.height), speedText, speedStyle);
        y += bh + gap;

        // ---- ASSIST(高速時の自動操作補助、2026-09-28)----
        // ON/OFFは端末ごと(マルチでも自分のキャラにだけ効く)。小さな1〜2行: 状態・判定速度・直近の自動行動と理由・
        // 行動できなかった主な理由。停止/カード選択/ポーズの時間制御には関与しない。
        HighSpeedAssist assist = HighSpeedAssist.Instance;
        if (assist != null)
        {
            Rect toggleRect = new Rect(x0, y, bw * 1.6f, bh);
            if (DrawStyledButton(toggleRect, assist.assistEnabled ? "ASSIST ON" : "ASSIST OFF", 11f, primary: assist.assistEnabled))
            {
                assist.SetEnabled(!assist.assistEnabled);
            }
            float now = Time.time;
            string last = !string.IsNullOrEmpty(assist.LastAction) && now - assist.LastActionTime < 3f ? $" 直近:{assist.LastAction}({assist.LastActionReason})" : "";
            string fail = !string.IsNullOrEmpty(assist.LastFailure) && now - assist.LastFailureTime < 4f ? $"\n不可:{assist.LastFailure}" : "";
            string assistText = $"{assist.StatusText()} 判定{assist.JudgedKmh:F0}km/h(ON≧{assist.engageKmh:F0}/OFF<{assist.releaseKmh:F0}){last}{fail}";
            GUIStyle assistStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            assistStyle.normal.textColor = assist.CurrentStatus == HighSpeedAssist.Status.Active ? new Color(0.55f, 0.9f, 1f)
                : assist.CurrentStatus == HighSpeedAssist.Status.ManualPriority ? new Color(1f, 0.85f, 0.4f) : new Color(0.6f, 1f, 0.7f);
            Vector2 sz = assistStyle.CalcSize(new GUIContent(assistText));
            Rect assistRect = new Rect(toggleRect.xMax + gap, y, sz.x + 14f, Mathf.Max(bh, sz.y + 4f));
            UiBackdrop.Draw(assistRect, 0.55f);
            GUI.Label(new Rect(assistRect.x + 6f, assistRect.y, assistRect.width - 6f, assistRect.height), assistText, assistStyle);
            y += Mathf.Max(bh, assistRect.height) + gap;
        }

        // ---- DEBUG TOOLS(開いた時だけ: 状態表示/距離ワープ/MILE/CARD)----
        if (DrawStyledButton(new Rect(x0, y, bw * 1.9f, bh - 4f), debugToolsOpen ? "DEBUG TOOLS ▲" : "DEBUG TOOLS ▼", 10f, primary: debugToolsOpen))
        {
            debugToolsOpen = !debugToolsOpen;
        }
        y += bh;
        if (!debugToolsOpen) return;

        string statusText = $"Distance: {Mathf.FloorToInt(MaxDistance)}"
            + (DistanceTierManager.Instance != null ? $"   EnemyHP: {DistanceTierManager.Instance.CurrentEnemyHp}   Tier: {(DistanceTierManager.Instance.CurrentTier != null ? DistanceTierManager.Instance.CurrentTier.tierName : "-")}" : "")
            + (WorldTimeCycle.Instance != null ? $"\nTime: {WorldTimeCycle.Instance.CurrentTimeName}" : "")
            + (BossManager.Instance != null ? $"   BossPhase: {(BossManager.Instance.IsBossPhase ? "ON" : "off")}   NextBoss: {Mathf.FloorToInt(BossManager.Instance.NextBossDistance)}" : "")
            // Reward/Card Ownership/Gacha/Fusion System Ver.1, item 16.
            + $"\nMILE: {TotalOwnedMile}   OwnedCardStacks: {CardInventory.Stacks.Count}";
        GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = 12;
        statusStyle.alignment = TextAnchor.UpperLeft;
        statusStyle.normal.textColor = new Color(0.6f, 1f, 0.7f);
        Vector2 statusSize = statusStyle.CalcSize(new GUIContent(statusText));
        Rect statusRect = new Rect(x0, y, statusSize.x + 10f, statusSize.y + 6f);
        UiBackdrop.Draw(statusRect, 0.55f);
        GUI.Label(statusRect, statusText, statusStyle);
        y = statusRect.yMax + gap;
        y = DrawDebugSpeedReadout(x0, y) + gap;

        float[] stops = DistanceTierManager.DebugWarpStops;
        for (int i = 0; i < stops.Length; i++)
        {
            Rect r = new Rect(x0 + i * (bw + gap), y, bw, bh);
            string label = stops[i] >= 1000f ? $"{stops[i] / 1000f:0.#}K" : $"{stops[i]:0}";
            if (DrawStyledButton(r, label, 12f, primary: false))
            {
                DebugWarpToDistance(stops[i]);
            }
        }
        y += bh + gap;

        // Reward/Card Ownership/Gacha/Fusion System Ver.1, item 16 - Dev
        // Build-only debug tools for repeatedly testing MILE/Gacha/Fusion/
        // Convert without needing to actually grind runs.
        (string label, System.Action action)[] mileButtons =
        {
            ("MILE +500", () => AddMile(500)),
            ("MILE +5000", () => AddMile(5000)),
            ("MILE RESET", () => { TotalOwnedMile = 0; SaveMile(); }),
            ("CARD +1 ALL", () => CardInventory.DebugAddOneOfEvery()),
            ("CARD RESET", () => CardInventory.ResetAll()),
        };
        for (int i = 0; i < mileButtons.Length; i++)
        {
            Rect r = new Rect(x0 + i * (bw + gap), y, bw, bh);
            if (DrawStyledButton(r, mileButtons[i].label, 10f, primary: false))
            {
                mileButtons[i].action();
            }
        }
    }
    static readonly float[] DebugSpeedSteps = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f, 8f };

    static float StepDebugSpeed(float current, int dir)
    {
        int nearest = 0;
        for (int i = 1; i < DebugSpeedSteps.Length; i++)
        {
            if (Mathf.Abs(DebugSpeedSteps[i] - current) < Mathf.Abs(DebugSpeedSteps[nearest] - current)) nearest = i;
        }
        return DebugSpeedSteps[Mathf.Clamp(nearest + dir, 0, DebugSpeedSteps.Length - 1)];
    }

    // Card Expansion/Gacha Evolution Ver.1, item 18 - Dev Build/Editor-only
    // debug tools for immediately observing Gacha Stage/Pool/Next Evolution/
    // Visual changes without grinding an actual run to each BEST-distance
    // threshold. `anchor` is the Home Room's titleBestRect, so this sits
    // directly under the BEST panel rather than floating unrelated.
    void DrawGachaDebugUI(Rect anchor)
    {
        float[] stops = { 0f, 5000f, 20000f, 50000f, 100000f };
        float bw = 74f, bh = 26f, gap = 4f;
        float y = anchor.yMax + 6f;
        for (int i = 0; i < stops.Length; i++)
        {
            Rect r = new Rect(anchor.x + i * (bw + gap), y, bw, bh);
            string label = stops[i] >= 1000f ? $"BEST {stops[i] / 1000f:0.#}K" : $"BEST {stops[i]:0}";
            if (DrawStyledButton(r, label, 10f, primary: false))
            {
                DebugSetBestDistance(stops[i]);
            }
        }

        Rect logRect = new Rect(anchor.x, y + bh + gap, bw * stops.Length + gap * (stops.Length - 1), bh);
        if (DrawStyledButton(logRect, "LOG GACHA POOL", 12f, primary: false))
        {
            DebugLogGachaPool();
        }
    }

    // Directly sets/persists BestDistance (same PlayerPrefs key a real new
    // best writes to) rather than routing through ReportDistance/GainExp -
    // a pure debug shortcut, not a simulated run, so it doesn't touch MaxDistance,
    // EXP, or anything else a real run would.
    public void DebugSetBestDistance(float value)
    {
        if (!Debug.isDebugBuild) return; // Release Build safety net, same pattern as DebugWarpToDistance
        BestDistance = value;
        PlayerPrefs.SetFloat(BestDistanceKey, BestDistance);
        PlayerPrefs.Save();
        Debug.Log($"[Debug] BestDistance set to {value}m -> Gacha Stage {CurrentGachaStage} ({GachaStage.StageNames[CurrentGachaStage - 1]})");
    }

    void DebugLogGachaPool()
    {
        List<CardDefinition> pool = BuildGachaPool();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Debug] Gacha Pool @ BEST {Mathf.FloorToInt(BestDistance)}m, Stage {CurrentGachaStage}: {pool.Count} cards");
        foreach (CardDefinition card in pool)
        {
            sb.AppendLine($"  {card.cardId}  {card.cardName}  {card.RarityStars}  gachaStage={card.gachaStage}  unlockDistance={card.unlockDistance}m");
        }
        Debug.Log(sb.ToString());
    }

    public void DebugWarpToDistance(float targetDistance)
    {
        if (!Debug.isDebugBuild) return; // Release Build safety net - a stray call can never actually warp outside a dev build
        MaxDistance = targetDistance;
        MaxDistanceExact = targetDistance;
        if (PlayerController.Instance != null)
        {
            // Floating Origin(2026-09-22) - プレイヤーを何万ユニットも実際に動かすと、地形チャンクを大量生成
            // してしまううえ座標精度も落ちる。論理距離だけを加算してその場で「N mに来た」ことにする
            // (地形/配置/ボスは論理距離で判断するので、その後は通常どおり進む)。
            float current = PlayerController.Instance.DistanceFromStart;
            FloatingOrigin.LogicalWarp(targetDistance - current);
        }
        Debug.Log($"[Debug] Warped to {targetDistance}m");
    }

    // Sizes a backdrop box to the text's own measured size (via CalcSize)
    // rather than the whole label rect, so it hugs the text instead of
    // drawing one giant bar across the screen.
    void DrawCenteredBackdrop(Rect labelRect, string text, GUIStyle style)
    {
        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect bg = new Rect(
            labelRect.x + labelRect.width / 2f - size.x / 2f - 12f,
            labelRect.y + (labelRect.height - size.y) / 2f - 4f,
            size.x + 24f,
            size.y + 8f);
        UiBackdrop.Draw(bg);
    }

    void DrawLeftBackdrop(Rect labelRect, string text, GUIStyle style)
    {
        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect bg = new Rect(
            labelRect.x - 10f,
            labelRect.y + (labelRect.height - size.y) / 2f - 4f,
            size.x + 20f,
            size.y + 8f);
        UiBackdrop.Draw(bg);
    }

    // Toggled by the small gear icon (see the (!HasStarted || IsGameOver)
    // block in OnGUI) instead of this whole column always being visible.
    bool showSettingsPanel;

    // Orientation/BGM/SE/INVINCIBLE/DEBUG/RESET SCORE - restyled to the
    // shared navy+gold button (secondary variant, same as DECK on the
    // title screen) instead of Unity's raw gray button chrome. Left fully
    // reachable (behind the gear icon) rather than hidden outside
    // development builds - INVINCIBLE/DEBUG/RESET SCORE are how this
    // project's own testing has been done all along, and hiding DEBUG's
    // own toggle would mean no way to ever turn it back on.
    // ギアボタンと(開いていれば)設定/DEBUG列。ホームの手前描画用に切り出した。
    void DrawSettingsChrome()
    {
        if (DrawStyledButton(GetGearButtonRect(), "⚙", 26f, primary: showSettingsPanel))
        {
            showSettingsPanel = !showSettingsPanel;
        }

        if (showSettingsPanel) DrawSettingsColumn();
    }

    void DrawSettingsColumn()
    {
        string orientationLabel = preferredOrientation == ScreenOrientation.Portrait ? "⇄ Portrait" : "⇄ Landscape";
        if (DrawStyledButton(GetOrientationButtonRect(), orientationLabel, 15f, primary: false))
        {
            ToggleOrientation();
        }

        if (AudioManager.Instance != null)
        {
            // Each tap cycles to the next of 5 volume steps (wrapping
            // back to mute after max) - shown as a filled/empty block
            // bar rather than a plain ON/OFF toggle.
            string bgmLabel = "BGM " + VolumeBar(AudioManager.Instance.BgmVolumeLevel);
            if (DrawStyledButton(GetBgmButtonRect(), bgmLabel, 15f, primary: false))
            {
                AudioManager.Instance.CycleBgmVolume();
            }

            string sfxLabel = "SE " + VolumeBar(AudioManager.Instance.SfxVolumeLevel);
            if (DrawStyledButton(GetSfxButtonRect(), sfxLabel, 15f, primary: false))
            {
                AudioManager.Instance.CycleSfxVolume();
            }
        }

        string invincibleLabel = "INVINCIBLE: " + (InvincibleMode ? "ON" : "OFF");
        if (DrawStyledButton(GetInvincibleButtonRect(), invincibleLabel, 13f, primary: false))
        {
            ToggleInvincible();
        }

        string debugLabel = "DEBUG: " + (DebugMode ? "ON" : "OFF");
        if (DrawStyledButton(GetDebugButtonRect(), debugLabel, 15f, primary: false))
        {
            ToggleDebugMode();
        }

        // Game Feel Visibility Pass - see GameFeelDebug's class comment.
        // Not persisted to PlayerPrefs on purpose - always starts OFF, so a
        // shipped build can never accidentally leave it on. Toggle before
        // START so it's active for the run that follows (this column isn't
        // shown during actual gameplay).
        string gameFeelFxLabel = "GAMEFEEL FX: " + (GameFeelDebug.VisibilityBoost ? "ON" : "OFF");
        if (DrawStyledButton(GetGameFeelFxButtonRect(), gameFeelFxLabel, 13f, primary: GameFeelDebug.VisibilityBoost))
        {
            GameFeelDebug.VisibilityBoost = !GameFeelDebug.VisibilityBoost;
        }
    }

    // Visual Style Ver.1 button: a UiBackdrop box (navy fill + thin gold
    // edge) with centered bold text, standing in for Unity's default IMGUI
    // button chrome (a plain gray bevel that doesn't match anything else in
    // the game). primary gives the screen's main call-to-action a fuller
    // fill and a warm gold-white text tint; secondary buttons pass false
    // for a visually quieter presence - e.g. START vs DECK on the title
    // screen, or a screen's own "back" button.
    // ornate opts into OrnateUi's decorative navy+gold+blue-accent frame
    // (see its class comment for why this is a separate treatment) instead
    // of UiBackdrop's plain thin-edge one - only the TOP screen's START/
    // DECK use it; the settings/debug column and every in-run element stay
    // on the plainer look, unchanged.
    // flashAlpha (0-1) draws an extra warm-white glow overlay on top of the
    // button, for a brief "just tapped this" flash (see startPressFlashTimer)
    // rather than any built-in Button transition - GUI.Button's own
    // hover/active tint doesn't reliably fire on touch devices the same way
    // it does with a mouse, so this is driven explicitly from the caller
    // instead.
    bool DrawStyledButton(Rect rect, string text, float fontSize, bool primary, bool ornate = false, float flashAlpha = 0f)
    {
        if (ornate) OrnateUi.DrawPanel(rect, primary ? 0.85f : 0.6f);
        else UiBackdrop.Draw(rect, primary ? 0.85f : 0.55f);
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = Mathf.RoundToInt(fontSize);
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = primary ? new Color(1f, 0.93f, 0.75f) : Color.white;
        GUI.Label(rect, text, style);

        if (flashAlpha > 0.001f)
        {
            Color prevFlash = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.8f, flashAlpha * 0.5f * prevFlash.a);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prevFlash;
        }

        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    // ===== Home Room UI reconstruction pass ===== //

    // A Rect defined as fractions [x0,y0]-[x1,y1] of `bounds` (itself a
    // screen-space Rect, e.g. bgRoomRect) - lets every room hotspot be
    // authored as "roughly this % across, this % down the background
    // image" instead of hand-computed pixel math, and keeps them aligned
    // with the painted room at any aspect ratio (bounds already accounts
    // for the background's own cover-scale crop).
    static Rect FracRect(Rect bounds, float x0, float y0, float x1, float y1)
    {
        return new Rect(
            bounds.x + bounds.width * x0,
            bounds.y + bounds.height * y0,
            bounds.width * (x1 - x0),
            bounds.height * (y1 - y0));
    }

    // Home画面改善依頼④(2026-09-15) - 「地図から出たような旅先表示」演出用。
    // outerの中に、指定アスペクト比(例: 地図フレーム画像自身の縦横比)を
    // 保ったまま最大サイズで収まる中央寄せのRectを返す(GUI.DrawTextureの
    // ScaleToFitと同じ考え方を、後段でその領域の内側にさらに写真を重ね
    // 描きしたい場合など、実際のRect自体が必要なケース向けに関数化した)。
    static Rect FitRectPreserveAspect(Rect outer, float aspect)
    {
        float outerAspect = outer.width / Mathf.Max(1f, outer.height);
        float w, h;
        if (outerAspect > aspect)
        {
            h = outer.height;
            w = h * aspect;
        }
        else
        {
            w = outer.width;
            h = w / aspect;
        }
        return new Rect(outer.x + (outer.width - w) / 2f, outer.y + (outer.height - h) / 2f, w, h);
    }

    // Home画面改善依頼②(2026-09-15), item 4 - 「タップ可能箇所の分かり
    // やすさ」への対応。Character/NEXT STAGEは既にOrnateUi.DrawPanel+常時
    // ゆるい金色パルスでタップ可能だと伝わっていたが、Door(背景に完全に
    // 溶け込んだ透明ホットスポット)とGacha機(タップ後のフラッシュのみ)
    // には常時の手がかりが一切無かった。マスター指示「常時派手に光らせ
    // たり大きなボタンを追加する必要はない」「薄いシアンまたは金の縁取
    // り」どおり、細い金色の枠線を控えめな不透明度で常時描画する軽量な
    // 共通ヘルパー。点滅させず一定の明るさに留める(「常時点滅する演出
    // は不要」)。
    static void DrawTapAffordanceBorder(Rect rect, float roomFadeAlpha, float thickness = 2f)
    {
        Color prev = GUI.color;
        GUI.color = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, 0.45f * roomFadeAlpha);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // Home画面改善依頼⑤(2026-09-16) - 「常時表示の枠線や判定枠っぽい見た目
    // は貼り付け感の原因になる」への対応。DrawTapAffordanceBorderの矩形の
    // 縁取り(いかにも「ここがボタンです」という見た目)をやめ、形状全体へ
    // 重ねるごく薄い明滅に置き換えた - 縁が無いぶん「UIのボタン」ではなく
    // 「そこにある物がわずかに息づいている」ように見える。Door/Gacha機/
    // NEXT STAGE地図など、各hotspotの見た目そのもの(扉の絵・機械の実写・
    // 地図の装飾フレーム)がすでに存在を主張しているため、常時の強い枠は
    // もう不要という判断。
    static void DrawAmbientGlow(Rect rect, float roomFadeAlpha, float minAlpha = 0.03f, float maxAlpha = 0.09f, float speed = 1.6f)
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);
        Color prev = GUI.color;
        GUI.color = new Color(1f, 0.9f, 0.6f, Mathf.Lerp(minAlpha, maxAlpha, pulse) * roomFadeAlpha);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // Home画面改善依頼⑪(2026-09-17), item4-5 - 「動くイラスト背景」の
    // ような控えめな環境アニメーション一式。共通ルール(派手にしない/
    // 周期を長く/振れ幅を小さく/同時に主張させすぎない)を守るため、
    // どの要素もTime.unscaledTimeベースの緩やかなSin波のみで変化させる。
    // 新規アセットは増やさない方針(既存のTexture2D.whiteTexture、または
    // 下のSoftGlowTex - 手続き的に1回だけ生成してキャッシュする柔らかい
    // 円形グラデーション、CreateRadialGlowSprite<Editor/SceneBuilder.cs>
    // と同じ発想をランタイム側で再実装したもの)だけで組んでいる。
    static Texture2D softGlowTexCache;
    static Texture2D SoftGlowTex()
    {
        if (softGlowTexCache != null) return softGlowTexCache;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxDist = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float alpha = Mathf.Clamp01(1f - dist);
                alpha *= alpha;
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        softGlowTexCache = tex;
        return softGlowTexCache;
    }

    // Home待機演出(2026-09-21)へ置き換え: 背景装飾(カーテンの揺れ/窓の光
    // 明滅/ランタンの揺らぎ/旧埃)のアニメは停止した(カーテンは静止表示のまま)。
    // 代わりに操作対象5か所の待機演出と光の粒子をHomeIdleFxで描く。
    void DrawHomeAmbientAnimations(float roomFadeAlpha, Rect[] quietZones)
    {
        if (bgRoomRect.width <= 0f || roomFadeAlpha <= 0.001f) return;
        HomeIdleFx fx = GetIdleFx();
        fx.Tick();
#if UNITY_EDITOR
        // 確認用(Editor専用): Home表示中に数字キー1..5で 扉/ガチャ/肖像画/カード/本 を即再生。
        if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode >= KeyCode.Alpha1 && Event.current.keyCode <= KeyCode.Alpha5)
            fx.DebugStart((int)Event.current.keyCode - (int)KeyCode.Alpha1);
        if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Alpha6)
            homeIdle.playbackSpeed = homeIdle.playbackSpeed < 0.5f ? 1f : 0.15f;
#endif

        DrawCurtainSway(roomFadeAlpha, 0f);   // 静止表示(揺れなし)
        fx.DrawDoor(bgRoomRect, roomFadeAlpha);
        fx.DrawCards(bgRoomRect, roomFadeAlpha);
        fx.DrawBookGlow(bgRoomRect, roomFadeAlpha);
        fx.DrawMotes(bgRoomRect, roomFadeAlpha, quietZones);
    }

    // A. カーテンの揺れ - 環境アニメーション構造修正依頼(2026-09-18):
    // 旧実装は背景テクスチャ自身をカーテンの範囲だけ切り出してもう一度
    // 重ね描きする方式だったが、背景に元々描かれた静止カーテンの上に
    // 動くカーテンが重なり「二重に見える」不自然さがあった。今回、
    // topBackground自体をカーテンを取り除いた版へ差し替え、カーテンは
    // 完全に独立した透過素材(homeCurtain、元画像からAI背景除去で切り出し
    // た同一アセットなので色/質感のズレが無い)を別レイヤーとして重ね、
    // その素材だけを上端(レール)を軸に回転させる、背景と分離した構造へ
    // 変更した。
    void DrawCurtainSway(float roomFadeAlpha, float t)
    {
        if (homeCurtain == null) return;
        // bgRoomRectはcover-scaleの都合で画面の外側へはみ出すことがある
        // (背景素材とScreenのアスペクト比の組み合わせ次第で、上下方向・
        // 左右方向のどちらにもはみ出し得る - 実機で2048x1024として読み
        // 込まれるNPOTスケール後の実寸で確認済み)。bgRoomRectの右端の
        // フラクションをそのまま基準にすると、はみ出し方向によっては
        // カーテンが画面外へ出て見切れてしまうため、実際に画面に見えて
        // いる範囲(bgRoomRectとScreenの共通部分)を基準にする。
        float visibleLeft = Mathf.Max(bgRoomRect.x, 0f);
        float visibleTop = Mathf.Max(bgRoomRect.y, 0f);
        float visibleRight = Mathf.Min(bgRoomRect.xMax, Screen.width);
        float visibleBottom = Mathf.Min(bgRoomRect.yMax, Screen.height);
        float visibleWidth = visibleRight - visibleLeft;
        float visibleHeight = visibleBottom - visibleTop;
        if (visibleWidth <= 0f || visibleHeight <= 0f) return;

        // 素材の実寸(縦横比)をそのまま使い、歪めずに配置する。
        float destHeight = visibleHeight * 0.55f;
        float destWidth = destHeight * (homeCurtain.width / (float)homeCurtain.height);
        Rect curtainRect = new Rect(
            visibleRight - visibleWidth * 0.005f - destWidth,
            visibleTop,
            destWidth,
            destHeight);
        if (curtainRect.width <= 0f || curtainRect.height <= 0f) return;

        // Home環境アニメーション強化依頼(2026-09-17) - 「目で分かる程度に
        // 揺れていることが分かるように」との指摘で振れ幅を約1.4°→3.8°へ
        // 拡大(周期はほぼ据え置き、速すぎる/激しい揺れにはしない)。
        float swayAngle = 0f; // 2026-09-21 揺れは停止(静止表示)。tは互換のため残す

        Matrix4x4 prevMatrix = GUI.matrix;
        Vector2 pivot = new Vector2(curtainRect.x + curtainRect.width * 0.5f, curtainRect.y);
        GUIUtility.RotateAroundPivot(swayAngle, pivot);
        Color prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, roomFadeAlpha);
        GUI.DrawTexture(curtainRect, homeCurtain, ScaleMode.StretchToFill);
        GUI.color = prevColor;
        GUI.matrix = prevMatrix;
    }

    // C. 埃/光の粒。位置・速度・周期をindexから決定論的に散らし(乱数を
    // 毎フレーム引かない)、下端から上端へゆっくり上昇しながらループする。
    // 上昇の前半/後半でSin(π×phase)によりフェードイン/アウトするため、
    // ループの継ぎ目が瞬間的に消える/現れることはない。
    static void DrawDustMotes(Rect area, float roomFadeAlpha, float t)
    {
        if (area.width <= 0f || area.height <= 0f) return;
        Texture2D glow = SoftGlowTex();
        // Home環境アニメーション強化依頼(2026-09-17) - 「埃/光粒を少し
        // 増やして空気が流れている感を出す」に対応し6→11粒へ増量。
        const int moteCount = 11;
        for (int i = 0; i < moteCount; i++)
        {
            float seed = i * 12.9898f;
            float frac01 = seed - Mathf.Floor(seed);
            float cycle = 16f + (i % 4) * 4f; // 16〜28秒かけて1往復
            float phase = Mathf.Repeat(t + seed * 3f, cycle) / cycle; // 0..1
            float driftX = Mathf.Sin(t * 0.12f + seed) * area.width * 0.05f;
            float baseX = area.x + area.width * Mathf.Repeat(0.1f + frac01 * 0.8f, 1f) + driftX;
            float y = area.yMax - phase * area.height;
            float fade = Mathf.Sin(phase * Mathf.PI);
            float size = Mathf.Lerp(3f, 6f, frac01);
            Rect moteRect = new Rect(baseX - size * 0.5f, y - size * 0.5f, size, size);
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.96f, 0.85f, 0.4f * fade * roomFadeAlpha);
            GUI.DrawTexture(moteRect, glow);
            GUI.color = prev;
        }
    }

    // E. ロゴのハイライト - 数秒に一度だけ、金属光沢のような細い帯を
    // ロゴの上を左から右へ流す。GUI.BeginGroupでロゴ自身のrectにクリップ
    // するため、帯が外へはみ出したりロゴ以外を明るくしたりしない。
    static void DrawHomeLogoHighlight(Rect logoRect, float logoFadeAlpha)
    {
        if (logoFadeAlpha <= 0.001f || logoRect.width <= 0f || logoRect.height <= 0f) return;
        // Home環境アニメーション強化依頼(2026-09-17) - 「5〜8秒に1回程度」
        // に合わせ周期を6→7秒へ、帯自体もやや明るく太くした。
        const float cycle = 7f;
        const float sweepWindow = 0.18f; // 周期のうちこの割合の間だけ帯が発生する
        float phase = Mathf.Repeat(Time.unscaledTime, cycle) / cycle;
        if (phase > sweepWindow) return;

        float sweepT = phase / sweepWindow;
        float travel = Mathf.Lerp(-logoRect.width * 0.3f, logoRect.width * 1.3f, sweepT);
        float streakAlpha = Mathf.Sin(sweepT * Mathf.PI) * 0.32f * logoFadeAlpha;
        if (streakAlpha <= 0.001f) return;

        GUI.BeginGroup(logoRect);
        Matrix4x4 prevMatrix = GUI.matrix;
        Vector2 pivotLocal = new Vector2(travel, logoRect.height * 0.5f);
        GUIUtility.RotateAroundPivot(20f, pivotLocal);
        Color prevColor = GUI.color;
        GUI.color = new Color(1f, 0.97f, 0.85f, streakAlpha);
        GUI.DrawTexture(new Rect(travel - logoRect.height * 0.19f, -logoRect.height, logoRect.height * 0.38f, logoRect.height * 3f), Texture2D.whiteTexture);
        GUI.color = prevColor;
        GUI.matrix = prevMatrix;
        GUI.EndGroup();
    }

    // A completely invisible tap target (no backdrop, no label - "大きな
    // メニューボタンとして見えないように") with a brief "少し光る" flash on
    // tap (item 3). flashTimer is one of the per-hotspot fields in Update's
    // UpdateHomeRoom - passed by ref so this one method drives all four.
    // interactable=false (while the Gacha Result popup is open) skips the
    // GUI.Button call entirely rather than just ignoring its result, so a
    // tap over a hotspot can't "consume" the event out from under the
    // popup's own OK button drawn later this same OnGUI pass.
    bool DrawRoomHotspot(Rect rect, ref float flashTimer, bool interactable = true)
    {
        bool tapped = interactable && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        if (tapped)
        {
            flashTimer = roomHotspotFlashDuration;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe);
        }

        if (flashTimer > 0f)
        {
            float f = flashTimer / roomHotspotFlashDuration;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.75f, f * 0.35f * prev.a);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
        return tapped;
    }

    // Home画面改善依頼⑦(2026-09-16), item 9 - 扉専用のタップ反応。ドア
    // 自体は背景アートに焼き込まれた1枚絵のため、取っ手だけ/縁だけを
    // 独立して光らせたり本当に拡大したりすることはできない。代わりに、
    // ①中心付近(取っ手のおおよその位置)を暖色でほんのり明るくする、
    // ②矩形の縁だけを金色でなぞる、③その縁をタップ直後だけrectよりひと
    // まわり大きく描いて一瞬膨らんだように見せる、の3つを組み合わせて
    // 「常時強く発光/点滅はしない、タップ時だけ軽く反応する」を近似する。
    bool DrawDoorHotspot(Rect rect, ref float flashTimer, bool interactable, float roomFadeAlpha)
    {
        bool tapped = interactable && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        if (tapped)
        {
            flashTimer = roomHotspotFlashDuration;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe);
        }

        if (flashTimer > 0f)
        {
            float f = flashTimer / roomHotspotFlashDuration;
            // 取っ手付近(扉のやや右寄り下側)をほんのり明るく。
            float handleSize = Mathf.Min(rect.width, rect.height) * 0.22f;
            Rect handleRect = new Rect(rect.x + rect.width * 0.62f - handleSize / 2f, rect.y + rect.height * 0.55f - handleSize / 2f, handleSize, handleSize);
            Color prevHandle = GUI.color;
            GUI.color = new Color(1f, 0.9f, 0.55f, f * 0.5f * roomFadeAlpha);
            GUI.DrawTexture(handleRect, Texture2D.whiteTexture);
            GUI.color = prevHandle;

            // 縁を金色でなぞり、タップ直後ほど少し外側へ膨らませる(軽い
            // Scale反応の近似)。
            float bulge = f * rect.width * 0.02f;
            Rect edgeRect = new Rect(rect.x - bulge, rect.y - bulge, rect.width + bulge * 2f, rect.height + bulge * 2f);
            float thickness = 2f + f * 2f;
            Color prevEdge = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.35f, f * 0.8f * roomFadeAlpha);
            GUI.DrawTexture(new Rect(edgeRect.x, edgeRect.y, edgeRect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(edgeRect.x, edgeRect.yMax - thickness, edgeRect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(edgeRect.x, edgeRect.y, thickness, edgeRect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(edgeRect.xMax - thickness, edgeRect.y, thickness, edgeRect.height), Texture2D.whiteTexture);
            GUI.color = prevEdge;
        }
        return tapped;
    }

    // Home画面 / Stage Select改善依頼(2026-09-16), item2 - Characterカード
    // UI(半透明パネル+見出し+アイコン)をHomeから撤去し、「壁に飾られた
    // 額縁付きの肖像画」として見せる。portraitFrameTexture(ChatGPT生成、
    // 中央が実アルファ透明)を自身のアスペクト比のまま中央寄せし、その
    // 内側の透明窓へ選択中キャラクターのportraitを重ねる - Home画面改善
    // 依頼④のNEXT STAGE地図フレーム+写真と全く同じ「フレームを描いた後
    // その無地/透明領域だけに絵を重ねる」合成パターン。常時の判定枠・
    // パルスするパネル地色は廃止し、フレーム全体へのごく薄い息づき
    // (DrawAmbientGlow)だけに絞った - 「タップ可能」は伝えつつ、主役は
    // あくまで肖像画自身であることを優先している。
    void DrawCharacterHotspot(Rect rect, bool roomInteractable, float roomFadeAlpha)
    {
        Rect frameRect = portraitFrameTexture != null
            ? FitRectPreserveAspect(rect, (float)portraitFrameTexture.width / Mathf.Max(1, portraitFrameTexture.height))
            : rect;

        // Home待機演出(2026-09-21) - 肖像画は額縁上中央の吊り位置を軸に、影・背景・
        // キャラ・質感・額縁をすべて同じ回転で一体に揺らす(キャラ切替後も同じ)。
        // タップ判定(下のGUI.Button(rect))と名前ラベルは回転させない。
        Matrix4x4 idlePrevMatrix = GUI.matrix;
        float idleAngle = GetIdleFx().PortraitAngle();
        if (Mathf.Abs(idleAngle) > 0.0001f) GUIUtility.RotateAroundPivot(idleAngle, new Vector2(frameRect.center.x, frameRect.y + frameRect.height * 0.035f));

        // 壁に掛かっている説得力のための、ごく薄い設置影(単色近似)。
        Color prevShadow = GUI.color;
        Rect shadowRect = new Rect(frameRect.x + frameRect.width * 0.035f, frameRect.y + frameRect.height * 0.03f, frameRect.width, frameRect.height);
        GUI.color = new Color(0f, 0f, 0f, 0.22f * roomFadeAlpha);
        GUI.DrawTexture(shadowRect, Texture2D.whiteTexture);
        GUI.color = prevShadow;

        CharacterDefinition selectedDef = CharacterDatabase.FindById(SelectedCharacterId);
        Texture2D portrait = selectedDef != null ? selectedDef.portrait : null;
        if (portrait != null)
        {
            // フレーム画像自身の内側の透明窓(実測、幅80.2%×高さ79.5%、
            // x=9.85%〜90.06%/y=13.02%〜92.51%)へポートレートを重ねる。
            // フレーム未設定時はrect全体を窓として扱う(Acceptance Test -
            // 画像未設定でも壊れない)。
            Rect windowRect = portraitFrameTexture != null
                ? new Rect(frameRect.x + frameRect.width * 0.0985f, frameRect.y + frameRect.height * 0.1302f, frameRect.width * 0.802f, frameRect.height * 0.795f)
                : frameRect;
            Rect portraitRect = FitRectPreserveAspect(windowRect, (float)portrait.width / Mathf.Max(1, portrait.height));
            Color prevIcon = GUI.color;

            // 肖像画背景追加依頼(2026-09-17) - portrait自身は透明背景の
            // 切り抜きなので、先に窓いっぱい(portraitRectではなくwindow
            // Rect全体 - キャラの周囲に隙間なく)へ共通の油彩風背景を敷く。
            // 「キャラの切り抜き」ではなく「額縁の中の1枚の絵」に見せる
            // ための下地 - portraitBackdropTextureが未生成の間はnullを
            // 許容し安全にスキップする。
            if (portraitBackdropTexture != null)
            {
                GUI.color = new Color(0.9f, 0.86f, 0.8f, roomFadeAlpha);
                GUI.DrawTexture(windowRect, portraitBackdropTexture, ScaleMode.StretchToFill);
            }

            // Home画面改善依頼⑨(2026-09-17), item1 - 「きれいな画像を額に
            // 貼った感」を減らし「壁に長く飾られた装飾画」に寄せるための
            // 2段構成。(a)わずかに拡大して低alphaで下敷きにした同じ
            // ポートレートがソフトフォーカス/ハレーションのように輪郭を
            // にじませる(IMGUIには本物のガウスぼかしが無いための代替)。
            // (b)本体は彩度/コントラストを落とす暖色寄りの乗算Tintで描く。
            // portraitAgingOverlayTexture(紙/キャンバス質感、ChatGPT生成
            // 予定)が用意でき次第(c)としてさらに重ねる - 現状はnullなので
            // 安全にスキップされる。
            Rect softRect = new Rect(
                portraitRect.x - portraitRect.width * 0.015f,
                portraitRect.y - portraitRect.height * 0.015f,
                portraitRect.width * 1.03f,
                portraitRect.height * 1.03f);
            GUI.color = new Color(0.82f, 0.78f, 0.7f, roomFadeAlpha * 0.35f);
            GUI.DrawTexture(softRect, portrait, ScaleMode.ScaleToFit);

            GUI.color = new Color(0.86f, 0.82f, 0.74f, roomFadeAlpha);
            GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);

            if (portraitAgingOverlayTexture != null)
            {
                GUI.color = new Color(1f, 1f, 1f, roomFadeAlpha * 0.4f);
                GUI.DrawTexture(portraitRect, portraitAgingOverlayTexture, ScaleMode.ScaleToFit);
            }

            GUI.color = prevIcon;
        }

        if (portraitFrameTexture != null)
        {
            Color prevFrame = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, roomFadeAlpha);
            GUI.DrawTexture(frameRect, portraitFrameTexture, ScaleMode.ScaleToFit);
            GUI.color = prevFrame;
        }

        // 常時のごく控えめな息づき - 扉(DrawAmbientGlow既定値)よりさらに
        // 控えめな上限にして、視覚優先順位「ドア>肖像画」を保つ。

        GUI.matrix = idlePrevMatrix; // ここから先(名前/タップ判定)は回転させない

        // 主役はあくまで肖像画自身なので、名前はフレーム下にごく小さく
        // 添えるだけに留める(常時の大きな「CHARACTER」見出しは撤去)。
        GUIStyle nameStyle = new GUIStyle(GUI.skin.label);
        nameStyle.fontSize = 13;
        nameStyle.fontStyle = FontStyle.Bold;
        nameStyle.alignment = TextAnchor.UpperCenter;
        nameStyle.normal.textColor = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, roomFadeAlpha * 0.85f);
        string nameLabel = selectedDef != null ? selectedDef.displayName : "";
        GUI.Label(new Rect(rect.x, frameRect.yMax + 2f, rect.width, 20f), nameLabel, nameStyle);

        bool tapped = roomInteractable && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        if (tapped)
        {
            characterHotspotFlashTimer = roomHotspotFlashDuration;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe);
        }

        if (characterHotspotFlashTimer > 0f)
        {
            float f = characterHotspotFlashTimer / roomHotspotFlashDuration;
            Color prevFlash = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.75f, f * 0.35f * roomFadeAlpha);
            GUI.DrawTexture(frameRect, Texture2D.whiteTexture);
            GUI.color = prevFlash;
        }

        if (tapped && roomFadeAlpha > 0.99f)
        {
            OpenCharacterSelect();
        }
    }

    // Home画面 / Stage Select改善依頼(2026-09-16), item4 - NEXT STAGE
    // ホットスポット(旧DrawStageHotspot)はHomeから撤去した。行き先選択は
    // 出発専用のStage Select画面(StageSelectUI)へ完全に分離している。
    // Gacha Result popup (item 4) - "NEW CARD / SPEED UP / Lv.1 / OWNED x3
    // / OK", closing straight back to the room (no forced navigation to
    // Card Edit). A fresh Gacha draw is always Lv.1, so this doesn't need
    // CardInventory.MaxCardLevel's "MAX" tag logic at all.
    void DrawGachaResultPopup()
    {
        if (!gachaResultOpen || gachaResultCard == null) return;

        // Item 14 - "★4/★5draw gets a slightly stronger Reveal" - a brief
        // pulsing glow behind the panel for its first
        // GachaResultRevealDuration seconds, separate from
        // gachaMachineShakeTimer (which already finished before the Result
        // popup even opens). Pure color/alpha, no extra sprites, so this
        // stays cheap and doesn't extend the overall presentation length.
        bool highRarity = gachaResultCard.rarity >= 4;
        float revealT = GachaResultRevealDuration > 0f ? Mathf.Clamp01(gachaResultRevealTimer / GachaResultRevealDuration) : 0f;

        Color dimPrev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f * dimPrev.a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = dimPrev;

        float panelWidth = Mathf.Min(440f, Screen.width * 0.8f);
        // Bugfix 2026-09-05, item 5 - +32 vs the Rarity-only layout to fit
        // the Gacha Stage/Next Evolution line moved in from the (now
        // removed) always-on Home Room label below.
        float panelHeight = 432f;
        Rect panelRect = new Rect(Screen.width / 2f - panelWidth / 2f, Screen.height / 2f - panelHeight / 2f, panelWidth, panelHeight);

        if (highRarity && revealT > 0f)
        {
            Color glowColor = gachaResultCard.rarity >= 5 ? new Color(1f, 0.65f, 0.2f) : new Color(0.6f, 0.75f, 1f);
            float pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(revealT * Mathf.PI * 5f));
            Color prevGlow = GUI.color;
            GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, pulse * revealT * prevGlow.a);
            float pad = 26f * revealT;
            GUI.DrawTexture(new Rect(panelRect.x - pad, panelRect.y - pad, panelRect.width + pad * 2f, panelRect.height + pad * 2f), Texture2D.whiteTexture);
            GUI.color = prevGlow;
        }

        OrnateUi.DrawPanel(panelRect, 0.92f);

        GUIStyle headlineStyle = new GUIStyle(GUI.skin.label);
        headlineStyle.fontSize = 24;
        headlineStyle.fontStyle = FontStyle.Bold;
        headlineStyle.alignment = TextAnchor.MiddleCenter;
        headlineStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        // Card UI改修(2026-09-08), item 6-5 - 「NEW/DUPLICATEを別ラベルで
        // 表示」。Gacha抽選は常にLv.1を1枚付与するため(下の"Lv.1  GAINED
        // +1"参照)、抽選後の合計所持数(gachaResultOwnedCount)が1ならその
        // 1枚が今回初めて得たもの=NEW、2以上なら既に持っていた=DUPLICATE
        // と判定できる(抽選ロジック自体には手を入れず、表示側だけで導出)。
        bool isNewCard = gachaResultOwnedCount <= 1;
        headlineStyle.normal.textColor = isNewCard ? new Color(1f, 0.85f, 0.4f) : new Color(0.7f, 0.85f, 1f);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 24f, panelRect.width, 34f), isNewCard ? "NEW CARD" : "DUPLICATE", headlineStyle);

        // Card UI / Rarity Frame pass, item 15 - "GachaでCardを引いた際も、
        // RevealしたCardのRarityに応じて同じFrameを使用". Drawn as a border
        // just around the icon (rather than replacing the popup's own
        // OrnateUi dialog chrome). Rarity 1 now also has a real frame Sprite
        // (Card UI改修2026-09-08 - CardFrameRarity1.png), so every Rarity
        // draws its own frame here now.
        float iconSize = highRarity ? 110f + 14f * revealT : 110f;
        Rect iconDrawRect = new Rect(panelRect.x + panelRect.width / 2f - iconSize / 2f, panelRect.y + 70f - (iconSize - 110f) / 2f, iconSize, iconSize);
        Sprite rarityFrameSprite = CardRarityFrames.GetFrame(gachaResultCard.rarity, null);
        if (rarityFrameSprite != null)
        {
            float framePad = iconSize * 0.22f;
            GUI.DrawTexture(new Rect(iconDrawRect.x - framePad, iconDrawRect.y - framePad, iconDrawRect.width + framePad * 2f, iconDrawRect.height + framePad * 2f), rarityFrameSprite.texture, ScaleMode.ScaleToFit);
        }
        if (gachaResultCard.icon != null)
        {
            GUI.DrawTexture(iconDrawRect, gachaResultCard.icon, ScaleMode.ScaleToFit);
        }

        GUIStyle nameStyle = new GUIStyle(GUI.skin.label);
        nameStyle.fontSize = 26;
        nameStyle.fontStyle = FontStyle.Bold;
        nameStyle.alignment = TextAnchor.MiddleCenter;
        nameStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(panelRect.x, panelRect.y + 190f, panelRect.width, 36f), gachaResultCard.cardName, nameStyle);

        // Rarity Visual, item 15 - ★ rating shown right under the name,
        // color-coded so higher Rarity reads as visually special without
        // ★1 ever looking "trash" (it still gets the same gold star glyph,
        // just fewer of them).
        GUIStyle starStyle = new GUIStyle(GUI.skin.label);
        starStyle.fontSize = 22;
        starStyle.fontStyle = FontStyle.Bold;
        starStyle.alignment = TextAnchor.MiddleCenter;
        starStyle.normal.textColor = gachaResultCard.rarity >= 5 ? new Color(1f, 0.65f, 0.2f)
            : gachaResultCard.rarity >= 4 ? new Color(0.65f, 0.8f, 1f)
            : new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 222f, panelRect.width, 28f), gachaResultCard.RarityStars, starStyle);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label);
        subStyle.fontSize = 20;
        subStyle.alignment = TextAnchor.MiddleCenter;
        subStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        // Item 6 - "今回取得枚数" (always +1 for a single Gacha draw) and
        // "取得後所持数" (total owned across all levels) shown as two
        // distinct lines rather than one ambiguous "OWNED xN".
        GUI.Label(new Rect(panelRect.x, panelRect.y + 252f, panelRect.width, 28f), "Lv.1  GAINED +1", subStyle);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 280f, panelRect.width, 28f), $"OWNED (TOTAL) x{gachaResultOwnedCount}", subStyle);

        // Bugfix 2026-09-05, item 5 - Gacha Stage/Next Evolution info moved
        // here from the (now removed) always-on Home Room background label -
        // shown only inside this post-tap popup instead of sitting directly
        // on the painted room scene.
        int gachaStage = CurrentGachaStage;
        float nextEvoDistance = GachaStage.NextEvolutionDistance(gachaStage);
        string gachaStageLine = nextEvoDistance > 0f
            ? $"CARD GACHA Lv.{gachaStage}   NEXT EVOLUTION {Mathf.FloorToInt(nextEvoDistance)}m"
            : $"CARD GACHA Lv.{gachaStage}   MAX EVOLUTION";
        GUIStyle gachaStageStyle = new GUIStyle(GUI.skin.label);
        gachaStageStyle.fontSize = 13;
        gachaStageStyle.alignment = TextAnchor.MiddleCenter;
        gachaStageStyle.normal.textColor = new Color(0.75f, 0.85f, 1f, 0.85f);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 312f, panelRect.width, 22f), gachaStageLine, gachaStageStyle);

        Rect okRect = new Rect(panelRect.x + panelRect.width / 2f - 90f, panelRect.yMax - 70f, 180f, 52f);
        if (DrawStyledButton(okRect, "OK", 22f, primary: true, ornate: true))
        {
            gachaResultOpen = false;
        }
    }

    // Item 5 - a brief toast, no dedicated screen/dialog.
    void DrawInsufficientMileToast()
    {
        if (gachaInsufficientMessageTimer <= 0f) return;

        float alpha = Mathf.Clamp01(gachaInsufficientMessageTimer / 0.3f); // quick fade at the very end
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 22;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = new Color(1f, 0.7f, 0.6f, alpha);

        string text = $"NOT ENOUGH MILE\n{GachaCostMile} MILE REQUIRED";
        Rect rect = new Rect(0f, Screen.height * 0.42f, Screen.width, 70f);
        Color prevBg = GUI.color;
        GUI.color = new Color(prevBg.r, prevBg.g, prevBg.b, prevBg.a * alpha);
        DrawCenteredBackdrop(rect, text, style);
        GUI.color = prevBg;
        GUI.Label(rect, text, style);
    }

    // Home画面 / Stage Select改善依頼(2026-09-16), item5/6/10 - 扉の
    // タップ動作。Active Runがあればそのまま再開(Continue)、無ければ
    // Stage Selectへ直接遷移する(以前はここで即Runを開始していたが、
    // 「出発時だけの専用画面」というStage Select方針に伴い、行き先を
    // 選んでから出発する流れへ変更した - 実際のRun開始はStage Select側の
    // 出発ボタン、DepartFromStageSelect参照)。
    void OnDoorTapped()
    {
        // マルチプレイ接続中はCONTINUE(シングルの中断データ)ではなく、Stage Selectから全員で出発する。
        if (RunCheckpoint.HasActiveRun && !NetSession.IsActive)
        {
            ContinueActiveRun();
        }
        else
        {
            OpenStageSelect();
        }
    }

    // Item 13 - "現在のRunと未確定MILEを破棄します" confirm, shown only
    // when the player deliberately taps "NEW RUN" despite an Active Run
    // already existing.
    void DrawNewRunConfirm()
    {
        if (!showNewRunConfirm) return;

        Color dimPrev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f * dimPrev.a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = dimPrev;

        float panelWidth = Mathf.Min(480f, Screen.width * 0.85f);
        float panelHeight = 230f;
        Rect panelRect = new Rect(Screen.width / 2f - panelWidth / 2f, Screen.height / 2f - panelHeight / 2f, panelWidth, panelHeight);
        OrnateUi.DrawPanel(panelRect, 0.92f);

        GUIStyle msgStyle = new GUIStyle(GUI.skin.label);
        msgStyle.fontSize = 20;
        msgStyle.alignment = TextAnchor.MiddleCenter;
        msgStyle.wordWrap = true;
        msgStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 26f, panelRect.width - 48f, 100f),
            "現在のRunと未確定MILEを\n破棄します。よろしいですか？", msgStyle);

        Rect yesRect = new Rect(panelRect.x + 28f, panelRect.yMax - 68f, panelRect.width / 2f - 42f, 50f);
        Rect noRect = new Rect(panelRect.x + panelRect.width / 2f + 14f, panelRect.yMax - 68f, panelRect.width / 2f - 42f, 50f);
        if (DrawStyledButton(yesRect, "破棄してNEW RUN", 15f, primary: true))
        {
            showNewRunConfirm = false;
            RunCheckpoint.Clear();
            // item5/6 - 破棄後はStage Selectへ遷移し、そこで行き先を
            // 選んでから出発する(DepartFromStageSelect参照)。
            OpenStageSelect();
        }
        if (DrawStyledButton(noRect, "キャンセル", 16f, primary: false))
        {
            showNewRunConfirm = false;
        }
    }

    // Item 9 - small Gameplay Pause/Menu button (bottom-right, mirroring
    // the title screen's gear icon at bottom-left).
    Rect GetPauseButtonRect() => new Rect(Screen.width - SafeRight() - UiMargin - 52f, Screen.height - SafeBottom() - UiMargin - 52f, 52f, 52f);

    void DrawPauseMenu()
    {
        Rect panelRect = new Rect(Screen.width - SafeRight() - UiMargin - 240f, Screen.height - SafeBottom() - UiMargin - 52f - 152f, 240f, 140f);
        OrnateUi.DrawPanel(panelRect, 0.92f);

        Rect resumeRect = new Rect(panelRect.x + 12f, panelRect.y + 12f, panelRect.width - 24f, 52f);
        if (DrawStyledButton(resumeRect, "RESUME", 18f, primary: true))
        {
            showPauseMenu = false;
            TimeControl.Resume(pauseMenuTimeOwner);
        }

        Rect returnRect = new Rect(panelRect.x + 12f, panelRect.y + 74f, panelRect.width - 24f, 52f);
        if (DrawStyledButton(returnRect, "RETURN TO HOME", 14f, primary: false))
        {
            showReturnHomeConfirm = true;
        }
    }

    // Item 9 - "RETURN TO HOMEを選択した場合は、確認ダイアログを表示して
    // ください". Explicitly states this is NOT a Finish (Run MILE stays
    // unconfirmed, Checkpoint is unaffected).
    void DrawReturnHomeConfirm()
    {
        if (!showReturnHomeConfirm) return;

        Color dimPrev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f * dimPrev.a);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = dimPrev;

        float panelWidth = Mathf.Min(480f, Screen.width * 0.85f);
        float panelHeight = 240f;
        Rect panelRect = new Rect(Screen.width / 2f - panelWidth / 2f, Screen.height / 2f - panelHeight / 2f, panelWidth, panelHeight);
        OrnateUi.DrawPanel(panelRect, 0.92f);

        GUIStyle msgStyle = new GUIStyle(GUI.skin.label);
        msgStyle.fontSize = 19;
        msgStyle.alignment = TextAnchor.MiddleCenter;
        msgStyle.wordWrap = true;
        msgStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 24f, panelRect.width - 48f, 130f),
            "Home Roomへ戻ります。\nRunは終了せず、この続きから\nCONTINUEできます。", msgStyle);

        Rect yesRect = new Rect(panelRect.x + 28f, panelRect.yMax - 68f, panelRect.width / 2f - 42f, 50f);
        Rect noRect = new Rect(panelRect.x + panelRect.width / 2f + 14f, panelRect.yMax - 68f, panelRect.width / 2f - 42f, 50f);
        if (DrawStyledButton(yesRect, "RETURN TO HOME", 15f, primary: true))
        {
            showReturnHomeConfirm = false;
            showPauseMenu = false;
            TimeControl.Resume(pauseMenuTimeOwner);
            ReturnToHome();
        }
        if (DrawStyledButton(noRect, "キャンセル", 16f, primary: false))
        {
            showReturnHomeConfirm = false;
        }
    }

    // Item 3 - one-shot "ESCAPE AVAILABLE" banner the first time 1000m is
    // crossed (see ReportDistance).
    void DrawEscapeAvailableBanner()
    {
        if (escapeAvailableBannerTimer <= 0f) return;

        float alpha = Mathf.Clamp01(escapeAvailableBannerTimer / 0.4f); // quick fade at the very end
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 26;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = new Color(0.6f, 0.95f, 1f, alpha); // Cyan/Light Blue, matching the escape presentation's palette

        string text = "ESCAPE AVAILABLE";
        Rect rect = new Rect(0f, Screen.height * 0.3f, Screen.width, 50f);
        Color prevBg = GUI.color;
        GUI.color = new Color(prevBg.r, prevBg.g, prevBg.b, prevBg.a * alpha);
        DrawCenteredBackdrop(rect, text, style);
        GUI.color = prevBg;
        GUI.Label(rect, text, style);
    }
}
