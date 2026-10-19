/*
 * One More Mile - 呼んだ本人のオンラインのデータを削除する(Cloud Code, JavaScript)。2026-10-19
 *
 * 安全のための決まり:
 *  - 削除の対象は「この呼び出しを認証された本人」(context.playerId)だけ。PlayerId を引数で受け取らない(他人の ID を渡しても消せない)。
 *  - 管理者の鍵(サービスアカウント)はアプリに入れない。Secret Manager の秘密 OMM_ADMIN_BASIC(base64("keyId:secret"))を、
 *    このスクリプトの中でだけ読む。値はログ/返り値に出さない。
 *
 * 消す物:
 *  1. Cloud Save: 本人の項目(omm_runs = 送った記録の runId の一覧 ほか、本人の既定の項目すべて)
 *  2. Leaderboards: 本人の記録を全部の表から(Admin API の purge。表示名 = コードネームも記録と一緒に消える)
 *  アカウント(匿名の PlayerId)そのものは、この後でアプリが AuthenticationService.DeleteAccountAsync() で消す。
 *
 * 返り値: { ok, cloudSave, leaderboards, message }。ok=false の時、アプリはアカウントを消さない(消せなかったデータが宙に浮かないように)。
 *
 * まだ本物のサービスでは動かしていない(development 環境ができてから確認する。production へはマスターの確認の後に配置)。
 * 必要な準備(Docs/PlayPrivacy_2026-10-19 の手順書):
 *  - サービスアカウント(権限: Leaderboards のスコアの削除 live_ops.leaderboards.scores.delete)
 *  - Secret Manager: OMM_ADMIN_BASIC(Cloud Code から読めるようにする)
 *  - ※ context.environmentId の名前は公式の資料で再確認すること(無い時は失敗として返す)
 */
const { DataApi } = require("@unity-services/cloud-save-1.4");
const axios = require("axios-1.6");

function status(e) { return e && e.response ? e.response.status : 0; }

module.exports = async ({ params, context, logger, secretManager }) => {
  const { projectId, playerId, environmentId, serviceToken } = context;
  const result = { ok: false, cloudSave: 0, leaderboards: 0, message: "" };
  if (!playerId) { result.message = "not signed in"; return result; }
  if (!environmentId) { result.message = "environment unknown"; return result; }

  // 1) Cloud Save(本人の既定の項目すべて)
  const cs = new DataApi({ accessToken: serviceToken });
  try {
    await cs.deleteItems(projectId, playerId);
    result.cloudSave = 1;
  } catch (e) {
    if (status(e) !== 404) { logger.error(`cloud save delete failed (${status(e)})`); result.message = "cloud save"; return result; }
  }

  // 2) Leaderboards(全部の表の本人の記録。Admin API はサービスアカウントの Basic 認証)
  let auth = null;
  try { auth = (await secretManager.getSecret("OMM_ADMIN_BASIC")).value; } catch (e) { auth = null; }
  if (!auth) { logger.error("secret OMM_ADMIN_BASIC not available"); result.message = "not configured"; return result; }
  const url = `https://services.api.unity.com/leaderboards/v1/projects/${projectId}/environments/${environmentId}/leaderboards/scores/players/${encodeURIComponent(playerId)}/purge`;
  try {
    await axios.delete(url, { headers: { Authorization: "Basic " + auth } });
    result.leaderboards = 1;
  } catch (e) {
    if (status(e) !== 404) { logger.error(`leaderboards purge failed (${status(e)})`); result.message = "leaderboards"; return result; }
  }

  logger.info(`deleted online data of ${playerId.substring(0, 6)}… (cloud save ${result.cloudSave}, leaderboards ${result.leaderboards})`);
  result.ok = true;
  return result;
};

// 引数は受け取らない(PlayerId を含め、何を渡されても使わない)
module.exports.params = {};
