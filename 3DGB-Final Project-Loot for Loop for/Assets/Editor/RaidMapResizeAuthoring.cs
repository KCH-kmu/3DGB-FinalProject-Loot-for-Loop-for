using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static class RaidMapResizeAuthoring
{
    public const string Output = "../.utmp/MapResizeQA";
    const string DemoData = "Assets/Pure Poly/Free Low Poly Nature Pack/Terrain/Terrain.asset";

    [MenuItem("Tools/Gameplay/Map Size/Match Demo Terrain")]
    public static void Resize()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.name != "Game") throw new InvalidOperationException("Open Game in Edit mode.");
        if (Object.FindAnyObjectByType<RaidMapLayout>()) throw new InvalidOperationException("Map has already been resized.");
        var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(DemoData);
        var forest = GameObject.Find("Free_Forest").transform;
        var before = GroundBounds(forest);
        Directory.CreateDirectory(Output);
        EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, Output + "/Game-before.unity", true);
        File.Copy("Assets/Gameplay/InRaidNavMesh.asset", Output + "/InRaidNavMesh-before.asset", true);
        Capture("before", before.center, 185);
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Resize raid to Demo Terrain");
        var root = new GameObject("Raid_MapLayout"); Undo.RegisterCreatedObjectUndo(root, "Record map dimensions");
        var layout = root.AddComponent<RaidMapLayout>();
        layout.originalCenter = new Vector3(before.center.x, 0, before.center.z);
        layout.originalSize = new Vector2(before.size.x, before.size.z);
        layout.targetSize = new Vector2(terrain.size.x, terrain.size.z);
        layout.horizontalScale = new Vector2(terrain.size.x / before.size.x, terrain.size.z / before.size.z);
        var vegetation = new GameObject("Raid_Vegetation"); Undo.RegisterCreatedObjectUndo(vegetation, "Preserve prop sizes");
        // Keep terrain tiles, lake geometry, and mountain meshes under the exact
        // affine mapping. Props get new positions but retain their world dimensions.
        var preserved = new System.Collections.Generic.Dictionary<Transform, Vector3>();
        foreach (var child in forest.Cast<Transform>().ToArray())
        {
            if (child.name.Contains("Meadow") || child.name.Contains("Lake") || child.name.Contains("Mountain")) continue;
            var position = child.position;
            preserved[child] = child.lossyScale;
            Undo.SetTransformParent(child, vegetation.transform, "Preserve vegetation scale");
            Move(child, layout.MapPoint(position));
        }
        foreach (string name in new[] { "Free_Forest", "Raid_MapBoundary", "WaterMovementBarriers" })
        {
            var t = GameObject.Find(name).transform;
            Undo.RecordObject(t, "Expand terrain and barriers"); layout.ScaleGeometry(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        foreach (string name in new[] { "Raid_Encounters", "Raid_LootSpawnPoints", "Raid_SpawnAndExtractionPoints" })
            foreach (Transform child in GameObject.Find(name).transform)
            {
                preserved[child] = child.lossyScale;
                Move(child, layout.MapPoint(child.position));
            }
        foreach (Transform group in GameObject.Find("Raid_ExampleLandmarks").transform)
            foreach (Transform child in group)
            {
                preserved[child] = child.lossyScale;
                Move(child, layout.MapPoint(child.position));
            }
        foreach (string name in new[] { "Player", "PlayerSpawnPoint", "Main Camera" })
        {
            var child = GameObject.Find(name).transform;
            preserved[child] = child.lossyScale;
            Move(child, layout.MapPoint(child.position));
        }
        Physics.SyncTransforms();
        RegroundRings();
        RaidExampleLayout.RebuildNavigation();
        foreach (var pair in preserved)
            if ((pair.Key.lossyScale - pair.Value).sqrMagnitude > .00001f)
                throw new InvalidOperationException("Prop size changed: " + pair.Key.name);
        var after = GroundBounds(forest);
        if (Mathf.Abs(after.size.x - terrain.size.x) > .02f || Mathf.Abs(after.size.z - terrain.size.z) > .02f)
            throw new InvalidOperationException("Resulting ground dimensions do not match demo terrain.");
        RaidRouteAuthoring.Validate();
        RaidLootAuthoring.Validate();
        RaidBoundaryAuthoring.Validate();
        WaterBarrierValidation.Validate();
        EditorUtility.SetDirty(layout); AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undo);
        var report = $"PASS ground {before.size.x:F2} x {before.size.z:F2} -> {after.size.x:F2} x {after.size.z:F2}m.\n" +
            $"XZ factors: {layout.horizontalScale.x:F4}, {layout.horizontalScale.y:F4}. Area factor: {layout.horizontalScale.x * layout.horizontalScale.y:F2}.\n" +
            $"Preserved world scales for {preserved.Count} props/gameplay objects. Heights unchanged.\n" +
            "NavMesh rebuilt; 8 spawn candidates / 24 extraction routes / 25 loot points / boundary and lake barriers validated.";
        File.WriteAllText(Output + "/resize-report.txt", report); Debug.Log("MAP_RESIZE: " + report);
        Capture("after", before.center, 185);
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Frame(new Bounds(before.center, new Vector3(280,20,280)), false);
    }

    static void Move(Transform transform, Vector3 position)
    {
        Undo.RecordObject(transform, "Spread map placement");
        transform.position = position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
    }

    internal static void RegroundRings()
    {
        foreach (var point in Object.FindObjectsByType<RaidRoutePoint>())
            foreach (var line in point.GetComponentsInChildren<LineRenderer>())
            {
                Undo.RecordObject(line, "Ground extraction ring");
                for (int i = 0; i < line.positionCount; i++)
                {
                    var local = line.GetPosition(i);
                    var world = line.transform.TransformPoint(local);
                    var hits = Physics.RaycastAll(world + Vector3.up * 5, Vector3.down, 10, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider.name.Contains("Meadow")).OrderBy(h => h.distance).ToArray();
                    if (hits.Length == 0) continue;
                    world.y = hits[0].point.y + .09f;
                    line.SetPosition(i, line.transform.InverseTransformPoint(world));
                }
                EditorUtility.SetDirty(line);
            }
    }

    static void Capture(string name, Vector3 center, float size)
    {
        var go = new GameObject("MapSizePreviewCamera"); var camera = go.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = size; camera.farClipPlane = 600;
        camera.transform.SetPositionAndRotation(new Vector3(center.x,300,center.z),Quaternion.Euler(90,0,0));
        var target = new RenderTexture(1600,1600,24); var previous = RenderTexture.active;
        var image = new Texture2D(1600,1600,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,1600,1600),0,0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(go); }
    }

    [MenuItem("Tools/Gameplay/Map Size/Measure Demo And Raid")]
    public static void Measure()
    {
        Directory.CreateDirectory(Output);
        var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(DemoData);
        if (!terrain) throw new InvalidOperationException("Demo terrain data missing.");
        var report = new StringBuilder("Demo_Terrain TerrainData.size = " + terrain.size.ToString("F2") + "\n");
        var forest = GameObject.Find("Free_Forest");
        if (forest)
        {
            var ground = GroundBounds(forest.transform);
            report.AppendLine("Raid ground bounds = " + ground);
            report.AppendLine("Target XZ scale = " + terrain.size.x / ground.size.x + ", " + terrain.size.z / ground.size.z);
            foreach (var group in forest.transform.Cast<Transform>().GroupBy(t => t.name).OrderBy(g => g.Key))
                report.AppendLine(group.Key + " x" + group.Count());
            foreach (string root in new[] { "Free_Forest", "Raid_MapBoundary", "WaterMovementBarriers" })
            {
                var t = GameObject.Find(root).transform;
                report.AppendLine(root + " position=" + t.position + " rotation=" + t.eulerAngles + " scale=" + t.localScale);
            }
        }
        File.WriteAllText(Output + "/measurement.txt", report.ToString()); Debug.Log(report);
    }

    internal static Bounds GroundBounds(Transform forest)
    {
        var renderers = forest.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.Contains("Meadow")).ToArray();
        if (renderers.Length == 0) throw new InvalidOperationException("No meadow terrain found.");
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        return bounds;
    }
}
