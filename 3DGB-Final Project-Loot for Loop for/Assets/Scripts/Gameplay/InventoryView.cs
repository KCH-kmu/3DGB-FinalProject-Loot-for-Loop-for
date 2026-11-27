using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Runtime uGUI keeps the same inventory available on both the raid and hideout player.
public sealed class InventoryView : MonoBehaviour
{
    static readonly Color Background = new Color(.055f, .073f, .09f, .99f);
    static readonly Color PanelColor = new Color(.085f, .11f, .135f);
    static readonly Color Accent = new Color(.53f, .86f, .63f);
    static readonly Color Muted = new Color(.58f, .66f, .71f);
    PlayerInventory owner;
    GameObject modal;
    Font font;
    Text containerTitle, containerCount, bagCount, status, empty, prompt, pageLabel;
    Button takeAll, previousPage, nextPage;
    readonly Slot[] lootSlots = new Slot[12];
    readonly Slot[] bagSlots = new Slot[16];
    int page;

    sealed class Slot
    {
        public Button button;
        public Text title, quantity, symbol;
        public Image marker, icon;
    }

    public void Initialize(PlayerInventory inventory)
    {
        owner = inventory;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        if (!FindAnyObjectByType<EventSystem>())
        {
            var events = new GameObject("InventoryEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        var hint = Box(canvasObject.transform, "InteractionHint", 0, 0, 560, 48, Background);
        hint.anchorMin = hint.anchorMax = new Vector2(.5f, 0);
        hint.pivot = new Vector2(.5f, 0);
        hint.anchoredPosition = new Vector2(0, 24);
        hint.GetComponent<Image>().raycastTarget = false;
        prompt = Label(hint, "Prompt", "[I]  Open bag", 8, 4, 544, 40, 17, TextAnchor.MiddleCenter);

        var overlay = Box(canvasObject.transform, "InventoryOverlay", 0, 0, 0, 0, new Color(0, 0, 0, .62f));
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        modal = overlay.gameObject;
        var window = Box(overlay, "InventoryWindow", 0, 0, 1160, 620, Background);
        window.anchorMin = window.anchorMax = new Vector2(.5f, .5f);
        window.pivot = new Vector2(.5f, .5f); window.anchoredPosition = Vector2.zero;
        Box(window, "Accent", 0, 0, 1160, 3, Accent);
        Label(window, "Eyebrow", "LOOT FOR LOOP  /  FIELD INVENTORY", 24, 16, 860, 24, 13).color = Accent;
        Label(window, "Title", "SUPPLIES & BACKPACK", 24, 41, 860, 40, 27);
        ActionButton(window, "CloseInventory", "CLOSE  [ESC]", 980, 26, 156, 44, owner.Close);

        var left = Box(window, "ContainerPanel", 24, 98, 430, 424, PanelColor);
        containerTitle = Label(left, "ContainerName", "CONTAINER", 18, 12, 298, 30, 18);
        containerCount = Label(left, "ContainerCount", "", 310, 12, 100, 30, 13, TextAnchor.MiddleRight);
        var right = Box(window, "BackpackPanel", 474, 98, 662, 424, PanelColor);
        Label(right, "BagTitle", "YOUR BACKPACK", 22, 12, 400, 30, 18);
        bagCount = Label(right, "BagCount", "0 / 16 SLOTS", 440, 12, 200, 30, 14, TextAnchor.MiddleRight);
        bagCount.color = Accent;
        for (int i = 0; i < lootSlots.Length; i++)
        {
            int index = i;
            lootSlots[i] = MakeSlot(left, "LootSlot_" + i, 18 + i % 3 * 134, 60 + i / 3 * 84, 124,
                () => owner.Take(page * lootSlots.Length + index));
        }
        for (int i = 0; i < bagSlots.Length; i++)
        {
            int index = i;
            bagSlots[i] = MakeSlot(right, "BagSlot_" + i, 22 + i % 4 * 156, 60 + i / 4 * 84, 144, () =>
            {
                if (index < owner.Bag.Stacks.Count)
                {
                    var item = owner.Bag.Stacks[index].item;
                    SetStatus(item.displayName + "  /  " + item.description);
                }
            });
        }
        empty = Label(left, "EmptyMessage", "", 18, 150, 394, 100, 18, TextAnchor.MiddleCenter);
        empty.color = Muted;
        takeAll = ActionButton(window, "TakeAll", "TAKE ALL  >", 24, 538, 220, 44, () => owner.TakeAll());
        takeAll.GetComponent<Image>().color = new Color(.16f, .32f, .24f);
        previousPage = ActionButton(window, "PreviousPage", "<", 260, 538, 44, 44, () => { page--; Refresh(); });
        pageLabel = Label(window, "Page", "1 / 1", 305, 538, 100, 44, 14, TextAnchor.MiddleCenter);
        nextPage = ActionButton(window, "NextPage", ">", 410, 538, 44, 44, () => { page++; Refresh(); });
        status = Label(window, "Status", "Click a container item to move its stack to your bag.", 474, 535, 662, 48, 15);
        status.color = Accent;
        Label(window, "Controls", "CLICK  Take stack     I  Backpack     E / ESC  Close     World remains active", 24, 590, 1112, 22, 12).color = Muted;
        modal.SetActive(false);
    }

    public void Show()
    {
        page = 0;
        modal.SetActive(true);
        prompt.transform.parent.gameObject.SetActive(false);
        SetStatus(owner.OpenContainer ? "Click a container item to move its stack to your bag." : "Your bag stays with you when changing scenes.");
        Refresh();
    }

    public void Hide() { modal.SetActive(false); }
    public void SetStatus(string message) => status.text = message;
    public void SetPrompt(LootContainer container, bool available)
    {
        prompt.transform.parent.gameObject.SetActive(available && !owner.IsOpen);
        prompt.text = container ? "[E]  Open " + container.Label + (container.HasBeenOpened && container.Contents.Stacks.Count == 0 ? "  (Empty)" : "") + "     [I]  Bag" : "[I]  Open bag";
    }

    public void Refresh()
    {
        var container = owner.OpenContainer;
        var contents = container ? container.Contents.Stacks : null;
        int count = contents == null ? 0 : contents.Count;
        int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)lootSlots.Length));
        page = Mathf.Clamp(page, 0, pages - 1);
        containerTitle.text = container ? container.Label.ToUpperInvariant() : "NO CONTAINER";
        containerCount.text = container ? count + " STACKS" : "";
        bagCount.text = owner.Bag.Stacks.Count + " / " + owner.Bag.Capacity + " SLOTS";
        for (int i = 0; i < lootSlots.Length; i++)
        {
            int index = page * lootSlots.Length + i;
            Fill(lootSlots[i], index < count ? contents[index] : null, i + 1);
        }
        for (int i = 0; i < bagSlots.Length; i++)
            Fill(bagSlots[i], i < owner.Bag.Stacks.Count ? owner.Bag.Stacks[i] : null, i + 1);
        empty.text = !container ? "Approach a supply container\nand press E to inspect it." : count == 0 ? "CONTAINER EMPTY\nEverything has been collected." : "";
        takeAll.interactable = count > 0;
        previousPage.interactable = page > 0;
        nextPage.interactable = page + 1 < pages;
        pageLabel.text = (page + 1) + " / " + pages;
    }

    void Fill(Slot slot, LootStack stack, int number)
    {
        bool occupied = stack != null && stack.item;
        slot.button.interactable = occupied;
        slot.title.text = occupied ? stack.item.displayName : "";
        slot.quantity.text = occupied ? "x" + stack.quantity : number.ToString("00");
        slot.quantity.color = occupied ? Color.white : new Color(.27f, .34f, .40f);
        slot.symbol.text = occupied && !stack.item.icon ? stack.item.symbol : "";
        slot.symbol.color = occupied ? stack.item.color : Color.clear;
        slot.marker.color = occupied ? stack.item.color : new Color(.16f, .2f, .24f);
        slot.icon.sprite = occupied ? stack.item.icon : null;
        slot.icon.enabled = occupied && stack.item.icon;
    }

    Slot MakeSlot(Transform parent, string name, float x, float y, float width, UnityEngine.Events.UnityAction click)
    {
        var button = ActionButton(parent, name, "", x, y, width, 74, click);
        var root = button.transform;
        var marker = Box(root, "Category", 0, 0, 3, 74, Accent).GetComponent<Image>();
        marker.raycastTarget = false;
        var icon = Box(root, "Icon", 12, 8, 30, 30, Color.white).GetComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false;
        return new Slot {
            button = button, marker = marker, icon = icon,
            symbol = Label(root, "Symbol", "", 12, 7, 52, 30, 18),
            quantity = Label(root, "Quantity", "", width - 62, 8, 52, 26, 15, TextAnchor.MiddleRight),
            title = Label(root, "ItemName", "", 12, 39, width - 20, 30, 13)
        };
    }

    RectTransform Box(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        Place(rect, x, y, width, height);
        go.GetComponent<Image>().color = color;
        return rect;
    }

    Text Label(Transform parent, string name, string text, float x, float y, float width, float height, int size, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, height);
        var label = go.GetComponent<Text>();
        label.font = font; label.fontSize = size; label.color = new Color(.91f, .94f, .95f);
        label.text = text; label.alignment = alignment; label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return label;
    }

    Button ActionButton(Transform parent, string name, string text, float x, float y, float width, float height, UnityEngine.Events.UnityAction click)
    {
        var rect = Box(parent, name, x, y, width, height, new Color(.13f, .17f, .21f));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.2f, 1.35f, 1.25f);
        colors.pressedColor = new Color(.7f, 1, .8f);
        colors.disabledColor = new Color(.65f, .65f, .65f, .7f);
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(click);
        if (!string.IsNullOrEmpty(text)) Label(rect, "Label", text, 4, 0, width - 8, height, 14, TextAnchor.MiddleCenter);
        return button;
    }

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
