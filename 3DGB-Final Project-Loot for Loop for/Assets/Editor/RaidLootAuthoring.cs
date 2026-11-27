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

public static class RaidLootAuthoring
{
    const string RootName = "Raid_LootSpawnPoints";
    const string Output = "../.utmp/RaidLootQA";
    static readonly Vector2[] Desired = {
        new Vector2(-25,27), new Vector2(-8,31), new Vector2(4,29), new Vector2(14,25),
        new Vector2(-26,13), new Vector2(-25,4), new Vector2(-25,-13),
        new Vector2(-14,9), new Vector2(-12,0),
        new Vector2(11,18), new Vector2(20,16), new Vector2(17,-12), new Vector2(9,-20),
        new Vector2(-7,-20), new Vector2(1,-21),
        new Vector2(-2,14), new Vector2(5,5), new Vector2(5,-7)
    };

    [MenuItem("Tools/Gameplay/Distribute Raid Loot Points")]
    public static void Build()
    {
        RequireEditScene();
        var partial = GameObject.Find(RootName);
        if (partial)
        {
            var previous = partial.GetComponent<RaidLootSpawner>();
            if (!previous || previous.Candidates.Count != 0) throw new InvalidOperationException("Loot points already configured.");
            Object.DestroyImmediate(partial);
            Physics.SyncTransforms();
        }
        var existing = Object.FindObjectsByType<RaidEncounter>(FindObjectsSortMode.None).Where(c => c.isCrate).ToArray();
        if (existing.Length != 7) throw new InvalidOperationException("Expected the original 7 containers before distribution.");
        var root = new GameObject(RootName);
        var spawner = root.AddComponent<RaidLootSpawner>();
        string[] models = { "CardboardBoxes_1", "Crate", "Health", "CardboardBoxes_2", "TrashContainer", "CardboardBoxes_3" };
        string[] labels = { "Abandoned parcels", "Supply crate", "Medical case", "Food supplies", "Salvage bin", "Military supplies" };
        string[] contents = { "Cloth x2 / Spare parts x1", "Ammo x20 / Spare parts x2", "Medical supplies x1", "Rations x2 / Cloth x1", "Scrap metal x3 / Wire x2", "Ammo x20 / Bandages x1" };
        var points = existing.ToList();
        for (int i = 0; i < Desired.Length; i++)
        {
            int kind = i % models.Length;
            float width = kind == 2 ? 1.1f : kind == 4 ? 1.8f : 1.4f;
            Vector3 point = FindClearPoint(Desired[i], width, points.ToArray());
            var go = RaidExampleLayout.Loot(root.transform, models[kind], point.x, point.z, width, (i * 47) % 360, labels[kind], contents[kind]);
            go.name = $"LootPoint_{i + 8:00}_{models[kind]}";
            points.Add(go.GetComponent<RaidEncounter>());
            Physics.SyncTransforms();
        }
        foreach (var loot in points) ConfigureDynamicObstacle(loot);
        string settingsPath = "Assets/Gameplay/RaidLootSettings.asset";
        var settings = AssetDatabase.LoadAssetAtPath<RaidLootSettings>(settingsPath);
        if (!settings) { settings = ScriptableObject.CreateInstance<RaidLootSettings>(); AssetDatabase.CreateAsset(settings, settingsPath); }
        spawner.Configure(settings, points.ToArray());
        EditorUtility.SetDirty(spawner);
        Physics.SyncTransforms();
        RaidExampleLayout.RebuildNavigation();
        Validate();
        EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene); AssetDatabase.SaveAssets();
        RaidExampleLayout.Capture();
        Selection.activeGameObject = root;
        Debug.Log("RAID_LOOT_LAYOUT_SAVED: 25 candidate points; each raid enables exactly 20 at 80%.");
    }

    static Vector3 FindClearPoint(Vector2 desired, float width, RaidEncounter[] existing)
    {
        var offsets = (from x in Enumerable.Range(-10,21) from z in Enumerable.Range(-10,21)
                       orderby x*x + z*z select new Vector2(x,z)).ToArray();
        var spawn = Object.FindFirstObjectByType<PlayerSpawnPoint>().transform.position;
        NavMesh.SamplePosition(spawn, out var source, 2, NavMesh.AllAreas);
        foreach (var offset in offsets)
        {
            var p = new Vector3(desired.x + offset.x, 0, desired.y + offset.y);
            var hits = Physics.RaycastAll(p + Vector3.up * 60, Vector3.down, 100, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.name.Contains("Meadow")).OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0) continue;
            p = hits[0].point;
            if (existing.Any(c => Vector3.Distance(c.transform.position, p) < 5.5f)) continue;
            if (!NavMesh.SamplePosition(p, out var nav, .65f, NavMesh.AllAreas)) continue;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(source.position, nav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            var overlaps = Physics.OverlapBox(p + Vector3.up * .75f, new Vector3(width * .65f, .65f, width * .65f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (overlaps.Any(c => !c.name.Contains("Meadow") && !c.name.Contains("Ground"))) continue;
            return p;
        }
        throw new InvalidOperationException("No clear reachable location near " + desired);
    }

    static void ConfigureDynamicObstacle(RaidEncounter loot)
    {
        // Do not bake a permanent hole: a missing container must leave walkable ground.
        var modifier = loot.GetComponent<NavMeshModifier>();
        if (!modifier) modifier = loot.gameObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
        modifier.overrideArea = false;
        var localBounds = new Bounds(); bool first = true;
        foreach (var filter in loot.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.sharedMesh) continue;
            var b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var v = b.center + Vector3.Scale(b.extents, new Vector3((i&1)==0?-1:1, (i&2)==0?-1:1, (i&4)==0?-1:1));
                var point = loot.transform.InverseTransformPoint(filter.transform.TransformPoint(v));
                if (first) { localBounds = new Bounds(point, Vector3.zero); first = false; } else localBounds.Encapsulate(point);
            }
        }
        var obstacle = loot.GetComponent<NavMeshObstacle>();
        if (!obstacle) obstacle = loot.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = localBounds.center;
        obstacle.size = new Vector3(Mathf.Max(.25f,localBounds.size.x), Mathf.Max(.5f,localBounds.size.y), Mathf.Max(.25f,localBounds.size.z));
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.carvingTimeToStationary = 0;
        EditorUtility.SetDirty(modifier); EditorUtility.SetDirty(obstacle);
    }

    [MenuItem("Tools/Gameplay/Validate Raid Loot")]
    public static void Validate()
    {
        var spawner = Object.FindFirstObjectByType<RaidLootSpawner>();
        if (!spawner || spawner.Candidates.Count == 0) throw new Exception("Expected registered loot points.");
        int total = spawner.Candidates.Count;
        int expected = Mathf.RoundToInt(total * spawner.ContainerFraction);
        if (spawner.Candidates.Distinct().Count() != total) throw new Exception("Duplicate loot registration.");
        var report = new StringBuilder();
        var source = Object.FindFirstObjectByType<PlayerSpawnPoint>().transform.position;
        if (!NavMesh.SamplePosition(source, out var start, 2, NavMesh.AllAreas)) throw new Exception("Spawn off NavMesh.");
        foreach (var loot in spawner.Candidates)
        {
            if (!loot || !loot.isCrate || !loot.GetComponent<NavMeshObstacle>().carving || !loot.GetComponent<NavMeshModifier>().ignoreFromBuild) throw new Exception("Invalid dynamic loot setup.");
            if (!loot.gameObject.activeInHierarchy) continue;
            bool reachable = false;
            for (int i = 0; i < 16 && !reachable; i++)
            {
                float angle = i * Mathf.PI / 8;
                var p = loot.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.8f;
                if (!NavMesh.SamplePosition(p, out var hit, .5f, NavMesh.AllAreas) || Vector3.Distance(hit.position, loot.transform.position) >= 2.5f) continue;
                var path = new NavMeshPath();
                reachable = NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            }
            if (!reachable) throw new Exception("Unreachable loot: " + loot.name);
            report.AppendLine("PASS reachable: " + loot.name + " " + loot.transform.position);
        }
        var patterns = new System.Collections.Generic.HashSet<string>();
        for (int seed = 0; seed < 100; seed++)
        {
            var selected = RaidLootSpawner.SelectSlots(total, spawner.ContainerFraction, seed);
            if (selected.Length != expected || selected.Distinct().Count() != expected || selected.Any(i => i < 0 || i >= total)) throw new Exception("Quota/uniqueness check failed.");
            if (!selected.SequenceEqual(RaidLootSpawner.SelectSlots(total,spawner.ContainerFraction,seed))) throw new Exception("Seed is not reproducible.");
            patterns.Add(string.Join(",", selected.OrderBy(i => i)));
        }
        if (patterns.Count < 2 || RaidLootSpawner.SelectSlots(total,0,1).Length != 0 || RaidLootSpawner.SelectSlots(total,1,1).Length != total || RaidLootSpawner.SelectSlots(0,.8f,1).Length != 0) throw new Exception("Selection boundary check failed.");
        report.AppendLine($"PASS 100 seeded selections: exact {expected}/{total}, unique indices, reproducible seeds, varying layouts; 0%, 100%, empty pool.");
        if (Application.isPlaying)
        {
            int active = spawner.Candidates.Count(c => c.gameObject.activeInHierarchy);
            if (active != expected || spawner.ActiveCount != expected) throw new Exception("Runtime quota mismatch.");
            foreach (var hidden in spawner.Candidates.Where(c => !c.gameObject.activeInHierarchy))
            {
                if (hidden.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeInHierarchy) || hidden.GetComponent<NavMeshObstacle>().isActiveAndEnabled) throw new Exception("Hidden loot still collides.");
                if (!NavMesh.SamplePosition(hidden.transform.position, out var hit, .4f, NavMesh.AllAreas)) throw new Exception("Hidden loot left a NavMesh hole: " + hidden.name);
            }
            report.AppendLine($"PASS Play: {expected} visible/searchable, {total-expected} fully inactive; absent containers leave no navigation holes. Seed=" + spawner.RaidSeed);
        }
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + (Application.isPlaying ? "/play-validation.txt" : "/edit-validation.txt"), report.ToString());
        Debug.Log("RAID_LOOT_VALIDATION_PASS\n" + report);
    }

    static void RequireEditScene()
    {
        if (Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/Game.unity") throw new InvalidOperationException("Open Game in Edit mode first.");
    }
}
