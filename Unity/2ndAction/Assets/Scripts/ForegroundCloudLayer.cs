using UnityEngine;

// Game Feel pass, section 16 - a thin decorative cloud layer drifting past
// in the sky. Purely cosmetic - no collider, no gameplay interaction.
//
// 雲の挙動全面見直し(2026-09-10) - マスター報告2点:
//   ①「空の雲が途中から出現し、途中で消えてしまう。敵のように右端から
//      出現し、左端になったら消えるように」
//   ②「雲が画面を追従しているように見える。もっとばらけさせて、数を増やして」
// 旧実装は毎フレーム雲の位置を「カメラ位置 + オフセット」で決め直して
// いたため、カメラ(=プレイヤーの自動走行)がどれだけ進んでも雲は画面に
// 貼り付いたまま、ゆっくり左へずれるだけ = 「画面を追従している」ように
// 見えていた。しかも再配置マージンが画面端 +30% しかなく、大きめの雲は
// 画面内に一部が残ったままワープ = 「途中で出現/消滅」して見えた。
// 新実装:雑魚敵と同じく X はワールド座標に固定し、カメラが通り過ぎる
// ことで相対的に右→左へ流れる。画面左端の充分外側まで来たら、右端の
// 充分外側へ回して Y/スケール/濃さ/流れる速さ/左右反転を引き直す。
// 数は 3→16 に増やし、配置する高さの帯も広げた(上寄りは維持しつつ
// 画面中央付近まで散らす。濃さは薄いのでプレイに被っても邪魔にならない)。
public class ForegroundCloudLayer : MonoBehaviour
{
    public Camera cam;
    public Sprite cloudSprite;
    // ②「数を増やして」。
    public int cloudCount = 16;
    // それぞれの雲が持つ、カメラ速度とは別の「自前の左流れ速度」の範囲。
    // カメラ(プレイヤー自動走行 5+)よりずっと遅いので主役はあくまで
    // カメラ通過による相対移動だが、これがあることで Level Up/ボス演出で
    // カメラが止まっている間も雲は流れ続ける + 雲ごとに僅かな速度差が出て
    // 平行移動のばらつきになる。
    public float driftSpeedMin = 0.35f;
    public float driftSpeedMax = 1.2f;
    // ②「ばらけさせて」。1つの雲が小さすぎない範囲でサイズをばらつかせる。
    public float scaleMin = 0.32f;
    public float scaleMax = 0.82f;
    // 薄め + 雲ごとにばらつき(奥行き感)。
    public float alphaMin = 0.22f;
    public float alphaMax = 0.46f;
    // 配置する高さの帯(カメラ中心からの上方向オフセット、orthographicSize
    // に対する比率)。0=画面中央の高さ、1=画面上端。上寄りだが中央付近まで
    // 散らす。
    public float bandLowFrac = 0.02f;
    public float bandHighFrac = 0.96f;

    class Cloud
    {
        public Transform t;
        public SpriteRenderer sr;
        public float worldX;       // ワールドX(カメラには追従しない)
        public float yOffsetFromCam; // 画面上の高さは保つ(遠景の平行移動レイヤー扱い)
        public float driftSpeed;
    }

    Cloud[] clouds;
    float halfWidthAtStart;
    // 雲の見た目上の最大ハーフ幅ぶんは端の外へ出してからワープ/生成する
    // ための固定パディング(素材幅 約4.4u × scaleMax の半分 + 余裕)。
    const float EdgePad = 4f;

    void Start()
    {
        if (cam == null || cloudSprite == null) { enabled = false; return; }

        clouds = new Cloud[Mathf.Max(1, cloudCount)];
        float halfW = CamHalfWidth();
        for (int i = 0; i < clouds.Length; i++)
        {
            GameObject go = new GameObject("ForegroundCloud" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cloudSprite;
            sr.sortingOrder = RenderOrder.EnvironmentFx;

            var c = new Cloud { t = go.transform, sr = sr };
            clouds[i] = c;

            // 初期配置だけは画面内〜左右の外側にまんべんなく散らす(全部が
            // 右端の外から入ってくるのを待つ必要はない)。
            float x0 = cam.transform.position.x + Random.Range(-halfW - EdgePad, halfW + EdgePad);
            Recycle(c, x0);
        }
    }

    float CamHalfWidth() => cam.orthographicSize * cam.aspect;

    // 右端の外へ(もしくは指定Xへ)雲を置き直し、見た目のパラメータを
    // すべて引き直す。
    void Recycle(Cloud c, float? forceX = null)
    {
        float halfW = CamHalfWidth();
        c.worldX = forceX ?? (cam.transform.position.x + halfW + EdgePad + Random.Range(0.5f, halfW * 0.9f));

        float band = Random.Range(bandLowFrac, bandHighFrac);
        c.yOffsetFromCam = cam.orthographicSize * band;

        float scale = Random.Range(scaleMin, scaleMax);
        // 大きい雲ほど僅かに遅く流す(近くにある小さめの雲の方が速い、
        // という平行移動の見え方)。
        float scaleT = Mathf.InverseLerp(scaleMin, scaleMax, scale);
        c.driftSpeed = Mathf.Lerp(driftSpeedMax, driftSpeedMin, scaleT) * Random.Range(0.85f, 1.15f);

        c.t.localScale = new Vector3(Random.value < 0.5f ? -scale : scale, scale, 1f);
        c.sr.color = new Color(1f, 1f, 1f, Random.Range(alphaMin, alphaMax));

        c.t.position = new Vector3(c.worldX, cam.transform.position.y + c.yOffsetFromCam, 0f);
    }

    void Update()
    {
        if (cam == null || clouds == null) return;
        float camX = cam.transform.position.x;
        float camY = cam.transform.position.y;
        float leftKill = camX - CamHalfWidth() - EdgePad;

        for (int i = 0; i < clouds.Length; i++)
        {
            Cloud c = clouds[i];
            if (c == null) continue;

            // Xはワールド固定(自前のゆるい左流れのみ) - カメラが右へ進む
            // ぶんは相対移動として画面上で勝手に左へ流れる。
            c.worldX -= c.driftSpeed * Time.deltaTime;

            if (c.worldX < leftKill)
            {
                Recycle(c); // 左端の外に出たら右端の外へ、パラメータ引き直し
                continue;
            }

            // 高さは画面上で一定(遠景平行移動レイヤー) - Xだけワールド固定。
            c.t.position = new Vector3(c.worldX, camY + c.yOffsetFromCam, 0f);
        }
    }
}
