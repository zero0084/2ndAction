using System.Collections.Generic;
using UnityEngine;

// Resources/Charactersに置かれた全CharacterDefinitionを読み込みキャッシュ
// する - EnemyDatabase/CardDatabaseと同じパターン。
public static class CharacterDatabase
{
    static List<CharacterDefinition> cached;

    public static IReadOnlyList<CharacterDefinition> AllCharacters
    {
        get
        {
            if (cached == null) Load();
            return cached;
        }
    }

    static void Load()
    {
        CharacterDefinition[] loaded = Resources.LoadAll<CharacterDefinition>("Characters");
        cached = new List<CharacterDefinition>(loaded);
        // Resources.LoadAllの読み込み順はファイルシステム依存で不定なため、
        // CharacterDefinition.sortOrder(CharacterDatabaseBuilderが明示的に
        // 割り当てる)で並べ替え、表示順を安定させる。
        cached.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
    }

    public static void Reset()
    {
        cached = null;
    }

    public static CharacterDefinition FindById(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        foreach (CharacterDefinition def in AllCharacters)
        {
            if (def.characterId == characterId) return def;
        }
        return null;
    }
}
