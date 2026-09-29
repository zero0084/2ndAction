// 長女(荒野街道)。大人のお姉さん、最も妖艶で落ち着いている。ゆっくり歩いて追ってくる(走らない)。
// 追跡の仕組みはReaperBase共通。将来のラストダンジョン戦ではここに長女の戦闘AIを書く。
public class ReaperEldest : ReaperBase
{
    protected override ReaperMotion MotionOf() => ReaperMotion.Walk;
}
