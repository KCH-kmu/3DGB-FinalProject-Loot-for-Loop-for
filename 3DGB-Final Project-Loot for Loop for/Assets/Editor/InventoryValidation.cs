using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class InventoryValidation
{
    const string Output = "../.utmp/InventoryQA";

    [MenuItem("Tools/Gameplay/Inventory/Validate Transfers")]
    public static void ValidateTransfers()
    {
        var a = ScriptableObject.CreateInstance<LootItemDefinition>();
        var b = ScriptableObject.CreateInstance<LootItemDefinition>();
        a.maxStack = 10; b.maxStack = 5;
        try
        {
            var source = new LootInventory(5);
            var bag = new LootInventory(2);
            source.Add(a, 20); source.Add(b, 5);
            bag.Add(a, 8); bag.Add(b, 5);
            Require(source.TransferTo(0, bag) == 2, "Partial stack must fill the remaining two spaces.");
            Require(source.Stacks[0].quantity == 8 && bag.Stacks[0].quantity == 10, "Partial transfer conserves quantities.");
            Require(source.TransferAllTo(bag) == 0 && source.Stacks.Count == 3, "Full bag leaves every source item intact.");
            Require(source.TransferTo(0, source) == 0 && source.TransferTo(-1, bag) == 0, "Self/invalid transfers are rejected.");
            var roomy = new LootInventory(10);
            Require(source.TransferAllTo(roomy) == 23 && source.Stacks.Count == 0, "Take all empties source exactly once.");
            Require(source.TransferAllTo(roomy) == 0 && roomy.Stacks.Sum(s => s.quantity) == 23, "Repeated take all cannot duplicate loot.");
            Require(bag.Stacks.Sum(s => s.quantity) + roomy.Stacks.Sum(s => s.quantity) == 38, "Total quantity is conserved.");
            var split = new LootInventory(2);
            Require(split.Add(a, 25) == 20 && split.Stacks.All(s => s.quantity == 10), "Stack limits and slot capacity are enforced.");
            Require(split.Add(null, 1) == 0 && split.Add(a, -1) == 0, "Invalid additions are ignored.");
            foreach (var c in Object.FindObjectsByType<LootContainer>(FindObjectsInactive.Include))
                Require(c.Contents.Stacks.Count > 0 && c.Contents.Stacks.All(s => s.item && s.quantity > 0), "Every authored container has valid contents.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/transfer-checks.txt", "PASS partial/full transfers, capacity, stack limits, conservation, take-all, duplicate prevention, invalid input, authored contents.");
            Debug.Log("INVENTORY_TRANSFER_CHECKS: PASS");
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    static int step, expected, moved;
    static double next, deadline;
    static PlayerInventory inventory;
    static TopDownPlayer player;
    static LootContainer crate;
    static Vector3 originalPosition;
    static Quaternion originalRotation;
    static bool oldBackground;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
    static RaidEncounter[] enemies;
    static bool running;
    static Vector3 stationaryPosition;
    static LootInventory originalBag;
    static AsyncOperation travel;

    [MenuItem("Tools/Gameplay/Inventory/Run Play Check")]
    public static void PlayCheck()
    {
        if (!EditorApplication.isPlaying || running) throw new InvalidOperationException("Enter a fresh Game Play session first.");
        inventory = Object.FindAnyObjectByType<PlayerInventory>();
        Require(inventory && inventory.Bag.Stacks.Count == 0, "Start with an empty bag.");
        originalBag = inventory.Bag;
        player = inventory.GetComponent<TopDownPlayer>();
        originalPosition = player.transform.position; originalRotation = player.transform.rotation;
        oldBackground = Application.runInBackground;
        oldInputBackground = InputSystem.settings.backgroundBehavior;
        oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.EnableDevice(Keyboard.current);
        enemies = Object.FindObjectsByType<RaidEncounter>().Where(c => !c.isCrate && c.enabled).ToArray();
        foreach (var enemy in enemies) enemy.enabled = false;
        running = true; step = 0; next = 0; deadline = EditorApplication.timeSinceStartup + 55;
        Directory.CreateDirectory(Output);
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!running) return;
        try
        {
            Require(EditorApplication.isPlaying, "Play mode stopped during validation.");
            Require(EditorApplication.timeSinceStartup < deadline, "Play check timed out.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .7;
            switch (step++)
            {
                case 0:
                    var containers = Object.FindObjectsByType<LootContainer>();
                    Require(containers.Length == ExpectedContainers(), "Configured loot spawn quota is maintained.");
                    crate = containers.First(c => c.Contents.Stacks.Count >= 2);
                    expected = crate.Contents.Stacks.Sum(s => s.quantity);
                    bool placed = false;
                    for (int i = 0; i < 16 && !placed; i++)
                    {
                        float angle = i * Mathf.PI / 8;
                        var point = crate.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.9f;
                        if (!NavMesh.SamplePosition(point, out var hit, .5f, NavMesh.AllAreas)) continue;
                        Teleport(hit.position, originalRotation); placed = crate.CanOpen(player);
                    }
                    Require(placed, "Reachable container found.");
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
                    break;
                case 1:
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    Require(inventory.IsOpen && inventory.OpenContainer == crate, "E opens nearest container.");
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/01-container-open.png"));
                    break;
                case 2:
                    var button = GameObject.Find("LootSlot_0").GetComponent<Button>();
                    var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position + new Vector3(35, -25, 0)) };
                    var hits = new System.Collections.Generic.List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointer, hits);
                    Require(hits.Any(h => h.gameObject == button.gameObject), "Slot is reachable by UI raycast.");
                    moved = crate.Contents.Stacks[0].quantity;
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    Require(inventory.Bag.Stacks.Count == 0, "Single click selects without moving items.");
                    ExecuteEvents.Execute(GameObject.Find("TransferSelected"), pointer, ExecuteEvents.pointerClickHandler);
                    Require(inventory.Bag.Stacks.Sum(s => s.quantity) == moved, "Selected item action transfers stack into bag.");
                    Require(crate.Contents.Stacks.Sum(s => s.quantity) + moved == expected, "Click removes same quantity from container.");
                    GridInventoryValidation.OpenContainer(inventory, crate);
                    GridInventoryValidation.QueueControlDrag(inventory);
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/02-stack-transferred.png"));
                    break;
                case 3:
                    if (!GridInventoryValidation.ControlDragComplete()) { step--; break; }
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    inventory.Close();
                    Require(inventory.Open(crate), "Partially looted container can reopen.");
                    Require(crate.Contents.Stacks.Sum(s => s.quantity) == expected - moved, "Reopen preserves remaining contents.");
                    var all = GameObject.Find("TakeAll").GetComponent<Button>();
                    ExecuteEvents.Execute(all.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                    Require(crate.Contents.Stacks.Count == 0 && inventory.Bag.Stacks.Sum(s => s.quantity) == expected, "Take all moves exactly the remaining contents.");
                    Require(inventory.TakeAll() == 0, "Repeated take-all cannot duplicate items.");
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/03-container-empty.png"));
                    break;
                case 4:
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Escape));
                    break;
                case 5:
                    Require(!inventory.IsOpen, "Escape closes inventory.");
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    break;
                case 6:
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.I));
                    break;
                case 7:
                    Require(inventory.IsOpen && !inventory.OpenContainer, "I opens backpack independently.");
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    inventory.Close();
                    Require(inventory.Open(crate) && crate.Contents.Stacks.Count == 0, "Empty container reopens without refilling.");
                    Teleport(originalPosition, originalRotation);
                    break;
                case 8:
                    Require(!inventory.IsOpen, "Leaving range closes the container.");
                    Require(!inventory.Open(crate), "Cannot open a distant container.");
                    inventory.Open(null);
                    stationaryPosition = player.transform.position;
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W));
                    break;
                case 9:
                    var delta = player.transform.position - stationaryPosition; delta.y = 0;
                    Require(delta.magnitude < .01f, "Movement is blocked while inventory is open.");
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    inventory.Close();
                    travel = SceneManager.LoadSceneAsync("Hideout_Bunker");
                    break;
                case 10:
                    if (!travel.isDone) { step--; break; }
                    inventory = Object.FindAnyObjectByType<PlayerInventory>();
                    if (!inventory || !inventory.GetComponent<InventoryView>()) { step--; break; }
                    Require(inventory.Bag == originalBag && inventory.Bag.Stacks.Sum(s => s.quantity) == expected, "Bag persists into hideout.");
                    Require(inventory.Open(null), "Backpack opens in hideout.");
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/04-hideout-backpack.png"));
                    break;
                case 11:
                    inventory.Close();
                    travel = SceneManager.LoadSceneAsync("Game");
                    break;
                case 12:
                    if (!travel.isDone) { step--; break; }
                    inventory = Object.FindAnyObjectByType<PlayerInventory>();
                    if (!inventory || !inventory.GetComponent<InventoryView>()) { step--; break; }
                    player = inventory.GetComponent<TopDownPlayer>();
                    Require(inventory.Bag == originalBag && inventory.Bag.Stacks.Sum(s => s.quantity) == expected, "Bag persists into next raid.");
                    Require(Object.FindObjectsByType<LootContainer>().Length == ExpectedContainers(), "New raid maintains configured loot spawn quota.");
                    Require(Object.FindObjectsByType<LootContainer>(FindObjectsInactive.Include).All(c => c.Contents.Stacks.Count > 0), "New raid replenishes container contents.");
                    inventory.Open(null);
                    player.GetComponent<RaidHealth>().Damage(1000);
                    break;
                case 13:
                    Require(!inventory.IsOpen && !player.InputEnabled && !inventory.Open(null), "Death closes inventory and cannot restore movement or reopen bag.");
                    Finish("PASS configured spawn quota; E opening; UI raycast and slot click; take all; conservation; partial/empty reopening; Escape; I backpack; range rejection/closure; movement lock; Game-Hideout-Game bag persistence; fresh raid contents; death closes and rejects UI.");
                    break;
            }
        }
        catch (Exception ex) { Finish("FAIL " + ex); }
    }

    static int ExpectedContainers()
    {
        var spawner = Object.FindAnyObjectByType<RaidLootSpawner>();
        return Mathf.RoundToInt(spawner.Candidates.Count * spawner.ContainerFraction);
    }

    static void Teleport(Vector3 position, Quaternion rotation)
    {
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false; player.transform.SetPositionAndRotation(position, rotation); controller.enabled = true;
    }

    static void Finish(string report)
    {
        GridInventoryValidation.CancelControlCheck();
        EditorApplication.update -= Tick;
        running = false;
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        if (inventory) inventory.Close();
        if (player) Teleport(originalPosition, originalRotation);
        foreach (var enemy in enemies) if (enemy) enemy.enabled = true;
        Application.runInBackground = oldBackground;
        InputSystem.settings.backgroundBehavior = oldInputBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        File.WriteAllText(Output + "/play-checks.txt", report);
        if (report.StartsWith("PASS")) Debug.Log("INVENTORY_PLAY_CHECKS: " + report); else Debug.LogError(report);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
