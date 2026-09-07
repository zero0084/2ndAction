using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Vertical Mode Prototype (2026-09-08) - throwaway verification tool, NOT
// part of the shipped game. This session had no way to see an actual
// rendered Unity Game View (no screen-capture tool available for a native
// Editor window), so this renders the new PortraitCameraRig camera to a
// PNG directly in batchmode/Edit mode instead, as a substitute for eyeballing
// it live. Opens the already-built Main.unity scene WITHOUT ever saving it
// back - a handful of throwaway marker objects are added purely for scale/
// depth reference (this project's terrain/enemies are only ever spawned at
// runtime via Update(), which never runs in pure Edit mode, so the scene
// as originally built has no course content to render at all).
public static class PortraitPreviewCapture
{
    [MenuItem("Tools/2ndAction/Capture Portrait Preview")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        PortraitCameraRig rig = Object.FindFirstObjectByType<PortraitCameraRig>();
        if (player == null || rig == null || rig.cam == null)
        {
            Debug.LogError($"PortraitPreviewCapture: missing dependency (player={player}, rig={rig}, cam={rig?.cam})");
            return;
        }

        player.transform.position = new Vector3(0f, 0f, 0f);

        // Throwaway scale/depth markers - reuse the player's own already-
        // configured visual scale as a rough "same silhouette size" sanity
        // check, plus a ground strip so the "floor receding into the
        // distance" read has something to show at all.
        GameObject groundStrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundStrip.name = "__PreviewGroundStrip";
        groundStrip.transform.position = new Vector3(30f, -0.5f, 0f);
        groundStrip.transform.localScale = new Vector3(80f, 1f, 3f);
        // .material (not .sharedMaterial) - Unity auto-instantiates a
        // per-renderer copy on first access, so this can never bleed the
        // tint into the project's actual shared default material asset.
        groundStrip.GetComponent<Renderer>().material.color = new Color(0.55f, 0.45f, 0.3f);

        // Upright cubes (NOT flat sprites) as enemy/platform stand-ins -
        // a flat, non-billboarded sprite viewed at this camera's oblique
        // angle reads as a weirdly stretched decal (confirmed in an earlier
        // capture), which would be a misleading stand-in for what an actual
        // (billboarded) enemy looks like. A cube's silhouette is a fairer
        // proxy for "roughly enemy-sized object standing on the ground" at
        // a glance.
        CreateMarkerCube("__PreviewMarkerNear", new Vector3(6f, 0.5f, 0f), Color.red);
        CreateMarkerCube("__PreviewMarkerMid", new Vector3(13f, 0.5f, 0f), Color.yellow);
        CreateMarkerCube("__PreviewMarkerFar", new Vector3(24f, 2f, 0f), Color.green);

        // Replicates PortraitCameraRig.LateUpdate()'s own math directly -
        // Edit mode never calls LateUpdate (no Play), so the rig's
        // transform would otherwise still sit at its GameObject's default
        // construction-time identity.
        Transform camT = rig.cam.transform;
        camT.position = player.transform.position + rig.positionOffset;
        Vector3 lookDir = (player.transform.position + rig.lookAtOffset) - camT.position;
        camT.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        int width = 720;
        int height = 1280;
        RenderTexture rt = new RenderTexture(width, height, 24);
        RenderTexture prevTarget = rig.cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        bool prevEnabled = rig.cam.enabled;

        rig.cam.enabled = true;
        rig.cam.targetTexture = rt;
        rig.cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        rig.cam.targetTexture = prevTarget;
        rig.cam.enabled = prevEnabled;
        RenderTexture.active = prevActive;
        rt.Release();

        string outPath = Path.Combine(Application.dataPath, "../../../portrait_preview.png");
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Debug.Log("PortraitPreviewCapture: saved " + Path.GetFullPath(outPath));

        // Never persisted - throwaway markers only, scene intentionally not saved.
        Object.DestroyImmediate(groundStrip);
        Object.DestroyImmediate(GameObject.Find("__PreviewMarkerNear"));
        Object.DestroyImmediate(GameObject.Find("__PreviewMarkerMid"));
        Object.DestroyImmediate(GameObject.Find("__PreviewMarkerFar"));
    }

    static void CreateMarkerCube(string name, Vector3 pos, Color tint)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 1.1f; // roughly Player-height-scale
        go.GetComponent<Renderer>().material.color = tint;
    }
}
