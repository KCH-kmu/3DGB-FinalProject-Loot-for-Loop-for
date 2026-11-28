using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class HideoutNpcAuthoring
{
    public const string Folder = "Assets/Gameplay/HideoutNpcs";
    public const string Output = "../.utmp/HideoutNpcs";
    const string Animals = "Assets/ThirdParty/AnimatedAnimals/FBX/";
    const string RootName = "Hideout_NPCs";
    static Font font;
    static Material wood, woodLight, woodDark, metal;

    static readonly string[] Models = { "Fox", "Deer", "Alpaca", "Donkey", "Wolf", "Bull", "Stag", "ShibaInu" };
    static readonly string[] Names = { "여우", "사슴", "알파카", "당나귀", "늑대", "황소", "수사슴", "시바견" };
    static readonly string[] Roles = { "잡화점", "의무실", "의류점", "보관소", "무기점", "작업장", "임무소", "안내소" };
    static readonly string[] Greetings = {
        "어서 와요! 숲에서 무사히 돌아왔군요. 작은 물건도 누군가에게는 꼭 필요한 보물이랍니다.",
        "다친 곳은 없나요? 이곳에서는 잠시 긴장을 풀어도 괜찮아요. 다음 출발 전에는 꼭 몸을 돌보세요.",
        "반가워요! 모험에는 튼튼한 가방과 편안한 옷이 필요하죠. 어떤 여행을 준비하고 있나요?",
        "짐이 많아 보이는군요! 가져갈 물건과 남겨 둘 물건을 미리 나누면 한결 가벼워질 거예요.",
        "왔군. 숲에서는 준비가 곧 생존이지. 출발하기 전에 장비와 탄약을 다시 확인해 둬.",
        "어서 오게! 낡은 장비도 잘 손보면 오래 쓸 수 있지. 쓸 만한 부품을 발견하면 기억해 두게.",
        "무사히 돌아와 다행이네. 숲에는 아직 우리가 모르는 길과 이야기가 많이 남아 있지.",
        "하이드아웃에 온 걸 환영해요! 여기는 다음 모험을 준비하고 주민들과 이야기하는 곳이에요."
    };
    static readonly string[] Details = {
        "나는 잡화점을 맡고 있어요. 지금은 인사만 나눌 수 있지만, 앞으로 물건을 사고파는 일을 도울 거예요.",
        "여기는 의무실이에요. 치료와 의약품 서비스는 준비 중이니, 오늘은 편하게 이야기를 나눠요.",
        "나는 의류점 담당이에요. 가방과 의류를 고르는 일은 나중에 도와드릴게요. 지금은 가게를 준비하고 있어요.",
        "여기는 보관소예요. 창고에 물건을 맡기는 기능은 준비 중이에요. 지금 소지품은 I키로 확인할 수 있어요.",
        "나는 무기점을 맡고 있다. 무기와 탄약 거래는 준비 중이야. 오늘은 얼굴부터 익혀 두자고.",
        "이곳은 작업장이네. 제작과 수리 서비스는 준비 중이야. 작업대가 준비되면 다시 이야기하세.",
        "나는 임무소를 맡고 있네. 새로운 의뢰와 지역 안내는 차차 준비할 예정이니 종종 들러 주게.",
        "왼쪽 방에는 생활과 보급 담당, 오른쪽 방에는 전투와 탐험 담당이 있어요. 앞쪽 방의 파란 포털로 가면 레이드에 진입해요."
    };

    [MenuItem("Tools/Gameplay/Hideout NPCs/Build Eight Residents")]
    public static void Build()
    {
        RequireScene();
        if (GameObject.Find(RootName)) throw new InvalidOperationException("Hideout NPCs already exist. Edit the existing stations instead of duplicating them.");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Output); AssetDatabase.Refresh();
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/NotoSansKR/NotoSansKR.ttf");
        if (!font) throw new InvalidOperationException("Noto Sans KR font must be imported first.");
        wood = Material("Counter_Wood", new Color(.34f, .18f, .075f));
        woodLight = Material("Counter_Planks", new Color(.53f, .32f, .14f));
        woodDark = Material("Counter_Frame", new Color(.20f, .105f, .045f));
        metal = Material("Counter_Brass", new Color(.59f, .46f, .21f));
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add eight hideout residents");
        var left = new GameObject("Left_LivingAndSupplies").transform; left.SetParent(root.transform);
        var right = new GameObject("Right_CombatAndExploration").transform; right.SetParent(root.transform);
        for (int i = 0; i < Models.Length; i++) BuildStation(i, i < 4 ? left : right);
        var player = Object.FindAnyObjectByType<TopDownPlayer>();
        if (!player) throw new InvalidOperationException("Hideout player missing.");
        var dialogue = player.GetComponent<HideoutDialogue>();
        if (!dialogue) dialogue = Undo.AddComponent<HideoutDialogue>(player.gameObject);
        dialogue.koreanFont = font; EditorUtility.SetDirty(dialogue);
        AssetDatabase.SaveAssets(); Physics.SyncTransforms();
        Validate();
        EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
        Capture();
        Selection.activeGameObject = root;
        Debug.Log("HIDEOUT_NPCS_READY: eight residents, counters, Korean signs, idle animations and E dialogue.");
    }

    static void BuildStation(int i, Transform parent)
    {
        int slot = i % 4;
        float roomX = i < 4 ? -12 : 12;
        var station = new GameObject("NPC_" + Models[i]); station.transform.SetParent(parent);
        station.transform.position = new Vector3(roomX + (slot % 2 == 0 ? -2.65f : 2.65f), 0, slot < 2 ? 4.1f : -2.3f);
        station.transform.rotation = Quaternion.Euler(0, slot < 2 ? 180 : 0, 0);
        var npc = station.AddComponent<HideoutNpc>(); npc.npcId = Models[i].ToLowerInvariant();
        npc.displayName = Names[i]; npc.roleName = Roles[i]; npc.dialogue = new[] { Greetings[i], Details[i] };
        npc.accent = i < 4 ? new Color(.64f, .86f, .71f) : new Color(.96f, .75f, .42f);
        var point = new GameObject("TalkPoint").transform; point.SetParent(station.transform, false); point.localPosition = new Vector3(0, .08f, 2.45f);
        npc.interactionPoint = point;

        var counter = new GameObject("WoodenCounter").transform; counter.SetParent(station.transform, false);
        for (int plank = 0; plank < 5; plank++)
            Cube(counter, "TopPlank_" + plank, new Vector3(-1.32f + plank * .66f, .93f, 1.25f), new Vector3(.645f, .16f, .88f), plank % 2 == 0 ? woodLight : wood);
        for (int side = -1; side <= 1; side += 2)
        {
            Cube(counter, "Leg_" + side, new Vector3(side * 1.44f, .43f, 1.25f), new Vector3(.18f, .86f, .66f), woodDark);
        }
        Cube(counter, "FrontPanel", new Vector3(0, .53f, 1.58f), new Vector3(3.05f, .58f, .10f), wood);
        Cube(counter, "LowerRail", new Vector3(0, .18f, 1.58f), new Vector3(3.1f, .10f, .14f), woodDark);
        Cube(counter, "SignPost", new Vector3(.83f, 1.12f, 1.25f), new Vector3(.08f, .26f, .08f), metal);
        var sign = Cube(station.transform, "WoodenNameplate", new Vector3(.83f, 1.30f, 1.3f), new Vector3(1.6f, .78f, .09f), woodDark, false);
        // All nameplates face the fixed top-down game camera, including the south row.
        sign.transform.rotation = Quaternion.Euler(55, 0, 0);
        Nameplate(station.transform, sign.transform.position, Names[i], Roles[i], npc.accent);
        var rug = Material("Room_" + (i < 4 ? "Supplies" : "Adventure"), i < 4 ? new Color(.13f, .25f, .22f) : new Color(.28f, .20f, .12f));
        Cube(station.transform, "StationMat", new Vector3(0, .012f, .3f), new Vector3(3.75f, .02f, 2.5f), rug, false);

        var modelPath = Animals + Models[i] + ".fbx";
        var animal = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), station.transform);
        animal.name = Models[i] + "_Resident";
        animal.transform.localPosition = Vector3.zero; animal.transform.localRotation = Quaternion.Euler(0, i % 2 == 0 ? 25 : -25, 0);
        var idleSource = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().First(c => c.name.EndsWith("|Idle"));
        idleSource.SampleAnimation(animal, 0);
        var b = BoundsOf(animal);
        float height = i == 0 || i == 7 ? 1.05f : 1.8f;
        animal.transform.localScale *= Mathf.Min(height / b.size.y, 2.35f / Mathf.Max(b.size.x, b.size.z));
        b = BoundsOf(animal);
        float platformHeight = i == 0 || i == 7 ? .34f : .06f;
        animal.transform.position += station.transform.position - station.transform.right * .42f - new Vector3(b.center.x, b.min.y - platformHeight, b.center.z);
        if (platformHeight > .1f) Cube(station.transform, "AnimalPlatform", new Vector3(-.42f, platformHeight / 2, 0), new Vector3(2.4f, platformHeight, 1.8f), woodDark);
        foreach (var renderer in animal.GetComponentsInChildren<Renderer>())
        {
            var sources = renderer.sharedMaterials;
            for (int m = 0; m < sources.Length; m++)
            {
                var source = sources[m];
                if (!source) throw new InvalidOperationException("Missing animal material: " + Models[i]);
                string key = Models[i] + "_" + source.name.Replace(" ", "_").Replace("/", "_");
                var converted = Material(key, source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                if (source.HasProperty("_MainTex") && source.GetTexture("_MainTex")) converted.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                sources[m] = converted;
            }
            renderer.sharedMaterials = sources;
        }
        string clipPath = Folder + "/" + Models[i] + "_Idle.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (!clip)
        {
            clip = Object.Instantiate(idleSource); clip.name = Models[i] + "_Idle";
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
        }
        string controllerPath = Folder + "/" + Models[i] + ".controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (!controller) { controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath); controller.AddMotion(clip); }
        var animator = animal.GetComponent<Animator>(); if (!animator) animator = animal.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var blocker = station.AddComponent<BoxCollider>(); blocker.center = new Vector3(-.42f, .65f, 0); blocker.size = new Vector3(1.3f, 1.3f, 1.8f);
    }

    static void Nameplate(Transform parent, Vector3 position, string name, string role, Color accent)
    {
        var go = new GameObject("NameplateText", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(parent, false); go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(55, 0, 0); go.transform.position -= go.transform.forward * .052f;
        go.transform.localScale = Vector3.one * .01f;
        var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(156, 76);
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text)); labelGo.transform.SetParent(go.transform, false);
        var labelRect = labelGo.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<Text>(); label.font = font; label.fontSize = 26; label.color = accent;
        label.lineSpacing = .8f;
        label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
        label.text = role + "\n<size=17>" + name + "</size>";
    }

    static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collision = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collision) Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    static Material Material(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .12f); EditorUtility.SetDirty(material); return material;
    }
    static Bounds BoundsOf(GameObject go)
    {
        // Imported skinned renderer bounds include the whole animation range (jumps etc.).
        // Fit the resident to its actual idle geometry instead of those inflated bounds.
        Bounds bounds = default; bool first = true;
        foreach (var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); renderer.BakeMesh(mesh, true);
            foreach (var vertex in mesh.vertices)
            {
                var world = renderer.transform.TransformPoint(vertex);
                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; } else bounds.Encapsulate(world);
            }
            Object.DestroyImmediate(mesh);
        }
        if (first) throw new InvalidOperationException("Animal has no skinned geometry: " + go.name);
        return bounds;
    }

    [MenuItem("Tools/Gameplay/Hideout NPCs/Audit Animal Geometry")]
    public static void AuditGeometry()
    {
        var animal = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Animals + "Fox.fbx"));
        var report = new StringBuilder();
        try
        {
            report.AppendLine("root scale " + animal.transform.localScale);
            var clip = AssetDatabase.LoadAllAssetsAtPath(Animals + "Fox.fbx").OfType<AnimationClip>().First(c => c.name.EndsWith("|Idle"));
            foreach (bool sample in new[] { false, true })
            {
                if (sample) clip.SampleAnimation(animal, 0);
                report.AppendLine("sample " + sample + " root scale " + animal.transform.localScale);
                foreach (var renderer in animal.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    report.AppendLine("renderer world " + renderer.bounds + " local " + renderer.localBounds + " scale " + renderer.transform.lossyScale);
                    foreach (bool scale in new[] { false, true })
                    {
                        var mesh = new Mesh(); renderer.BakeMesh(mesh, scale); mesh.RecalculateBounds();
                        report.AppendLine("bake useScale=" + scale + " mesh " + mesh.bounds);
                        Object.DestroyImmediate(mesh);
                    }
                }
            }
            File.WriteAllText(Output + "/geometry.txt", report.ToString());
        }
        finally { Object.DestroyImmediate(animal); }
    }

    [MenuItem("Tools/Gameplay/Hideout NPCs/Rebuild Authored Residents")]
    public static void Rebuild()
    {
        RequireScene();
        var root = GameObject.Find(RootName);
        if (root) Undo.DestroyObjectImmediate(root);
        Build();
    }

    [MenuItem("Tools/Gameplay/Hideout NPCs/Validate Layout")]
    public static void Validate()
    {
        RequireScene(); Physics.SyncTransforms(); Directory.CreateDirectory(Output);
        var npcs = Object.FindObjectsByType<HideoutNpc>(FindObjectsSortMode.None);
        if (npcs.Length != 8 || npcs.Count(n => n.transform.position.x < 0) != 4 || npcs.Count(n => n.transform.position.x > 0) != 4)
            throw new InvalidOperationException("Expected exactly four NPCs in each room.");
        var report = new StringBuilder(); var player = Object.FindAnyObjectByType<TopDownPlayer>();
        foreach (var npc in npcs)
        {
            if (!npc.interactionPoint || npc.dialogue.Length != 2 || !npc.GetComponentInChildren<Animator>().runtimeAnimatorController || !npc.GetComponentInChildren<Text>().font)
                throw new InvalidOperationException("NPC setup incomplete: " + npc.npcId);
            var stand = npc.interactionPoint.position;
            CheckSegment(new Vector3(0, .08f, 0), new Vector3(Mathf.Sign(stand.x) * 12, .08f, 0), player);
            CheckSegment(new Vector3(Mathf.Sign(stand.x) * 12, .08f, 0), new Vector3(stand.x, .08f, 0), player);
            CheckSegment(new Vector3(stand.x, .08f, 0), stand, player);
            report.AppendLine($"PASS {npc.npcId}: {npc.roleName}, reachable talk point {stand}, counter, sign, idle animation, 2 dialogue pages.");
        }
        File.WriteAllText(Output + "/layout-checks.txt", report.ToString());
        Debug.Log("HIDEOUT_LAYOUT_CHECKS: PASS\n" + report);
    }
    static void CheckSegment(Vector3 from, Vector3 to, TopDownPlayer player)
    {
        var delta = to - from;
        foreach (var hit in Physics.CapsuleCastAll(from + Vector3.up * .4f, from + Vector3.up * 1.4f, .3f, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(player.transform)) throw new InvalidOperationException("NPC customer route blocked by " + hit.collider.name);
    }

    [MenuItem("Tools/Gameplay/Hideout NPCs/Capture Layout")]
    public static void Capture()
    {
        Directory.CreateDirectory(Output);
        CaptureRoom(new Vector3(-12, 0, 0), "left-room.png", 8);
        CaptureRoom(new Vector3(12, 0, 0), "right-room.png", 8);
        CaptureRoom(new Vector3(0, 0, 1), "overview.png", 14);
    }
    static void CaptureRoom(Vector3 center, string filename, float size)
    {
        var go = new GameObject("NPCPreviewCamera"); var camera = go.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = size; camera.farClipPlane = 150;
        camera.transform.position = center + new Vector3(0, 24, -17); camera.transform.LookAt(center);
        var rt = new RenderTexture(1600, 1000, 24); var previous = RenderTexture.active;
        var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        try
        {
            Canvas.ForceUpdateCanvases(); camera.targetTexture = rt; camera.Render(); camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply(); File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
    }
    static void RequireScene()
    {
        if (Application.isPlaying || EditorSceneManager.GetActiveScene().name != "Hideout_Bunker") throw new InvalidOperationException("Open Hideout_Bunker in Edit mode.");
    }
}
