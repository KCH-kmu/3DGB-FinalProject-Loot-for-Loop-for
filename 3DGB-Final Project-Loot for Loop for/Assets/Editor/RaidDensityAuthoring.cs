using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object = UnityEngine.Object;

public static class RaidDensityAuthoring
{
    const string RootName = "Raid_AddedContent";
    const string Output = "../.utmp/RaidDensityQA";
    static System.Random random;
    static RaidRoutePoint[] routes;
    static List<RaidEncounter> loot;
    static readonly List<Vector3> sites = new List<Vector3>();
    static readonly List<Vector3> treePositions = new List<Vector3>();
    static Vector3 source;
    static Bounds area;

    [MenuItem("Tools/Gameplay/Density/Resize To 200m")]
    public static void Resize()
    {
        RequireEdit();
        var layout = Object.FindAnyObjectByType<RaidMapLayout>();
        if (!layout) throw new InvalidOperationException("Map layout metadata is missing.");
        if (Vector2.Distance(layout.targetSize, new Vector2(200,200)) < .01f) return;
        if (GameObject.Find(RootName)) throw new InvalidOperationException("Resize before adding density.");
        Directory.CreateDirectory(Output);
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, Output + "/Game-before.unity", true);
        File.Copy("Assets/Gameplay/InRaidNavMesh.asset", Output + "/NavMesh-before.asset", true);
        var center = layout.originalCenter;
        var factor = new Vector3(200 / layout.targetSize.x, 1, 200 / layout.targetSize.y);
        Vector3 Map(Vector3 p) => center + Vector3.Scale(p-center, factor);
        foreach (string name in new[] { "Free_Forest", "Raid_MapBoundary", "WaterMovementBarriers" })
        {
            var t = GameObject.Find(name).transform; Undo.RecordObject(t, "Resize map to 200m");
            t.position = Map(t.position); t.localScale = Vector3.Scale(t.localScale, factor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        foreach (string name in new[] { "Raid_Vegetation", "Raid_Encounters", "Raid_LootSpawnPoints", "Raid_SpawnAndExtractionPoints" })
            foreach (Transform t in GameObject.Find(name).transform) Move(t, Map(t.position));
        foreach (Transform group in GameObject.Find("Raid_ExampleLandmarks").transform)
            foreach (Transform t in group) Move(t, Map(t.position));
        foreach (string name in new[] { "Player", "PlayerSpawnPoint", "Main Camera" })
        { var t = GameObject.Find(name).transform; Move(t, Map(t.position)); }
        Undo.RecordObject(layout, "Record 200m layout");
        layout.horizontalScale = Vector2.Scale(layout.horizontalScale, new Vector2(factor.x,factor.z));
        layout.targetSize = new Vector2(200,200); EditorUtility.SetDirty(layout);
        Physics.SyncTransforms(); RaidMapResizeAuthoring.RegroundRings(); RaidExampleLayout.RebuildNavigation();
        RaidRouteAuthoring.Validate(); RaidLootAuthoring.Validate();
        Save();
        Debug.Log("RAID_DENSITY_RESIZE: ground 200 x 200m; prop sizes preserved.");
    }

    [MenuItem("Tools/Gameplay/Density/Populate Forest And Landmarks")]
    public static void Populate()
    {
        RequireEdit();
        if (GameObject.Find(RootName)) throw new InvalidOperationException("Additional content already exists.");
        var layout = Object.FindAnyObjectByType<RaidMapLayout>();
        if (!layout || Vector2.Distance(layout.targetSize,new Vector2(200,200))>.01f) throw new InvalidOperationException("Resize to 200m first.");
        random = new System.Random(20261127); sites.Clear(); treePositions.Clear();
        routes = Object.FindObjectsByType<RaidRoutePoint>();
        var spawner = Object.FindAnyObjectByType<RaidLootSpawner>();
        loot = spawner.Candidates.ToList();
        var templates = loot.ToArray();
        source = Object.FindAnyObjectByType<PlayerSpawnPoint>().transform.position;
        NavMesh.SamplePosition(source, out var start, 2, NavMesh.AllAreas); source = start.position;
        area = new Bounds(routes[0].transform.position, Vector3.zero);
        foreach (var p in routes) area.Encapsulate(p.transform.position);
        area.Expand(new Vector3(12,0,10));
        var root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root, "Populate raid");
        var landmarks = Child(root.transform,"Landmarks");
        var containers = Child(root.transform,"LootPoints");
        var trees = Child(root.transform,"TreeGroves");
        var rocks = Child(root.transform,"RockGroups");
        var plants = Child(root.transform,"GroundCover");
        var originals = GameObject.Find("Raid_Vegetation").transform.Cast<Transform>().ToArray();
        var treeTemplates = originals.Where(t=>t.name.Contains("Tree")).ToArray();
        var rockTemplates = originals.Where(t=>t.name.Contains("Rock")).ToArray();
        var plantTemplates = originals.Where(t=>t.name.Contains("Grass") || t.name.Contains("Flower") || t.name.Contains("Daffodil")).ToArray();
        var desired = new[] {new Vector2(-24,12),new Vector2(-4,26),new Vector2(13,23),new Vector2(22,-6),new Vector2(-9,-17),new Vector2(-15,1)};
        string[] names = { "04_West_RangerCamp", "05_North_CargoDepot", "06_East_FieldStation", "07_East_WreckYard", "08_South_SupplyShelter", "09_Central_RelayCamp" };
        string[] models = { "Structure_2", "Container_Long", "Structure_3", "Debris_BrokenCar", "Structure_4", "Container_Small" };
        float[] widths = { 6,7,6,5,6,5 };
        for (int i=0;i<desired.Length;i++)
        {
            var wanted = layout.MapPoint(new Vector3(desired[i].x,0,desired[i].y));
            var p = FindNear(wanted, 16, widths[i]*.55f, false, true);
            sites.Add(p);
            var site = Child(landmarks,names[i]);
            RaidExampleLayout.Place(site,models[i],p.x,p.z,widths[i],i*57);
            Physics.SyncTransforms();
            var prop = FindNear(p+new Vector3(4.5f,0,-3),7,1.8f,false,false);
            RaidExampleLayout.Place(site,i%2==0?"Pallet":"SackTrench",prop.x,prop.z,2.4f,i*29);
            Physics.SyncTransforms();
            var small = FindNear(p+new Vector3(-4.5f,0,2),7,.8f,false,false);
            RaidExampleLayout.Place(site,i%2==0?"GasCan":"Debris_Tires",small.x,small.z,1.2f,i*31);
            Physics.SyncTransforms();
            for (int j=0;j<3;j++)
            {
                float a = (j*120+i*35)*Mathf.Deg2Rad;
                var q = FindNear(p + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*7,10,1.3f,false,false);
                AddLoot(templates[(i*3+j)%templates.Length],containers,q);
            }
        }
        while (loot.Count < 50)
        {
            var p = FindGlobal(1.3f,false,true);
            AddLoot(templates[loot.Count % templates.Length],containers,p);
        }
        spawner.Configure(AssetDatabase.LoadAssetAtPath<RaidLootSettings>("Assets/Gameplay/RaidLootSettings.asset"),loot.ToArray());
        EditorUtility.SetDirty(spawner);

        // Irregular groves, with open road surfaces and clear space around exits and loot.
        var anchors = new List<Vector3>();
        for (int i=0;i<22;i++)
        {
            var anchor = FindGlobal(1.2f,true,true);
            anchors.Add(anchor);
        }
        Scatter(trees,treeTemplates,220,anchors,1.45f,3.5f,false);
        Scatter(rocks,rockTemplates,110,anchors,1.1f,2.7f,false);
        Scatter(plants,plantTemplates,440,anchors,.3f,.7f,true);
        RaidExampleLayout.RebuildNavigation();
        Polish();
    }

