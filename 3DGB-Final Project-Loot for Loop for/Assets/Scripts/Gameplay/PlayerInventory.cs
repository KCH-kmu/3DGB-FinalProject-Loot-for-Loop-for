using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-150)]
[RequireComponent(typeof(TopDownPlayer))]
public sealed class PlayerInventory : MonoBehaviour
{
    // A play session owns the bag; scene transitions keep it, a new Play starts empty.
    static LootInventory sessionBag;
    public LootInventory Bag => sessionBag ?? (sessionBag = new LootInventory(16));
    public LootContainer OpenContainer { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsBlocking => IsOpen || closedFrame == Time.frameCount;
    TopDownPlayer player;
    InventoryView view;
    LootContainer[] containers;
    bool previousCursorVisible;
    CursorLockMode previousCursorLock;
    int closedFrame = -1;
    bool viewingContainer;
    HideoutDialogue dialogue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => sessionBag = null;

    void Start()
    {
        player = GetComponent<TopDownPlayer>();
        dialogue = GetComponent<HideoutDialogue>();
        containers = FindObjectsByType<LootContainer>();
        view = gameObject.AddComponent<InventoryView>();
        view.Initialize(this);
        Bag.Changed += Refresh;
    }

    void Update()
    {
        if (!view) return;
        if (dialogue && dialogue.IsBlocking) { view.SetPrompt(null, false); return; }
        var keyboard = Keyboard.current;
        var health = GetComponent<RaidHealth>();
        bool alive = !health || health.health > 0;
        if (IsOpen)
        {
            if (!alive || !player.InputEnabled || (viewingContainer && (!OpenContainer || !OpenContainer.CanOpen(player))) ||
                (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame ||
                 keyboard.iKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))) Close();
            return;
        }
        var nearest = alive && player.InputEnabled ? NearestContainer() : null;
        view.SetPrompt(nearest, alive && player.InputEnabled && (!dialogue || !dialogue.NearbyNpc));
        if (!alive || !player.InputEnabled || keyboard == null || closedFrame == Time.frameCount) return;
        if (keyboard.iKey.wasPressedThisFrame) Open(null);
        else if (keyboard.eKey.wasPressedThisFrame && nearest) Open(nearest);
    }

    public LootContainer NearestContainer()
    {
        LootContainer nearest = null;
        float best = LootContainer.InteractionRange * LootContainer.InteractionRange;
        if (containers == null) return null;
        foreach (var container in containers)
        {
            if (!container || !container.CanOpen(player)) continue;
            float distance = (container.transform.position - player.transform.position).sqrMagnitude;
            if (distance < best) { best = distance; nearest = container; }
        }
        return nearest;
    }

    public bool Open(LootContainer container)
    {
        if (IsOpen || !view || !player.InputEnabled || (dialogue && dialogue.IsBlocking)) return false;
        var health = GetComponent<RaidHealth>();
        if (health && health.health <= 0) return false;
        if (container && !container.CanOpen(player)) return false;
        OpenContainer = container;
        viewingContainer = container;
        if (container) { container.MarkOpened(); container.Contents.Changed += Refresh; }
        previousCursorVisible = Cursor.visible;
        previousCursorLock = Cursor.lockState;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsOpen = true;
        view.Show();
        return true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        if (OpenContainer) OpenContainer.Contents.Changed -= Refresh;
        OpenContainer = null;
        viewingContainer = false;
        IsOpen = false;
        closedFrame = Time.frameCount;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        if (view) view.Hide();
    }

    public int Take(int index)
    {
        if (!CanTransfer()) return 0;
        int moved = OpenContainer.Contents.TransferTo(index, Bag);
        view.SetStatus(moved > 0 ? $"{moved}개를 가방으로 옮겼습니다." : "가방이 가득 찼습니다. 아이템은 상자에 남습니다.");
        return moved;
    }

    public int TakeAll()
    {
        if (!CanTransfer()) return 0;
        int moved = OpenContainer.Contents.TransferAllTo(Bag);
        view.SetStatus(OpenContainer.Contents.Stacks.Count == 0 ? "상자의 아이템을 모두 가져왔습니다." :
            $"{moved}개를 옮겼습니다. 가방이 가득 차 나머지는 상자에 남습니다.");
        return moved;
    }

    public LootInventory InventoryFor(bool bag) => bag ? Bag : OpenContainer ? OpenContainer.Contents : null;

    public bool CanEdit(LootInventory inventory)
    {
        var health = GetComponent<RaidHealth>();
        if (!IsOpen || !player.InputEnabled || (health && health.health <= 0) || (dialogue && dialogue.IsBlocking) ||
            (viewingContainer && (!OpenContainer || !OpenContainer.CanOpen(player)))) { Close(); return false; }
        return inventory != null && (inventory == Bag || (OpenContainer && inventory == OpenContainer.Contents));
    }

    public int MoveSlot(LootInventory source, int sourceSlot, LootInventory destination, int targetSlot, int quantity)
        => CanEdit(source) && CanEdit(destination) ? source.Move(sourceSlot, destination, targetSlot, quantity) : 0;

    public int SplitSlot(bool bag, int slot)
    {
        var inventory = InventoryFor(bag);
        return CanEdit(inventory) ? inventory.Split(slot) : 0;
    }

    public int QuickTransfer(bool bag, int slot)
    {
        if (!CanTransfer()) return 0;
        return InventoryFor(bag).TransferSlotTo(slot, InventoryFor(!bag));
    }

    bool CanTransfer()
    {
        return OpenContainer && CanEdit(Bag) && CanEdit(OpenContainer.Contents);
    }

    void Refresh() { if (view && IsOpen) view.Refresh(); }
    void OnDisable() => Close();
    void OnDestroy() { Close(); if (sessionBag != null) sessionBag.Changed -= Refresh; }
}
