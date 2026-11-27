using UnityEngine;

[CreateAssetMenu(menuName = "Loot for Loop/Inventory Item")]
public sealed class LootItemDefinition : ScriptableObject
{
    public string itemId;
    public string displayName;
    [TextArea] public string description;
    public string symbol;
    public Color color = Color.white;
    public Sprite icon;
    [Min(1)] public int maxStack = 10;
    public int StackLimit => Mathf.Max(1, maxStack);
}
