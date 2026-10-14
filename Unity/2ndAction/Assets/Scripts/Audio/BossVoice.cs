using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ボスの声(2026-10-14、マスターの依頼「ボスの鳴き声・咆哮の追加」)。
//  ・素材は種類ごとに共通(獣 / 竜 / 昆虫 / ゴーレム / 人型 / 死神 / 機械)× 場面(咆哮 / うなり / 断末魔)。
//    ボスごとの違いは Pitch と音量で出す(下の表)。音は Tools/audio/gen_audio_v3.py で合成した自作の音(外部の素材なし)。
//  ・鳴らす場面(多すぎないように):
//      咆哮   … 登場して画面に入った時に1回(ボスごと)。直前に遭遇の重低音(BossWarning)が鳴っていたら、聞き分けられるよう少し待つ
//      うなり … 段階が上がった時 / 必殺技の始まり(同じボスは 6秒に1回まで)
//      断末魔 … 撃破(BOSS FINISH の始まり。最後の一撃の音の少し後)
//  ・再生は AudioManager の SE と同じ道(SE の音量・消音・同時に鳴る数の上限・優先度に従う)。マルチは各端末で自分で鳴らす
//    (登場の咆哮は JOIN でも鳴らす。段階/必殺技は HOST の判断なので HOST/ソロだけ。撃破は全員)。
//  ・ポーズ / ゲームオーバー / ホームへ戻る時は止める(StopAll)。戦闘の判定・ボスの AI・HitStop・FINISH の処理は変えない(音を足すだけ)。
//  ・すぐ戻す: Enabled = false で声を全部止められる(重低音とジャンプは AudioLibrary の割り当てを元のファイルへ)。
public static class BossVoice
{
    public enum Kind { Beast, Dragon, Insect, Golem, Humanoid, Reaper, Mech }
    public enum Moment { Roar, Growl, Death }
    public static bool Enabled = true;

    public struct Profile { public Kind kind; public float pitch, vol; public Profile(Kind k, float p, float v = 1f) { kind = k; pitch = p; vol = v; } }

    // ボスの名前(DeathKey: "Wild/X" / "Cave/X" / "Sky/X" / "Dragon" / "Majin" / "Reaper")→ 種類 + 高さ + 大きさ
    public static readonly Dictionary<string, Profile> Table = new Dictionary<string, Profile>
    {
        // 荒野街道
        { "Wild/Wolf", new Profile(Kind.Beast, 1.08f) },          // 巨大オオカミ
        { "Wild/GoblinRider", new Profile(Kind.Beast, 1.2f, 0.85f) }, // ゴブリンとオオカミ
        { "Wild/Serpent", new Profile(Kind.Dragon, 1.3f, 0.9f) }, // 大蛇
        { "Wild/Cyclops", new Profile(Kind.Humanoid, 0.78f) },    // 巨人
        { "Wild/Spider", new Profile(Kind.Insect, 1.0f) },        // 大蜘蛛
        { "Wild/Golem", new Profile(Kind.Golem, 1.0f) },
        { "Wild/Griffin", new Profile(Kind.Beast, 1.38f, 0.9f) }, // 鳥と獣の甲高い声
        { "Wild/Hydra", new Profile(Kind.Dragon, 1.1f) },
        { "Wild/Demon", new Profile(Kind.Humanoid, 0.9f) },
        { "Wild/Dragon", new Profile(Kind.Dragon, 1.0f) },
        { "Wild/BlackKnight", new Profile(Kind.Humanoid, 1.05f, 0.9f) },
        // 自然洞窟
        { "Cave/Centipede", new Profile(Kind.Insect, 1.12f) },
        { "Cave/Scorpion", new Profile(Kind.Insect, 0.95f) },
        { "Cave/Mole", new Profile(Kind.Beast, 0.85f) },
        { "Cave/Troll", new Profile(Kind.Humanoid, 0.74f) },
        { "Cave/Worm", new Profile(Kind.Insect, 0.68f) },         // 異質な鳴き声(低く)
        { "Cave/CrystalGolem", new Profile(Kind.Golem, 1.15f) },
        { "Cave/Bat", new Profile(Kind.Beast, 1.5f, 0.85f) },
        { "Cave/ScorpionKing", new Profile(Kind.Insect, 0.8f) },
        { "Cave/Basilisk", new Profile(Kind.Dragon, 1.2f) },
        { "Cave/Drake", new Profile(Kind.Dragon, 0.92f) },        // 地底竜
        { "Cave/AncientDemon", new Profile(Kind.Humanoid, 0.8f) },
        // 天空回廊
        { "Dragon", new Profile(Kind.Dragon, 0.96f) },            // 竜(天空 1km / 荒野 80km)
        { "Majin", new Profile(Kind.Humanoid, 0.95f) },           // 魔人
        { "Sky/Behemoth", new Profile(Kind.Beast, 0.75f) },
        { "Sky/Titan", new Profile(Kind.Humanoid, 0.7f) },
        { "Sky/Jellyfish", new Profile(Kind.Reaper, 1.3f, 0.8f) }, // 不思議な共鳴
        { "Sky/Leviathan", new Profile(Kind.Dragon, 0.8f) },
        { "Sky/Fenrir", new Profile(Kind.Beast, 0.9f) },
        { "Sky/SkyGolem", new Profile(Kind.Golem, 0.9f) },
        { "Sky/Phoenix", new Profile(Kind.Beast, 1.45f, 0.9f) },
        { "Sky/SkySerpent", new Profile(Kind.Dragon, 1.05f) },
        { "Sky/Guardian", new Profile(Kind.Humanoid, 1.15f) },
        { "Sky/MechDragon", new Profile(Kind.Mech, 1.0f) },       // メカドラゴン(ラスダンのまれなボス)
        // 死神(100,000m の追跡 / ラスダンの三姉妹)
        { "Reaper", new Profile(Kind.Reaper, 1.0f) },
    };

