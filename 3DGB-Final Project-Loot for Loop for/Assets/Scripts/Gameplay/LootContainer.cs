using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RaidEncounter))]
public sealed class LootContainer : MonoBehaviour
{
    [SerializeField] LootStack[] startingContents = Array.Empty<LootStack>();
    LootInventory contents;
    RaidEncounter encounter;
    public const float InteractionRange = 2.5f;
    public string Label => (encounter ? encounter : encounter = GetComponent<RaidEncounter>()).crateLabel;
    public bool HasBeenOpened { get; private set; }
    public LootInventory Contents
    {
        get
        {
            if (contents != null) return contents;
            int slots = 12;
            foreach (var entry in startingContents)
                if (entry != null && entry.item && entry.quantity > 0)
                    slots += Mathf.CeilToInt((float)entry.quantity / entry.item.StackLimit);
            contents = new LootInventory(slots);
            foreach (var entry in startingContents)
                if (entry != null) contents.Add(entry.item, entry.quantity);
            return contents;
        }
    }

    public void Configure(LootStack[] entries)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Configure loot in Edit mode.");
        startingContents = entries;
        contents = null;
    }

    public bool CanOpen(TopDownPlayer player)
    {
        if (!player || !isActiveAndEnabled || !GetComponent<RaidEncounter>().isCrate) return false;
        var health = player.GetComponent<RaidHealth>();
        return (!health || health.health > 0) &&
            Vector3.Distance(transform.position, player.transform.position) < InteractionRange;
    }

    public void MarkOpened() => HasBeenOpened = true;
}
