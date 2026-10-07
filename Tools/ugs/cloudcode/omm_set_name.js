/*
 * One More Mile - コードネームの変更を、掲載中の自分の記録の表示名へ反映する(Cloud Code, JavaScript)。2026-10-08
 * 距離は変えない(同じ距離・同じ metadata のキャラ/オート/疾走のまま、名前だけ差し替えて書き直す)。
 * Leaderboards の更新の種類は "Keep latest score"(omm_submit_run.js の前提と同じ)。
 * まだ実際のサービスでは動かしていない。
 */
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");

const STAGES = ["wasteland_road", "natural_cave", "sky_corridor", "last_corridor"];
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
  const seg = typeof Intl !== "undefined" && Intl.Segmenter ? [...new Intl.Segmenter().segment(out)].map(s => s.segment) : [...out];
  return seg.slice(0, NAME_MAX).join("").trim();
}

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId, serviceToken } = context;
  const lb = new LeaderboardsApi({ accessToken: serviceToken });
  const name = sanitizeName(params.name);
  let updated = 0;
  for (const stage of STAGES) {
    const board = "omm_best_" + stage;
    let entry = null;
    try { entry = (await lb.getLeaderboardPlayerScore(projectId, board, playerId)).data; } catch (e) { continue; }
    let meta = {};
    try { meta = entry.metadata ? JSON.parse(entry.metadata) : {}; } catch (e) { meta = {}; }
    meta.n = name;
    await lb.addLeaderboardPlayerScore(projectId, board, playerId, { score: entry.score, metadata: meta });
    updated++;
  }
  logger.info(`renamed ${playerId} on ${updated} boards`);
  return { status: "ok", message: String(updated) };
};

module.exports.params = { name: { type: "String", required: true } };
