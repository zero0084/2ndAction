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
// 高速走行の視認性補正(2026-09-22) - 雲を「ワールド固定(Player速度と1:1で流れる)」から
// 「カメラ相対のParallaxレイヤー」へ変更。高速になるほど雲が猛烈に流れて背景がうるさくなる問題への対処。
//   ・画面上の流れる速さ = Player速度 × 層の倍率(遠景10〜20%/中景25〜40%/近景40〜60%)。
//   ・倍率は速度が上がるほど少し弱め、さらに画面上の速さに上限(maxScrollSpeed)を設ける。
//   ・夜は昼よりゆっくり・薄くする(細かい動きのノイズを減らす)。
// Playerの実際の移動速度・地形/障害物の流れには一切影響しない(見た目のレイヤーだけ)。
[DefaultExecutionOrder(100)]
public class ForegroundCloudLayer : MonoBehaviour
{
    public Camera cam;
    public Sprite cloudSprite;
    // ②「数を増やして」。
    public int cloudCount = 16;
    // それぞれの雲が持つ、カメラ速度とは別の「自前の左流れ速度」の範囲。
    public float driftSpeedMin = 0.35f;
    public float driftSpeedMax = 1.2f;
    // ②「ばらけさせて」。1つの雲が小さすぎない範囲でサイズをばらつかせる。
    public float scaleMin = 0.32f;
    public float scaleMax = 0.82f;
    // 薄め + 雲ごとにばらつき(奥行き感)。
    public float alphaMin = 0.22f;
    public float alphaMax = 0.46f;
    // 配置する高さの帯(カメラ中心からの上方向オフセット、orthographicSize
    // に対する比率)。0=画面中央の高さ、1=画面上端。
    public float bandLowFrac = 0.02f;
    public float bandHighFrac = 0.96f;

    [Header("Parallax (高速走行の視認性補正)")]
    // 画面上の流れる速さ = Player速度 × parallax。小さく遠い雲ほど小さい倍率。
    public float parallaxFar = 0.12f;   // 最も遠い(小さい)雲: Player速度の約12%
    public float parallaxNear = 0.45f;  // 最も近い(大きい)雲: 約45%
    // 速度倍率が最大のとき、parallaxをこの割合まで弱める(1=弱めない)。
    public float highSpeedParallaxDamp = 0.6f;
    // 画面上の流れの上限(ユニット/秒)。どれだけ速くなってもこれ以上は流さない。
    public float maxScrollSpeed = 3.2f;
    // 夜の見え方(昼=1)。動きを抑え、細部を薄くして遠景を安定して見せる。
    public float nightScrollScale = 0.35f;
    public float nightAlphaScale = 0.55f;

    class Cloud
    {
        public Transform t;
        public SpriteRenderer sr;
        public float rel;            // カメラ中心からの画面上のX(カメラ相対)
        public float yOffsetFromCam; // 画面上の高さは保つ(遠景の平行移動レイヤー扱い)
        public float driftSpeed;
        public float parallax;
        public float baseAlpha;
    }

    Cloud[] clouds;
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

            // 初期配置だけは画面内〜左右の外側にまんべんなく散らす。
            Recycle(c, Random.Range(-halfW - EdgePad, halfW + EdgePad));
        }
    }

    float CamHalfWidth() => cam.orthographicSize * cam.aspect;

    // 右端の外へ(もしくは指定の相対Xへ)雲を置き直し、見た目のパラメータをすべて引き直す。
    void Recycle(Cloud c, float? forceRel = null)
    {
        float halfW = CamHalfWidth();
        c.rel = forceRel ?? (halfW + EdgePad + Random.Range(0.5f, halfW * 0.9f));

        float band = Random.Range(bandLowFrac, bandHighFrac);
        c.yOffsetFromCam = cam.orthographicSize * band;

        float scale = Random.Range(scaleMin, scaleMax);
        float scaleT = Mathf.InverseLerp(scaleMin, scaleMax, scale);
        c.driftSpeed = Mathf.Lerp(driftSpeedMin, driftSpeedMax, 1f - scaleT) * Random.Range(0.85f, 1.15f);
        // 大きい雲ほど近い層 = 少し速く流れる。
        c.parallax = Mathf.Lerp(parallaxFar, parallaxNear, scaleT) * Random.Range(0.9f, 1.1f);

        c.t.localScale = new Vector3(Random.value < 0.5f ? -scale : scale, scale, 1f);
        c.baseAlpha = Random.Range(alphaMin, alphaMax);
        c.sr.color = new Color(1f, 1f, 1f, c.baseAlpha);
        c.t.position = new Vector3(cam.transform.position.x + c.rel, cam.transform.position.y + c.yOffsetFromCam, 0f);
    }

    void LateUpdate()
    {
        if (cam == null || clouds == null) return;
        float camX = cam.transform.position.x;
        float camY = cam.transform.position.y;
        float leftKill = -CamHalfWidth() - EdgePad;

        float speed = 0f, ratio = 1f, maxRatio = 2f;
        var pc = PlayerController.Instance;
        if (pc != null) { speed = pc.CurrentAutoRunSpeed; ratio = pc.SpeedRatio; maxRatio = pc.MaxSpeedRatio; }
        float hi = Mathf.Clamp01((ratio - 1f) / Mathf.Max(0.01f, maxRatio - 1f));
        float night = WorldTimeCycle.Instance != null ? WorldTimeCycle.Instance.NightAmount : 0f;
        float scrollScale = Mathf.Lerp(1f, nightScrollScale, night) * Mathf.Lerp(1f, highSpeedParallaxDamp, hi);
        float alphaScale = Mathf.Lerp(1f, nightAlphaScale, night);
        float dt = Time.deltaTime;

        for (int i = 0; i < clouds.Length; i++)
        {
            Cloud c = clouds[i];
            if (c == null) continue;

            // 画面上の流れ = Player速度×層倍率(上限あり) + 自前のゆるい流れ。
            float scroll = Mathf.Min(speed * c.parallax * scrollScale, maxScrollSpeed) + c.driftSpeed;
            c.rel -= scroll * dt;

            if (c.rel < leftKill)
            {
                Recycle(c); // 左端の外に出たら右端の外へ、パラメータ引き直し
                continue;
            }

            Color tint = SceneryCycle.CloudTint; // 夕焼け/夜/夜明けの空に合わせた雲の色(担当外のステージは白)
            c.sr.color = new Color(tint.r, tint.g, tint.b, c.baseAlpha * alphaScale);
            c.t.position = new Vector3(camX + c.rel, camY + c.yOffsetFromCam, 0f);
        }
    }
}
