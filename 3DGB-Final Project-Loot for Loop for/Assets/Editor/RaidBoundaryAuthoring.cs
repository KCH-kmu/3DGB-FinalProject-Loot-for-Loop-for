using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public static class RaidBoundaryAuthoring
{
    const string RootName = "Raid_MapBoundary";
    const string PrefabFolder = "Assets/Pure Poly/Free Low Poly Nature Pack/Prefabs/";
    // Clockwise, through the existing mountain belt and the newly closed north edge.
    static readonly Vector3[] Perimeter = {
        new Vector3(-38, 0, 28), new Vector3(-30, 0, 32),
        new Vector3(-15, 0, 35), new Vector3(0, 0, 35),
        new Vector3(15, 0, 33), new Vector3(27, 0, 27),
        new Vector3(32, 0, 13), new Vector3(34, 0, -10),
        new Vector3(25, 0, -23), new Vector3(9, 0, -32),
        new Vector3(-13, 0, -32), new Vector3(-29, 0, -23),
        new Vector3(-38, 0, -9), new Vector3(-41, 0, 10)
    };

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        Rebuild();
    }

    public static void VerifySavedScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        Validate();
        var spawn = Object.FindFirstObjectByType<PlayerSpawnPoint>();
        if (!NavMesh.SamplePosition(spawn.transform.position, out var start, 2, NavMesh.AllAreas))
            throw new System.InvalidOperationException("Spawn is no longer on the raid NavMesh.");
        foreach (var encounter in Object.FindObjectsByType<RaidEncounter>(FindObjectsSortMode.None))
        {
            if (!NavMesh.SamplePosition(encounter.transform.position, out var end, 3, NavMesh.AllAreas))
                throw new System.InvalidOperationException("Encounter is unreachable: " + encounter.name);
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                throw new System.InvalidOperationException("Raid route is disconnected: " + encounter.name);
        }

        // Walk toward both former northern exits with the real scenery colliders.
        var probe = new GameObject("NorthExitWalkingProbe");
        try
        {
            var controller = probe.AddComponent<CharacterController>();
            controller.height = 2.1f;
            controller.center = Vector3.up * 1.05f;
            controller.radius = .32f;
            controller.skinWidth = .032f;
            controller.stepOffset = .3f;
            controller.slopeLimit = 45;
            foreach (float x in new[] { -16f, 4f })
            {
                controller.enabled = false;
                var ground = Physics.RaycastAll(new Vector3(x, 50, 24), Vector3.down, 80)
                    .Where(h => h.collider.name.Contains("Meadow")).OrderBy(h => h.distance).First();
                probe.transform.position = ground.point + Vector3.up * .1f;
                controller.enabled = true;
                Physics.SyncTransforms();
                float verticalSpeed = 0;
                for (int step = 0; step < 500; step++)
                {
                    if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
                    verticalSpeed = Mathf.Max(-30, verticalSpeed + Physics.gravity.y * .02f);
                    controller.Move((Vector3.forward * 7 + Vector3.up * verticalSpeed) * .02f);
                    if (probe.transform.position.y < -2 || probe.transform.position.z > 36)
                        throw new System.InvalidOperationException("Northern exit escape/fall: " + probe.transform.position);
                }
                Debug.Log("BOUNDARY_NORTH_WALK_PASS: x=" + x + ", stopped at " + probe.transform.position);
            }
        }
        finally { Object.DestroyImmediate(probe); }
        Debug.Log("BOUNDARY_SAVED_SCENE_PASS: saved collisions, spawn-to-encounter NavMesh routes and both northern exits verified.");
    }

    [MenuItem("Tools/Gameplay/Rebuild Raid Map Boundary")]
    public static void Rebuild()
    {
        if (Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/Game.unity")
            throw new System.InvalidOperationException("Open Game in Edit mode before rebuilding its boundary.");
        var existing = GameObject.Find(RootName);
        if (existing) Object.DestroyImmediate(existing);
        var root = new GameObject(RootName);
        var modifier = root.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");

        // Complete the open end of the horseshoe with the same mountain prefabs.
        AddScenery(root.transform, "PP_Forest_Mountain_Moss_02", new Vector3(-30, -.35f, 39), .50f, 37);
        AddScenery(root.transform, "PP_Forest_Mountain_Moss_01", new Vector3(-17, -.45f, 42), .56f, 118);
        AddScenery(root.transform, "PP_Forest_Mountain_Moss_02", new Vector3(-3, -.35f, 43), .60f, 221);
        AddScenery(root.transform, "PP_Forest_Mountain_Moss_01", new Vector3(11, -.25f, 41), .55f, 294);
        AddScenery(root.transform, "PP_Forest_Mountain_Moss_02", new Vector3(24, -.25f, 36), .50f, 154);

        // Smaller outcrops soften the join between meadow/path ends and the ridge.
        for (int i = 0; i < 7; i++)
        {
            float x = -25 + i * 7;
            float z = 33 + Mathf.Sin(i * .6f) * 1.8f;
            AddScenery(root.transform, i % 2 == 0 ? "PP_Rock_Moss_Grown_11" : "PP_Rock_Moss_Grown_09",
                new Vector3(x, .1f, z), .38f + (i % 3) * .045f, i * 67);
        }

        // Overlapping vertical cores inside the rock belt close seams and stop
        // climbing/sliding over sloped mountain meshes. No per-frame runtime logic.
        var collisionRoot = new GameObject("Continuous_Cliff_Collision");
        collisionRoot.transform.SetParent(root.transform, false);
        for (int i = 0; i < Perimeter.Length; i++)
        {
            var a = Perimeter[i];
            var b = Perimeter[(i + 1) % Perimeter.Length];
            var wall = new GameObject("Cliff_Core_" + i.ToString("00"));
            wall.transform.SetParent(collisionRoot.transform, false);
            wall.transform.position = (a + b) * .5f + Vector3.up * 6;
            wall.transform.rotation = Quaternion.LookRotation(b - a);
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.8f, 28, Vector3.Distance(a, b) + 2);
        }
        Physics.SyncTransforms();
        Validate();
        RebuildNavigation();
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("../.utmp/BoundaryQA");
        Capture("after-overview", new Vector3(0, 110, 2), new Vector3(90, 0, 0), 65);
        Capture("after-north", new Vector3(-5, 44, 1), new Vector3(52, 0, 0), 32);
        Debug.Log("BOUNDARY_BUILD_COMPLETE: 5 mountains, 7 rocks, 14 overlapping cliff cores; scene and NavMesh saved.");
    }

    static void AddScenery(Transform parent, string name, Vector3 position, float scale, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + name + ".prefab");
        if (!prefab) throw new System.InvalidOperationException("Missing boundary prefab: " + name);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = "Boundary_" + name;
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        instance.transform.localScale = Vector3.one * scale;
        foreach (var collider in instance.GetComponentsInChildren<Collider>()) collider.enabled = true;
    }

    static void RebuildNavigation()
    {
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (!surface || !surface.navMeshData) throw new System.InvalidOperationException("Raid NavMesh is missing.");
        var savedData = surface.navMeshData;
        surface.BuildNavMesh();
        var rebuilt = surface.navMeshData;
        if (!rebuilt || rebuilt == savedData) throw new System.InvalidOperationException("Raid NavMesh rebuild failed.");
        // Keep the original asset GUID and all references to it.
        surface.RemoveData();
        EditorUtility.CopySerialized(rebuilt, savedData);
        surface.navMeshData = savedData;
        surface.AddData();
        EditorUtility.SetDirty(savedData);
        EditorUtility.SetDirty(surface);
        Object.DestroyImmediate(rebuilt);
    }

    [MenuItem("Tools/Gameplay/Validate Raid Map Boundary")]
    public static void Validate()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Validate in Edit mode.");
        var root = GameObject.Find(RootName);
        if (!root) throw new System.InvalidOperationException("Raid boundary is missing.");
        var cores = root.transform.Find("Continuous_Cliff_Collision");
        if (!cores || cores.GetComponentsInChildren<BoxCollider>().Length != Perimeter.Length)
            throw new System.InvalidOperationException("Incomplete boundary collision.");
        var otherColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
            .Where(c => c.enabled && !c.transform.IsChildOf(cores)).ToArray();
        var probe = new GameObject("BoundaryValidationProbe");
        int checks = 0;
        try
        {
            // Isolate the boundary so another tree/rock cannot hide a gap.
            foreach (var collider in otherColliders) collider.enabled = false;
            var controller = probe.AddComponent<CharacterController>();
            controller.height = 2.1f;
            controller.center = Vector3.up * 1.05f;
            controller.radius = .32f;
            controller.skinWidth = .032f;
            controller.stepOffset = .3f;
            controller.slopeLimit = 45;
            for (int i = 0; i < Perimeter.Length; i++)
            {
                var a = Perimeter[i];
                var b = Perimeter[(i + 1) % Perimeter.Length];
                var along = (b - a).normalized;
                var outward = new Vector3(-along.z, 0, along.x);
                int samples = Mathf.CeilToInt(Vector3.Distance(a, b) / .5f);
                for (int sample = 0; sample <= samples; sample++)
                {
                    foreach (float height in new[] { .2f, 6f, 12f })
                    {
                        CheckCrossing(controller, Vector3.Lerp(a, b, (float)sample / samples), outward, height);
                        checks++;
                    }
                }
                var incoming = (a - Perimeter[(i + Perimeter.Length - 1) % Perimeter.Length]).normalized;
                var cornerOutward = (outward + new Vector3(-incoming.z, 0, incoming.x)).normalized;
                CheckCrossing(controller, a, cornerOutward, .2f);
                checks++;
            }
        }
        finally
        {
            Object.DestroyImmediate(probe);
            foreach (var collider in otherColliders) if (collider) collider.enabled = true;
            Physics.SyncTransforms();
        }
        Directory.CreateDirectory("../.utmp/BoundaryQA");
        string result = "PASS: " + checks + " CharacterController crossing attempts; perimeter sampled every <=0.5m at 3 heights, including diagonal corners. Unrelated colliders disabled during checks.";
        File.WriteAllText("../.utmp/BoundaryQA/validation.txt", result);
        Debug.Log("BOUNDARY_VALIDATION_" + result);
    }

    static void CheckCrossing(CharacterController controller, Vector3 edge, Vector3 outward, float height)
    {
        controller.enabled = false;
        controller.transform.position = edge - outward * 3 + Vector3.up * height;
        controller.enabled = true;
        Physics.SyncTransforms();
        for (int step = 0; step < 35; step++) controller.Move(outward * .35f);
        if (Vector3.Dot(controller.transform.position - edge, outward) > 0)
            throw new System.InvalidOperationException("Boundary escape at " + edge + ", height=" + height);
    }

    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        Directory.CreateDirectory("../.utmp/BoundaryQA");
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        var lines = renderers.Where(r => r.name.Contains("Meadow") || r.name.Contains("Mountain") || r.name.Contains("Water"))
            .Select(r => r.name + " position=" + r.transform.position.ToString("F2") + " min=" + r.bounds.min.ToString("F2") + " max=" + r.bounds.max.ToString("F2"));
        File.WriteAllLines("../.utmp/BoundaryQA/bounds.txt", lines);
        Capture("before", new Vector3(0, 100, 0), new Vector3(90, 0, 0), 65);
        Debug.Log("BOUNDARY_INSPECT_COMPLETE");
    }

    static void Capture(string name, Vector3 position, Vector3 angles, float size)
    {
        var go = new GameObject("Boundary preview camera");
        var camera = go.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(position, Quaternion.Euler(angles));
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.farClipPlane = 500;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .20f, .23f);
        var target = new RenderTexture(1400, 1400, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1400, 1400, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1400, 1400), 0, 0);
        image.Apply();
        File.WriteAllBytes("../.utmp/BoundaryQA/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(go);
    }
}
