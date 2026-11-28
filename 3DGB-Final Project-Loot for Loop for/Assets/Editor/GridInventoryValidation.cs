using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class GridInventoryValidation
{
    const string Output = "../.utmp/InventoryQA";
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static int Total(params LootInventory[] inventories) => inventories.Sum(i => i.Stacks.Sum(s => s.quantity));
    static void Valid(LootInventory inventory)
    {
        Require(inventory.Stacks.Select(s => s.Slot).Distinct().Count() == inventory.Stacks.Count, "Unique addresses");
        Require(inventory.Stacks.All(s => s.Slot >= 0 && s.Slot < inventory.Capacity && s.quantity > 0 && s.quantity <= s.item.StackLimit), "Bounds/stack limits");
    }

    [MenuItem("Tools/Gameplay/Inventory/Validate Grid")]
    public static void Model()
    {
        var a = ScriptableObject.CreateInstance<LootItemDefinition>(); a.maxStack = 10;
        var b = ScriptableObject.CreateInstance<LootItemDefinition>(); b.maxStack = 5;
        try
        {
            var bag = new LootInventory(4); var box = new LootInventory(2);
            bag.Add(a, 9); bag.Add(b, 5); box.Add(a, 8); box.Add(b, 4);
            int total = Total(bag, box), notices = 0;
            bag.Changed += () => { Require(Total(bag, box) == total, "Atomic bag notification"); notices++; Valid(bag); Valid(box); };
            box.Changed += () => { Require(Total(bag, box) == total, "Atomic box notification"); notices++; };
            var original = bag.At(0);
            Require(bag.Move(0, bag, 3, 9) == 9 && bag.At(0) == null && bag.At(3) == original, "Stable holes after reorder");
            Require(bag.Split(3) == 4 && bag.At(0).quantity == 4 && bag.At(3).quantity == 5, "Odd split rounds down");
            Require(bag.Move(0, bag, 3, 4) == 4 && bag.At(0) == null && bag.At(3).quantity == 9, "Merge removes emptied source");
            Require(bag.Move(3, box, 0, 9) == 2 && bag.At(3).quantity == 7 && box.At(0).quantity == 10, "Partial merge keeps overflow");
            Require(bag.Move(3, box, 0, 7) == 0, "Full matching stack rejects drop");
            Require(bag.Move(3, box, 1, 3) == 0, "Partial stacks cannot swap unlike items");
            var swapped = box.At(1);
            Require(bag.Move(3, box, 1, 7) == 7 && bag.At(3) == swapped && box.At(1) == original, "Cross-container whole stack swap works when destination full");
            Require(box.Move(1, bag, 2, 3) == 3 && box.At(1).quantity == 4 && bag.At(2).quantity == 3, "Partial drop to empty cell");
            Require(bag.Move(2, bag, 2, 3) == 0 && bag.Move(-1, box, 0, 1) == 0 &&
                bag.Move(2, box, 9, 1) == 0 && bag.Move(2, null, 0, 1) == 0 &&
                bag.Move(2, box, 1, 0) == 0 && bag.Move(2, box, 1, 99) == 0, "Invalid operations do not mutate");
            var before = bag.At(1); var other = bag.At(2);
            Require(bag.Move(1, bag, 2, 5) == 5 && bag.At(2) == before && bag.At(1) == other, "Same-bag swap");
            Require(Total(bag, box) == total && notices > 0, "Conservation through all operations");
            Valid(bag); Valid(box);
            var full = new LootInventory(1); full.Add(a, 9);
            Require(full.Split(0) == 0 && full.At(0).quantity == 9, "Full grid cannot split");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/grid-model-checks.txt", "PASS stable cells, odd split, merge overflow, full rejection, intra/cross inventory swaps, partial drops, invalid inputs, full split rejection, quantity conservation, atomic notifications. " + DateTime.Now);
            Debug.Log("GRID_MODEL_CHECKS: PASS");
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    static GameObject Cell(bool bag, int slot) => GameObject.Find((bag ? "BagSlot_" : "LootSlot_") + slot);
    static PointerEventData Pointer(GameObject target) => new PointerEventData(EventSystem.current) {
        position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position + new Vector3(40, -40, 0)),
        button = PointerEventData.InputButton.Left
    };
    static void Drag(GameObject source, GameObject target)
    {
        var pointer = Pointer(source);
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.beginDragHandler);
        Require(source.GetComponentInParent<InventoryView>().IsDragging, "UI begins drag");
        pointer.position = Pointer(target).position;
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.dragHandler);
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Require(hits.Count > 0 && hits[0].gameObject == target, "Ghost does not block the drop target");
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.dropHandler);
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.endDragHandler);
    }

    // Called from the raid integration test after its first stack has been collected.
    public static void OpenContainer(PlayerInventory inventory, LootContainer crate)
    {
        var bag = inventory.Bag; var stack = bag.At(0); int quantity = stack.quantity;
        int total = Total(bag, crate.Contents);
        Drag(Cell(true, 0), Cell(true, 15));
        Require(bag.At(0) == null && bag.At(15) == stack, "Drag reorder preserves grid address");
        var source = Cell(true, 15); var pointer = Pointer(source);
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.beginDragHandler);
        ExecuteEvents.Execute(source, pointer, ExecuteEvents.endDragHandler);
        Require(bag.At(15) == stack && Total(bag, crate.Contents) == total, "Drop outside cancels without loss");
        if (quantity >= 2)
        {
            pointer.button = PointerEventData.InputButton.Right;
            ExecuteEvents.Execute(source, pointer, ExecuteEvents.pointerClickHandler);
            Require(bag.At(0) != null && bag.At(15).quantity == quantity - quantity / 2, "Right click splits");
            Drag(Cell(true, 0), Cell(true, 15));
            Require(bag.At(0) == null && stack.quantity == quantity, "Drag merges split stack");
        }
        Drag(Cell(true, 15), Cell(false, 0));
        Require(bag.Stacks.Count == 0 && crate.Contents.At(0) == stack, "Bag-to-container drop");
        Drag(Cell(false, 0), Cell(true, 0));
        Require(bag.At(0) == stack && Total(bag, crate.Contents) == total, "Container-to-bag drop");
        ExecuteEvents.Execute(Cell(true, 0), Pointer(Cell(true, 0)), ExecuteEvents.beginDragHandler);
        inventory.Close();
        Require(!inventory.GetComponent<InventoryView>().IsDragging && bag.At(0) == stack, "Close while dragging cancels safely");
        Require(inventory.Open(crate), "Reopens after cancelled drag");
    }

    static PlayerInventory controlInventory;
    static bool controlComplete;
    static Exception controlFailure;
    public static void QueueControlDrag(PlayerInventory inventory)
    {
        controlInventory = inventory; controlComplete = false; controlFailure = null;
        InputSystem.onAfterUpdate -= CheckControlInput;
        InputSystem.onAfterUpdate += CheckControlInput;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftCtrl));
    }
    static void CheckControlInput()
    {
        // EditorApplication.update reads the Editor input buffer. UI events use Dynamic input.
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || !Keyboard.current.leftCtrlKey.isPressed) return;
        InputSystem.onAfterUpdate -= CheckControlInput;
        try { ControlDrag(controlInventory); } catch (Exception ex) { controlFailure = ex; }
        controlComplete = true;
    }
    public static bool ControlDragComplete()
    {
        if (controlFailure != null) throw controlFailure;
        return controlComplete;
    }
    public static void CancelControlCheck() => InputSystem.onAfterUpdate -= CheckControlInput;

    static void ControlDrag(PlayerInventory inventory)
    {
        Require(UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed, "Test Ctrl input must be applied before drag");
        Require(UnityEngine.InputSystem.Keyboard.current.ctrlKey.isPressed, "Synthetic Ctrl input must be pressed");
        var stack = inventory.Bag.At(0); int quantity = stack.quantity;
        Drag(Cell(true, 0), Cell(true, 15));
        if (quantity >= 2)
            Require(inventory.Bag.At(0) != null && inventory.Bag.At(0).quantity == quantity - quantity / 2 && inventory.Bag.At(15).quantity == quantity / 2, "Ctrl drag moves half");
        // Ctrl remains held, so merge the complete remaining half explicitly through the controller.
        var moved = inventory.Bag.At(15);
        Require(inventory.MoveSlot(inventory.Bag, 15, inventory.Bag, 0, moved.quantity) > 0, "Restore test stack");
        Require(inventory.Bag.At(0).quantity == quantity && inventory.Bag.At(15) == null, "Ctrl drag conserves quantity");
        File.WriteAllText(Output + "/grid-ui-checks.txt", "PASS uGUI drag/drop and raycast, reorder, outside cancellation, right-click split, merge, bag-container round trip, close-mid-drag, Ctrl half drag. " + DateTime.Now);
        Debug.Log("GRID_UI_CHECKS: PASS");
    }

    [MenuItem("Tools/Gameplay/Inventory/Preview Grid")]
    public static void Preview()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Use a fresh Hideout Play session.");
        var inventory = Object.FindAnyObjectByType<PlayerInventory>();
        foreach (string id in new[] { "cloth", "parts", "scrap", "wire", "medical", "rations", "ammo", "bandage" })
        {
            var item = AssetDatabase.LoadAssetAtPath<LootItemDefinition>("Assets/Gameplay/InventoryItems/" + id + ".asset");
            inventory.Bag.Add(item, Mathf.Min(item.StackLimit, id == "ammo" ? 24 : 3));
        }
        inventory.Close(); inventory.Open(null);
        inventory.GetComponent<InventoryView>().Select(true, 1);
        Directory.CreateDirectory(Output);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/grid-preview.png"));
    }
}
