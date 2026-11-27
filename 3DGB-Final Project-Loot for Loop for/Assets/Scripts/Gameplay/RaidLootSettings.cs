using UnityEngine;

[CreateAssetMenu(menuName = "Loot for Loop/Raid Loot Settings")]
public sealed class RaidLootSettings : ScriptableObject
{
    [SerializeField, Range(0, 1)] float containerFraction = .8f;
    public float ContainerFraction => Mathf.Clamp01(containerFraction);
}
