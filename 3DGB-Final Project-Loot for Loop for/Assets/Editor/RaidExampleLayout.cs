using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class RaidExampleLayout
{
    const string Models = "Assets/ThirdParty/ToonShooter/Environment/FBX/";
    const string Output = "../.utmp/ExampleLayout";
    const string RootName = "Raid_ExampleLandmarks";

    [MenuItem("Tools/Gameplay/Build Example Landmarks")]
    public static void Build()
    {
        if (Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/Game.unity")
            throw new System.InvalidOperationException("Open Game in Edit mode first.");
        if (GameObject.Find(RootName)) throw new System.InvalidOperationException("Example landmarks already exist.");
        var root = new GameObject(RootName);
        var displaced = new GameObject("Displaced_Vegetation_Restore_To_Free_Forest");
        displaced.transform.SetParent(root.transform);
        displaced.SetActive(false);
        var supply = new GameObject("01_Northwest_SupplyOutpost"); supply.transform.SetParent(root.transform);
        Place(supply.transform, "Structure_1", -19, 25, 8, 180, displaced.transform);
        Place(supply.transform, "Container_Small", -11, 27, 4, 15, displaced.transform);
        Place(supply.transform, "Pallet", -14, 26, 1.8f, 0);
        Loot(supply.transform, "Health", -20, 18, 1.1f, 20, "Medical case", "Medical supplies x1");
        Loot(supply.transform, "CardboardBoxes_2", -12, 20, 1.7f, 25, "Supply parcels", "Rations x2 / Cloth x3");
        var checkpoint = new GameObject("02_East_TankCheckpoint"); checkpoint.transform.SetParent(root.transform);
        Place(checkpoint.transform, "Tank", 18, 6, 4.6f, 225, displaced.transform);
        Place(checkpoint.transform, "SackTrench", 20, 2, 3.8f, 20, displaced.transform);
        Place(checkpoint.transform, "Barrier_Single", 15, 9, 2.5f, 90);
        Loot(checkpoint.transform, "Health", 14, 3, 1.1f, -30, "Field medical case", "Bandages x2");
        Loot(checkpoint.transform, "CardboardBoxes_3", 20, -3, 1.8f, 30, "Military supplies", "Ammo x20 / Spare parts x2");
        var salvage = new GameObject("03_Southwest_SalvageSite"); salvage.transform.SetParent(root.transform);
        Place(salvage.transform, "Debris_BrokenCar", -18, -9, 5, -35, displaced.transform);
        Place(salvage.transform, "Debris_Tires", -21, -11, 1.8f, 20);
        Place(salvage.transform, "GasCan", -16, -9, .6f, 0);
        Loot(salvage.transform, "TrashContainer", -21, -5, 2.2f, 110, "Salvage bin", "Scrap metal x3 / Wire x2");
        Loot(salvage.transform, "CardboardBoxes_1", -14, -14, 1.6f, -15, "Abandoned parcels", "Cloth x2 / Spare parts x1");
        ClearLootFootprints();
        Physics.SyncTransforms();
        RebuildNavigation();
        Validate();
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(-3, 1, 7), new Vector3(65, 20, 65)), false);
        Capture();
        Debug.Log("EXAMPLE_LAYOUT_SAVED: 3 landmarks, 6 searchable examples. Loot is a preview, not persistent inventory.");
    }

    internal static void RebuildNavigation()
    {
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        var saved = surface.navMeshData;
        surface.BuildNavMesh();
        var rebuilt = surface.navMeshData;
        if (!rebuilt || rebuilt == saved) throw new System.InvalidOperationException("NavMesh rebuild failed.");
        surface.RemoveData(); EditorUtility.CopySerialized(rebuilt, saved);
        surface.navMeshData = saved; surface.AddData();
        EditorUtility.SetDirty(saved); EditorUtility.SetDirty(surface); Object.DestroyImmediate(rebuilt);
    }

    static void ClearLootFootprints()
    {
        var root = GameObject.Find(RootName);
        var displaced = root.transform.Find("Displaced_Vegetation_Restore_To_Free_Forest");
        var forest = GameObject.Find("Free_Forest").transform;
        var vegetation = GameObject.Find("Raid_Vegetation");
        foreach (var loot in root.GetComponentsInChildren<RaidEncounter>())
        {
            var bounds = BoundsOf(loot.gameObject); bounds.Expand(1f);
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.transform.IsChildOf(forest) && (!vegetation || !renderer.transform.IsChildOf(vegetation.transform))) continue;
                string n = renderer.name;
                if (!(n.Contains("Tree") || n.Contains("Rock") || n.Contains("Grass") || n.Contains("Flower")) || n.Contains("Mountain")) continue;
                if (!bounds.Intersects(renderer.bounds)) continue;
                var original = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject) ?? renderer.gameObject;
                original.transform.SetParent(displaced, true);
            }
        }
    }

    [MenuItem("Tools/Gameplay/Clear Example Loot Footprints")]
    public static void PolishLootFootprints()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode first.");
        ClearLootFootprints(); Physics.SyncTransforms(); RebuildNavigation(); Validate();
        var scene = GameObject.Find(RootName).scene;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Capture();
    }

    internal static GameObject Place(Transform parent, string model, float x, float z, float width, float yaw, Transform displaced = null)
    {
        var wrapper = new GameObject(model);
        wrapper.transform.SetParent(parent);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Models + model + ".fbx");
        if (!prefab) throw new System.InvalidOperationException("Missing example model: " + model);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, wrapper.transform);
        var bounds = BoundsOf(instance);
        instance.transform.localScale *= width / Mathf.Max(bounds.size.x, bounds.size.z);
        bounds = BoundsOf(instance);
        instance.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        wrapper.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var ground = Physics.RaycastAll(new Vector3(x, 60, z), Vector3.down, 100)
            .Where(h => h.collider.name.Contains("Meadow") || h.collider.name.Contains("Ground"))
            .OrderBy(h => h.distance).First();
        wrapper.transform.position = ground.point - Vector3.up * .03f;
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
        }
        var nav = wrapper.AddComponent<NavMeshModifier>(); nav.overrideArea = true; nav.area = 1;
        if (displaced)
        {
            bounds = BoundsOf(instance); bounds.Expand(.5f);
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.transform.IsChildOf(parent.root)) continue;
                string n = renderer.name;
                if (!(n.Contains("Tree") || n.Contains("Rock") || n.Contains("Grass") || n.Contains("Flower")) || n.Contains("Mountain")) continue;
                if (!bounds.Intersects(renderer.bounds)) continue;
                var original = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject) ?? renderer.gameObject;
                var vegetation = GameObject.Find("Raid_Vegetation");
                if (original.transform.IsChildOf(GameObject.Find("Free_Forest").transform) || (vegetation && original.transform.IsChildOf(vegetation.transform))) original.transform.SetParent(displaced, true);
            }
        }
        return wrapper;
    }

    internal static GameObject Loot(Transform parent, string model, float x, float z, float width, float yaw, string label, string preview)
    {
        var go = Place(parent, model, x, z, width, yaw);
        go.name = "Loot_" + model;
        var encounter = go.AddComponent<RaidEncounter>(); encounter.isCrate = true;
        encounter.crateLabel = label; encounter.lootPreview = preview;
        var marker = new GameObject("Loot_Outline"); marker.transform.SetParent(go.transform, true);
        var line = marker.AddComponent<LineRenderer>();
        line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Gameplay/LootOutline.mat");
        line.useWorldSpace = false; line.loop = true; line.widthMultiplier = .035f; line.positionCount = 4;
        float half = width * .55f, y = go.transform.position.y + .06f;
        var points = new[] {new Vector3(-half,0,-half),new Vector3(-half,0,half),new Vector3(half,0,half),new Vector3(half,0,-half)};
        marker.transform.position = new Vector3(go.transform.position.x, y, go.transform.position.z);
        marker.transform.rotation = go.transform.rotation;
        line.SetPositions(points);
        return go;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    [MenuItem("Tools/Gameplay/Validate Example Landmarks")]
    public static void Validate()
    {
        var spawn = Object.FindFirstObjectByType<PlayerSpawnPoint>();
        if (!NavMesh.SamplePosition(spawn.transform.position, out var start, 2, NavMesh.AllAreas)) throw new System.Exception("Spawn off NavMesh");
        var report = new StringBuilder();
        foreach (var loot in GameObject.Find(RootName).GetComponentsInChildren<RaidEncounter>())
        {
            bool reachable = false;
            for (int i = 0; i < 16 && !reachable; i++)
            {
                float angle = i * Mathf.PI / 8;
                var p = loot.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.8f;
                if (!NavMesh.SamplePosition(p, out var target, .7f, NavMesh.AllAreas) || Vector3.Distance(target.position, loot.transform.position) >= 2.5f) continue;
                var path = new NavMeshPath();
                reachable = NavMesh.CalculatePath(start.position, target.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            }
            if (!reachable) throw new System.Exception("Unreachable example loot: " + loot.name + " " + loot.transform.position);
            report.AppendLine("PASS reachable: " + loot.crateLabel + " " + loot.transform.position);
        }
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/validation.txt", report.ToString());
        Debug.Log("EXAMPLE_LAYOUT_VALIDATION_PASS\n" + report);
    }

    [MenuItem("Tools/Gameplay/Capture Example Landmarks")]
    public static void Capture()
    {
        Directory.CreateDirectory(Output);
        CaptureShot("overview", new Vector3(-3, 67, -33), 43);
        CaptureShot("supply-outpost", new Vector3(-16, 20, 12), 11);
        CaptureShot("tank-checkpoint", new Vector3(18, 18, -6), 10);
        CaptureShot("salvage-site", new Vector3(-18, 18, -19), 10);
    }

    internal static void CaptureShot(string name, Vector3 position, float size)
    {
        var go = new GameObject("ExamplePreviewCamera");
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = size; camera.farClipPlane = 300;
        camera.transform.SetPositionAndRotation(position, Quaternion.Euler(60,0,0));
        var target = new RenderTexture(1600, 1200, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render(); camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(1600,1200,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1600,1200),0,0); image.Apply();
        File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(go);
    }

    static double deadline, nextStep;
    static int interactionStep;
    static RaidEncounter[] previewLoot;
    static bool testing, initialized, oldRunInBackground;
    static InputSettings.BackgroundBehavior oldBackground;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;

    [MenuItem("Tools/Gameplay/Check Example Loot In Play")]
    public static void CheckInteractions()
    {
        if (!EditorApplication.isPlaying) throw new System.InvalidOperationException("Enter Play mode first.");
        if (testing) return;
        oldRunInBackground = Application.runInBackground;
        oldBackground = InputSystem.settings.backgroundBehavior;
        oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.EnableDevice(Keyboard.current);
        var spawner = Object.FindFirstObjectByType<RaidLootSpawner>();
        previewLoot = spawner ? spawner.Candidates.Where(c => c && c.gameObject.activeInHierarchy).ToArray() : GameObject.Find(RootName).GetComponentsInChildren<RaidEncounter>();
        interactionStep = 0; initialized = false; testing = true;
        deadline = EditorApplication.timeSinceStartup + 60; nextStep = 0;
        EditorApplication.update += InteractionTick;
    }

    static void InteractionTick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) { FinishInteractionCheck("FAIL: Play stopped or timed out"); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup < nextStep) return;
        if (interactionStep >= previewLoot.Length * 3) { FinishInteractionCheck("PASS: all " + previewLoot.Length + " active containers opened with E; inventory contents available."); return; }
        var loot = previewLoot[interactionStep / 3];
        var player = Object.FindFirstObjectByType<TopDownPlayer>();
        if (!initialized) {
            foreach (var enemy in Object.FindObjectsByType<RaidEncounter>()) if (!enemy.isCrate) enemy.enabled = false;
            initialized = true;
        }
        if (interactionStep % 3 == 0)
        {
            var controller = player.GetComponent<CharacterController>();
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8;
                var p = loot.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.6f;
                if (!NavMesh.SamplePosition(p, out var hit, .5f, NavMesh.AllAreas) || Vector3.Distance(hit.position, loot.transform.position) >= 2.3f) continue;
                controller.enabled = false; player.transform.position = hit.position + Vector3.up * .1f; controller.enabled = true;
                break;
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }
        else if (interactionStep % 3 == 1) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
        else
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            var inventory = player.GetComponent<PlayerInventory>();
            var container = loot.GetComponent<LootContainer>();
            if (!container || !container.HasBeenOpened || !inventory.IsOpen || inventory.OpenContainer != container || container.Contents.Stacks.Count == 0) { FinishInteractionCheck("FAIL: " + loot.crateLabel); return; }
            inventory.Close();
        }
        interactionStep++; nextStep = EditorApplication.timeSinceStartup + .3;
    }

    static void FinishInteractionCheck(string result)
    {
        EditorApplication.update -= InteractionTick; testing = false;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        Application.runInBackground = oldRunInBackground;
        InputSystem.settings.backgroundBehavior = oldBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/interaction-check.txt", result);
        Debug.Log("EXAMPLE_INTERACTION_" + result);
        EditorApplication.isPlaying = false;
    }

    [MenuItem("Tools/Gameplay/Audit Example Assets")]
    public static void AuditAssets()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        foreach (string name in new[] { "Structure_1", "Structure_2", "Structure_3", "Structure_4", "Tank", "Container_Small", "Container_Long", "TrashContainer", "Health", "CardboardBoxes_2", "Debris_BrokenCar", "SackTrench", "MetalFence" })
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".fbx");
            var meshes = AssetDatabase.LoadAllAssetsAtPath(Models + name + ".fbx").OfType<Mesh>();
            report.AppendLine(name + " imported=" + (model != null) + " meshes=" + string.Join("; ", meshes.Select(m => m.name + " " + m.bounds.size.ToString("F2"))));
        }
        foreach (string path in Directory.GetFiles("Assets/ThirdParty/AnimatedAnimals/FBX", "*.fbx"))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            report.AppendLine(Path.GetFileNameWithoutExtension(path) + " imported=" + (model != null) + " clips=" + string.Join(", ", clips.Select(c => c.name + " (" + c.length.ToString("F1") + "s)")));
        }
        File.WriteAllText(Output + "/asset-audit.txt", report.ToString());
        Debug.Log("EXAMPLE_ASSET_AUDIT_COMPLETE\n" + report);
    }
}
