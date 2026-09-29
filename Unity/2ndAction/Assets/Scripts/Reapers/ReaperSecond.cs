// 次女(自然洞窟)。長女より若い、少しツンツンした不機嫌そうな少女。地面すれすれを低空浮遊して追ってくる
// (必死に飛ばない。腕を組む/鎌を肩に担ぐ等の余裕のある姿勢)。
public class ReaperSecond : ReaperBase
{
    protected override ReaperMotion MotionOf() => ReaperMotion.Float;
    protected override float BaseHeight => data != null ? data.floatHeight : 0.35f;
}
