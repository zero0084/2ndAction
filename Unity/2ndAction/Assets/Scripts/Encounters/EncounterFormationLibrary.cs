using System.Collections.Generic;
using UnityEngine;

// 全ステージで再利用する汎用Formation(Ground Line / Ground Cluster / Staggered / Ground + Air /
// Air Swarm / Gauntlet / Rest など)。ステージ専用のFormationはStageEncounterProfile側に持つ。
// Resources/Encounters/GenericFormations.asset(Tools/2ndAction/Build Encounter Profilesで作成)。
[CreateAssetMenu(menuName = "OneMoreMile/Encounter Formation Library", fileName = "GenericFormations")]
public class EncounterFormationLibrary : ScriptableObject
{
    public List<EncounterFormation> formations = new List<EncounterFormation>();

    static EncounterFormationLibrary cached;
    static bool loaded;

    public static EncounterFormationLibrary Instance
    {
        get
        {
            if (!loaded) { loaded = true; cached = Resources.Load<EncounterFormationLibrary>("Encounters/GenericFormations"); }
            return cached;
        }
    }

    public EncounterFormation Find(string id)
    {
        foreach (var f in formations) if (f != null && f.formationId == id) return f;
        return null;
    }
}
