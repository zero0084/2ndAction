/*
 * One More Mile - ランキングのラン開始の受付(Cloud Code, JavaScript)。2026-11-25(セキュリティ監査 B 案)
 * ランの開始の時に、サーバーが「受付番号(ticket)」と開始時刻を発行し、そのプレイヤーの Protected の Cloud Save に控える
 * (プレイヤーは Protected を読み書きできない。書くのはこのスクリプトと omm_submit_run だけ)。
 * omm_submit_run は、この受付があること・未使用であること・マップ/キャラが同じこと・サーバーの経過時間で走れる距離であることを確かめる。
 *  ・受付はランキングに参加した人のソロのランだけ(端末が判断して呼ぶ)。オフラインで始めたランは受付が無い = ランキングに載らないだけ
 *    (ローカルの BEST・報酬は今まで通り)
 *  ・受付は 1人 MAX_TICKETS 件(古い順に捨てる)・TICKET_DAYS 日で無効。1分に START_PER_MIN 回まで
 */
const { DataApi } = require("@unity-services/cloud-save-1.4");

const STAGES = ["wasteland_road", "natural_cave", "sky_corridor", "last_corridor"];
const CHARACTERS = ["swordsman", "dual_blade", "noble_lady", "gunslinger", "dragon_lancer", "archer", "mage", "fighter", "ninja", "miko", "vampire", "dragonkin", "luka", "pentatuttulu"];
const KEY = "omm_tickets";
const MAX_TICKETS = 30;
const TICKET_DAYS = 30;
const START_PER_MIN = 6;

function newId() {
  try { return require("crypto").randomBytes(16).toString("hex"); }
  catch (e) {
    let s = "";
    for (let i = 0; i < 32; i++) s += Math.floor(Math.random() * 16).toString(16);
    return s;
  }
}

module.exports = async ({ params: args, context, logger }) => {
  let p = args.run;
  if (typeof p === "string") { try { p = JSON.parse(p); } catch (e) { p = null; } }
  if (!p || typeof p !== "object" || Array.isArray(p)) return { status: "rejected", message: "bad request" };
  if (!STAGES.includes(p.stageId)) return { status: "rejected", message: "bad map" };
  if (!CHARACTERS.includes(p.characterId)) return { status: "rejected", message: "bad character" };
  const speed = Number(p.speedPercent);
  if (!(speed >= 50 && speed <= 150)) return { status: "rejected", message: "bad speed setting" };
  if (context.environmentName === "production" && (p.buildKind !== "release" || p.env !== "production")) return { status: "rejected", message: "dev build" };

  const { projectId, playerId, serviceToken } = context;
  const cs = new DataApi({ accessToken: serviceToken });
  let list = [];
  try {
    const g = await cs.getProtectedItems(projectId, playerId, [KEY]);
    const item = (g.data.results || []).find(x => x.key === KEY);
    if (item && Array.isArray(item.value)) list = item.value;
  } catch (e) {
    const st = e && e.response ? e.response.status : 0;
    if (st !== 404) { logger.info(`ticket read failed (${st})`); return { status: "error", message: "temporary" }; }
  }
  const now = Math.floor(Date.now() / 1000);
  list = list.filter(t => t && typeof t.id === "string" && now - Number(t.t) < TICKET_DAYS * 86400);
  if (list.filter(t => now - Number(t.t) < 60).length >= START_PER_MIN) return { status: "rejected", message: "too many starts" };

  const ticket = { id: newId(), t: now, st: p.stageId, ch: p.characterId, sp: Math.round(speed), v: String(p.appVersion || "").substring(0, 32) };
  list.push(ticket);
  if (list.length > MAX_TICKETS) list = list.slice(list.length - MAX_TICKETS);
  await cs.setProtectedItem(projectId, playerId, { key: KEY, value: list });
  return { status: "ok", ticket: ticket.id, serverTime: now };
};

module.exports.params = {
  // run: { stageId, characterId, speedPercent, appVersion, buildKind, env }
  run: { type: "JSON", required: true },
};
