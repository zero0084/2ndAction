/*
 * One More Mile - ランキングへの投稿(Cloud Code, JavaScript)。2026-10-08
 * 端末から届いた値を検査してから、Leaderboards へ「自己ベストより長い時だけ」書く。
 * 検査の規則は Unity 側の LeaderboardRules(Assets/Scripts/Online/Leaderboard.cs)と同じ。
 * 2026-10-14: ソロ専用(CPU サポート/マルチのランは端末が送らず、届いても "solo only" で拒否する。サポート用の表/スクリプトは作らない)。
 * 2026-10-20: 引数は JSON の run 1つ(Cloud Code の引数の宣言は 10 個まで)。端末も同じ版でそろえること(UgsLeaderboardBackend.SubmitArgs)。
 * 2026-10-14: 環境の分離。production の環境では、製品版(buildKind = "release")が production で作った記録(env = "production")だけを書く。
 *   開発版/古い版(buildKind が無い)からの送信は "dev build" で拒否する。development 等の環境では開発版の送信を受け付ける。
 *   context.environmentName が渡らない場合は環境の確認を省く(ログに残す)。※ 本物のサービスでは未確認。
 *
 * 前提(Dashboard):
 *  - Leaderboards: omm_best_wasteland_road / omm_best_natural_cave / omm_best_sky_corridor / omm_best_last_corridor
 *    並び順=高い順(降順)、更新の種類="Keep latest score"(自己ベストの判定はこのスクリプトが行う。表示名の更新で書き直すため)
 *  - Access Control: プレイヤーが Leaderboards へ直接書くのを禁止(Tools/ugs/access/omm_leaderboards.ac)
 *  - Cloud Save: 送信済みの runId を自分のプレイヤーデータ "omm_runs" に保存する
 * 2026-10-20: development に配置して本物で確認(Docs/UgsDevelopment_2026-10-20)。
 * 2026-11-25(セキュリティ監査 B 案): ラン開始の受付(omm_start_run が Protected の Cloud Save "omm_tickets" に控えた ticket)が必須。
 *   受付があること・未使用・マップ/キャラが同じ・サーバーが測った経過時間で走れる距離(受付の前に走った分の余裕つき)を確かめ、
 *   結果が決まったら受付を消す(同じ受付で二度載せない)。受付の無い記録(オフラインで始めた/古い版)は "no ticket" で拒否
 */
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const STAGES = ["wasteland_road", "natural_cave", "sky_corridor", "last_corridor"];
const CHARACTERS = ["swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer", "archer", "mage", "fighter", "ninja", "miko", "vampire", "dragonkin", "luka", "pentatuttulu"]; // 2026-10-27: 13・14人目(旧版の12人の記録もそのまま受け付ける)
const MAX_METERS = 2000000;
const MAX_SPEED_MPS = 300 / 3.6;   // ×SPEED_SLACK = 360km/h(端末の LeaderboardRules.MaxSpeedMps と同じ)。正当な最大 ≒ 330km/h(下の SERVER_SPEED_MPS の説明)
const SPEED_SLACK = 1.2, DISTANCE_SLACK = 300;
const RECENT_RUNS = 200;
const DEVICE_CLOCK_SLACK = 2 * 86400; // 端末の時計(開始/確定の時刻)のずれの許容。端末の LeaderboardRules.DeviceClockSlack と同じ
const TICKET_KEY = "omm_tickets", TICKET_DAYS = 90;
const SERVER_SPEED_MPS = 360 / 3.6;   // サーバーが測った経過時間に対する平均の速さの上限。2026-11-25: 160 では最大強化の正当な走りを拒否しうるため変更。
//  自然 100km/h × 設定 120% × キャラ 1.15 × 速さのカード ×1.45 ≒ 200km/h、80km から FINAL DRIVE +20%、DIVE BOOST +26%、ULTIMATE ×1.12 が重なると ≒ 330km/h
const PRE_TICKET_METERS = 1000;       // 受付が取れる前(ラン開始の直後の通信待ち)に走ってよい距離
const NAME_MAX = 16;

