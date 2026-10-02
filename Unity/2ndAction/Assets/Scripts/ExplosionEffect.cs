using System.Collections.Generic;
using UnityEngine;

// A small burst of fading/scattering squares standing in for an explosion,
// since no explosion art/video was supplied. Runs independently of whatever
// spawned it so it always finishes its full duration.
public class ExplosionEffect : MonoBehaviour
{
    static Sprite burstParticle;

    // 撃破エフェクト本番素材化(2026-09-23) - BossFx/CaveBossFxと同じ考え方。
    // Assets/Resources/Effects/burst.png(ChatGPT生成、白基調)があれば使い、
    // 無ければ従来のOneShotSpriteEffect.SoftDotSprite()(走行ダスト/ヒット
    // スパークと共用の丸い粒)へフォールバックする。ダスト/スパーク自体は
    // 意図的にSoftDotSpriteのままにする(OneShotSpriteEffect.cs参照、
    // 「新規アセットは増やさない」という別の意図的な設計判断のため)。
    static Sprite BurstParticleSprite()
    {
        if (burstParticle != null) return burstParticle;
        burstParticle = Resources.Load<Sprite>("Effects/burst");
        if (burstParticle != null) return burstParticle;
        burstParticle = OneShotSpriteEffect.SoftDotSprite();
        return burstParticle;
    }

    class Particle
    {
        public Transform t;
        public Vector2 velocity;
        public SpriteRenderer sr;
        public float rotSpeed;
    }

    readonly List<Particle> particles = new List<Particle>();
    float duration;
    float elapsed;

    public static ExplosionEffect Create(Sprite sprite, Vector3 position, Color color, int count = 16, float duration = 3f, float sizeScale = 1f, float speedScale = 1f, int sortingOrder = 20)
    {
        GameObject go = new GameObject("Explosion");
        go.transform.position = position;

        ExplosionEffect fx = go.AddComponent<ExplosionEffect>();
        fx.duration = duration;
        fx.Init(sprite, color, count, sizeScale, speedScale, sortingOrder);
        return fx;
    }

    // 敵撃破時の飛散パーティクル(2026-09-10) - マスターの「敵を倒した後に
    // 以前出していたパーティクルを出して」「敵(雑魚敵やボス)の大きさに比例
    // して数とサイズを調整」「色は雑魚敵=青/ドラゴン=赤/魔人=紫/機械龍=黄」
    // 対応の共通入口。subjectWorldHeight = 撃破された対象のワールド高さ
    // (SpriteRenderer.bounds.size.y など、lossyScale込みの実寸)を渡すと、
    // そこから数/粒サイズ/飛散速度/寿命をまとめて算出する。雑魚敵(高さ
    // 約1〜1.5)は控えめ、ボス(高さ約6〜12)は数もサイズも大きい派手な
    // 破裂になる。
    public static ExplosionEffect CreateForDefeat(Vector3 position, Color color, float subjectWorldHeight, int sortingOrder = 20)
    {
        float h = Mathf.Max(0.2f, subjectWorldHeight);
        int count = Mathf.Clamp(Mathf.RoundToInt(9f + h * 4.5f), 9, 64);
        float sizeScale = Mathf.Clamp(0.55f + h * 0.30f, 0.55f, 4.5f);
        float speedScale = Mathf.Clamp(0.85f + h * 0.12f, 0.85f, 2.8f);
        float duration = Mathf.Clamp(0.5f + h * 0.05f, 0.5f, 1.2f);

        Color c = color;
        c.a = 1f;
        return Create(BurstParticleSprite(), position, c, count: count, duration: duration, sizeScale: sizeScale, speedScale: speedScale, sortingOrder: sortingOrder);
    }

    void Init(Sprite sprite, Color color, int count, float sizeScale, float speedScale, int sortingOrder)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject p = new GameObject("Particle");
            p.transform.SetParent(transform);
            p.transform.localPosition = Vector3.zero;
            p.transform.localScale = Vector3.one * (Random.Range(0.15f, 0.32f) * sizeScale);
            p.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            SpriteRenderer sr = p.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(1.5f, 4.5f) * speedScale;
            Vector2 vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

            particles.Add(new Particle { t = p.transform, velocity = vel, sr = sr, rotSpeed = Random.Range(-360f, 360f) });
        }
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        float drag = 1f - t * 0.6f;

        foreach (Particle p in particles)
        {
            p.t.localPosition += (Vector3)(p.velocity * Time.deltaTime * drag);
            p.t.Rotate(0f, 0f, p.rotSpeed * Time.deltaTime);

            Color c = p.sr.color;
            c.a = 1f - t;
            p.sr.color = c;
        }

        if (elapsed >= duration)
        {
            Destroy(gameObject);
        }
    }
}
