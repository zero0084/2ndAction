using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// マルチプレイ対応Phase 1(2026-09-25) - NetSessionが接続時に生成するプレイヤー用プレハブ
// (Resources/Net/NetPlayer.prefab: NetworkObject + NetPlayer)を作る。
// NetworkObjectの識別子(GlobalObjectIdHash)はプレハブアセットに保存された値を全端末で
// 共有するため、必ずエディタ上でアセットとして作る(実行時生成では端末間で一致しない)。
public static class NetPrefabBuilder
{
    const string ResourcesFolder = "Assets/Resources";
    const string NetFolder = ResourcesFolder + "/Net";
    const string PrefabPath = NetFolder + "/NetPlayer.prefab";

    [MenuItem("Tools/OneMoreMile/Build Net Prefabs")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(NetFolder)) AssetDatabase.CreateFolder(ResourcesFolder, "Net");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var go = new GameObject("NetPlayer");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetPlayer>();
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab.GetComponent<NetPlayer>() == null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (contents.GetComponent<NetworkObject>() == null) contents.AddComponent<NetworkObject>();
            contents.AddComponent<NetPlayer>();
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        // 保存直後はGlobalObjectIdHashが未確定のことがあるため、NetworkObject自身の検証処理で確定させる。
        NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
        MethodInfo onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        onValidate?.Invoke(networkObject, null);
        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssets();

        FieldInfo hashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        object hash = hashField != null ? hashField.GetValue(networkObject) : "?";
        Debug.Log($"[NET] NetPlayer prefab ready: {PrefabPath} GlobalObjectIdHash={hash}");
    }
}
