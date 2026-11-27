using System;
using System.Collections.Generic;
using UnityEngine;

// Scene objects are authoring previews. Select the raid's containers before gameplay starts.
[DefaultExecutionOrder(-200)]
public sealed class RaidLootSpawner : MonoBehaviour
{
    [SerializeField] RaidLootSettings settings;
    [SerializeField] RaidEncounter[] candidates = Array.Empty<RaidEncounter>();
    bool initialized;
    public IReadOnlyList<RaidEncounter> Candidates => candidates;
    public int ActiveCount { get; private set; }
    public int RaidSeed { get; private set; }
    public float ContainerFraction => settings ? settings.ContainerFraction : .8f;

    public void Configure(RaidLootSettings configuration, RaidEncounter[] points)
    {
        if (initialized) throw new InvalidOperationException("A running raid cannot be reconfigured.");
        settings = configuration;
        candidates = points;
    }

    void Awake()
    {
        if (initialized) return;
        initialized = true;
        var valid = new List<RaidEncounter>();
        var unique = new HashSet<RaidEncounter>();
        foreach (var point in candidates)
            if (point && point.isCrate && unique.Add(point)) valid.Add(point);
        RaidSeed = Guid.NewGuid().GetHashCode();
        var selected = SelectSlots(valid.Count, ContainerFraction, RaidSeed);
        var enabledSlots = new HashSet<int>(selected);
        for (int i = 0; i < valid.Count; i++) valid[i].gameObject.SetActive(enabledSlots.Contains(i));
        ActiveCount = selected.Length;
        Debug.Log($"RAID_LOOT_SPAWN: {ActiveCount}/{valid.Count} containers; seed={RaidSeed}", this);
    }

    // Exact quota, not independent coin flips: 25 points at 80% always gives 20.
    public static int[] SelectSlots(int count, float fraction, int seed)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var shuffled = new int[count];
        for (int i = 0; i < count; i++) shuffled[i] = i;
        var random = new System.Random(seed);
        for (int i = count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int swap = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = swap;
        }
        int active = Mathf.RoundToInt(count * Mathf.Clamp01(fraction));
        var result = new int[active];
        Array.Copy(shuffled, result, active);
        return result;
    }
}
