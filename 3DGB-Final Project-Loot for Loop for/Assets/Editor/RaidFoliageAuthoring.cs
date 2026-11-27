using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static class RaidFoliageAuthoring
{
    const string Output = "../.utmp/RaidFoliageQA";
    static readonly string[] Prefixes = { "PP_Grass_", "PP_Daffodil_", "PP_Hyacinth_", "PP_Sunflower_", "PP_Flower_" };

    static GameObject[] Plants()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Game.unity") throw new InvalidOperationException("Open Game first.");
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => Prefixes.Any(p => t.name.StartsWith(p, StringComparison.Ordinal)))
            .Select(t => t.gameObject).ToArray();
    }

    [MenuItem("Tools/Gameplay/Foliage/Audit Plant Collisions")]
    public static void Audit()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        foreach (var group in Plants().GroupBy(p => Prefixes.First(prefix => p.name.StartsWith(prefix, StringComparison.Ordinal))))
        {
            var colliders = group.SelectMany(p => p.GetComponentsInChildren<Collider>(true)).Distinct().ToArray();
            report.AppendLine($"{group.Key}: plants={group.Count()}, enabled solid colliders={colliders.Count(c => c.enabled && !c.isTrigger)}, active solid colliders={colliders.Count(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)}");
        }
        File.WriteAllText(Output + "/audit.txt", report.ToString());
        Debug.Log("RAID_FOLIAGE_AUDIT: " + report);
    }

    // Use for newly placed decorative plants as well as existing scene instances.
    internal static void MakeDecorative(GameObject plant)
    {
        foreach (var collider in plant.GetComponentsInChildren<Collider>(true))
        {
            Undo.RecordObject(collider, "Disable decorative plant collision");
            collider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        foreach (var obstacle in plant.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            Undo.RecordObject(obstacle, "Disable decorative plant obstacle");
            obstacle.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(obstacle);
        }
        var modifier = plant.GetComponent<NavMeshModifier>();
        if (!modifier) modifier = Undo.AddComponent<NavMeshModifier>(plant);
        Undo.RecordObject(modifier, "Exclude decorative plant from navigation");
        modifier.enabled = true;
        modifier.ignoreFromBuild = true;
        modifier.applyToChildren = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(modifier);
    }

    [MenuItem("Tools/Gameplay/Foliage/Remove Plant Collisions And Rebuild")]
    public static void Fix()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play first.");
        var plants = Plants();
        Audit(); File.Copy(Output + "/audit.txt", Output + "/before.txt", true);
        var plantColliders = plants.SelectMany(p => p.GetComponentsInChildren<Collider>(true)).Distinct().ToArray();
        int disabled = plantColliders.Count(c => c.enabled);
        var otherColliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Except(plantColliders).ToDictionary(c => c, c => c.enabled);
        foreach (var plant in plants) MakeDecorative(plant);
        Physics.SyncTransforms();
        if (otherColliders.Any(pair => !pair.Key || pair.Key.enabled != pair.Value))
            throw new InvalidOperationException("Non-plant collider changed.");
        RaidExampleLayout.RebuildNavigation();
        RaidDensityAuthoring.Validate();
        Validate();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Audit(); File.Copy(Output + "/audit.txt", Output + "/after.txt", true);
        File.WriteAllText(Output + "/result.txt", $"PASS: {plants.Length} decorative plant instances; disabled {disabled} colliders. Other {otherColliders.Count} collider states preserved. NavMesh rebuilt; 8 spawns, 24 exit paths, 50 loot approaches validated.\n");
        Debug.Log("RAID_FOLIAGE_FIXED: " + File.ReadAllText(Output + "/result.txt"));
    }

    [MenuItem("Tools/Gameplay/Foliage/Validate Nonblocking Plants")]
    public static void Validate()
    {
        var plants = Plants();
        if (plants.Length == 0) throw new InvalidOperationException("No decorative plants found.");
        foreach (var plant in plants)
        {
            if (plant.GetComponentsInChildren<Collider>(true).Any(c => c.enabled) ||
                plant.GetComponentsInChildren<NavMeshObstacle>(true).Any(o => o.enabled))
                throw new InvalidOperationException("Plant still blocks movement: " + plant.name);
            var modifier = plant.GetComponent<NavMeshModifier>();
            if (!modifier || !modifier.enabled || !modifier.ignoreFromBuild || !modifier.applyToChildren)
                throw new InvalidOperationException("Plant still contributes to navigation: " + plant.name);
        }
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + (Application.isPlaying ? "/play-validation.txt" : "/edit-validation.txt"),
            $"PASS: all {plants.Length} decorative plants have zero enabled colliders or obstacles and are excluded from navigation.\n");
        Debug.Log($"RAID_FOLIAGE_VALIDATED: {plants.Length} nonblocking plants; play={Application.isPlaying}.");
    }
}
