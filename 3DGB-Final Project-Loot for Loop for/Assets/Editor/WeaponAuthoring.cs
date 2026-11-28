using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class WeaponAuthoring
{
    const string Folder = "Assets/Gameplay/Weapons";
    public const string Output = "../.utmp/Weapons";
    static readonly string[] Models = { "Revolver", "AK", "SMG", "Shotgun" };
    static readonly string[] Names = { "리볼버", "소총", "기관단총", "산탄총" };

    [MenuItem("Tools/Gameplay/Weapons/Build Four Weapons")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Gameplay", "Weapons");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var definitions = new WeaponDefinition[4]; var report = new StringBuilder();
        for (int i = 0; i < 4; i++)
        {
            var kind = (WeaponKind)i;
            string path = Folder + "/" + kind + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (!definition) { definition = ScriptableObject.CreateInstance<WeaponDefinition>(); AssetDatabase.CreateAsset(definition, path); }
            definition.kind = kind; definition.displayName = Names[i];
            definition.shotInterval = new[] { .32f, .09f, .085f, .9f }[i];
            definition.burstCooldown = .35f;
            definition.reloadSeconds = new[] { 1.8f, 2.2f, 1.8f, .65f }[i];
            definition.shellReloadStartSeconds = .35f;
            definition.spreadHalfAngle = new[] { .6f, 1.5f, 5f, 9f }[i];
            definition.damage = new[] { 34, 20, 12, 9 }[i];
            definition.range = new[] { 50f, 80f, 55f, 35f }[i];
            definition.startingReserve = new[] { 36, 90, 90, 24 }[i];
            float length = new[] { .45f, .9f, .65f, 1f }[i];
            definition.modelPrefab = Model(Models[i], length, report);
            definition.muzzleLocalPosition = new Vector3(0, .1f, length * .85f);
            definition.gripOffset = Vector3.zero; definition.modelEuler = Vector3.zero;
            EditorUtility.SetDirty(definition); definitions[i] = definition;
        }
        const string catalogPath = "Assets/Resources/WeaponCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(catalogPath);
        if (!catalog) { catalog = ScriptableObject.CreateInstance<WeaponCatalog>(); AssetDatabase.CreateAsset(catalog, catalogPath); }
        catalog.weapons = definitions;
        const string tracerPath = Folder + "/ShotTracer.mat";
        var tracer = AssetDatabase.LoadAssetAtPath<Material>(tracerPath);
        if (!tracer)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader");
            if (!shader) throw new InvalidOperationException("URP Unlit shader missing.");
            tracer = new Material(shader); AssetDatabase.CreateAsset(tracer, tracerPath);
        }
        tracer.SetColor("_BaseColor", new Color(1, .7f, .15f)); EditorUtility.SetDirty(tracer);
        catalog.tracerMaterial = tracer; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/model-audit.txt", report.ToString());
        Debug.Log("FOUR_WEAPONS_READY: Revolver 6 / Rifle 30 / SMG 30 / Shotgun 8x12.");
    }
    static GameObject Model(string name, float length, StringBuilder report)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/ToonShooter/Guns/FBX/" + name + ".fbx");
        if (!source) throw new InvalidOperationException("Weapon model missing: " + name);
        var root = new GameObject(name + "_Equipped");
        try
        {
            var model = Object.Instantiate(source, root.transform);
            model.name = name; model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var original = materials[i];
                    string path = Folder + "/" + name + "_Material_" + i + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!mat)
                    {
                        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
                        mat = new Material(shader); AssetDatabase.CreateAsset(mat, path);
                    }
                    mat.SetColor("_BaseColor", original && original.HasProperty("_Color") ? original.GetColor("_Color") : Color.gray);
                    if (original && original.HasProperty("_MainTex")) mat.SetTexture("_BaseMap", original.GetTexture("_MainTex"));
                    mat.SetFloat("_Smoothness", .25f); EditorUtility.SetDirty(mat); materials[i] = mat;
                }
                renderer.sharedMaterials = materials;
            }
            var bounds = Bounds(model); report.AppendLine(name + " original bounds " + bounds);
            if (bounds.size.x > bounds.size.z)
                model.transform.localRotation = Quaternion.Euler(0, 0, -90) * Quaternion.Euler(0, -90, 0);
            bounds = Bounds(model);
            model.transform.localScale *= length / Mathf.Max(.001f, bounds.size.z);
            bounds = Bounds(model);
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y + .08f, bounds.min.z + length * .15f);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            var result = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + "_Equipped.prefab");
            report.AppendLine(name + " equipped bounds " + Bounds(model));
            return result;
        }
        finally { Object.DestroyImmediate(root); }
    }
    static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException("Model has no renderer.");
        var b = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
        return b;
    }
}
