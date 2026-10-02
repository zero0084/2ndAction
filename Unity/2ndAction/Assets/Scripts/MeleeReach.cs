using UnityEngine;

// 攻撃判定の調整と高速時の相打ち対策(2026-09-30)。
// プレイヤーの近接判定(BoxCollider2D、PlayerAttackInfo付き)に付け、判定が出た瞬間(有効化/出し直し)に
// キャラごとの設定(CharacterDefinition.attackHitbox*/highSpeedHitAssist*)で形だけを整える。
// 技のコード(ArmKitBox/ArmLanceHitbox/DoAttack)はTransformで位置・大きさ・角度を決め、Colliderのsize/offsetは
// 既定値のまま使っているので、ここではsize/offsetだけを書き換える(技のコードとは書く場所が重ならない)。
//  ・コードがsize/offsetを書いた(=判定を出し直した)らそれを新しい基準にする。
//  ・形を変えるのは判定が出た瞬間だけ。振っている最中は変えない(形の作り直しで同じ相手へ二重に当たらないように)。
//  ・判定が無効の間は基準の形へ戻す。
//  ・広げる方向: 通常(横)の攻撃は体から遠い側、上攻撃は上、下攻撃は下。着地衝撃や細かい多段(結界等)は対象外。
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(BoxCollider2D))]
public class MeleeReach : MonoBehaviour
{
    // 前後比較の自動テスト専用(false=従来の判定)。通常は常にtrue。
    public static bool Enabled = true;
    // nearPadで体の方へ伸ばせる限界(体の中心からこの距離だけ後ろまで)
    const float NearPadBehind = 0.25f;

    public float forwardScale = 1f;
    public float verticalScale = 1f;
    public float crossScale = 1f;
    public float nearPad;
    public bool speedAssist;
    public float speedAssistStartMps;
    public float speedAssistSeconds;
    public float speedAssistMax;

    // 最後に判定を出した時の値(デバッグ/テスト用)
    public float LastSpeedAssist { get; private set; }
    public Vector2 LastBaseSize { get; private set; }
    public Vector2 LastSize { get; private set; }

    BoxCollider2D box;
    PlayerAttackInfo info;
    PlayerController owner;
    Vector2 baseSize = Vector2.one, baseOffset;
    Vector2 appliedSize, appliedOffset;
    bool hasApplied, wasEnabled;
    int appliedSwing = -1;

    public void Setup(PlayerController pc, CharacterDefinition def)
    {
        owner = pc;
        forwardScale = Mathf.Max(0.5f, def != null ? def.attackHitboxForwardScale : 1f);
        verticalScale = Mathf.Max(0.5f, def != null ? def.attackHitboxVerticalScale : 1f);
        crossScale = Mathf.Max(0.5f, def != null ? def.attackHitboxCrossScale : 1f);
        nearPad = Mathf.Max(0f, def != null ? def.attackHitboxNearPad : 0f);
        speedAssist = def != null && def.highSpeedHitAssistEnabled;
        speedAssistStartMps = def != null ? def.highSpeedHitAssistStartKmh / GameManager.KmhPerMps : 0f;
        speedAssistSeconds = def != null ? Mathf.Max(0f, def.highSpeedHitAssistSeconds) : 0f;
        speedAssistMax = def != null ? Mathf.Max(0f, def.highSpeedHitAssistMax) : 0f;
        appliedSwing = -1; // 次に判定が出た時に新しい設定で作り直す
    }

    void Awake()
    {
        box = GetComponent<BoxCollider2D>();
        info = GetComponent<PlayerAttackInfo>();
        baseSize = box.size;
        baseOffset = box.offset;
    }

    void FixedUpdate() => Refresh();
    void LateUpdate() => Refresh();

    void Refresh()
    {
        if (box == null) return;
        // 技のコードがsize/offsetを書いた = 判定を出し直した。書かれた値を新しい基準にする。
        if (hasApplied && (box.size != appliedSize || box.offset != appliedOffset))
        {
            baseSize = box.size;
            baseOffset = box.offset;
            appliedSwing = -1;
        }
        bool on = box.enabled && gameObject.activeInHierarchy;
        if (!on)
        {
            if (wasEnabled || (hasApplied && (appliedSize != baseSize || appliedOffset != baseOffset))) Write(baseSize, baseOffset);
            wasEnabled = false;
            appliedSwing = -1;
            return;
        }
        int swing = info != null ? info.SwingId : 0;
        if (wasEnabled && swing == appliedSwing) return; // 振っている最中は形を変えない
        wasEnabled = true;
        appliedSwing = swing;
        Vector2 size = baseSize, offset = baseOffset;
        LastSpeedAssist = 0f;
        if (Enabled && owner != null && Applies()) Shape(ref size, ref offset);
        LastBaseSize = baseSize;
        LastSize = size;
        Write(size, offset);
    }

