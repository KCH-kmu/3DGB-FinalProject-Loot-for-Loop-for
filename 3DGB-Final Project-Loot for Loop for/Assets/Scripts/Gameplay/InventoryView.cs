using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Slots retain their addresses, including gaps; dragging never removes items until a valid drop.
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
    Text detailName, detailDescription, detailQuantity, detailSymbol, ghostQuantity, ghostSymbol;
    Image detailIcon, ghostIcon;
    RectTransform ghost, canvasRect;
    Button takeAll, previousPage, nextPage, transfer, split;
    readonly Slot[] lootSlots = new Slot[12];
    readonly Slot[] bagSlots = new Slot[16];
    int page, selectedSlot = -1;
    bool selectedBag;
    LootInventory dragInventory;
    LootStack dragStack;
    int dragSlot, dragQuantity, dragOriginalQuantity;
    public bool IsDragging => dragStack != null;

    sealed class Slot
    {
        public Button button;
        public Text quantity, symbol;
        public Image marker, icon;
        public InventoryGridSlot input;
    }

    public void Initialize(PlayerInventory inventory)
    {
        owner = inventory;
        var settings = Resources.Load<InventoryUiSettings>("InventoryUiSettings");
        font = settings && settings.font ? settings.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasRect = canvasObject.GetComponent<RectTransform>();
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
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
        hint.pivot = new Vector2(.5f, 0); hint.anchoredPosition = new Vector2(0, 24);
        hint.GetComponent<Image>().raycastTarget = false;
        prompt = Label(hint, "Prompt", "[I] 가방 열기", 8, 4, 544, 40, 17, TextAnchor.MiddleCenter);

        var overlay = Box(canvasObject.transform, "InventoryOverlay", 0, 0, 0, 0, new Color(0, 0, 0, .62f));
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        modal = overlay.gameObject;
        var window = Box(overlay, "InventoryWindow", 0, 0, 1120, 620, Background);
        window.anchorMin = window.anchorMax = new Vector2(.5f, .5f);
        window.pivot = new Vector2(.5f, .5f); window.anchoredPosition = Vector2.zero;
        Box(window, "Accent", 0, 0, 1120, 3, Accent);
        Label(window, "Eyebrow", "LOOT FOR LOOP  /  FIELD INVENTORY", 24, 16, 800, 24, 13).color = Accent;
        Label(window, "Title", "보급품과 가방", 24, 43, 800, 40, 27);
        ActionButton(window, "CloseInventory", "닫기  [ESC]", 940, 26, 156, 44, owner.Close);
        var backpack = Box(window, "BackpackPanel", 24, 104, 380, 424, PanelColor);
        Label(backpack, "BagTitle", "내 가방", 18, 10, 160, 34, 18);
        bagCount = Label(backpack, "BagCount", "", 180, 10, 182, 34, 14, TextAnchor.MiddleRight);
        var container = Box(window, "ContainerPanel", 420, 104, 316, 424, PanelColor);
        containerTitle = Label(container, "ContainerName", "", 18, 10, 210, 34, 16);
        containerCount = Label(container, "ContainerCount", "", 230, 10, 68, 34, 13, TextAnchor.MiddleRight);
        bagCount.color = Accent;
        for (int i = 0; i < lootSlots.Length; i++)
            lootSlots[i] = MakeSlot(container, "LootSlot_" + i, 30 + i % 3 * 88, 60 + i / 3 * 88, false, i);
        for (int i = 0; i < bagSlots.Length; i++)
            bagSlots[i] = MakeSlot(backpack, "BagSlot_" + i, 18 + i % 4 * 88, 60 + i / 4 * 88, true, i);
        empty = Label(container, "EmptyMessage", "", 18, 158, 280, 100, 17, TextAnchor.MiddleCenter);
        empty.color = Muted;

        var details = Box(window, "ItemDetails", 752, 104, 344, 424, PanelColor);
        Label(details, "DetailHeading", "아이템 정보", 20, 10, 304, 34, 16).color = Muted;
        detailIcon = Box(details, "DetailIcon", 20, 64, 64, 64, Color.white).GetComponent<Image>();
        detailIcon.preserveAspect = true; detailIcon.raycastTarget = false;
        detailSymbol = Label(details, "DetailSymbol", "", 20, 64, 64, 64, 23, TextAnchor.MiddleCenter);
        detailName = Label(details, "DetailName", "", 100, 62, 224, 70, 22);
        detailDescription = Label(details, "Description", "", 20, 152, 304, 84, 16);
        detailDescription.color = Muted;
        detailQuantity = Label(details, "StackInfo", "", 20, 246, 304, 40, 16);
        transfer = ActionButton(details, "TransferSelected", "상자 ↔ 가방으로 옮기기", 20, 306, 304, 42, QuickSelected);
        split = ActionButton(details, "SplitSelected", "절반 나누기", 20, 360, 304, 42, SplitSelected);
        takeAll = ActionButton(window, "TakeAll", "모두 가져오기", 420, 544, 164, 40, () => owner.TakeAll());
        takeAll.GetComponent<Image>().color = new Color(.16f, .32f, .24f);
        previousPage = ActionButton(window, "PreviousPage", "<", 596, 544, 36, 40, () => { if (!IsDragging) { page--; Refresh(); } });
        pageLabel = Label(window, "Page", "", 632, 544, 68, 40, 14, TextAnchor.MiddleCenter);
        nextPage = ActionButton(window, "NextPage", ">", 700, 544, 36, 40, () => { if (!IsDragging) { page++; Refresh(); } });
        status = Label(window, "Status", "", 24, 540, 380, 46, 14); status.color = Accent;
        Label(window, "Controls", "드래그: 이동 / 교환    Ctrl+드래그: 절반 이동    우클릭: 분할    Shift+클릭 / 더블클릭: 빠른 이동", 24, 591, 1072, 22, 12).color = Muted;

        ghost = Box(canvasObject.transform, "DraggedItem", 0, 0, 80, 80, new Color(.12f, .19f, .23f, .9f));
        ghost.anchorMin = ghost.anchorMax = ghost.pivot = new Vector2(.5f, .5f);
        ghost.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        ghostIcon = Box(ghost, "Icon", 8, 8, 64, 64, Color.white).GetComponent<Image>(); ghostIcon.preserveAspect = true;
        ghostSymbol = Label(ghost, "Symbol", "", 8, 8, 64, 64, 22, TextAnchor.MiddleCenter);
        ghostQuantity = Label(ghost, "Quantity", "", 4, 52, 70, 26, 15, TextAnchor.MiddleRight);
        ghost.gameObject.SetActive(false); modal.SetActive(false);
    }

    public void Show()
    {
        page = 0; selectedSlot = -1; CancelDrag(); modal.SetActive(true);
        prompt.transform.parent.gameObject.SetActive(false);
        SetStatus("아이템을 선택해 정보를 확인하세요.\n드래그하면 이동하거나 중첩할 수 있습니다.");
        Refresh();
    }
    public void Hide() { CancelDrag(); modal.SetActive(false); }
    void OnDisable() { if (ghost) CancelDrag(); }
    public void SetStatus(string message) => status.text = message;
    public void SetPrompt(LootContainer container, bool available)
    {
        prompt.transform.parent.gameObject.SetActive(available && !owner.IsOpen);
        prompt.text = container ? "[E] " + container.Label + (container.HasBeenOpened && container.Contents.Stacks.Count == 0 ? " (비어 있음)" : "") + "    [I] 가방" : "[I] 가방 열기";
    }
    public void Refresh()
    {
        var container = owner.OpenContainer;
        var contents = container ? container.Contents : null;
        int count = contents == null ? 0 : contents.Stacks.Count;
        int pages = Mathf.Max(1, Mathf.CeilToInt((contents == null ? 0 : contents.Capacity) / 12f));
        page = Mathf.Clamp(page, 0, pages - 1);
        containerTitle.text = container ? container.Label : "열린 상자 없음";
        containerCount.text = container ? count + " 묶음" : "";
        bagCount.text = owner.Bag.Stacks.Count + " / " + owner.Bag.Capacity + " 칸";
        for (int i = 0; i < lootSlots.Length; i++)
        {
            int slot = page * lootSlots.Length + i;
            lootSlots[i].input.SlotIndex = slot;
            Fill(lootSlots[i], contents?.At(slot), contents != null && slot < contents.Capacity);
        }
        for (int i = 0; i < bagSlots.Length; i++) Fill(bagSlots[i], owner.Bag.At(i), i < owner.Bag.Capacity);
        // Empty containers still accept drops; do not cover their grid with text.
        empty.text = container ? "" : "상자 가까이에서 E키를 눌러\n내용물을 확인하세요.";
        takeAll.interactable = count > 0 && !IsDragging;
        previousPage.interactable = page > 0 && !IsDragging;
        nextPage.interactable = page + 1 < pages && !IsDragging;
        pageLabel.text = (page + 1) + " / " + pages;
        UpdateDetails();
    }
    void Fill(Slot slot, LootStack stack, bool valid)
    {
        bool occupied = stack != null && stack.item;
        slot.button.interactable = valid;
        slot.quantity.text = occupied ? "x" + stack.quantity : (slot.input.SlotIndex + 1).ToString("00");
        slot.quantity.color = occupied ? Color.white : new Color(.27f, .34f, .40f);
        slot.symbol.text = occupied && !stack.item.icon ? stack.item.symbol : "";
        slot.symbol.color = occupied ? stack.item.color : Color.clear;
        slot.icon.sprite = occupied ? stack.item.icon : null;
        slot.icon.enabled = occupied && stack.item.icon;
        slot.marker.color = selectedSlot == slot.input.SlotIndex && selectedBag == slot.input.IsBag ? Accent : new Color(.2f, .27f, .31f);
    }
    public void Select(bool bag, int slot)
    {
        if (IsDragging) return;
        selectedBag = bag; selectedSlot = slot; Refresh();
    }
    void UpdateDetails()
    {
        var inventory = owner.InventoryFor(selectedBag);
        var stack = inventory?.At(selectedSlot);
        detailName.text = stack == null ? "아이템 선택" : stack.item.displayName;
        detailDescription.text = stack == null ? "빈칸으로 이동하거나 같은 아이템 위에 놓아 중첩할 수 있습니다." : stack.item.description;
        detailQuantity.text = stack == null ? "" : "수량  " + stack.quantity + " / " + stack.item.StackLimit;
        detailIcon.sprite = stack?.item.icon; detailIcon.enabled = stack != null && stack.item.icon;
        detailSymbol.text = stack != null && !stack.item.icon ? stack.item.symbol : "";
        if (stack != null) detailSymbol.color = stack.item.color;
        transfer.interactable = stack != null && owner.OpenContainer && !IsDragging;
        split.interactable = stack != null && stack.quantity > 1 && inventory.Stacks.Count < inventory.Capacity && !IsDragging;
    }
    void QuickSelected() => Quick(selectedBag, selectedSlot);
    void SplitSelected() => Split(selectedBag, selectedSlot);
    public void Quick(bool bag, int slot)
    {
        if (IsDragging) return;
        int moved = owner.QuickTransfer(bag, slot);
        SetStatus(moved > 0 ? moved + "개를 옮겼습니다." : "옮길 공간이나 열린 상자를 확인하세요.");
    }
    public void Split(bool bag, int slot)
    {
        if (IsDragging) return;
        int moved = owner.SplitSlot(bag, slot);
        SetStatus(moved > 0 ? moved + "개를 빈칸에 나눴습니다." : "2개 이상의 아이템과 빈칸이 필요합니다.");
    }
    public void BeginDrag(bool bag, int slot, PointerEventData pointer)
    {
        CancelDrag();
        var inventory = owner.InventoryFor(bag);
        var stack = inventory?.At(slot);
        if (stack == null || !owner.CanEdit(inventory)) return;
        dragInventory = inventory; dragStack = stack; dragSlot = slot; dragOriginalQuantity = stack.quantity;
        dragQuantity = Keyboard.current != null && Keyboard.current.ctrlKey.isPressed && stack.quantity > 1 ? stack.quantity / 2 : stack.quantity;
        ghostIcon.sprite = stack.item.icon; ghostIcon.enabled = stack.item.icon;
        ghostSymbol.text = stack.item.icon ? "" : stack.item.symbol; ghostSymbol.color = stack.item.color;
        ghostQuantity.text = "x" + dragQuantity; ghost.gameObject.SetActive(true);
        pointer.eligibleForClick = false; Drag(pointer); Refresh();
    }
    public void Drag(PointerEventData pointer)
    {
        if (!IsDragging) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pointer.position, null, out var local))
            ghost.anchoredPosition = local;
    }
    bool DragValid() => IsDragging && dragInventory.At(dragSlot) == dragStack && dragStack.quantity == dragOriginalQuantity;
    public void Hover(InventoryGridSlot target, bool enter)
    {
        var slot = target.IsBag ? bagSlots[target.SlotIndex] : lootSlots[target.SlotIndex % 12];
        if (!enter || !IsDragging) { Fill(slot, owner.InventoryFor(target.IsBag)?.At(target.SlotIndex), slot.button.interactable); return; }
        bool valid = DragValid() && dragInventory.CanMove(dragSlot, owner.InventoryFor(target.IsBag), target.SlotIndex, dragQuantity);
        slot.marker.color = valid ? Accent : new Color(.95f, .3f, .25f);
    }
    public void Drop(bool bag, int slot)
    {
        if (!IsDragging) return;
        var source = dragInventory; int sourceSlot = dragSlot, quantity = dragQuantity;
        bool valid = DragValid(); CancelDrag();
        int moved = valid ? owner.MoveSlot(source, sourceSlot, owner.InventoryFor(bag), slot, quantity) : 0;
        if (moved > 0) { selectedBag = bag; selectedSlot = slot; }
        SetStatus(moved > 0 ? moved + "개를 옮겼습니다." : "이 칸으로 옮길 수 없습니다.\n아이템은 원래 위치에 남습니다.");
        if (owner.IsOpen) Refresh();
    }
    public void CancelDrag()
    {
        dragStack = null; dragInventory = null;
        if (ghost) ghost.gameObject.SetActive(false);
        if (modal && modal.activeSelf && owner.IsOpen) Refresh();
    }
    Slot MakeSlot(Transform parent, string name, float x, float y, bool bag, int slot)
    {
        var button = ActionButton(parent, name, "", x, y, 80, 80, () => { });
        var input = button.gameObject.AddComponent<InventoryGridSlot>();
        input.Initialize(this, bag, slot);
        button.onClick.AddListener(() => Select(input.IsBag, input.SlotIndex));
        var marker = Box(button.transform, "Category", 0, 0, 80, 3, Accent).GetComponent<Image>(); marker.raycastTarget = false;
        var icon = Box(button.transform, "Icon", 8, 6, 64, 64, Color.white).GetComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false;
        return new Slot {
            button = button, marker = marker, icon = icon, input = input,
            symbol = Label(button.transform, "Symbol", "", 8, 8, 64, 56, 20, TextAnchor.MiddleCenter),
            quantity = Label(button.transform, "Quantity", "", 5, 54, 70, 24, 14, TextAnchor.MiddleRight)
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
