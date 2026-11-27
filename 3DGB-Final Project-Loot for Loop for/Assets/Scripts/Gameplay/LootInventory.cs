using System;
using System.Collections.Generic;

[Serializable]
public sealed class LootStack
{
    public LootItemDefinition item;
    public int quantity;
    public LootStack(LootItemDefinition item, int quantity) { this.item = item; this.quantity = quantity; }
}

// Each slot owns its stack. Transfers move only the amount accepted by the destination.
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

    public int Add(LootItemDefinition item, int quantity)
    {
        if (!item || quantity <= 0) return 0;
        int remaining = quantity;
        foreach (var stack in stacks)
        {
            if (stack.item != item) continue;
            int moved = Math.Min(remaining, Math.Max(0, item.StackLimit - stack.quantity));
            stack.quantity += moved;
            remaining -= moved;
            if (remaining == 0) break;
        }
        while (remaining > 0 && stacks.Count < Capacity)
        {
            int moved = Math.Min(remaining, item.StackLimit);
            stacks.Add(new LootStack(item, moved));
            remaining -= moved;
        }
        int added = quantity - remaining;
        if (added > 0) Changed?.Invoke();
        return added;
    }

    public int TransferTo(int index, LootInventory destination)
    {
        if (destination == null || destination == this || index < 0 || index >= stacks.Count) return 0;
        var source = stacks[index];
        int moved = destination.Add(source.item, source.quantity);
        if (moved == 0) return 0;
        source.quantity -= moved;
        if (source.quantity == 0) stacks.RemoveAt(index);
        Changed?.Invoke();
        return moved;
    }

    public int TransferAllTo(LootInventory destination)
    {
        int moved = 0;
        // Backwards iteration keeps indices valid when an entire stack is removed.
        for (int i = stacks.Count - 1; i >= 0; i--) moved += TransferTo(i, destination);
        return moved;
    }
}
