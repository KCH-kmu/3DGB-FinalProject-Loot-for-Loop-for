using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static class UnrealAssetExampleAuthoring
{
    const string Folder = "Assets/ThirdParty/UnrealBuildingExample";
    const string RootName = "Raid_UnrealAssetExample";
    const string Output = "../.utmp/UnrealBridge";
    const string PrefabPath = "Assets/Gameplay/UnrealRuinedBuilding.prefab";

    [MenuItem("Tools/Gameplay/Unreal Example/Import And Place Ruined Building")]
    public static void Build()
    {
        RequireEdit();
        if (GameObject.Find(RootName)) throw new InvalidOperationException("The Unreal building example already exists.");
        var modelPath = Folder + "/PostApocalypticBuilding.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.SaveAndReimport();
        ConfigureTexture("Building_Texture_0.png", true, false, false);
        ConfigureTexture("Building_Texture_1.png", false, false, true);
        // PNG export retains the glTF source normal map. Unreal's import-time green flip is not baked into that source.
        ConfigureTexture("Building_Texture_2.png", false, true, false);
        var orm = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Building_Texture_1.png");
        var pixels = orm.GetPixels32();
        for (int i=0; i<pixels.Length; i++)
        {
            var c=pixels[i];
            pixels[i]=new Color32(c.b,c.r,0,(byte)(255-c.g)); // glTF ORM -> URP metallic R, occlusion G, smoothness A.
        }
        var packed = new Texture2D(orm.width,orm.height,TextureFormat.RGBA32,false,true);
        packed.SetPixels32(pixels); packed.Apply();
        File.WriteAllBytes(Folder + "/Building_URP_Mask.png",packed.EncodeToPNG());
        Object.DestroyImmediate(packed);
        AssetDatabase.ImportAsset(Folder + "/Building_URP_Mask.png",ImportAssetOptions.ForceSynchronousImport);
        ConfigureTexture("Building_URP_Mask.png",false,false,false);
        ConfigureTexture("Building_Texture_1.png",false,false,false);
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Building_URP.mat");
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material,Folder + "/Building_URP.mat");
        }
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Building_Texture_0.png"));
        material.SetColor("_BaseColor",Color.white);
        material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Building_Texture_2.png"));
        material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Building_URP_Mask.png"));
        material.SetTexture("_OcclusionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Building_URP_Mask.png"));
        material.SetFloat("_Metallic",1); material.SetFloat("_Smoothness",1); material.SetFloat("_OcclusionStrength",.8f);
        material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.EnableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(material);

        var template = new GameObject("Unreal_RuinedBuilding");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),template.transform);
        var bounds = BoundsOf(model);
        model.transform.localScale *= 20 / Mathf.Max(bounds.size.x,bounds.size.z);
        bounds = BoundsOf(model);
        model.transform.position -= new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            renderer.sharedMaterials = Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        var modifier = template.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true; modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        PrefabUtility.SaveAsPrefabAsset(template,PrefabPath);
        Object.DestroyImmediate(template);

        var root = new GameObject(RootName);
        var building = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),root.transform);
        building.transform.position = Vector3.one * -500;
        Physics.SyncTransforms();
        var moved = new List<(Transform plant,Transform parent,bool active)>();
        try
        {
            var size = BoundsOf(building).size;
            var p = FindPlacement(size);
            building.transform.position = p - Vector3.up*.04f;
            var clearing = BoundsOf(building); clearing.Expand(new Vector3(2,0,2));
            var displaced = new GameObject("DisplacedVegetation"); displaced.transform.SetParent(root.transform); displaced.SetActive(false);
            var plants = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r=>clearing.Intersects(r.bounds)).Select(r=>VegetationRoot(r.transform)).Where(t=>t).Distinct().ToArray();
            foreach(var plant in plants)
            {
                moved.Add((plant,plant.parent,plant.gameObject.activeSelf));
                plant.SetParent(displaced.transform,true);
            }
            Physics.SyncTransforms();
            RaidExampleLayout.RebuildNavigation();
            RaidDensityAuthoring.Validate();
            long triangles=building.GetComponentsInChildren<MeshFilter>().Sum(f=>(long)f.sharedMesh.GetIndexCount(0)/3);
            File.WriteAllText(Output + "/unity-result.txt",$"PASS Unreal FBX + 3 source textures -> URP. Building at {p:F2}, bounds {BoundsOf(building).size:F2}, triangles {triangles}. Moved {moved.Count} overlapping vegetation objects into inactive storage. 8 spawns, 24 extraction paths and 50 loot points remain reachable.\n");
            Capture();
            EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene); AssetDatabase.SaveAssets();
            Selection.activeGameObject=building;
            if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Frame(BoundsOf(building),false);
            Debug.Log("UNREAL_EXAMPLE_READY: " + File.ReadAllText(Output + "/unity-result.txt"));
        }
        catch
        {
            foreach(var item in moved) { item.plant.SetParent(item.parent,true); item.plant.gameObject.SetActive(item.active); }
            Object.DestroyImmediate(root); Physics.SyncTransforms(); RaidExampleLayout.RebuildNavigation();
            throw;
        }
    }

    static void ConfigureTexture(string name,bool srgb,bool normal,bool readable)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(Folder + "/" + name);
        importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        importer.sRGBTexture=srgb; importer.isReadable=readable; importer.maxTextureSize=2048;
        importer.flipGreenChannel=false;
        importer.SaveAndReimport();
    }

    static Transform VegetationRoot(Transform t)
    {
        while(t && t.parent)
        {
            if(t.parent.name=="Raid_Vegetation" || t.parent.name=="TreeGroves" || t.parent.name=="RockGroups" || t.parent.name=="GroundCover") return t;
            t=t.parent;
        }
        return null;
    }

    static bool IsGround(Collider c) => c.transform.root.name=="Free_Forest" && (c.name.Contains("Meadow") || c.name.StartsWith("PP_Lake_Ground"));

    static Vector3 FindPlacement(Vector3 size)
    {
        var offsets=from x in Enumerable.Range(-15,31) from z in Enumerable.Range(-18,37) orderby x*x+z*z select new Vector3(x*2,0,z*2);
        var points=Object.FindObjectsByType<RaidRoutePoint>();
        var loot=Object.FindObjectsByType<LootContainer>();
        foreach(var offset in offsets)
        {
            var p=new Vector3(54,0,8)+offset;
            var heights=new List<float>(); bool ground=true;
            foreach(float x in new[]{-size.x*.5f,0,size.x*.5f}) foreach(float z in new[]{-size.z*.5f,0,size.z*.5f})
            {
                var hits=Physics.RaycastAll(p+new Vector3(x,50,z),Vector3.down,80,~0,QueryTriggerInteraction.Ignore).Where(h=>IsGround(h.collider)).OrderBy(h=>h.distance).ToArray();
                if(hits.Length==0) { ground=false; break; }
                heights.Add(hits[0].point.y);
            }
            if(!ground || heights.Count!=9 || heights.Max()-heights.Min()>.65f) continue;
            p.y=heights.Average();
            var reserved=new Bounds(p+Vector3.up*3,new Vector3(size.x+8,12,size.z+8));
            if(points.Any(t=>reserved.Contains(t.transform.position)) || loot.Any(l=>reserved.Contains(l.transform.position))) continue;
            var collisions=Physics.OverlapBox(p+Vector3.up*5,new Vector3(size.x*.5f+1,4.5f,size.z*.5f+1),Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(collisions.Any(c=>!IsGround(c)&&!VegetationRoot(c.transform))) continue;
            return p;
        }
        throw new InvalidOperationException("No clear footprint found for the imported building.");
    }

    static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();
        var result=renderers[0].bounds; foreach(var r in renderers.Skip(1))result.Encapsulate(r.bounds); return result;
    }

    [MenuItem("Tools/Gameplay/Unreal Example/Capture Building")]
    public static void Capture()
    {
        RequireEdit(); Directory.CreateDirectory(Output);
        var bounds=BoundsOf(GameObject.Find(RootName).transform.GetChild(0).gameObject);
        var go=new GameObject("UnrealExamplePreviewCamera"); var camera=go.AddComponent<Camera>();
        camera.orthographic=true; camera.orthographicSize=18; camera.farClipPlane=300;
        var target=new RenderTexture(1400,1000,24); var previous=RenderTexture.active; var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);
        try
        {
            camera.transform.position=bounds.center+new Vector3(26,28,-32); camera.transform.LookAt(bounds.center);
            camera.targetTexture=target; camera.Render();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();File.WriteAllBytes(Output+"/unity-landmark.png",image.EncodeToPNG());
        }
        finally { RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(go);Object.DestroyImmediate(target);Object.DestroyImmediate(image); }
    }

    static void RequireEdit()
    {
        if(Application.isPlaying||EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Game.unity")throw new InvalidOperationException("Open Game in Edit mode.");
    }
}
