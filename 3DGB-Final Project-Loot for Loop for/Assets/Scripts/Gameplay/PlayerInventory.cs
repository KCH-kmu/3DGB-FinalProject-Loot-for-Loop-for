using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(TopDownPlayer))]
public sealed class PlayerInventory : MonoBehaviour
{
    // A play session owns the bag; scene transitions keep it, a new Play starts empty.
    static LootInventory sessionBag;
    public LootInventory Bag => sessionBag ?? (sessionBag = new LootInventory(16));
    public LootContainer OpenContainer { get; private set; }
    public bool IsOpen { get; private set; }
    TopDownPlayer player;
    InventoryView view;
    LootContainer[] containers;
    bool previousCursorVisible;
    CursorLockMode previousCursorLock;
    int closedFrame = -1;
    bool viewingContainer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() => sessionBag = null;

    void Start()
    {
        player = GetComponent<TopDownPlayer>();
        containers = FindObjectsByType<LootContainer>();
        view = gameObject.AddComponent<InventoryView>();
        view.Initialize(this);
        Bag.Changed += Refresh;
    }

    void Update()
    {
        if (!view) return;
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
        view.SetPrompt(nearest, alive && player.InputEnabled);
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
        if (IsOpen || !view || !player.InputEnabled) return false;
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
        view.SetStatus(moved > 0 ? $"Moved {moved} item(s) to your bag." : "Bag full. Items remain in the container.");
        return moved;
    }

    public int TakeAll()
    {
        if (!CanTransfer()) return 0;
        int moved = OpenContainer.Contents.TransferAllTo(Bag);
        view.SetStatus(OpenContainer.Contents.Stacks.Count == 0 ? "Container emptied. All items are in your bag." :
            $"Moved {moved} item(s). Bag full; remaining items stay here.");
        return moved;
    }

    bool CanTransfer()
    {
        if (IsOpen && player.InputEnabled && OpenContainer && OpenContainer.CanOpen(player)) return true;
        Close();
        return false;
    }

    void Refresh() { if (view && IsOpen) view.Refresh(); }
    void OnDisable() => Close();
    void OnDestroy() { Close(); if (sessionBag != null) sessionBag.Changed -= Refresh; }
}