function sanitizeName(raw) {
  if (typeof raw !== "string") return "";
  let out = "", space = false;
  for (const ch of raw) {
    if (/\s/u.test(ch)) { space = true; continue; }
    if (/[\p{Cc}]/u.test(ch)) continue;
    if (/[\p{Cf}]/u.test(ch) && ch !== "‍") continue;
    if (ch === "<" || ch === ">" || ch === "|") continue;
    if (space && out.length > 0) out += " ";
    space = false;
    out += ch;
  }
  // 見た目の文字数(書記素)で切る
  const seg = typeof Intl !== "undefined" && Intl.Segmenter ? [...new Intl.Segmenter().segment(out)].map(s => s.segment) : [...out];
  return seg.slice(0, NAME_MAX).join("").trim();
}

function validate(p, serverBest, recentRuns, nowUtc) {
  // 2026-10-14: ソロだけ。端末はソロの記録に partySize = 1 を付けて送る。CPU サポート(2〜4)/仲間あり/人数の無い送信(古い版)は書かない
  if (Number(p.partySize) !== 1 || (p.allies !== undefined && String(p.allies) !== "")) return "solo only";
  if (!/^[0-9a-fA-F]{32}$/.test(p.runId || "")) return "bad run id";
  if (!STAGES.includes(p.stageId)) return "bad map";
  if (!CHARACTERS.includes(p.characterId)) return "bad character";
  const meters = Number(p.meters), play = Number(p.playSeconds), sprint = Number(p.sprintFrom);
  if (!Number.isInteger(meters) || meters < 1 || meters > MAX_METERS) return "distance out of range";
  if (!(play > 0) || play > 48 * 3600) return "bad play time";
  if (!(sprint >= 0) || sprint > meters) return "bad sprint";
  if (Boolean(p.usedSprint) !== (sprint > 0)) return "sprint flag mismatch";
  if (sprint > 0 && sprint > serverBest) return "sprint beyond the confirmed best";
  const ran = meters - sprint;
  if (ran > MAX_SPEED_MPS * SPEED_SLACK * play + DISTANCE_SLACK) return "too fast";
  const started = Number(p.startedUtc), committed = Number(p.committedUtc);
  // 2026-11-25 第4回: 端末の時計のずれで正当な記録を拒否しない(時間の検査の本体は受付のサーバーの時刻。ここは明らかに壊れた値だけ)
  if (committed > nowUtc + DEVICE_CLOCK_SLACK) return "time in the future";
  if (!(started > 0) || started > committed + DEVICE_CLOCK_SLACK) return "bad start time"; // ラン中に端末の時計が戻された(進んでいたのを直した)時も拒否しない
  // ここまでの端末の時刻は「明らかに壊れた値」を落とすだけ。走れる距離・受付の期限・使い回し・同時送信の検査はサーバーの時刻(checkTicket)だけを使う
  if (committed - started + DEVICE_CLOCK_SLACK < play) return "play time longer than the wall clock";
  if (recentRuns.includes(p.runId)) return "duplicate";
  return "";
}

// 受付の一覧を読む(書き込みの鍵 writeLock も)。404 = まだ無い
async function readTickets(cs, projectId, playerId, logger) {
  try {
    const g = await cs.getProtectedItems(projectId, playerId, [TICKET_KEY]);
    const item = (g.data.results || []).find(x => x.key === TICKET_KEY);
    return { list: item && Array.isArray(item.value) ? item.value : [], lock: item ? item.writeLock : undefined };
  } catch (e) {
    const st = e && e.response ? e.response.status : 0;
    if (st === 404) return { list: [], lock: undefined };
    logger.info(`ticket read failed (${st})`);
    return { error: true };
  }
}
// 読んだ時の鍵で書く(その間に別の書き込みがあれば 409 = false)。鍵が無い(初めて)時は鍵なしで書く
async function writeTickets(cs, projectId, playerId, list, lock, logger) {
  const body = { key: TICKET_KEY, value: list };
  if (lock) body.writeLock = lock;
  try { await cs.setProtectedItem(projectId, playerId, body); return true; }
  catch (e) { const st = e && e.response ? e.response.status : 0; logger.info(`ticket write failed (${st})`); return false; }
}

