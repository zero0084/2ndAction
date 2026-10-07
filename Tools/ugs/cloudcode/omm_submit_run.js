/*
 * One More Mile - ランキングへの投稿(Cloud Code, JavaScript)。2026-10-08
 * 端末から届いた値を検査してから、Leaderboards へ「自己ベストより長い時だけ」書く。
 * 検査の規則は Unity 側の LeaderboardRules(Assets/Scripts/Online/Leaderboard.cs)と同じ。
 *
 * 前提(Dashboard):
 *  - Leaderboards: omm_best_wasteland_road / omm_best_natural_cave / omm_best_sky_corridor / omm_best_last_corridor
 *    並び順=高い順(降順)、更新の種類="Keep latest score"(自己ベストの判定はこのスクリプトが行う。表示名の更新で書き直すため)
 *  - Access Control: プレイヤーが Leaderboards へ直接書くのを禁止(Tools/ugs/access/omm_leaderboards.ac)
 *  - Cloud Save: 送信済みの runId を自分のプレイヤーデータ "omm_runs" に保存する
 * まだ実際のサービスでは動かしていない(プロジェクトが Unity Cloud に未連携のため)。連携後に確認する。
 */
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const STAGES = ["wasteland_road", "natural_cave", "sky_corridor", "last_corridor"];
const CHARACTERS = ["swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer", "archer", "mage", "fighter", "ninja", "miko", "vampire", "dragonkin"];
const MAX_METERS = 2000000;
const MAX_SPEED_MPS = 160 / 3.6;
const SPEED_SLACK = 1.2, DISTANCE_SLACK = 300;
const RECENT_RUNS = 200;
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
  if (committed > nowUtc + 600) return "time in the future";
  if (!(started > 0) || started > committed) return "bad start time";
  if (committed - started + 120 < play) return "play time longer than the wall clock";
  if (recentRuns.includes(p.runId)) return "duplicate";
  return "";
}

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId, serviceToken } = context;
  const lb = new LeaderboardsApi({ accessToken: serviceToken });
  const cs = new DataApi({ accessToken: serviceToken });
  const board = "omm_best_" + params.stageId;
  if (!STAGES.includes(params.stageId)) return { status: "rejected", message: "bad map" };

  // 今の自己ベスト(無ければ 0)
  let best = 0;
  try {
    const r = await lb.getLeaderboardPlayerScore(projectId, board, playerId);
    best = Math.floor(r.data.score || 0);
  } catch (e) { /* 404 = まだ記録なし */ }

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
  if (err) { logger.info(`rejected ${playerId} ${params.stageId} ${params.meters}: ${err}`); return { status: "rejected", message: err }; }

  runs.push(params.runId);
  if (runs.length > RECENT_RUNS) runs = runs.slice(runs.length - RECENT_RUNS);
  await cs.setItem(projectId, playerId, { key: "omm_runs", value: runs });

  if (params.meters <= best) return { status: "not_better", message: `best ${best}m` };

  const metadata = { n: sanitizeName(params.name), c: params.characterId, a: params.usedAuto ? 1 : 0, s: params.usedSprint ? 1 : 0 };
  await lb.addLeaderboardPlayerScore(projectId, board, playerId, { score: params.meters, metadata });
  logger.info(`accepted ${playerId} ${params.stageId} ${params.meters}m (was ${best})`);
  return { status: "accepted", message: "" };
};

module.exports.params = {
  runId: { type: "String", required: true },
  stageId: { type: "String", required: true },
  characterId: { type: "String", required: true },
  meters: { type: "NUMERIC", required: true },
  playSeconds: { type: "NUMERIC", required: true },
  sprintFrom: { type: "NUMERIC", required: true },
  usedAuto: { type: "BOOLEAN", required: true },
  usedSprint: { type: "BOOLEAN", required: true },
  startedUtc: { type: "NUMERIC", required: true },
  committedUtc: { type: "NUMERIC", required: true },
  appVersion: { type: "String", required: false },
  name: { type: "String", required: false },
};
