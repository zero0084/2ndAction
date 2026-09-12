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
                if (!string.IsNullOrEmpty(id) && CardDatabase.FindById(id) != null && !deckCards.Contains(id))
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
        if (PlayerController.Instance != null) PlayerController.Instance.ApplyCharacterBaseStats(def);
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

    // Stage Select画面のConfirmからのみ呼ばれる。「選択ステージ=次回
    // NEW RUNで出発するステージ」という仕様どおり、Active Run/Checkpoint
    // (RunCheckpoint.cs)には一切触れない。
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
            int stacks = Mathf.Max(1, characterCardLevels[i]);
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
    // ステージ選択導線追加(2026-09-12) - Home中央下の新規ホットスポット用。
    float stageHotspotFlashTimer;

    // Desk "CARD GACHA" machine prop, drawn directly onto the room scene
    // (not its own screen/canvas) - see SceneBuilder for the import.
    public Texture2D gachaMachineTexture;
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
        // 見落とし)。新設のstageHotspotFlashTimerも同じ仕組みのため、
        // ここで両方まとめて追加する。
        if (characterHotspotFlashTimer > 0f) characterHotspotFlashTimer -= Time.unscaledDeltaTime;
        if (stageHotspotFlashTimer > 0f) stageHotspotFlashTimer -= Time.unscaledDeltaTime;
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
    public float BestTime { get; private set; }
    public float RunTime { get; private set; }
    public bool InvincibleMode { get; private set; }
    public bool DebugMode { get; private set; }
    public int Lives { get; private set; }
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
        LoadDeck();
        LoadMile();
        LoadCharacterCards();
        LoadSelectedCharacter();
        LoadSelectedStage();

        // Bug #001 診断フェーズ (2026-09-08) - Application.logMessageReceived
        // フックは一度だけ登録すれば十分(static event、二重登録防止は
        // EnsureHooked自身が行う)。
        BossDiagnostics.EnsureHooked();
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

    void StartGame()
    {
        if (startTransitioning || HasStarted) return;
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
                HasStarted = true;
                runStartTime = Time.time;
                activeRunCharacterId = SelectedCharacterId;
                activeRunStageId = SelectedStageId;
                ApplyCharacterBaseStats(CharacterDatabase.FindById(activeRunCharacterId));
                ApplyCharacterCardEffects();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayGameplayBgm();
                startTransitioning = false;
            });
            return;
        }
        StartCoroutine(StartGameTransition());
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

        HasStarted = true;
        runStartTime = Time.time;
        activeRunCharacterId = SelectedCharacterId;
        activeRunStageId = SelectedStageId;
        ApplyCharacterBaseStats(CharacterDatabase.FindById(activeRunCharacterId));
        ApplyCharacterCardEffects();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayGameplayBgm();

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

    Rect GetBestPanelRect() => new Rect(SafeLeft() + UiMargin, SafeTop() + UiMargin, 168f, HudPanelHeight);
    Rect GetDistancePanelRect() => new Rect(SafeLeft() + UiMargin, GetBestPanelRect().yMax + HudPanelGap, 168f, HudPanelHeight);
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

    void ToggleInvincible()
    {
        InvincibleMode = !InvincibleMode;
        PlayerPrefs.SetInt(InvincibleKey, InvincibleMode ? 1 : 0);
        PlayerPrefs.Save();
    }

    void ToggleDebugMode()
    {
        DebugMode = !DebugMode;
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

    // Called once, right when BossManager locks the Gate (immediately
    // after ClampMaxDistanceTo) - captures "what raw distance corresponds
    // to the moment Distance froze", the reference point EndBossDistance
    // Exclusion needs to compute how far the Player travelled during the
    // fight.
    public void BeginBossDistanceExclusion()
    {
        bossPhaseEntryRawDistance = lastRawDistanceSeen;
    }

    // Called once, right where GameManager already calls BossManager.
    // EndBossPhase() (Boss Reward completion) - folds the whole fight's
    // raw movement into the permanent exclusion offset.
    public void EndBossDistanceExclusion()
    {
        distanceExclusionOffset += (lastRawDistanceSeen - bossPhaseEntryRawDistance);
    }

    public void ReportDistance(float rawDistance)
    {
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
    }

    // Accumulates EXP and rolls over into as many level-ups as it covers
    // (in case a big lump sum, e.g. a boss kill, crosses more than one
    // threshold at once). Suspended while a level-up choice is already
    // pending - the game is paused then anyway, so nothing would visibly
    // change, and it avoids queuing up a second choice before the first is
    // resolved.
    void GainExp(float amount)
    {
        if (amount <= 0f || levelUpPending) return;

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
        Time.timeScale = 1f;
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
        Time.timeScale = 0f;

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
        Time.timeScale = 0f;

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
            Time.timeScale = 1f;
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
        if (IsGameOver) return DamageResult.Ignored;
        if (PresentationDamageLock) return DamageResult.Ignored;
        if (!bypassInvincibleMode && InvincibleMode) return DamageResult.Ignored;
        if (PlayerController.Instance != null && PlayerController.Instance.TryConsumeShield()) return DamageResult.Ignored;

        Lives = Mathf.Max(0, Lives - 1);
        heartDamageFlashTimer = heartDamageFlashDuration;
        if (Lives <= 0)
        {
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
            RunCheckpoint.Clear();
            FinishRun();
            return DamageResult.GameOver;
        }
        // Item 11 - HP is the single most important piece of "強制終了に
        // よる逃げ対策" state; saved the instant it actually changes; not
        // gated on !IsGameOver since a fatal hit already returned above.
        SaveInterruptState();
        return DamageResult.Hit;
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

    void AddLife(int amount = 1)
    {
        Lives = Mathf.Min(maxLives, Lives + amount);
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
        // somehow ended while a level-up pause was still active, this
        // guarantees the next run doesn't start frozen.
        Time.timeScale = 1f;
        levelUpPending = false;
        pendingChoices = null;
        lastLevelUpDiagnostic = ""; // Bugfix 2026-09-08 - see UpdatePendingChoiceWatchdog's matching comment
        // Time.time itself doesn't advance while paused for a level-up
        // choice (Time.timeScale = 0), so this naturally excludes any time
        // spent on those pauses from the recorded run time.
        RunTime = Time.time - runStartTime;

        IsNewBestDistance = MaxDistance > BestDistance;
        if (IsNewBestDistance)
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
        RunCheckpoint.Clear();
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
        Time.timeScale = 1f; // defensive - same reasoning as FinishRun's own reset, in case this is ever reached while still paused
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
        if (HasStarted && DebugMode) BossDiagnostics.DrawDebugPanel();
        // Bugfix 2026-09-08 - マスターから「Snapshotファイルはどこにある
        // か」との質問。logcat/adbを前提にせず、フリーズ/例外検知時に自動
        // で画面上に直接テキスト表示する(手動Dumpボタンでも同様) - スク
        // リーンショットを撮るだけで内容を保存・共有できる。
        if (HasStarted && DebugMode) BossDiagnostics.DrawSnapshotOverlayIfAny();

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
            DrawStatPanel(GetBestPanelRect(), "BEST", FormatDistance(BestDistance), HudGoldColor);
            DrawStatPanel(GetDistancePanelRect(), "DISTANCE", FormatDistance(MaxDistance), HudValueColor, flashIntensity: DistanceFlashIntensity);

            DrawLevelAndExp();
            DrawHeartsPanel();

            if (DebugMode) DrawDebugSpeedReadout();
            if (DebugMode && Debug.isDebugBuild) DrawDistanceWarpDebugUI();

            DrawUnlockAnnouncement();
            DrawEscapeAvailableBanner();

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
                    Time.timeScale = showPauseMenu ? 0f : 1f;
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

        if ((!HasStarted || IsGameOver) && !AnyOverlayOpen)
        {
            // A small gear icon (bottom-left, per the reference mockup)
            // toggles the whole settings/debug column below instead of it
            // always being on screen - reads as a much simpler title
            // screen at a glance while keeping every toggle (including
            // DEBUG's own, which must stay reachable somehow or it could
            // never be turned back on) exactly as available as before,
            // just one tap further away.
            if (DrawStyledButton(GetGearButtonRect(), "⚙", 26f, primary: showSettingsPanel))
            {
                showSettingsPanel = !showSettingsPanel;
            }

            if (showSettingsPanel) DrawSettingsColumn();
        }

        if (!HasStarted && !AnyOverlayOpen)
        {
            // One-shot intro fade, staggered: logo first, then the room's
            // tap targets shortly after - same two-window timing the old
            // START/DECK buttons used, just applied to the room hotspots
            // instead. Both windows are short (0.5s) and don't block input.
            float logoFadeAlpha = Mathf.Clamp01(titleIntroTimer / 0.5f);
            float roomFadeAlpha = Mathf.Clamp01((titleIntroTimer - 0.25f) / 0.5f);

            if (titleLogo != null)
            {
                // Home Room UI reconstruction pass - new "ONE MORE MILE /
                // To the Next Me" logo, much wider/shorter aspect than the
                // old one; same width-fraction approach, height follows
                // automatically from the new texture's own aspect ratio.
                // Ver.1 finishing pass, item 8 - "少し縮小・上寄せ" (was
                // hiding too much of the door/room below it): 0.6 -> 0.46
                // width fraction, top margin 0.03 -> 0.015 of screen height.
                float logoWidth = Mathf.Min(Screen.width * 0.46f, titleLogo.width);
                float logoHeight = logoWidth * (titleLogo.height / (float)titleLogo.width);
                Rect logoRect = new Rect(Screen.width / 2f - logoWidth / 2f, Screen.height * 0.015f, logoWidth, logoHeight);
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
            if (bgRoomRect.width > 0f)
            {
                Color prevRoom = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, roomFadeAlpha);
                // Every hotspot below skips its own hit-test entirely while
                // the Gacha Result popup is up, so a tap can't be consumed
                // out from under that popup's own OK button (see
                // DrawRoomHotspot's own comment).
                bool roomInteractable = !gachaResultOpen && !showNewRunConfirm;

                // Door (center) - Run Continuation/Checkpoint Ver.1, item
                // 13 - CONTINUE (if an Active Run exists) or a fresh Run,
                // same as the old START button.
                Rect doorRect = FracRect(bgRoomRect, 0.40f, 0.14f, 0.565f, 0.65f);
                if (DrawRoomHotspot(doorRect, ref doorHotspotFlashTimer, roomInteractable) && roomFadeAlpha > 0.99f)
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
                Rect characterRect = FracRect(bgRoomRect, 0.02f, 0.05f, 0.30f, 0.35f);
                DrawCharacterHotspot(characterRect, roomInteractable, roomFadeAlpha);

                // ステージ選択導線追加(2026-09-12) - 参考画像の「中央の床
                // ラグに次の行き先を表示」に相当。Active Runが既にある間は
                // (CONTINUEでしか再開できず、ステージは変更不可のため)
                // 表示自体を出さない - Acceptance Test 7。Door(0.40-0.565
                // /0.14-0.65)・Bed(0.0-0.32/0.52-1.0)・Book(0.78-1.0/
                // 0.78-1.0)のどれとも重ならない、床が見えている中央下部の
                // 領域を使う。
                if (!RunCheckpoint.HasActiveRun)
                {
                    Rect stageRect = FracRect(bgRoomRect, 0.34f, 0.68f, 0.66f, 0.97f);
                    DrawStageHotspot(stageRect, roomInteractable, roomFadeAlpha);
                }

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
                    float machineWidth = bgRoomRect.width * 0.15f;
                    float machineAspect = gachaMachineTexture.height / (float)gachaMachineTexture.width;
                    float machineHeight = machineWidth * machineAspect;
                    float shakeOffset = gachaMachineShakeTimer > 0f
                        ? Mathf.Sin(gachaMachineShakeTimer * 55f) * 4f * (gachaMachineShakeTimer / gachaMachineShakeDuration)
                        : 0f;
                    // Bugfix 2026-09-06 (再調整) - the previous 0.87/0.47
                    // anchor still read as floating in front of the chair
                    // rather than resting on the desk once seen on a real
                    // device (aspect-ratio letterboxing shifts how a fixed
                    // fraction of the source image actually lands on screen
                    // in ways a single Editor-side composite check against
                    // the raw 1536x1024 art can't fully catch). Nudged
                    // further right/down (0.85/0.50) toward the desk's
                    // open surface, and paired with an explicit contact
                    // shadow below (see shadowRect) so it reads as "resting
                    // on something" regardless of the exact pixel alignment.
                    Rect machineRect = new Rect(
                        bgRoomRect.x + bgRoomRect.width * 0.85f - machineWidth / 2f + shakeOffset,
                        bgRoomRect.y + bgRoomRect.height * 0.50f,
                        machineWidth, machineHeight);

                    // Contact shadow - a soft, squashed dark ellipse-ish
                    // patch right at the machine's own base, giving it a
                    // grounded feel independent of exactly how its Rect
                    // lines up with the painted desk beneath it.
                    Color prevShadow = GUI.color;
                    float shadowWidth = machineWidth * 0.75f;
                    float shadowHeight = machineHeight * 0.12f;
                    Rect shadowRect = new Rect(machineRect.x + (machineWidth - shadowWidth) / 2f, machineRect.yMax - shadowHeight * 0.5f, shadowWidth, shadowHeight);
                    GUI.color = new Color(0f, 0f, 0f, 0.35f * roomFadeAlpha);
                    GUI.DrawTexture(shadowRect, Texture2D.whiteTexture);
                    GUI.color = prevShadow;

                    Color prevMachine = GUI.color;
                    float glow = Mathf.Max(deskHotspotFlashTimer / roomHotspotFlashDuration, gachaMachineShakeTimer > 0f ? 0.5f : 0f);
                    // Item 13 - Gacha Visual Evolution: since no dedicated
                    // per-stage art exists yet, a simple tint stands in
                    // (real art can just replace GachaMachineStageTint's
                    // per-case color with Color.white once it exists, no
                    // other code changes needed).
                    Color stageTint = GachaMachineStageTint(CurrentGachaStage);
                    Color baseColor = new Color(stageTint.r, stageTint.g, stageTint.b, roomFadeAlpha);
                    GUI.color = Color.Lerp(baseColor, new Color(1f, 0.92f, 0.6f, roomFadeAlpha), Mathf.Clamp01(glow));
                    GUI.DrawTexture(machineRect, gachaMachineTexture, ScaleMode.ScaleToFit);
                    GUI.color = prevMachine;

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
            Rect titleBestRect = new Rect(SafeLeft() + UiMargin, SafeTop() + UiMargin, 190f, 72f);
            DrawStatPanel(titleBestRect, "BEST", FormatDistance(BestDistance), HudGoldColor, ornate: true);

            Rect titleMileRect = new Rect(Screen.width - SafeRight() - UiMargin - 190f, SafeTop() + UiMargin, 190f, 72f);
            DrawStatPanel(titleMileRect, "MILE", TotalOwnedMile.ToString(), HudGoldColor, ornate: true);

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
            LevelLine = highest > 0 ? $"Lv.{highest}" : ""
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
        string stackLabel = currentStack > 0 ? $"Lv.{currentStack} -> Lv.{currentStack + 1}" : "NEW  Lv.1";
        return new RewardCardData
        {
            CardId = card.cardId,
            Icon = card.icon,
            Title = card.cardName,
            Description = card.description,
            Rarity = card.rarity,
            LevelLine = stackLabel,
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
            ShowEquippedBadge = equipped
        };
    }

    void DrawDebugSpeedReadout()
    {
        bool autoRun = PlayerController.Instance != null && PlayerController.Instance.autoRunEnabled;
        string text = $"P-speed {measuredPlayerSpeed:F2}   AutoRun {(autoRun ? "ON" : "OFF")}";

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
        style.fontSize = 14;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.yellow;

        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect rect = new Rect(SafeLeft() + UiMargin, SafeTop() + UiMargin + 36f, size.x + 10f, size.y + 6f);
        UiBackdrop.Draw(rect, 0.55f);
        GUI.Label(rect, text, style);
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
    void DrawDistanceWarpDebugUI()
    {
        string statusText = $"Distance: {Mathf.FloorToInt(MaxDistance)}"
            + (DistanceTierManager.Instance != null ? $"\nEnemyHP: {DistanceTierManager.Instance.CurrentEnemyHp}   Tier: {(DistanceTierManager.Instance.CurrentTier != null ? DistanceTierManager.Instance.CurrentTier.tierName : "-")}" : "")
            + (WorldTimeCycle.Instance != null ? $"\nTime: {WorldTimeCycle.Instance.CurrentTimeName}" : "")
            + (BossManager.Instance != null ? $"\nBossPhase: {(BossManager.Instance.IsBossPhase ? "ON" : "off")}   NextBoss: {Mathf.FloorToInt(BossManager.Instance.NextBossDistance)}" : "")
            // Reward/Card Ownership/Gacha/Fusion System Ver.1, item 16.
            + $"\nMILE: {TotalOwnedMile}   OwnedCardStacks: {CardInventory.Stacks.Count}";

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = 14;
        statusStyle.alignment = TextAnchor.UpperLeft;
        statusStyle.normal.textColor = new Color(0.6f, 1f, 0.7f);
        Vector2 statusSize = statusStyle.CalcSize(new GUIContent(statusText));
        Rect statusRect = new Rect(SafeLeft() + UiMargin, SafeTop() + UiMargin + 100f, statusSize.x + 10f, statusSize.y + 6f);
        UiBackdrop.Draw(statusRect, 0.55f);
        GUI.Label(statusRect, statusText, statusStyle);

        float[] stops = DistanceTierManager.DebugWarpStops;
        float bw = 62f, bh = 26f, gap = 4f;
        for (int i = 0; i < stops.Length; i++)
        {
            Rect r = new Rect(SafeLeft() + UiMargin + i * (bw + gap), statusRect.yMax + 6f, bw, bh);
            string label = stops[i] >= 1000f ? $"{stops[i] / 1000f:0.#}K" : $"{stops[i]:0}";
            if (DrawStyledButton(r, label, 12f, primary: false))
            {
                DebugWarpToDistance(stops[i]);
            }
        }

        // Reward/Card Ownership/Gacha/Fusion System Ver.1, item 16 - Dev
        // Build-only debug tools for repeatedly testing MILE/Gacha/Fusion/
        // Convert without needing to actually grind runs. Same
        // DebugMode+Debug.isDebugBuild gate as the row above (this whole
        // method is already only called under that condition).
        float debugRowY = statusRect.yMax + 6f + bh + 6f;
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
            Rect r = new Rect(SafeLeft() + UiMargin + i * (bw + gap), debugRowY, bw, bh);
            if (DrawStyledButton(r, mileButtons[i].label, 10f, primary: false))
            {
                mileButtons[i].action();
            }
        }
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
        if (PlayerController.Instance != null)
        {
            Vector3 p = PlayerController.Instance.transform.position;
            p.x = targetDistance;
            PlayerController.Instance.transform.position = p;
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

    // キャラクター選択画面(2026-09-12) - 他の4つのホットスポット(Door/
    // Bed/Book/Gacha機)と違い、対応する物が室内アートに一切描かれていない
    // ため、DrawRoomHotspotのような完全に透明な当たり判定だけでは「ここが
    // 押せる」ことが伝わらない。マスター指示の「CHARACTER・キャラクター
    // アイコン・軽い金色発光・タップ可能だと分かる表示」どおり、簡単な
    // パネル+選択中キャラクターのポートレート+ラベル+常時のゆるい金色
    // パルスを明示的に描画する。
    void DrawCharacterHotspot(Rect rect, bool roomInteractable, float roomFadeAlpha)
    {
        OrnateUi.DrawPanel(rect, 0.85f);

        // 常時のゆっくりした金色パルス - 「タップ可能だと分かる表示」。
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
        Color prevGlow = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.4f, (0.10f + 0.10f * pulse) * roomFadeAlpha);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prevGlow;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 18;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.UpperCenter;
        labelStyle.normal.textColor = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, roomFadeAlpha);
        GUI.Label(new Rect(rect.x, rect.y + 6f, rect.width, 24f), "CHARACTER", labelStyle);

        CharacterDefinition selectedDef = CharacterDatabase.FindById(SelectedCharacterId);
        Texture2D portrait = selectedDef != null ? selectedDef.portrait : null;
        if (portrait != null)
        {
            float availableW = rect.width - 24f;
            float availableH = rect.height - 40f;
            float portraitAspect = portrait.height / (float)portrait.width;
            float iconW = availableW;
            float iconH = iconW * portraitAspect;
            if (iconH > availableH)
            {
                iconH = availableH;
                iconW = iconH / portraitAspect;
            }
            Rect iconRect = new Rect(rect.x + (rect.width - iconW) / 2f, rect.y + 32f, iconW, iconH);
            Color prevIcon = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, roomFadeAlpha);
            GUI.DrawTexture(iconRect, portrait, ScaleMode.ScaleToFit);
            GUI.color = prevIcon;
        }

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
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prevFlash;
        }

        if (tapped && roomFadeAlpha > 0.99f)
        {
            OpenCharacterSelect();
        }
    }

    // ステージ選択導線追加(2026-09-12) - マスター提供の参考画像「中央の
    // 床ラグに次の行き先(NEXT STAGE)を表示」に相当。DrawCharacterHotspot
    // と同じ「対応する物が室内アートに描かれていないため、パネル+ラベル+
    // 淡い金色パルスを明示的に描画する」パターン。タップでStage Selectを
    // 開くだけで、Run開始そのものはDoorホットスポット(OnDoorTapped)が
    // 引き続き担う。
    void DrawStageHotspot(Rect rect, bool roomInteractable, float roomFadeAlpha)
    {
        OrnateUi.DrawPanel(rect, 0.85f);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
        Color prevGlow = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.4f, (0.10f + 0.10f * pulse) * roomFadeAlpha);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prevGlow;

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.alignment = TextAnchor.UpperCenter;
        headerStyle.normal.textColor = new Color(HudGoldColor.r, HudGoldColor.g, HudGoldColor.b, roomFadeAlpha);
        GUI.Label(new Rect(rect.x, rect.y + 6f, rect.width, 20f), "NEXT STAGE", headerStyle);

        StageDefinition selectedDef = StageDatabase.FindById(SelectedStageId);

        GUIStyle nameStyle = new GUIStyle(GUI.skin.label);
        nameStyle.fontSize = 22;
        nameStyle.fontStyle = FontStyle.Bold;
        nameStyle.alignment = TextAnchor.MiddleCenter;
        nameStyle.normal.textColor = new Color(1f, 1f, 1f, roomFadeAlpha);
        GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.32f, rect.width, 32f),
            selectedDef != null ? selectedDef.displayName : "-", nameStyle);

        GUIStyle routeStyle = new GUIStyle(GUI.skin.label);
        routeStyle.fontSize = 13;
        routeStyle.alignment = TextAnchor.MiddleCenter;
        routeStyle.normal.textColor = new Color(0.8f, 0.85f, 0.95f, roomFadeAlpha);
        GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.62f, rect.width, 24f),
            selectedDef != null ? selectedDef.routeText : "", routeStyle);

        bool tapped = roomInteractable && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        if (tapped)
        {
            stageHotspotFlashTimer = roomHotspotFlashDuration;
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx(roomTapSe);
        }

        if (stageHotspotFlashTimer > 0f)
        {
            float f = stageHotspotFlashTimer / roomHotspotFlashDuration;
            Color prevFlash = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.75f, f * 0.35f * roomFadeAlpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prevFlash;
        }

        if (tapped && roomFadeAlpha > 0.99f)
        {
            OpenStageSelect();
        }
    }

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

    // Item 13 - the door's tap action, split between CONTINUE (an Active
    // Run already exists) and a fresh Run.
    void OnDoorTapped()
    {
        if (RunCheckpoint.HasActiveRun)
        {
            ContinueActiveRun();
        }
        else
        {
            StartGame();
            startPressFlashTimer = startPressFlashDuration;
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
            StartGame();
            startPressFlashTimer = startPressFlashDuration;
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
            Time.timeScale = 1f;
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
            Time.timeScale = 1f;
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
