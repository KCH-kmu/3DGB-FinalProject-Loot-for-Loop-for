using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RavenmoreIconAuthoring
{
    const string Root = "Assets/ThirdParty/Ravenmore";
    const string Output = "../.utmp/Ravenmore";

    [MenuItem("Tools/Gameplay/Inventory/Connect Ravenmore Icons")]
    public static void Connect()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        Assign("cloth", "SurvivalCrafting/icon_042.png");
        Assign("rations", "SurvivalCrafting/icon_050.png");
        Assign("medical", "ModernResources/icon_23.png");
        Assign("parts", "ModernResources/icon_114.png");
        Assign("scrap", "ModernResources/icon_75.png");
        Assign("wire", "ModernResources/icon_122.png");
        // No matching ammunition/dressing icons in the free sets: retain the existing symbols.
        const string settingsPath = "Assets/Resources/InventoryUiSettings.asset";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var settings = AssetDatabase.LoadAssetAtPath<InventoryUiSettings>(settingsPath);
        if (!settings) { settings = ScriptableObject.CreateInstance<InventoryUiSettings>(); AssetDatabase.CreateAsset(settings, settingsPath); }
        settings.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/NotoSansKR/NotoSansKR.ttf");
        if (!settings.font) throw new InvalidOperationException("Korean UI font is missing.");
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        Debug.Log("RAVENMORE_ICONS_CONNECTED: six items; ammunition/bandages retain symbols; Korean font linked.");
    }

    static void Assign(string id, string icon)
    {
        var item = AssetDatabase.LoadAssetAtPath<LootItemDefinition>("Assets/Gameplay/InventoryItems/" + id + ".asset");
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/" + icon);
        if (!item || !sprite) throw new InvalidOperationException("Missing item/icon: " + id);
        item.icon = sprite; EditorUtility.SetDirty(item);
    }

    [MenuItem("Tools/Gameplay/Inventory/Import Ravenmore Icons")]
    public static void Import()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        foreach (string path in Directory.GetFiles(Root, "*.png", SearchOption.AllDirectories))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 64; importer.SaveAndReimport();
        }
        Directory.CreateDirectory(Output);
        ContactSheet("ModernResources"); ContactSheet("SurvivalCrafting");
        Debug.Log("RAVENMORE_ICONS_IMPORTED: 124 modern + 100 survival free PNG sprites.");
    }

    static void ContactSheet(string pack)
    {
        var paths = Directory.GetFiles(Root + "/" + pack, "*.png").OrderBy(p => int.Parse(Path.GetFileNameWithoutExtension(p).Replace("icon_", ""))).ToArray();
        int rows = Mathf.CeilToInt(paths.Length / 10f), height = rows * 94;
        var root = new GameObject("IconContactSheet", typeof(RectTransform), typeof(Canvas));
        var cameraGo = new GameObject("ContactCamera", typeof(Camera));
        var camera = cameraGo.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = height / 2f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .14f, .17f);
        camera.cullingMask = 1 << 5; camera.transform.position = new Vector3(0, 10000, -10);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        root.layer = 5; root.transform.position = new Vector3(0, 10000, 0);
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, height);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < paths.Length; i++)
        {
            var go = new GameObject("Icon", typeof(RectTransform), typeof(Image)); go.layer = 5; go.transform.SetParent(root.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(64, 64);
            rect.anchoredPosition = new Vector2(-450 + i % 10 * 100, height / 2 - 34 - i / 10 * 94);
            go.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
            var label = new GameObject("ID", typeof(RectTransform), typeof(Text)); label.layer = 5; label.transform.SetParent(root.transform, false);
            label.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 24);
            label.GetComponent<RectTransform>().anchoredPosition = rect.anchoredPosition - Vector2.up * 43;
            var text = label.GetComponent<Text>(); text.font = font; text.fontSize = 16; text.alignment = TextAnchor.MiddleCenter;
            text.text = Path.GetFileNameWithoutExtension(paths[i]); text.color = Color.white;
        }
        var target = new RenderTexture(1000, height, 24); var image = new Texture2D(1000, height, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try
        {
            Canvas.ForceUpdateCanvases(); camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1000, height), 0, 0); image.Apply(); File.WriteAllBytes(Output + "/" + pack + ".png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(root); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(target); Object.DestroyImmediate(image); }
    }
}
