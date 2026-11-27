using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static class RaidNavigationAuthoring
{
    const string Output = "../.utmp/RaidNavigationQA";

    static NavMeshSurface Surface()
    {
        if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Game.unity")
            throw new InvalidOperationException("Open Game in Edit mode first.");
        return Object.FindFirstObjectByType<NavMeshSurface>();
    }

    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

    // Lake_Ground contains the dry banks surrounding the water, not the water surface.
    static bool IsGround(Component c) => c && c.transform.root.name == "Free_Forest" &&
        (c.name.Contains("Meadow") || c.name.StartsWith("PP_Lake_Ground", StringComparison.Ordinal));

    // Inspect the exact source list used by the installed AI Navigation package.
    static List<NavMeshBuildSource> Sources(NavMeshSurface surface) =>
        (List<NavMeshBuildSource>)typeof(NavMeshSurface).GetMethod("CollectSources", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(surface, null);

    [MenuItem("Tools/Gameplay/Navigation/Audit Walkable Surfaces")]
    public static void Audit() { WriteAudit("audit"); Capture("audit"); }

    [MenuItem("Tools/Gameplay/Navigation/Restrict To Ground And Rebuild")]
    public static void RestrictToGround()
    {
        var surface = Surface();
        if (!File.Exists(Output + "/before.txt")) { WriteAudit("before"); Capture("before"); }
        int notWalkable = NavMesh.GetAreaFromName("Not Walkable");
        int walkable = NavMesh.GetAreaFromName("Walkable");
        var ground = GameObject.Find("Free_Forest").GetComponentsInChildren<Collider>()
            .Where(c => c.enabled && !c.isTrigger && IsGround(c)).Select(c => c.gameObject).Distinct().ToArray();
        if (ground.Length == 0) throw new InvalidOperationException("No meadow/path/lake-bank ground colliders found.");
        Undo.RecordObject(surface, "Restrict raid navigation to ground");
        // Keep all physical obstacles in the build. Ignoring them would bake paths through them.
        surface.defaultArea = notWalkable;
        foreach (var go in ground)
        {
            var modifier = go.GetComponent<NavMeshModifier>();
            if (!modifier) modifier = Undo.AddComponent<NavMeshModifier>(go);
            Undo.RecordObject(modifier, "Allow meadow/path navigation");
            modifier.enabled = true;
            modifier.overrideArea = true;
            modifier.area = walkable;
            modifier.ignoreFromBuild = false;
            modifier.applyToChildren = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(modifier);
        }
        var unexpected = Sources(surface).Where(s => s.area != notWalkable && !IsGround(s.component)).ToArray();
        if (unexpected.Length > 0) throw new InvalidOperationException("Non-ground walkable sources remain: " + unexpected.Length);
        RaidExampleLayout.RebuildNavigation();
        WriteAudit("after");
        FinishAndSave();
    }

    [MenuItem("Tools/Gameplay/Navigation/Validate Ground Navigation And Save")]
    public static void FinishAndSave()
    {
        var surface = Surface();
        int notWalkable = NavMesh.GetAreaFromName("Not Walkable");
        if (surface.defaultArea != notWalkable || surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders ||
            Sources(surface).Any(s => s.area != notWalkable && !IsGround(s.component)))
            throw new InvalidOperationException("Only physical ground surfaces may be walkable; restrict and rebuild first.");
        var adjustments = new StringBuilder();
        foreach (var point in Object.FindObjectsByType<RaidRoutePoint>())
        {
            var old = point.transform.position;
            if (NavMesh.SamplePosition(old, out _, .4f, NavMesh.AllAreas)) continue;
            var position = FindNearbyRoad(old);
            Undo.RecordObject(point.transform, "Move route onto ground navigation");
            point.transform.position = position;
            adjustments.AppendLine($"{point.pointLabel}: {old:F3} -> {position:F3}, moved {Vector3.Distance(old, position):F2}m along the road.");
        }
        if (adjustments.Length > 0)
        {
            RaidMapResizeAuthoring.RegroundRings();
            File.AppendAllText(Output + "/route-adjustments.txt", adjustments.ToString());
        }
        Physics.SyncTransforms();
        RaidDensityAuthoring.Validate();
        WriteAudit("after");
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        Capture("after");
        EditorSceneManager.SaveScene(surface.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = surface.gameObject;
        Debug.Log("RAID_NAV_FIXED: only meadow/path/lake-bank ground explicitly walkable; all other build sources Not Walkable; 8 spawns, 24 exit routes and 50 loot approaches validated.");
    }

    static Vector3 FindNearbyRoad(Vector3 desired)
    {
        if (!NavMesh.SamplePosition(Object.FindAnyObjectByType<PlayerSpawnPoint>().transform.position, out var start, 2, NavMesh.AllAreas))
            throw new InvalidOperationException("Player source is off ground navigation.");
        var offsets = from x in Enumerable.Range(-16, 33) from z in Enumerable.Range(-16, 33)
            orderby x*x+z*z select new Vector3(x*.5f, 0, z*.5f);
        int noRoad=0, offMesh=0, blocked=0, disconnected=0;
        foreach (var offset in offsets)
        {
            var hits = Physics.RaycastAll(desired + offset + Vector3.up * 50, Vector3.down, 80, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => IsGround(h.collider) && h.collider.name.Contains("Meadow_Path")).OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0) { noRoad++; continue; }
            var p = hits[0].point + Vector3.up * .05f;
            if (!NavMesh.SamplePosition(p, out var nav, .35f, NavMesh.AllAreas)) { offMesh++; continue; }
            if (Physics.CheckCapsule(p + Vector3.up*.5f, p + Vector3.up*1.8f, .42f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) { blocked++; continue; }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, nav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { disconnected++; continue; }
            return p;
        }
        throw new InvalidOperationException($"No nearby road: noRoad={noRoad}, offMesh={offMesh}, blocked={blocked}, disconnected={disconnected}");
    }

    static void Capture(string name)
    {
        var triangles = NavMesh.CalculateTriangulation();
        var preview = new GameObject("NavigationAuditPreview") { hideFlags = HideFlags.HideAndDontSave };
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var target = new RenderTexture(1800, 1800, 24);
        var texture = new Texture2D(1800, 1800, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            mesh.vertices = triangles.vertices.Select(v => v + Vector3.up * .15f).ToArray();
            mesh.triangles = triangles.indices;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            material.SetColor("_BaseColor", new Color(.1f, .7f, .95f));
            preview.AddComponent<MeshFilter>().sharedMesh = mesh;
            preview.AddComponent<MeshRenderer>().sharedMaterial = material;
            var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(preview.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 162; camera.farClipPlane = 600;
            var center = Object.FindAnyObjectByType<RaidMapLayout>().originalCenter;
            camera.transform.SetPositionAndRotation(center + Vector3.up * 400, Quaternion.Euler(90, 0, 0));
            camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1800, 1800), 0, 0); texture.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(preview); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            Object.DestroyImmediate(target); Object.DestroyImmediate(texture);
        }
    }

    static void WriteAudit(string name)
    {
        var surface = Surface();
        Physics.SyncTransforms();
        Directory.CreateDirectory(Output);
        var sources = Sources(surface);
        var report = new StringBuilder($"Surface={surface.name}; geometry={surface.useGeometry}; defaultArea={surface.defaultArea}; sources={sources.Count}\n");
        var spawn = Object.FindAnyObjectByType<PlayerSpawnPoint>();
        NavMesh.SamplePosition(spawn.transform.position, out var spawnHit, 2, NavMesh.AllAreas);
        var first = Object.FindAnyObjectByType<RaidRouteDirector>().Points[0];
        NavMesh.SamplePosition(first.transform.position, out var firstHit, 2, NavMesh.AllAreas);
        report.AppendLine($"EDITOR_SPAWN {spawn.transform.position:F3} nearest={spawnHit.position:F3}");
        foreach (var point in Object.FindObjectsByType<RaidRoutePoint>())
        {
            var p = point.transform.position;
            bool found = NavMesh.SamplePosition(p, out var hit, 8, NavMesh.AllAreas);
            report.AppendLine($"ROUTE {point.pointLabel}: {p:F3}, nearest={(found ? hit.position.ToString("F3") : "none")}, distance={(found ? Vector3.Distance(p,hit.position) : -1):F3}");
            var path = new NavMeshPath();
            NavMesh.CalculatePath(spawnHit.position, hit.position, NavMesh.AllAreas, path);
            report.AppendLine("  From editor spawn: " + path.status);
            NavMesh.CalculatePath(firstHit.position, hit.position, NavMesh.AllAreas, path);
            report.AppendLine("  From route 1: " + path.status);
        }
        foreach (var group in sources.GroupBy(s => s.area))
            report.AppendLine($"Area {group.Key}: {group.Count()} sources");
        foreach (var source in sources.Where(s => s.area != 1 && !IsGround(s.component)))
            report.AppendLine($"NON_GROUND_WALKABLE_SOURCE area={source.area} shape={source.shape}: {(source.component ? PathOf(source.component.transform) : "no component")}");
        var mesh = NavMesh.CalculateTriangulation();
        int elevated = 0, unsupported = 0;
        float totalArea = 0, elevatedArea = 0;
        var hitsByObject = new Dictionary<string, int>();
        var samples = new StringBuilder();
        for (int i = 0; i < mesh.indices.Length; i += 3)
        {
            var a = mesh.vertices[mesh.indices[i]];
            var b = mesh.vertices[mesh.indices[i + 1]];
            var c = mesh.vertices[mesh.indices[i + 2]];
            var p = (a + b + c) / 3;
            float area = Vector3.Cross(b - a, c - a).magnitude * .5f;
            totalArea += area;
            var hits = Physics.RaycastAll(p + Vector3.up * 50, Vector3.down, 150, ~0, QueryTriggerInteraction.Ignore);
            var ground = hits.Where(h => IsGround(h.collider)).OrderBy(h => Mathf.Abs(p.y - h.point.y)).ToArray();
            if (ground.Length > 0 && Mathf.Abs(p.y - ground[0].point.y) <= .5f) continue;
            var support = hits.OrderBy(h => Mathf.Abs(p.y - h.point.y)).FirstOrDefault();
            string key = support.collider ? PathOf(support.collider.transform) : "NO_COLLIDER";
            if (!support.collider) unsupported++;
            elevated++; elevatedArea += area;
            hitsByObject[key] = hitsByObject.TryGetValue(key, out int n) ? n + 1 : 1;
            NavMesh.SamplePosition(p, out var detail, 3, NavMesh.AllAreas);
            samples.AppendLine($"{p:F3} area={area:F3} support={key} supportHeight={support.point.y:F3} navSample={detail.position:F3} groundDelta={(ground.Length > 0 ? p.y - ground[0].point.y : 999):F3}");
        }
        // CalculateTriangulation provides coarse polygons; SamplePosition uses the more accurate detail surface.
        // Edge rasterization and coarse heights can differ from collider geometry. These are diagnostics, not proof of walkable obstacle tops.
        report.AppendLine($"Triangles={mesh.indices.Length / 3}; area={totalArea:F2}m2; coarse triangle/ground discrepancies={elevated}; area={elevatedArea:F2}m2; no collider={unsupported}");
        foreach (var pair in hitsByObject.OrderByDescending(x => x.Value)) report.AppendLine($"COARSE_TRIANGLE_DISCREPANCIES {pair.Value}: {pair.Key}");
        File.WriteAllText(Output + "/" + name + ".txt", report.ToString());
        File.WriteAllText(Output + "/" + name + "-samples.txt", samples.ToString());
        Debug.Log($"RAID_NAV_AUDIT: {mesh.indices.Length / 3} triangles, {elevated} coarse-height/edge diagnostics; {sources.Count(s => s.area != 1 && !IsGround(s.component))} walkable non-ground sources. Report: {Output}/{name}.txt");
    }
}