// 受付(ticket)の検査。tickets = Protected に控えた一覧。返す: [エラー, 使った受付]
function checkTicket(p, tickets, nowUtc) {
  if (!/^[0-9a-f]{32}$/.test(p.ticket || "")) return ["no ticket", null];
  const t = tickets.find(x => x && x.id === p.ticket);
  if (!t || t.d) return ["unknown or used ticket", null];
  if (nowUtc - Number(t.t) > TICKET_DAYS * 86400) return ["ticket expired", t];
  if (t.st !== p.stageId || t.ch !== p.characterId) return ["ticket for another map or character", t];
  // 使った受付は、ラスダンの続き(continuation)が前より長い距離で使う時だけ(合わせて3回まで)
  if (Number(t.u || 0) > 0 && (p.continuation !== true || Number(p.meters) <= Number(t.m || 0) || Number(t.u) >= 3)) return ["unknown or used ticket", null];
  const elapsed = Math.max(0, nowUtc - Number(t.t));
  const ran = Number(p.meters) - Number(p.sprintFrom);
  if (ran > SERVER_SPEED_MPS * elapsed + DISTANCE_SLACK + PRE_TICKET_METERS) return ["too fast for the server time", t];
  if (Number(p.playSeconds) > elapsed + 600) return ["play time longer than the server time", t];
  return ["", t];
}