    static int Scatter(Transform parent, Transform[] templates, int count, List<Vector3> anchors, float clearance, float spacing, bool decoration)
    {
        int placed=0;
        var positions = new List<Vector3>();
        for(int attempt=0;attempt<count*150 && placed<count;attempt++)
        {
            var anchor=anchors[attempt%anchors.Count];
            float angle=Next(0,Mathf.PI*2), radius=Mathf.Sqrt(Next(0,1))*14;
            var wanted=attempt%4==0?RandomPoint():anchor+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
            if(!TryGround(wanted,true,out var p) || !Clear(p,clearance,true) || positions.Any(q=>Horizontal(q,p)<spacing)) continue;
            if (!decoration && treePositions.Any(q=>Horizontal(q,p)<2.6f)) continue;
            var template=templates[random.Next(templates.Length)];
            var go=Object.Instantiate(template.gameObject,parent);go.name=$"{template.name.Trim()}_Added_{placed+1:000}";
            go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,Next(0,360),0));
            go.transform.localScale=template.lossyScale*Next(decoration?.8f:.85f,decoration?1.4f:1.25f);
            var renderers=go.GetComponentsInChildren<Renderer>();
            float bottom=renderers.Min(r=>r.bounds.min.y);
            go.transform.position+=Vector3.up*(p.y-bottom-.035f);
            if(decoration)
            {
                RaidFoliageAuthoring.MakeDecorative(go);
            }
            positions.Add(p);if(!decoration)treePositions.Add(p);
            placed++;Physics.SyncTransforms();
        }
        if(placed<count*.9f)throw new InvalidOperationException($"Insufficient placement space for {parent.name}: {placed}/{count}");
        return placed;
    }

    static void AddLoot(RaidEncounter template,Transform parent,Vector3 position)
    {
        var go=Object.Instantiate(template.gameObject,parent);
        go.name=$"LootPoint_{loot.Count+1:00}_{template.crateLabel.Replace(' ','_')}";
        go.transform.SetPositionAndRotation(position-Vector3.up*.03f,Quaternion.Euler(0,Next(0,360),0));
        loot.Add(go.GetComponent<RaidEncounter>());Physics.SyncTransforms();
    }

    static Vector3 FindNear(Vector3 wanted,float radius,float clearance,bool grass,bool reserveSites)
    {
        for(int i=0;i<1800;i++)
        {
            float a=Next(0,Mathf.PI*2),r=i==0?0:Mathf.Sqrt(Next(0,1))*radius;
            if(TryGround(wanted+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*r,grass,out var p) && Clear(p,clearance,reserveSites)) return p;
        }
        throw new InvalidOperationException("No clear placement near "+wanted);
    }
    static Vector3 FindGlobal(float clearance,bool grass,bool reserveSites)
    {
        for(int i=0;i<12000;i++)if(TryGround(RandomPoint(),grass,out var p)&&Clear(p,clearance,reserveSites))return p;
        throw new InvalidOperationException("No global placement space.");
    }
    static Vector3 RandomPoint()=>new Vector3(Next(area.min.x,area.max.x),0,Next(area.min.z,area.max.z));
    static bool TryGround(Vector3 p,bool grass,out Vector3 ground)
    {
        ground=default;
        var hits=Physics.RaycastAll(p+Vector3.up*70,Vector3.down,100,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
            .Where(h=>h.collider.name.Contains("Meadow")).OrderBy(h=>h.distance).ToArray();
        if(hits.Length==0 || (grass&&hits[0].collider.name.Contains("Path")))return false;
        ground=hits[0].point;
        if(!NavMesh.SamplePosition(ground,out var nav,.4f,NavMesh.AllAreas))return false;
        return true;
    }
    static bool Clear(Vector3 p,float radius,bool reserveSites)
    {
        if(routes.Any(r=>Horizontal(p,r.transform.position)<8+radius))return false;
        if(loot.Any(l=>Horizontal(p,l.transform.position)<3.6f+radius))return false;
        if(reserveSites&&sites.Any(s=>Horizontal(p,s)<10+radius))return false;
        var overlaps=Physics.OverlapBox(p+Vector3.up*1.5f,new Vector3(radius,1.25f,radius),Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        return !overlaps.Any(c=>!c.name.Contains("Meadow")&&!c.name.Contains("Ground"));
    }
    static float Horizontal(Vector3 a,Vector3 b)=>new Vector2(a.x-b.x,a.z-b.z).magnitude;
    static float Next(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
    static Transform Child(Transform parent,string name){var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
    static void Move(Transform t,Vector3 p){Undo.RecordObject(t,"Resize map placements");t.position=p;PrefabUtility.RecordPrefabInstancePropertyModifications(t);}
    static void Save(){AssetDatabase.SaveAssets();var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
    static void RequireEdit(){if(Application.isPlaying||EditorSceneManager.GetActiveScene().name!="Game")throw new InvalidOperationException("Open Game in Edit mode.");}

    [MenuItem("Tools/Gameplay/Density/Polish Access And Save")]
    public static void Polish()
    {
        RequireEdit();
        random = new System.Random(20261128);
        routes = Object.FindObjectsByType<RaidRoutePoint>();
        loot = Object.FindAnyObjectByType<RaidLootSpawner>().Candidates.ToList();
        source = Object.FindAnyObjectByType<PlayerSpawnPoint>().transform.position;
        if(!NavMesh.SamplePosition(source,out var start,2,NavMesh.AllAreas))throw new InvalidOperationException("Spawn off navigation.");
        source=start.position;
        int relocated=0;
        foreach(var container in loot)
        {
            if(Reachable(container.transform.position))continue;
            var original=container.transform.position;
            bool found=false;
            for(int i=0;i<3000;i++)
            {
                float a=Next(0,Mathf.PI*2),r=1+Mathf.Sqrt(Next(0,1))*11;
                if(!TryGround(original+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*r,false,out var p))continue;
                if(routes.Any(t=>Horizontal(p,t.transform.position)<8)||loot.Any(l=>l!=container&&Horizontal(p,l.transform.position)<4))continue;
                if(Physics.OverlapBox(p+Vector3.up*1.5f,new Vector3(1.3f,1.25f,1.3f),Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .Any(c=>!c.name.Contains("Meadow")&&!c.transform.IsChildOf(container.transform)))continue;
                if(!Reachable(p))continue;
                Move(container.transform,p-Vector3.up*.03f);Physics.SyncTransforms();found=true;relocated++;break;
            }
            if(!found)throw new InvalidOperationException("Unable to repair loot access: "+container.name);
        }
        Validate(); Save();
        var root=GameObject.Find(RootName);
        int trees=root.transform.Find("TreeGroves").childCount, rocks=root.transform.Find("RockGroups").childCount, plants=root.transform.Find("GroundCover").childCount;
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output+"/content-counts.txt",$"Added {trees} trees, {rocks} rocks, {plants} ground plants.\nAdded 6 landmarks with 12 secondary props.\nAdded 25 loot containers; total 50; active per raid 40 (80%).\nGround 200 x 200m; 8 entry candidates and 3 opposite exits retained.\nAdjusted {relocated} containers to accessible positions after baking.");
        RaidRouteAuthoring.Capture();
        File.Copy("../.utmp/RaidRoutesQA/overview.png",Output+"/overview.png",true);
        Selection.activeGameObject=root;
        Debug.Log("RAID_DENSITY_COMPLETE: "+File.ReadAllText(Output+"/content-counts.txt"));
    }

    static bool Reachable(Vector3 p)
    {
        for(int i=0;i<16;i++)
        {
            float a=i*Mathf.PI/8;
            if(!NavMesh.SamplePosition(p+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*1.8f,out var hit,.5f,NavMesh.AllAreas)||Vector3.Distance(hit.position,p)>=2.5f)continue;
            var path=new NavMeshPath();
            if(NavMesh.CalculatePath(source,hit.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete)return true;
        }
        return false;
    }

    [MenuItem("Tools/Gameplay/Density/Validate Dense Map")]
    public static void Validate()
    {
        RequireEdit();
        var bounds=RaidMapResizeAuthoring.GroundBounds(GameObject.Find("Free_Forest").transform);
        if(Mathf.Abs(bounds.size.x-200)>.05f||Mathf.Abs(bounds.size.z-200)>.05f)throw new InvalidOperationException("Map is not 200m square.");
        RaidRouteAuthoring.Validate();RaidLootAuthoring.Validate();
        var containers=Object.FindObjectsByType<LootContainer>();
        if(containers.Length!=50||containers.Any(c=>c.Contents.Stacks.Count==0))throw new InvalidOperationException("Expected 50 populated loot candidates.");
        Debug.Log("RAID_DENSITY_VALIDATED: 200m ground, 8 safe spawns, 24 exit paths, 50 reachable populated containers.");
    }

    [MenuItem("Tools/Gameplay/Density/Capture Landmark Details")]
    public static void CaptureDetails()
    {
        RequireEdit(); Directory.CreateDirectory(Output);
        foreach(Transform site in GameObject.Find(RootName).transform.Find("Landmarks"))
        {
            var renderers=site.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
            var position=bounds.center+new Vector3(0,20,-12);
            RaidExampleLayout.CaptureShot(site.name,position,13);
            File.Copy("../.utmp/ExampleLayout/"+site.name+".png",Output+"/"+site.name+".png",true);
        }
    }
}