    void Write(Vector2 size, Vector2 offset)
    {
        if (box.size != size) box.size = size;
        if (box.offset != offset) box.offset = offset;
        appliedSize = box.size;
        appliedOffset = box.offset;
        hasApplied = true;
    }

    bool Applies()
    {
        if (info == null) return false;
        if (info.kind == PlayerAttackKind.DownImpact) return false; // 着地衝撃(範囲)はそのまま
        if (info.suppressHitStop) return false;                     // 結界/燃える地面のような細かい多段はそのまま
        if (info.fixedReach) return false;                          // 技の側で「間合いを変えない」と指定(吸血鬼の血のSlash)
        return true;
    }

    void Shape(ref Vector2 size, ref Vector2 offset)
    {
        Transform t = transform;
        Vector3 ax = t.TransformVector(Vector3.right);
        Vector3 ay = t.TransformVector(Vector3.up);
        float ux = ax.magnitude, uy = ay.magnitude;
        if (ux < 1e-4f || uy < 1e-4f) return;
        Vector3 dx = ax / ux, dy = ay / uy;
        bool vertical = info.kind == PlayerAttackKind.Up || info.kind == PlayerAttackKind.Down;
        // 伸ばす軸: 横の攻撃は横向きに近い方の軸、上/下の攻撃は縦向きに近い方の軸
        int i = vertical ? (Mathf.Abs(dx.y) >= Mathf.Abs(dy.y) ? 0 : 1) : (Mathf.Abs(dx.x) >= Mathf.Abs(dy.x) ? 0 : 1);
        Vector3 dir = i == 0 ? dx : dy;
        float unit = i == 0 ? ux : uy;           // この軸の1(ローカル)が何mか
        float s = i == 0 ? size.x : size.y;
        float halfW = s * unit * 0.5f;
        Vector3 center = t.TransformPoint(offset);
        float along = Vector3.Dot(center - owner.transform.position, dir); // 体(足元の基準点)から判定の中心まで(m、軸方向)

        // 体から遠い側の向き(+1/-1)。0=判定が体をまたいでいる(両側へ半分ずつ)
        float away;
        if (vertical)
        {
            float up = info.kind == PlayerAttackKind.Up ? 1f : -1f;
            away = Mathf.Sign(dir.y) * up;
        }
        else away = Mathf.Abs(along) < halfW * 0.25f ? 0f : Mathf.Sign(along);

        float extra = s * ((vertical ? verticalScale : forwardScale) - 1f);
        float grow = 0f, shift = 0f;           // ローカル単位
        grow += extra; shift += away * extra * 0.5f;

        // 体に近い側の端を体の方へ(密着した敵に当たり抜けないように)。体の中心より NearPadBehind m 後ろまで。
        if (away != 0f && nearPad > 0f)
        {
            float gap = away * along - halfW; // 体の基準点から近い側の端まで(m)
            float pad = Mathf.Min(nearPad, Mathf.Max(0f, gap + NearPadBehind)) / unit;
            grow += pad; shift -= away * pad * 0.5f;
        }

        // 高速補正: 前(走る向き=ワールド+X)へ出る横の攻撃だけ
        if (!vertical && speedAssist)
        {
            float fwd = away != 0f ? away : Mathf.Sign(dir.x);
            if (fwd * dir.x > 0.5f)
            {
                float v = owner.CurrentAutoRunSpeed;
                float amt = Mathf.Clamp((v - speedAssistStartMps) * speedAssistSeconds, 0f, speedAssistMax);
                LastSpeedAssist = amt;
                float a = amt / unit;
                grow += a; shift += fwd * a * 0.5f;
            }
        }

        if (i == 0) { size.x += grow; offset.x += shift; size.y *= crossScale; }
        else { size.y += grow; offset.y += shift; size.x *= crossScale; }
    }
}