module.exports = async ({ params: args, context, logger }) => {
  // 2026-10-20: 記録は1つの JSON の引数 run にまとめて受け取る(宣言できる引数は 10 個まで、宣言しない引数はスクリプトへ届かない。
  //   development で確かめた)。中の値の形と範囲は validate で確かめる
  let params = args.run;
  if (typeof params === "string") { try { params = JSON.parse(params); } catch (e) { params = null; } }
  if (!params || typeof params !== "object" || Array.isArray(params)) return { status: "rejected", message: "bad request" };
  const { projectId, playerId, serviceToken } = context;
  const lb = new LeaderboardsApi({ accessToken: serviceToken });
  const cs = new DataApi({ accessToken: serviceToken });
  const board = "omm_best_" + params.stageId;
  if (!STAGES.includes(params.stageId)) return { status: "rejected", message: "bad map" };

  // 2026-10-14: 環境の分離(production へは製品版の、production で作った記録だけ)
  const envName = context.environmentName;
  if (envName === "production") {
    if (params.buildKind !== "release" || params.env !== "production") {
      logger.info(`rejected ${playerId} ${params.stageId}: dev build or other environment (buildKind=${params.buildKind}, env=${params.env})`);
      return { status: "rejected", message: "dev build" };
    }
  } else if (envName === undefined) {
    logger.info("environmentName not in context: environment check skipped");
  }

  // 今の自己ベスト(無ければ 0)
  let best = 0;
  try {
    const r = await lb.getLeaderboardPlayerScore(projectId, board, playerId);
    best = Math.floor(r.data.score || 0);
  } catch (e) {
    // 2026-11-25 セキュリティ監査: 404(まだ記録なし)以外の失敗で 0 とみなすと、低い記録で上書きされ得た → 一時的な失敗として返す(端末が後で送り直す)
    const st = e && e.response ? e.response.status : 0;
    if (st !== 404) { logger.info(`best lookup failed (${st}) for ${playerId}`); return { status: "error", message: "temporary" }; }
  }

  // 送信済みの runId
  let runs = [];
  try {
    const g = await cs.getItems(projectId, playerId, ["omm_runs"]);
    const item = (g.data.results || []).find(x => x.key === "omm_runs");
    if (item && Array.isArray(item.value)) runs = item.value;
  } catch (e) { /* 無し */ }

  const now = Math.floor(Date.now() / 1000);
  const err = validate(params, best, runs, now);
  if (err === "duplicate") return { status: "duplicate", message: err };
  if (err) { logger.info(`rejected ${playerId.substring(0, 6)} ${params.stageId} ${params.meters}: ${err}`); return { status: "rejected", message: err }; }

  // 2026-11-25: ラン開始の受付(サーバーの時刻)。Cloud Save の書き込みの鍵(writeLock)で、同じ受付の同時の提出を1つだけ通す
  const rt = await readTickets(cs, projectId, playerId, logger);
  if (rt.error) return { status: "error", message: "temporary" };
  let tickets = rt.list;
  const [terr, used] = checkTicket(params, tickets, now);
  if (terr) {
    // 拒否した受付は消さずに無効の印(消すと omm_start_run の回数の数えから外れてしまう)
    if (used && !used.d) { used.d = 1; await writeTickets(cs, projectId, playerId, tickets, rt.lock, logger); }
    logger.info(`rejected ${playerId.substring(0, 6)} ${params.stageId} ${params.meters}: ${terr}`);
    return { status: "rejected", message: terr };
  }
  // ① 先に受付へ「使った」の印(回数・距離・runId)を、読んだ時の鍵で書く。同時に別の提出が書いていたら失敗 = 一時的な失敗として返す
  //   (端末は後で送り直し、その時は「使った受付」で拒否 or 重複)
  used.u = Number(used.u || 0) + 1; used.m = Number(params.meters); used.r = params.runId;
  if (!(await writeTickets(cs, projectId, playerId, tickets, rt.lock, logger))) return { status: "error", message: "temporary" };

  // ② 記録の書き込み。失敗したら ① の印を戻す(送り直しで通るように)
  const better = params.meters > best;
  if (better) {
    const metadata = { n: sanitizeName(params.name), c: params.characterId, a: params.usedAuto ? 1 : 0, s: params.usedSprint ? 1 : 0 };
    try { await lb.addLeaderboardPlayerScore(projectId, board, playerId, { score: params.meters, metadata }); }
    catch (e) {
      const back = await readTickets(cs, projectId, playerId, logger);
      if (!back.error) {
        const t = back.list.find(x => x && x.id === used.id && x.r === params.runId);
        if (t) { t.u = Math.max(0, Number(t.u || 1) - 1); delete t.r; if (t.u === 0) delete t.m; await writeTickets(cs, projectId, playerId, back.list, back.lock, logger); }
      }
      logger.info(`leaderboard write failed (${e && e.response ? e.response.status : 0}) - claim reverted`);
      return { status: "error", message: "temporary" };
    }
  }
  // ③ 送信済みの runId(重複の拒否)
  runs.push(params.runId);
  if (runs.length > RECENT_RUNS) runs = runs.slice(runs.length - RECENT_RUNS);
  await cs.setItem(projectId, playerId, { key: "omm_runs", value: runs });

  if (!better) return { status: "not_better", message: `best ${best}m` };
  logger.info(`accepted ${playerId.substring(0, 6)} ${params.stageId} ${params.meters}m (was ${best})`);
  return { status: "accepted", message: "" };
};

module.exports.params = {
  // run: { runId, stageId, characterId, meters, playSeconds, sprintFrom, usedAuto, usedSprint, startedUtc, committedUtc,
  //        appVersion, name, partySize(ソロの宣言。1 だけ受け付ける), allies, buildKind, env(環境の分離。production では release + production 以外を拒否),
  //        ticket(2026-11-25: omm_start_run が発行したラン開始の受付) }
  run: { type: "JSON", required: true },
};
