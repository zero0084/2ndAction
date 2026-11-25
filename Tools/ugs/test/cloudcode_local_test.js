// Cloud Code(omm_start_run / omm_submit_run)の手元の試験(2026-11-25)。UGS の SDK を置き換えて、検査の規則だけを確かめる。
//  使い方: node Tools/ugs/test/cloudcode_local_test.js   (秘密は使わない・本物のサービスへは繋がない)
const Module = require("module");
const path = require("path");
const store = { default: {}, protected: {} };
let board = {};
let failBest = 0, failAdd = 0;
const locks = {};   // Cloud Save の writeLock の代わり(書くたびに変わる)
let lockSeq = 0;
const tick = () => new Promise(r => setImmediate(r)); // 本物の通信と同じく、await の間に別の処理が割り込めるようにする
class DataApi {
  async getItems(p, pl, keys) { await tick(); return { data: { results: keys.filter(k => k in store.default).map(k => ({ key: k, value: JSON.parse(JSON.stringify(store.default[k])) })) } }; }
  async setItem(p, pl, it) { await tick(); store.default[it.key] = JSON.parse(JSON.stringify(it.value)); }
  async getProtectedItems(p, pl, keys) { await tick(); return { data: { results: keys.filter(k => k in store.protected).map(k => ({ key: k, value: JSON.parse(JSON.stringify(store.protected[k])), writeLock: locks[k] })) } }; }
  async setProtectedItem(p, pl, it) {
    await tick();
    if (it.writeLock !== undefined && it.writeLock !== locks[it.key]) { const e = new Error("conflict"); e.response = { status: 409 }; throw e; }
    store.protected[it.key] = JSON.parse(JSON.stringify(it.value)); locks[it.key] = "L" + (++lockSeq);
  }
}
class LeaderboardsApi {
  async getLeaderboardPlayerScore(p, b, pl) {
    if (failBest) { const e = new Error("x"); e.response = { status: failBest }; throw e; }
    if (!(b in board)) { const e = new Error("nf"); e.response = { status: 404 }; throw e; }
    return { data: { score: board[b] } };
  }
  async addLeaderboardPlayerScore(p, b, pl, d) { await tick(); if (failAdd) { const e = new Error("x"); e.response = { status: failAdd }; throw e; } board[b] = d.score; }
}
const origLoad = Module._load;
Module._load = function (req, parent, isMain) {
  if (req === "@unity-services/cloud-save-1.4") return { DataApi };
  if (req === "@unity-services/leaderboards-1.1") return { LeaderboardsApi };
  return origLoad.apply(this, arguments);
};
const dir = path.join(__dirname, "..", "cloudcode");
const start = require(path.join(dir, "omm_start_run.js"));
const submit = require(path.join(dir, "omm_submit_run.js"));
const logger = { info() {}, error() {}, warning() {} };
const ctx = (env) => ({ projectId: "p", playerId: "player123456", serviceToken: "t", environmentName: env });
let fails = 0;
function check(ok, msg) { console.log((ok ? "OK  " : "NG  ") + msg); if (!ok) fails++; }
const realNow = Date.now;
let clock = 1_800_000_000_000;
Date.now = () => clock;
function runParams(o) {
  return Object.assign({ runId: Math.random().toString(16).slice(2).padEnd(32, "0").slice(0, 32), stageId: "wasteland_road", characterId: "swordsman", meters: 5000, playSeconds: 300, sprintFrom: 0,
    usedAuto: false, usedSprint: false, startedUtc: Math.floor(clock / 1000) - 400, committedUtc: Math.floor(clock / 1000), appVersion: "1.0", name: "A", partySize: 1, allies: "",
    buildKind: "release", env: "production" }, o);
}
(async () => {
  const env = "production";
  // 1. 受付なし → 拒否
  let r = await submit({ params: { run: runParams({}) }, context: ctx(env), logger });
  check(r.status === "rejected" && r.message === "no ticket", `no ticket is rejected (${r.status} ${r.message})`);
  // 2. 受付 → 5分後に 5000m → 受け付け
  let s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  check(s.status === "ok" && /^[0-9a-f]{32}$/.test(s.ticket), `start issues a ticket (${s.status})`);
  clock += 400 * 1000;
  r = await submit({ params: { run: runParams({ ticket: s.ticket }) }, context: ctx(env), logger });
  check(r.status === "accepted", `a normal run with a ticket is accepted (${r.status} ${r.message})`);
  // 3. 同じ受付の再利用(別の runId)→ 拒否
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 6000 }) }, context: ctx(env), logger });
  check(r.status === "rejected" && /used/.test(r.message), `the same ticket cannot be used again (${r.status} ${r.message})`);
  // 4. ラスダンの続き(continuation)で前より長い → 受け付け / 短い → 拒否
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 7000, continuation: true }) }, context: ctx(env), logger });
  check(r.status === "accepted", `a continuation of the same run with a longer distance is accepted (${r.status} ${r.message})`);
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 6500, continuation: true }) }, context: ctx(env), logger });
  check(r.status === "rejected", `a continuation with a shorter distance is rejected (${r.status} ${r.message})`);
  // 5. サーバーの時刻で走れない距離(受付の 60 秒後に 100km)→ 拒否
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  clock += 60 * 1000;
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 100000, playSeconds: 3000, startedUtc: Math.floor(clock / 1000) - 3100 }) }, context: ctx(env), logger });
  check(r.status === "rejected" && /server time/.test(r.message), `a distance impossible for the server time is rejected (${r.status} ${r.message})`);
  // 6. 別のマップ/キャラの受付
  s = await start({ params: { run: { stageId: "natural_cave", characterId: "ninja", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  clock += 400 * 1000;
  r = await submit({ params: { run: runParams({ ticket: s.ticket }) }, context: ctx(env), logger });
  check(r.status === "rejected" && /another map/.test(r.message), `a ticket for another map/character is rejected (${r.status} ${r.message})`);
  // 7. 存在しない受付
  r = await submit({ params: { run: runParams({ ticket: "0123456789abcdef0123456789abcdef" }) }, context: ctx(env), logger });
  check(r.status === "rejected", `an unknown ticket is rejected (${r.status} ${r.message})`);
  // 8. 開発版の受付は production で拒否
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "dev", env } }, context: ctx(env), logger });
  check(s.status === "rejected", `a dev build cannot get a production ticket (${s.status} ${s.message})`);
  // 9. 1分に7回 → 拒否
  let last;
  for (let i = 0; i < 7; i++) last = await start({ params: { run: { stageId: "sky_corridor", characterId: "mage", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  check(last.status === "rejected", `too many starts in a minute are rejected (${last.status} ${last.message})`);
  // 10. 自己ベストの読み込みの一時的な失敗 → "error"(端末が後で送り直す)
  clock += 120 * 1000;
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  clock += 400 * 1000; failBest = 500;
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 9000 }) }, context: ctx(env), logger });
  check(r.status === "error", `a temporary failure is reported as error, not as best 0 (${r.status})`);
  failBest = 0;
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 9000 }) }, context: ctx(env), logger });
  check(r.status === "accepted", `the retry after the temporary failure is accepted (${r.status} ${r.message})`);
  // 12. 同じ受付の同時の提出 → 1つだけ通る(書き込みの鍵)
  clock += 120 * 1000;
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  clock += 900 * 1000;
  const both = await Promise.all([
    submit({ params: { run: runParams({ ticket: s.ticket, meters: 12000, playSeconds: 800, startedUtc: Math.floor(clock / 1000) - 880 }) }, context: ctx(env), logger }),
    submit({ params: { run: runParams({ ticket: s.ticket, meters: 13000, playSeconds: 800, startedUtc: Math.floor(clock / 1000) - 880 }) }, context: ctx(env), logger })]);
  const okN = both.filter(x => x.status === "accepted" || x.status === "not_better").length;
  check(okN === 1, `two simultaneous submits with the same ticket: only one goes through (${both.map(x => x.status + " " + x.message).join(" / ")})`);
  const again = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 14000, playSeconds: 800, startedUtc: Math.floor(clock / 1000) - 880 }) }, context: ctx(env), logger });
  check(again.status === "rejected", `... and the ticket cannot be used afterwards (${again.status} ${again.message})`);
  // 13. 記録の書き込みに失敗 → 受付の印を戻す → 送り直しで通る
  clock += 120 * 1000;
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  clock += 900 * 1000; failAdd = 503;
  const rp = runParams({ ticket: s.ticket, meters: 20000, playSeconds: 850, startedUtc: Math.floor(clock / 1000) - 880 });
  r = await submit({ params: { run: rp }, context: ctx(env), logger });
  check(r.status === "error", `a failed leaderboard write is a temporary error (${r.status})`);
  failAdd = 0;
  r = await submit({ params: { run: rp }, context: ctx(env), logger });
  check(r.status === "accepted", `the retry after a failed write is accepted (the claim was reverted) (${r.status} ${r.message})`);
  // 14. 1日の上限
  store.protected.omm_ticket_day = { d: new Date(clock).toISOString().substring(0, 10), n: 500 };
  clock += 120 * 1000;
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  check(s.status === "rejected" && /today/.test(s.message), `the daily limit of starts (${s.status} ${s.message})`);
  // 15. 90日を過ぎた受付は無効
  delete store.protected.omm_ticket_day;
  s = await start({ params: { run: { stageId: "wasteland_road", characterId: "swordsman", speedPercent: 100, appVersion: "1.0", buildKind: "release", env } }, context: ctx(env), logger });
  clock += 91 * 86400 * 1000;
  r = await submit({ params: { run: runParams({ ticket: s.ticket, meters: 3000, playSeconds: 400 }) }, context: ctx(env), logger });
  check(r.status === "rejected" && /expired|unknown/.test(r.message), `a ticket older than 90 days is not accepted (${r.status} ${r.message})`);
  // 16. 受付の一覧の大きさ(30件まで)
  check(store.protected.omm_tickets.length <= 30, `at most 30 tickets are kept (${store.protected.omm_tickets.length})`);
  // 11. 受付は Protected(プレイヤーが書けない場所)にある
  check(Array.isArray(store.protected.omm_tickets) && !("omm_tickets" in store.default), "tickets are kept in Protected Cloud Save only");
  Date.now = realNow;
  console.log(fails === 0 ? "ALL PASSED" : `FAILED ${fails}`);
  process.exit(fails === 0 ? 0 : 1);
})();