    public static bool TryProfile(string key, out Profile p)
    {
        if (!string.IsNullOrEmpty(key) && Table.TryGetValue(key, out p)) return true;
        // 表に無い名前は "Wild/Wolf" → "Wolf" の部分で探す(ラスダンの借りたボスなど)
        if (!string.IsNullOrEmpty(key))
        {
            string tail = key.Substring(key.LastIndexOf('/') + 1);
            foreach (var kv in Table) if (kv.Key.EndsWith("/" + tail) || kv.Key == tail) { p = kv.Value; return true; }
        }
        p = default; return false;
    }

    public static SeId IdFor(Kind k, Moment m)
    {
        int b = m == Moment.Roar ? (int)SeId.BossRoarBeast : m == Moment.Growl ? (int)SeId.BossGrowlBeast : (int)SeId.BossDeathBeast;
        return (SeId)(b + (int)k);
    }

    // ---- 回数の制限 ----
    static readonly HashSet<Object> roared = new HashSet<Object>();
    static readonly Dictionary<Object, float> lastGrowl = new Dictionary<Object, float>();
    public const float GrowlCooldown = 6f, AfterBoomSeconds = 1.3f;
    public static int Played { get; private set; }            // 確認用
    public static string LastKey { get; private set; } = "";  // 確認用
    public static SeId LastId { get; private set; }

    // 登場して画面に入った時(ボスごとに1回)
    public static void Arrive(Object boss, string key) => ArriveAfter(boss, key, 0f);
    public static void ArriveAfter(Object boss, string key, float delay)
    {
        if (!Enabled || AudioManager.Instance == null) return;
        if (boss != null && !roared.Add(boss)) return;
        // 遭遇の重低音(BossWarning)の直後なら、少し待ってから(重なって聞き取りにくくならないように)
        float since = AudioManager.Instance.SinceLastPlayed(SeId.BossWarning);
        float wait = Mathf.Max(delay, AfterBoomSeconds - since);
        if (wait > 0.01f) AudioManager.Instance.StartCoroutine(Later(boss, key, Moment.Roar, wait));
        else Play(key, Moment.Roar);
    }

    // 段階が上がった / 必殺技の始まり(同じボスは GrowlCooldown 秒に1回)
    public static void Growl(Object boss, string key)
    {
        if (!Enabled || AudioManager.Instance == null) return;
        float now = Time.unscaledTime;
        if (boss != null && lastGrowl.TryGetValue(boss, out float t) && now - t < GrowlCooldown) return;
        if (boss != null) lastGrowl[boss] = now;
        Play(key, Moment.Growl);
    }

    // 撃破(BOSS FINISH の始まり)。そのボスの咆哮/うなりは止めて、最後の一撃の音の少し後に断末魔
    public static void Death(Object boss, string key)
    {
        if (!Enabled || AudioManager.Instance == null) return;
        if (TryProfile(key, out var p)) { AudioManager.Instance.StopSe(IdFor(p.kind, Moment.Roar)); AudioManager.Instance.StopSe(IdFor(p.kind, Moment.Growl)); }
        AudioManager.Instance.StartCoroutine(Later(null, key, Moment.Death, 0.22f));
        if (boss != null) lastGrowl.Remove(boss);
    }

    static IEnumerator Later(Object boss, string key, Moment m, float wait)
    {
        float w = 0f;
        while (w < wait) { yield return null; w += Time.unscaledDeltaTime; if (Blocked) yield break; }
        if (m != Moment.Death && boss == null && !ReferenceEquals(boss, null)) yield break; // 待つ間に消えたボス
        Play(key, m);
    }

    // ポーズ / ゲームオーバーの間は新しく鳴らさない
    static bool Blocked { get { var gm = GameManager.Instance; return gm == null || gm.IsGameOver || gm.PauseMenuOpen; } }

    public static bool Play(string key, Moment m)
    {
        if (!Enabled || AudioManager.Instance == null || Blocked && m != Moment.Death) return false;
        if (!TryProfile(key, out var p)) { Debug.Log($"[BossVoice] no voice for '{key}'"); return false; }
        var sid = IdFor(p.kind, m);
        bool ok = AudioManager.Instance.PlaySePitch(sid, p.pitch, p.vol);
        if (ok) { Played++; LastKey = key; LastId = sid; }
        return ok;
    }

    // ポーズ / ゲームオーバー / ホームへ戻る: 鳴っている声を止める
    public static void StopAll()
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        for (int k = 0; k <= (int)Kind.Mech; k++)
            foreach (Moment m in System.Enum.GetValues(typeof(Moment))) am.StopSe(IdFor((Kind)k, m));
    }

    public static void ResetRun() { roared.Clear(); lastGrowl.Clear(); }
}
