#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;

// 2026-11-02 動作のつなぎ(MotionLink)の確認: -qaMotion <dir> [-mtChar swordsman] [-mtStage wasteland_road] [-mtAt 1400]
//   [-mtSeq "F,0.9,F,0.25,F,0.42,F,1.3,U,0.6,D,1.5,B,1.2"](入力 と 待ち秒 を交互。F/B/U/D = フリックの向き)
//   [-mtLinks 0](つなぎを止めて前の見た目を撮る)[-mtMobs goblin](0.9秒ごとに目の前へ雑魚)[-mtRec 1](毎フレームの画像を書く)
// 30fps で進め、毎フレーム「状態/つなぎのキー/絵の名前/つなぎ(lead/settle)/プレイヤーの画面の位置と高さ」を motion.tsv に書く
public partial class QaSweep
{
    IEnumerator MotionMode()
    {
        Application.targetFrameRate = 60;
        Random.InitState(20261102);
        string ch = Arg("-mtChar", "swordsman");
        yield return BeginRun(ch, Arg("-mtStage", "wasteland_road"));
        SetGod(true);
        if (HighSpeedAssist.Instance != null) HighSpeedAssist.Instance.SetEnabled(false);
        bool links = Arg("-mtLinks", "1") == "1";
        var anim = pc.GetComponent<PlayerAnimator>();
        if (!links) anim.Motion.SetLinks(null);
        PlayerAnimator.QaLegacyMotion = !links; EnemyAnimator.QaLegacyMotion = !links;
        L($"[motion] char={ch} links={(anim.Motion.Links != null ? anim.Motion.Links.Length : 0)}");
        // 平らで穴の無い所(雑魚は自分で出す)。-mtFlat 0 で普通の地形
        var tm = TerrainManager.Instance;
        bool flat = Arg("-mtFlat", "1") == "1";
        if (flat && tm != null)
        {
            tm.enemySpawnChance = 0f; tm.pitChanceBase = 0f; tm.pitChanceMax = 0f;
            tm.DebugSetRouteBranch(false);
            if (EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = false;
            yield return new WaitForSecondsRealtime(0.5f);
            tm.RequestFlatRun(40);
        }
        WarpTo(float.Parse(Arg("-mtAt", "1400"), CultureInfo.InvariantCulture));
        yield return new WaitForSeconds(1.5f);
        if (flat && tm != null)
        {
            // 闘技場と同じ: これから作る地面を全部平地にして、その先の新しい地面へ移す(段差・穴・飾りなし)
            tm.ConfigureArena();
            float nx = tm.NextGenerateX + 4f;
            tm.GenerateNow(nx + 900f);
            HaPlace(new Vector3(nx + 2f, 0f, 0f));
            yield return new WaitForSeconds(1.0f);
        }
        float mobDist = float.Parse(Arg("-mtMobDist", "6"), CultureInfo.InvariantCulture);
        int mobHp = int.Parse(Arg("-mtMobHp", "100000"));
        // -mtCards attack_speed_up:9,... = カードを持たせる(ATTACK SPEED UP 取得後の連撃の確認)
        foreach (var part in Arg("-mtCards", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':'); var c = CardDatabase.FindById(kv[0]);
            if (c == null) { L($"[motion] unknown card {kv[0]}"); continue; }
            gm.ApplyCardEffectsStacked(c, kv.Length > 1 ? int.Parse(kv[1]) : 1);
            L($"[motion] card {kv[0]} lv{(kv.Length > 1 ? kv[1] : "1")} attackSpeed={pc.AttackSpeedMultiplier:F2}");
        }
        bool stand = Arg("-mtStand", "0") == "1"; // その場で立って(走らずに)敵の動きを見る
        if (stand) PlayerController.DebugRunOnlyScale = 0f;
        bool rec = Arg("-mtRec", "1") == "1";
        string frames = System.IO.Path.Combine(outDir, "frames");
        if (rec) System.IO.Directory.CreateDirectory(frames);
        // 2026-11-03: 絵の大きさの確認用に tex(絵の元画像)/ppu/表示の拡大(lossyScale)/カメラの写す高さ(orthographicSize)も書く
        var tsv = new StringBuilder("frame\tt\tstate\tkey\tsprite\tlink\tinput\tsx\tsy\thpx\tenemy\tekey\tex\tey\ttex\tppu\tlsx\tlsy\tortho\n");
        string[] mobs = Arg("-mtMobs", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        var seq = Arg("-mtSeq", "F,0.9,F,0.25,F,0.42,F,1.3,U,0.6,D,1.5,B,1.2,F,1.0").Split(',');
        Time.captureFramerate = 30;
        int f = 0; float t = 0f; int si = 0; float wait = 0.6f; float mobT = 0.3f; int mi = 0; float reviveAt = -1f;
        int startShown = MotionLinkPlayer.Shown;
        var sr = anim.VisualRenderer;
        while (si < seq.Length || wait > 0f)
        {
            string input = "";
            wait -= 1f / 30f;
            if (wait <= 0f && si < seq.Length)
            {
                input = seq[si++];
                if (input == "H") // 2026-11-03: 被弾(敵の攻撃と同じ入口)→ 復帰。HP は戻す
                {
                    // 無敵の設定を通さずに HP の処理 → 被弾のリアクション(PlayerController.TakeDamage の後半と同じ)
                    var res = gm.TryDamagePlayer(true, "QaMotionHurt");
                    if (res == GameManager.DamageResult.Hit) typeof(PlayerController).GetMethod("ApplyDamageReaction", NP).Invoke(pc, new object[] { false, CombatScale.PlayerHit, false });
                    typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { gm.maxLives });
                    L($"[motion] hurt at frame {f}: {res}");
                }
                else if (input == "K") // 2026-11-03: HP が尽きて倒れる(最後に置く。GAME OVER の倒れる絵を撮る)
                {
                    typeof(GameManager).GetProperty("Lives").GetSetMethod(true).Invoke(gm, new object[] { 1 });
                    var res = gm.TryDamagePlayer(true, "QaMotionDeath");
                    L($"[motion] death at frame {f}: {res} over={gm.IsGameOver}");
                }
                else if (input == "R") // 2026-11-03: 協力プレイの DOWN → 1.5秒後に復活(NetEnterDown/NetRevive = CPU の仲間・マルチと同じ処理)
                {
                    pc.NetEnterDown(false); reviveAt = t + 1.5f;
                    L($"[motion] down at frame {f}");
                }
                else if (input != "W") // W = 何もしない(走りだけを撮る)
                pc.debugInjectOnce = input == "U" ? PlayerController.FlickDirection.Up : input == "D" ? PlayerController.FlickDirection.Down : input == "B" ? PlayerController.FlickDirection.Backward : PlayerController.FlickDirection.Forward;
                wait = si < seq.Length ? float.Parse(seq[si++], CultureInfo.InvariantCulture) : 0.8f;
            }
            if (reviveAt > 0f && t >= reviveAt) { reviveAt = -1f; pc.NetRevive(); L($"[motion] revive at frame {f}"); }
            if (mobs.Length > 0 && (mobT -= 1f / 30f) <= 0f)
            {
                mobT = float.Parse(Arg("-mtMobEvery", "1.4"), CultureInfo.InvariantCulture);
                var def = EnemyDatabase.FindById(mobs[mi++ % mobs.Length]);
                if (def != null && tm != null)
                {
                    float wx = pc.transform.position.x + mobDist;
                    for (int k = 0; k < 20 && !tm.GetHeightAt(wx).HasValue; k++) wx += 0.5f;
                    var go = tm.SpawnEncounterEnemy(def, new Vector2(wx, tm.GetHeightAt(wx) ?? pc.transform.position.y), def.aiTier, def.behaviorKind);
                    var en = go != null ? go.GetComponent<EnemyController>() : null;
                    if (en != null) { HaEnsureHp.Invoke(en, null); HaEnemyHp.SetValue(en, mobHp); }
                }
            }
            yield return new WaitForEndOfFrame();
            if (rec)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // Android は CaptureScreenshot の絶対パスが書かれない → 画面を絵にして自分で書く(-mtRecEvery n で n フレームごと)
                if (f % Mathf.Max(1, int.Parse(Arg("-mtRecEvery", "1"))) == 0)
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(frames, $"f{f:00000}.jpg"), tex.EncodeToJPG(80));
                    Destroy(tex);
                }
#else
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(frames, $"f{f:00000}.png"));
#endif
            }
            var cam = Camera.main;
            Vector3 sp = cam != null && sr != null ? cam.WorldToScreenPoint(sr.bounds.center) : Vector3.zero;
            float hpx = cam != null ? Mathf.Abs(cam.WorldToScreenPoint(pc.transform.position + Vector3.up * 1.2f).y - cam.WorldToScreenPoint(pc.transform.position).y) : 0f;
            var m = anim.Motion;
            // 一番近い敵(前方20m以内)の絵・つなぎのキー・画面の位置
            EnemyAnimator near = null; float best = 20f;
            foreach (var ea in FindObjectsByType<EnemyAnimator>(FindObjectsSortMode.None))
            {
                float d = ea.transform.position.x - pc.transform.position.x;
                if (Mathf.Abs(d) < best && ea.isActiveAndEnabled) { best = Mathf.Abs(d); near = ea; }
            }
            var esr = near != null ? near.GetComponentInChildren<SpriteRenderer>() : null;
            Vector3 ep = cam != null && esr != null ? cam.WorldToScreenPoint(esr.bounds.center) : Vector3.zero;
            tsv.Append($"{f}\t{t:F3}\t{anim.CurrentState}\t{anim.MotionKey}\t{(sr != null && sr.sprite != null ? sr.sprite.name : "-")}\t{(m.Active != null ? (m.Settling ? "settle" : "lead") : "")}\t{input}\t{sp.x:F0}\t{Screen.height - sp.y:F0}\t{hpx:F0}" +
                       $"\t{(esr != null && esr.sprite != null ? esr.sprite.name : "-")}\t{(near != null ? near.MotionKey : "")}\t{ep.x:F0}\t{Screen.height - ep.y:F0}" +
                       $"\t{(sr != null && sr.sprite != null ? sr.sprite.texture.name + "|" + sr.sprite.texture.width + "x" + sr.sprite.texture.height : "-")}\t{(sr != null && sr.sprite != null ? sr.sprite.pixelsPerUnit : 0f):F2}" +
                       $"\t{(sr != null ? sr.transform.lossyScale.x : 0f):F4}\t{(sr != null ? sr.transform.lossyScale.y : 0f):F4}\t{(cam != null ? cam.orthographicSize : 0f):F3}\n");
            f++; t += 1f / 30f;
            yield return null;
        }
        Time.captureFramerate = 0;
        PlayerController.DebugRunOnlyScale = 1f;
        PlayerAnimator.QaLegacyMotion = false; EnemyAnimator.QaLegacyMotion = false;
        if (flat && EncounterDirector.Instance != null) EncounterDirector.Instance.enabled = true;
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "motion.tsv"), tsv.ToString());
        L($"[motion] frames={f} linksShown={MotionLinkPlayer.Shown - startShown} last={MotionLinkPlayer.LastShown}");
        Check(f > 30, "motion: frames written");
        yield return EndRun();
    }
}
#endif
