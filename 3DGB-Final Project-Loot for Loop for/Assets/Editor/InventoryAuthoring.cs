using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class InventoryAuthoring
{
    const string Folder = "Assets/Gameplay/InventoryItems";

    [MenuItem("Tools/Gameplay/Inventory/Configure Raid Containers")]
    public static void Configure()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.name != "Game") throw new InvalidOperationException("Open Game in Edit mode first.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Gameplay", "InventoryItems");
        var items = new Dictionary<string, LootItemDefinition>();
        Add(items, "Ammo", "ammo", "AM", 60, new Color(.95f, .75f, .35f), "Ammunition for raid weapons.");
        Add(items, "Medical supplies", "medical", "+", 3, new Color(.91f, .43f, .45f), "A field medical kit.");
        Add(items, "Bandages", "bandage", "+B", 5, new Color(.95f, .65f, .61f), "Clean dressings for injuries.");
        Add(items, "Rations", "rations", "FD", 5, new Color(.58f, .82f, .43f), "Packaged food for long raids.");
        Add(items, "Cloth", "cloth", "CL", 10, new Color(.71f, .75f, .82f), "Fabric salvaged for crafting.");
        Add(items, "Spare parts", "parts", "SP", 10, new Color(.49f, .73f, .91f), "Mechanical components for repairs.");
        Add(items, "Scrap metal", "scrap", "MT", 10, new Color(.65f, .71f, .77f), "Metal recovered from discarded equipment.");
        Add(items, "Wire", "wire", "WR", 10, new Color(.85f, .59f, .37f), "Electrical wire for crafting.");

        var crates = Object.FindObjectsByType<RaidEncounter>(FindObjectsInactive.Include).Where(c => c.isCrate).ToArray();
        if (crates.Length == 0) throw new InvalidOperationException("Expected raid container candidates.");
        // Convert the previous display-only previews once, in the editor. Runtime uses typed data.
        foreach (var crate in crates)
        {
            var entries = new List<LootStack>();
            foreach (string text in crate.lootPreview.Split('/'))
            {
                string entry = text.Trim();
                int separator = entry.LastIndexOf(" x", StringComparison.Ordinal);
                if (separator < 1 || !items.TryGetValue(entry.Substring(0, separator), out var item) ||
                    !int.TryParse(entry.Substring(separator + 2), out int count) || count <= 0)
                    throw new InvalidOperationException("Unknown preview: " + entry);
                entries.Add(new LootStack(item, count));
            }
            var container = crate.GetComponent<LootContainer>();
            if (!container) container = Undo.AddComponent<LootContainer>(crate.gameObject);
            Undo.RecordObject(container, "Configure raid loot");
            container.Configure(entries.ToArray());
            EditorUtility.SetDirty(container);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"INVENTORY_CONFIGURED: {crates.Length} containers, 8 item definitions, 16-slot session backpack.");
    }

    static void Add(Dictionary<string, LootItemDefinition> items, string label, string id, string symbol, int maxStack, Color color, string description)
    {
        string path = Folder + "/" + id + ".asset";
        var item = AssetDatabase.LoadAssetAtPath<LootItemDefinition>(path);
        if (!item)
        {
            item = ScriptableObject.CreateInstance<LootItemDefinition>();
            item.itemId = id; item.displayName = label; item.symbol = symbol;
            item.maxStack = maxStack; item.color = color; item.description = description;
            AssetDatabase.CreateAsset(item, path);
        }
        items.Add(label, item);
    }
}
