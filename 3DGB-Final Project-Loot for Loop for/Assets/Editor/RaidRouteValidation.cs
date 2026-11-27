using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RaidRouteValidation
{
    static int phase, round;
    static double next, deadline;
    static bool running, oldBackground;
    static TopDownPlayer player;
    static RaidRouteDirector route;
    static RaidRoutePoint target;
    static LootInventory bag;
    static AsyncOperation loading;
    static Vector3 entryPosition;
    static StringBuilder report;

    [MenuItem("Tools/Gameplay/Raid Routes/Run Round Trip Check")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || running) throw new InvalidOperationException("Enter Play mode first.");
        oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        report = new StringBuilder(); round = 0; phase = 0; next = 0;
        deadline = EditorApplication.timeSinceStartup + 110;
        running = true; EditorApplication.update += Tick;
    }

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new Exception("Route check stopped or timed out at phase " + phase);
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .25;
            switch (phase)
            {
                case 0:
                    loading = SceneManager.LoadSceneAsync("Hideout_Bunker"); phase++; break;
                case 1:
                    if (!Ready("Hideout_Bunker")) return;
                    player = Object.FindAnyObjectByType<TopDownPlayer>();
                    if (!player || !player.GetComponent<InventoryView>()) return;
                    bag = player.GetComponent<PlayerInventory>().Bag;
                    Require(bag.Stacks.Count == 0, "Start with an empty session bag.");
                    var ammo = AssetDatabase.LoadAssetAtPath<LootItemDefinition>("Assets/Gameplay/InventoryItems/ammo.asset");
                    bag.Add(ammo, 7);
                    Deploy(); phase++; break;
                case 2:
                    if (SceneManager.GetActiveScene().name != "Game") return;
                    route = Object.FindAnyObjectByType<RaidRouteDirector>();
                    player = Object.FindAnyObjectByType<TopDownPlayer>();
                    if (!route || !player || !player.GetComponent<InventoryView>()) return;
                    foreach (var enemy in Object.FindObjectsByType<RaidEncounter>()) if (!enemy.isCrate) enemy.enabled = false;
                    Require(route.Points.Count == 8 && route.ExitIndices.Length == 3, "Eight candidates / three exits.");
                    Require(Vector3.Distance(player.transform.position, route.SpawnPoint.transform.position) < .4f, "Actual portal arrival uses selected spawn.");
                    Require(!route.SpawnPoint.IsExtraction && route.Points.Count(p => p.IsExtraction) == 3, "Entry never acts as extraction.");
                    Require(bag == player.GetComponent<PlayerInventory>().Bag && bag.Stacks.Sum(s=>s.quantity) == 7, "Bag survives deployment.");
                    entryPosition = player.transform.position;
                    report.AppendLine($"Round {round+1}: actual hideout portal -> entry {route.SpawnIndex+1}; exits {string.Join(",",route.ExitIndices.Select(i=>i+1))}.");
                    target = route.Points[route.ExitIndices[round % 3]];
                    next = EditorApplication.timeSinceStartup + 3.4; phase++; break;
                case 3:
                    Require(SceneManager.GetActiveScene().name == "Game" && route.ExtractionProgress == 0, "Waiting on entry does not extract.");
                    var dormant = route.Points.First(p=>!p.IsExtraction && p != route.SpawnPoint);
                    Teleport(dormant.transform.position);
                    next = EditorApplication.timeSinceStartup + 3.4; phase++; break;
                case 4:
                    Require(route.ExtractionProgress == 0 && !route.IsExtracting, "Dormant point cannot extract.");
                    Teleport(target.transform.position);
                    next = EditorApplication.timeSinceStartup + 1; phase++; break;
                case 5:
                    Require(route.CurrentExtraction == target && route.ExtractionProgress > .2f && route.ExtractionProgress < route.ExtractionSeconds, "Active point begins countdown.");
                    Directory.CreateDirectory(RaidRouteAuthoring.Output);
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(RaidRouteAuthoring.Output + "/round-" + (round+1) + "-extraction.png"));
                    Teleport(entryPosition);
                    phase++; break;
                case 6:
                    Require(route.CurrentExtraction == null && route.ExtractionProgress == 0, "Leaving ring resets countdown.");
                    Teleport(target.transform.position);
                    player.GetComponent<PlayerInventory>().Open(null);
                    next = EditorApplication.timeSinceStartup + 3.4; phase++; break;
                case 7:
                    Require(route.ExtractionProgress == 0, "Inventory blocks extraction.");
                    player.GetComponent<PlayerInventory>().Close();
                    next = EditorApplication.timeSinceStartup + 1; phase++; break;
                case 8:
                    Require(route.CurrentExtraction == target && route.ExtractionProgress > .2f, "Closing bag restarts countdown.");
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(RaidRouteAuthoring.Output + "/round-" + (round+1) + "-countdown.png"));
                    phase++; break;
                case 9:
                    if (SceneManager.GetActiveScene().name != "Hideout_Bunker") return;
                    player = Object.FindAnyObjectByType<TopDownPlayer>();
                    if (!player || !player.GetComponent<InventoryView>()) return;
                    Require(player.InputEnabled && player.GetComponent<PlayerInventory>().Bag == bag && bag.Stacks.Sum(s=>s.quantity) == 7, "Successful extraction preserves bag and restores hideout input.");
                    report.AppendLine("PASS entry/dormant rejection, active countdown, cancel on leaving, inventory block, extraction to hideout, 7 items preserved.");
                    round++;
                    if (round == 3) { Finish("PASS\n" + report); return; }
                    Deploy(); phase = 2; break;
            }
        }
        catch (Exception e) { Finish("FAIL phase=" + phase + " " + e + "\n" + report); }
    }

    static bool Ready(string scene) => loading != null && loading.isDone && SceneManager.GetActiveScene().name == scene;
    static void Deploy()
    {
        var portal = Object.FindAnyObjectByType<ScenePortal>();
        Require(portal, "Hideout portal exists.");
        var p = portal.transform.TransformPoint(portal.GetComponent<BoxCollider>().center);
        p.y = player.transform.position.y;
        Teleport(p);
    }
    static void Teleport(Vector3 position)
    {
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false; player.transform.position = position; controller.enabled = true;
        Physics.SyncTransforms();
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Finish(string result)
    {
        EditorApplication.update -= Tick; running = false;
        Application.runInBackground = oldBackground;
        Directory.CreateDirectory(RaidRouteAuthoring.Output);
        File.WriteAllText(RaidRouteAuthoring.Output + "/round-trip.txt", result);
        if (result.StartsWith("PASS")) Debug.Log("RAID_ROUND_TRIP: " + result); else Debug.LogError(result);
    }
}
