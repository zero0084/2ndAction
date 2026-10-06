using UnityEngine;

// BOSS FINISH の「体」(1枚絵のボス: ドラゴン/魔人)。格子のセルは無いので、体全体の動き/色だけで演出する。
public class BossDeathAdapter : IBossDeathBody
{
    readonly Transform root; readonly float height; readonly SpriteRenderer[] srs; readonly string key; readonly System.Action onDone;
    public BossDeathAdapter(Transform root, float height, SpriteRenderer[] srs, string key, System.Action onDone)
    { this.root = root; this.height = height; this.srs = srs; this.key = key; this.onDone = onDone; }
    public Transform DeathRoot => root;
    public float DeathBodyHeight => height;
    public BossRig DeathRig => null;
    public string DeathKey => key;
    public void SetDeathColor(Color c) { if (srs != null) foreach (var s in srs) if (s != null) s.color = c; }
    public void OnDeathVisualFinished() => onDone?.Invoke();

    // 撃破の見た目の前の片付け(当たり判定/攻撃判定/重ねの絵/HP バー)
    public static void Prepare(MonoBehaviour boss, SpriteRenderer[] overlays, DragonHealthBar hpBar)
    {
        foreach (var c in boss.GetComponentsInChildren<Collider2D>(true)) if (c != null) c.enabled = false;
        foreach (var hb in boss.GetComponentsInChildren<BossHitbox>(true)) if (hb != null) hb.Deactivate();
        if (overlays != null) foreach (var o in overlays) if (o != null) o.enabled = false;
        if (hpBar != null && boss.isActiveAndEnabled) boss.StartCoroutine(hpBar.FadeOutRoutine(0.25f));
    }

    public static BossFinishInfo Decide(BossFinishInfo pending, bool pendingSet, int netAttacker, Transform self)
    {
        BossFinishInfo fi;
        if (pendingSet && netAttacker <= 0) fi = pending;
        else
        {
            var pc = PlayerController.Instance;
            float d = pc != null ? Mathf.Sign(self.position.x - pc.transform.position.x) : 1f;
            fi = new BossFinishInfo { attack = BossFinalAttack.Other, dir = (sbyte)(d < 0f ? -1 : 1) };
        }
        var bm = BossManager.Instance;
        fi.firstKill = !(bm != null && (bm.CurrentEncounterIsRematch || (bm.CurrentBossEncounter != null && bm.CurrentBossEncounter.rush)));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (BossFinish.DebugFirstKill >= 0) fi.firstKill = BossFinish.DebugFirstKill == 1;
#endif
        return fi;
    }
}
