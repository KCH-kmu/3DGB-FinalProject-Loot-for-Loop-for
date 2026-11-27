using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object = UnityEngine.Object;

public static class RaidRouteAuthoring
{
    public const string Output = "../.utmp/RaidRoutesQA";
    const string RootName = "Raid_SpawnAndExtractionPoints";
    static readonly Vector2[] Desired = {
        new Vector2(-16.5f, 30.5f), new Vector2(4.5f, 30.5f),
        new Vector2(15.8f, 15), new Vector2(16, -11),
        new Vector2(3, -18), new Vector2(-14, -15.5f),
        new Vector2(-24, -3), new Vector2(-25, 24)
    };
    static readonly string[] Labels = { "01 North Outpost", "02 North Forest", "03 East Forest", "04 East Checkpoint",
        "05 South Trail", "06 South Bend", "07 West Salvage", "08 West Outpost" };

    [MenuItem("Tools/Gameplay/Raid Routes/Build Eight Points")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.name != "Game") throw new InvalidOperationException("Open Game in Edit mode.");
        if (GameObject.Find(RootName)) throw new InvalidOperationException("Route points already exist; adjust their transforms instead of duplicating them.");
        var spawn = Object.FindAnyObjectByType<PlayerSpawnPoint>();
        if (!spawn) throw new InvalidOperationException("Player spawn binding is missing.");
        Physics.SyncTransforms();
        // Resolve every safe location before mutating the scene.
        var positions = Desired.Select(p => FindRoadPoint(p, spawn.transform.position)).ToArray();
        if (!AssetDatabase.IsValidFolder("Assets/Gameplay/RaidRoutes")) AssetDatabase.CreateFolder("Assets/Gameplay", "RaidRoutes");
        var dormant = Material("Dormant", new Color(.28f,.32f,.34f));
        var entry = Material("Entry", new Color(.1f,.8f,1));
        var exit = Material("Extraction", new Color(.25f,1,.4f));
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create raid routes");
        root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var director = root.AddComponent<RaidRouteDirector>();
        var points = new RaidRoutePoint[positions.Length];
        Vector3 center = positions.Aggregate(Vector3.zero, (sum, p) => sum + p) / positions.Length;
        for (int i = 0; i < points.Length; i++)
        {
            var go = new GameObject("RoutePoint_" + Labels[i].Replace(' ', '_'));
            go.transform.SetParent(root.transform, false);
            var facing = center - positions[i]; facing.y = 0;
            go.transform.SetPositionAndRotation(positions[i], Quaternion.LookRotation(facing));
            var point = go.AddComponent<RaidRoutePoint>(); points[i] = point;
            point.pointLabel = Labels[i];
            point.dormantMaterial = dormant; point.entryMaterial = entry; point.exitMaterial = exit;
            var ring = new GameObject("ExtractionRing").AddComponent<LineRenderer>();
            ring.transform.SetParent(go.transform, false);
            ring.useWorldSpace = false; ring.loop = true; ring.widthMultiplier = .09f;
            ring.positionCount = 48; ring.sharedMaterial = entry;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            for (int j = 0; j < 48; j++)
            {
                float a = j * Mathf.PI * 2 / 48;
                var p = positions[i] + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * point.extractionRadius;
                var hits = Physics.RaycastAll(p + Vector3.up * 4, Vector3.down, 8, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider.name.Contains("Meadow")).OrderBy(h => h.distance).ToArray();
                if (hits.Length > 0) p.y = hits[0].point.y;
                ring.SetPosition(j, go.transform.InverseTransformPoint(p + Vector3.up * .09f));
            }
            // Non-colliding marker, placed beside the road rather than in the spawn capsule.
            var pole = Primitive(go.transform, "RouteBeacon", new Vector3(1.9f, 1.2f, 0), new Vector3(.12f, 2.4f, .12f), entry);
            var flag = Primitive(go.transform, "RouteFlag", new Vector3(1.6f, 2.2f, 0), new Vector3(.65f, .4f, .10f), entry);
            point.markers = new Renderer[] { ring, pole, flag };
        }
        director.Configure(points);
        Undo.RecordObject(spawn, "Bind random raid spawn"); spawn.raidRoutes = director; EditorUtility.SetDirty(spawn);
        AssetDatabase.SaveAssets();
        Validate();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Capture();
        Selection.activeGameObject = root;
        Debug.Log("RAID_ROUTES_SAVED: eight road-edge points; random entry; three opposite exits.");
    }

    static Material Material(string name, Color color)
    {
        string path = "Assets/Gameplay/RaidRoutes/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Gameplay/LootOutline.mat");
        if (!source) throw new InvalidOperationException("Existing URP marker material is missing.");
        material = new Material(source) { name = name };
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Tools/Gameplay/Raid Routes/Anchor Rings To Points")]
    public static void AnchorRings()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        foreach (var point in Object.FindObjectsByType<RaidRoutePoint>())
            foreach (var ring in point.GetComponentsInChildren<LineRenderer>())
            {
                if (!ring.useWorldSpace) continue;
                Undo.RecordObject(ring, "Anchor route ring");
                var positions = new Vector3[ring.positionCount]; ring.GetPositions(positions);
                for (int i = 0; i < positions.Length; i++) positions[i] = ring.transform.InverseTransformPoint(positions[i]);
                ring.useWorldSpace = false; ring.SetPositions(positions); EditorUtility.SetDirty(ring);
            }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }

    static Renderer Primitive(Transform parent, string name, Vector3 localPosition, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = localPosition; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        return renderer;
    }

    static Vector3 FindRoadPoint(Vector2 desired, Vector3 source)
    {
        if (!NavMesh.SamplePosition(source, out var start, 2, NavMesh.AllAreas)) throw new InvalidOperationException("Original spawn is off navigation.");
        var offsets = (from x in Enumerable.Range(-6,13) from z in Enumerable.Range(-6,13)
            orderby x*x+z*z select new Vector2(x*.5f,z*.5f)).ToArray();
        foreach (var offset in offsets)
        {
            var p = new Vector3(desired.x + offset.x, 0, desired.y + offset.y);
            var hits = Physics.RaycastAll(p + Vector3.up * 50, Vector3.down, 80, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.name.Contains("Meadow_Path")).OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0) continue;
            p = hits[0].point + Vector3.up * .05f;
            if (!NavMesh.SamplePosition(p, out var nav, .35f, NavMesh.AllAreas)) continue;
            if (Physics.CheckCapsule(p + Vector3.up * .5f, p + Vector3.up * 1.8f, .42f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (Object.FindObjectsByType<RaidEncounter>().Any(e => !e.isCrate && Vector3.Distance(e.transform.position,p) < 9)) continue;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, nav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            return p;
        }
        throw new InvalidOperationException("No clear road spawn near " + desired);
    }

    [MenuItem("Tools/Gameplay/Raid Routes/Validate Routes")]
    public static void Validate()
    {
        var director = Object.FindAnyObjectByType<RaidRouteDirector>();
        if (!director || director.Points.Count != 8) throw new InvalidOperationException("Expected eight routes.");
        var positions = director.Points.Select(p => p.transform.position).ToArray();
        var center = positions.Aggregate(Vector3.zero, (sum,p) => sum+p) / positions.Length;
        var report = new StringBuilder();
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (!NavMesh.SamplePosition(p, out var source, .4f, NavMesh.AllAreas)) throw new InvalidOperationException("Off NavMesh: " + i);
            if (Physics.CheckCapsule(p + Vector3.up * .5f, p + Vector3.up * 1.8f, .42f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("Blocked spawn: " + i);
            var exits = RaidRouteDirector.SelectOppositeExits(positions, i);
            if (exits.Length != 3 || exits.Distinct().Count() != 3 || exits.Contains(i)) throw new InvalidOperationException("Invalid extraction selection.");
            foreach (int j in exits)
            {
                var a = positions[i] - center; a.y = 0; var b = positions[j] - center; b.y = 0;
                if (Vector3.Dot(a.normalized, b.normalized) >= 0) throw new InvalidOperationException($"Exit {j+1} is not opposite entry {i+1}.");
                if (!NavMesh.SamplePosition(positions[j], out var target, .4f, NavMesh.AllAreas)) throw new InvalidOperationException("Exit off navigation.");
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(source.position, target.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("Unreachable extraction.");
            }
            report.AppendLine($"PASS {director.Points[i].pointLabel} {p:F2} -> exits {string.Join(",",exits.Select(j=>j+1))}; clear capsule, opposite half, 3 complete routes.");
        }
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/validation.txt", report.ToString());
        Debug.Log("RAID_ROUTE_VALIDATION: " + report);
    }

    [MenuItem("Tools/Gameplay/Raid Routes/Audit Map")]
    public static void Audit()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            if (r.name.IndexOf("Meadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.sharedMaterials.Any(m => m && (m.name.Contains("Ground") || m.name.Contains("Road"))))
                report.AppendLine(r.name + " position=" + r.transform.position + " bounds=" + r.bounds + " materials=" + string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null")));
        File.WriteAllText(Output + "/map-audit.txt", report.ToString());
        Capture();
        Debug.Log("RAID_MAP_AUDIT: " + report);
    }

    [MenuItem("Tools/Gameplay/Raid Routes/Capture Overview")]
    public static void Capture()
    {
        Directory.CreateDirectory(Output);
        var go = new GameObject("RoutePreviewCamera");
        var camera = go.AddComponent<Camera>();
        var layout = Object.FindAnyObjectByType<RaidMapLayout>();
        camera.orthographic = true; camera.orthographicSize = layout ? Mathf.Max(layout.targetSize.x, layout.targetSize.y) * .74f : 43;
        camera.farClipPlane = 600;
        var center = layout ? layout.originalCenter : new Vector3(-3,0,2);
        camera.transform.SetPositionAndRotation(center + Vector3.up * 400, Quaternion.Euler(90, 0, 0));
        var target = new RenderTexture(1600, 1600, 24);
        var previous = RenderTexture.active;
        var image = new Texture2D(1600, 1600, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0,0,1600,1600),0,0); image.Apply();
            File.WriteAllBytes(Output + "/overview.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(go);
        }
    }
}
