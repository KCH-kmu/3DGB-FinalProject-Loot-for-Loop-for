using System;
using System.Collections.Generic;

[Serializable]
public sealed class LootStack
{
    public LootItemDefinition item;
    public int quantity;
    // Runtime grid address; authored container entries only serialize item and quantity.
    public int Slot { get; internal set; } = -1;
    public LootStack(LootItemDefinition item, int quantity) { this.item = item; this.quantity = quantity; }
}

public sealed class LootInventory
{
    readonly List<LootStack> stacks = new List<LootStack>();
    public IReadOnlyList<LootStack> Stacks => stacks;
    public int Capacity { get; }
    public event Action Changed;

    public LootInventory(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public LootStack At(int slot) => Valid(slot) ? stacks.Find(s => s.Slot == slot) : null;
    bool Valid(int slot) => slot >= 0 && slot < Capacity;
    int FirstFree()
    {
        for (int i = 0; i < Capacity; i++) if (At(i) == null) return i;
        return -1;
    }

    public int Add(LootItemDefinition item, int quantity)
    {
        int added = AddInternal(item, quantity);
        if (added > 0) Changed?.Invoke();
        return added;
    }

    int AddInternal(LootItemDefinition item, int quantity)
    {
        if (!item || quantity <= 0) return 0;
        int remaining = quantity;
        foreach (var stack in stacks)
        {
            if (stack.item != item) continue;
            int moved = Math.Min(remaining, Math.Max(0, item.StackLimit - stack.quantity));
            stack.quantity += moved; remaining -= moved;
            if (remaining == 0) break;
        }
        while (remaining > 0 && stacks.Count < Capacity)
        {
            int moved = Math.Min(remaining, item.StackLimit);
            stacks.Add(new LootStack(item, moved) { Slot = FirstFree() }); remaining -= moved;
        }
        return quantity - remaining;
    }

    // Retain the compact list-index API for existing callers.
    public int TransferTo(int index, LootInventory destination)
        => index >= 0 && index < stacks.Count ? TransferSlotTo(stacks[index].Slot, destination) : 0;

    public int TransferSlotTo(int slot, LootInventory destination, int quantity = int.MaxValue)
    {
        var source = At(slot);
        if (source == null || destination == null || destination == this || quantity <= 0) return 0;
        int moved = destination.AddInternal(source.item, Math.Min(quantity, source.quantity));
        if (moved == 0) return 0;
        Reduce(source, moved); Notify(destination);
        return moved;
    }

    public bool CanMove(int sourceSlot, LootInventory destination, int targetSlot, int quantity)
    {
        var source = At(sourceSlot);
        if (source == null || destination == null || !destination.Valid(targetSlot) || quantity <= 0 || quantity > source.quantity ||
            (destination == this && sourceSlot == targetSlot)) return false;
        var target = destination.At(targetSlot);
        if (target == null) return true;
        if (target.item == source.item) return target.quantity < source.item.StackLimit;
        return quantity == source.quantity;
    }

    // Atomic drop: observers see both inventories after the transfer completes.
    public int Move(int sourceSlot, LootInventory destination, int targetSlot, int quantity)
    {
        if (!CanMove(sourceSlot, destination, targetSlot, quantity)) return 0;
        var source = At(sourceSlot);
        var target = destination.At(targetSlot);
        int moved = quantity;
        if (target != null && target.item != source.item)
        {
            if (destination != this)
            {
                stacks.Remove(source); destination.stacks.Remove(target);
                stacks.Add(target); destination.stacks.Add(source);
            }
            source.Slot = targetSlot; target.Slot = sourceSlot;
        }
        else if (target != null)
        {
            moved = Math.Min(quantity, source.item.StackLimit - target.quantity);
            target.quantity += moved; Reduce(source, moved);
        }
        else if (quantity == source.quantity)
        {
            if (destination != this) { stacks.Remove(source); destination.stacks.Add(source); }
            source.Slot = targetSlot;
        }
        else
        {
            source.quantity -= quantity;
            destination.stacks.Add(new LootStack(source.item, quantity) { Slot = targetSlot });
        }
        Notify(destination); return moved;
    }

    public int Split(int slot)
    {
        var source = At(slot); int empty = FirstFree();
        if (source == null || source.quantity < 2 || empty < 0) return 0;
        return Move(slot, this, empty, source.quantity / 2);
    }

    void Reduce(LootStack stack, int quantity)
    {
        stack.quantity -= quantity;
        if (stack.quantity == 0) { stacks.Remove(stack); stack.Slot = -1; }
    }
    void Notify(LootInventory destination)
    {
        Changed?.Invoke();
        if (destination != this) destination.Changed?.Invoke();
    }

    public int TransferAllTo(LootInventory destination)
    {
        int moved = 0;
        for (int i = stacks.Count - 1; i >= 0; i--) moved += TransferTo(i, destination);
        return moved;
    }
}
