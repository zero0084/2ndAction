using System.Collections.Generic;
using UnityEngine;

// A small burst of fading/scattering squares standing in for an explosion,
// since no explosion art/video was supplied. Runs independently of whatever
// spawned it so it always finishes its full duration.
public class ExplosionEffect : MonoBehaviour
{
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

    public static ExplosionEffect Create(Sprite sprite, Vector3 position, Color color, int count = 16, float duration = 3f)
    {
        GameObject go = new GameObject("Explosion");
        go.transform.position = position;

        ExplosionEffect fx = go.AddComponent<ExplosionEffect>();
        fx.duration = duration;
        fx.Init(sprite, color, count);
        return fx;
    }

    void Init(Sprite sprite, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject p = new GameObject("Particle");
            p.transform.SetParent(transform);
            p.transform.localPosition = Vector3.zero;
            p.transform.localScale = Vector3.one * Random.Range(0.15f, 0.32f);
            p.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            SpriteRenderer sr = p.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 20;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(1.5f, 4.5f);
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
