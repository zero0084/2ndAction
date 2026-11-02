using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 2026-11-02 動作のつなぎ(MotionLink.cs)の取り込み。
// Assets/Art/MotionLink/<持ち主>/links.tsv と <名前>_NN.png を読み、キャラ定義(Resources/Characters/<id>.asset)または
// 敵定義(持ち主が enemy_<id> → Resources/Enemies/<id>.asset)の motionLinks へ入れる。
// links.tsv(タブ区切り、# はコメント): 名前 from to settle(0/1/A) fps contact resume 基準の絵(Assets/ からのパス)
//   settle: 0 = 入る直前、1 = 戻り、A = 空中用(to の動作を空中で出した時にその絵の代わり)
//   基準の絵 = 同じ縮尺の元のコマ。PPU・取り込みの設定を写す。ピボットは <png>.pivot があればそれ、無ければ基準の絵と同じ
// 実行: -executeMethod MotionLinkImporter.ImportAll(シーンは作り直さない)
public static class MotionLinkImporter
{
    const string Root = "Assets/Art/MotionLink";

    [MenuItem("Tools/One More Mile/Import Motion Links")]
    public static void ImportAll()
    {
        if (!AssetDatabase.IsValidFolder(Root)) { Debug.Log("[MotionLink] no folder"); return; }
        int owners = 0, links = 0, frames = 0;
        var done = new Dictionary<string, (MotionLink[] l, Vector2[] o)>();
        var aliases = new List<(string owner, string target)>();
        foreach (var dir in Directory.GetDirectories(Root))
        {
            // same_as.txt = 同じ絵の別の敵(例: enemy_goblin_elite → enemy_goblin)。素材を複製しない
            string alias = Path.Combine(dir, "same_as.txt");
            if (File.Exists(alias)) { aliases.Add((Path.GetFileName(dir), File.ReadAllText(alias).Trim())); continue; }
            string owner = Path.GetFileName(dir);
            string tsv = Path.Combine(dir, "links.tsv");
            if (!File.Exists(tsv) && !File.Exists(Path.Combine(dir, "run_offsets.txt"))) continue;
            var list = new List<MotionLink>();
            foreach (var raw in File.Exists(tsv) ? File.ReadAllLines(tsv) : new string[0])
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var c = line.Split('\t');
                if (c.Length < 8) { Debug.LogWarning($"[MotionLink] {owner}: bad line '{line}'"); continue; }
                string name = c[0];
                string refPath = c[7].Trim();
                var pngs = Directory.GetFiles(dir, name + "_*.png").Select(p => p.Replace('\\', '/')).OrderBy(p => p, System.StringComparer.Ordinal).ToArray();
                if (pngs.Length == 0) { Debug.LogWarning($"[MotionLink] {owner}/{name}: no frames"); continue; }
                foreach (var p in pngs) CopyImport(refPath, p);
                var sprites = pngs.Select(p => AssetDatabase.LoadAssetAtPath<Sprite>(p)).Where(s => s != null).ToArray();
                if (sprites.Length != pngs.Length) { Debug.LogWarning($"[MotionLink] {owner}/{name}: sprites {sprites.Length}/{pngs.Length}"); continue; }
                list.Add(new MotionLink
                {
                    from = c[1], to = c[2], settle = c[3].Trim() == "1", air = c[3].Trim() == "A",
                    fps = float.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture),
                    contactFrame = int.Parse(c[5]), resumeFrame = int.Parse(c[6]), frames = sprites,
                });
                links++; frames += sprites.Length;
            }
            // 走りのコマごとの表示位置の補正(run_offsets.txt: 1行 = 1コマ "dx dy"、ワールド単位)
            Vector2[] offs = null;
            string ofp = Path.Combine(dir, "run_offsets.txt");
            if (File.Exists(ofp))
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                offs = File.ReadAllLines(ofp).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .Select(l => { var v = l.Split(' '); return new Vector2(float.Parse(v[0], ic), float.Parse(v[1], ic)); }).ToArray();
            }
            if (!Assign(owner, list.ToArray(), offs)) continue;
            done[owner] = (list.ToArray(), offs);
            owners++;
        }
        foreach (var (owner, target) in aliases)
            if (done.TryGetValue(target, out var d) && Assign(owner, d.l, d.o)) owners++;
            else Debug.LogWarning($"[MotionLink] alias {owner} -> {target} not found");
        AssetDatabase.SaveAssets();
        Debug.Log($"[MotionLink] imported owners={owners} links={links} frames={frames}");
    }

    static bool Assign(string owner, MotionLink[] links, Vector2[] offs)
    {
        if (owner.StartsWith("enemy_"))
        {
            var ed = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/Resources/Enemies/{owner.Substring(6)}.asset");
            if (ed == null) { Debug.LogWarning($"[MotionLink] no enemy {owner}"); return false; }
            ed.motionLinks = links; ed.runFrameOffsets = offs; EditorUtility.SetDirty(ed); return true;
        }
        var cd = AssetDatabase.LoadAssetAtPath<CharacterDefinition>($"Assets/Resources/Characters/{owner}.asset");
        if (cd == null) { Debug.LogWarning($"[MotionLink] no character {owner}"); return false; }
        cd.motionLinks = links; cd.runFrameOffsets = offs; EditorUtility.SetDirty(cd); return true;
    }

    static void CopyImport(string refPath, string png)
    {
        AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceSynchronousImport);
        var src = AssetImporter.GetAtPath(refPath) as TextureImporter;
        var dst = AssetImporter.GetAtPath(png) as TextureImporter;
        if (src == null || dst == null) { Debug.LogWarning($"[MotionLink] import ref missing {refPath} for {png}"); return; }
        var s = new TextureImporterSettings();
        src.ReadTextureSettings(s);
        dst.SetTextureSettings(s);
        dst.textureType = TextureImporterType.Sprite;
        dst.spriteImportMode = SpriteImportMode.Single; // スクリプトで Sprite にする時は spriteImportMode も必須
        dst.spritePixelsPerUnit = src.spritePixelsPerUnit;
        dst.spritePivot = src.spritePivot;
        // 道具(ml_extract.py)が書いたピボット(<png>.pivot = "x y"、0〜1)があればそれを使う(キャンバスの大きさを絵に合わせた時)
        string pv = png + ".pivot";
        if (File.Exists(pv))
        {
            var v = File.ReadAllText(pv).Trim().Split(' ');
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(float.Parse(v[0], ic), float.Parse(v[1], ic));
            dst.SetTextureSettings(s);
            dst.spritePivot = s.spritePivot;
        }
        dst.npotScale = src.npotScale;
        dst.mipmapEnabled = src.mipmapEnabled;
        dst.filterMode = src.filterMode;
        dst.textureCompression = src.textureCompression;
        dst.SaveAndReimport();
    }
}
