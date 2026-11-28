using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class HideoutDialogueView : MonoBehaviour
{
    HideoutDialogue owner;
    Font font;
    GameObject window, hint;
    Text title, body, next, progress, prompt;
    Image stripe;
    public bool WindowVisible => window && window.activeSelf;

    public void Initialize(HideoutDialogue dialogue, Font dialogueFont)
    {
        owner = dialogue; font = dialogueFont ? dialogueFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject("HideoutDialogueCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 120;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        if (!FindAnyObjectByType<EventSystem>())
        {
            var events = new GameObject("HideoutEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var hintRect = Panel(root.transform, "NpcInteractionHint", 560, 52, 24);
        hint = hintRect.gameObject; hintRect.GetComponent<Image>().raycastTarget = false;
        prompt = Label(hintRect, "Prompt", 12, 4, 536, 44, 18); prompt.alignment = TextAnchor.MiddleCenter;
        var card = Panel(root.transform, "NpcDialogueWindow", 860, 252, 24); window = card.gameObject;
        var strip = new GameObject("RoleAccent", typeof(RectTransform), typeof(Image)); strip.transform.SetParent(card, false);
        Place(strip.GetComponent<RectTransform>(), 0, 0, 860, 4); stripe = strip.GetComponent<Image>();
        title = Label(card, "Speaker", 28, 20, 660, 36, 24);
        body = Label(card, "DialogueText", 28, 70, 804, 96, 21);
        body.alignment = TextAnchor.UpperLeft;
        progress = Label(card, "Page", 28, 198, 470, 32, 15);
        Button(card, "CloseDialogue", "닫기 [ESC]", 690, 20, 142, 36, owner.Close);
        next = Button(card, "NextDialogue", "다음 [E]", 654, 190, 178, 42, owner.Advance);
        Hide();
    }

    public void Show(HideoutNpc npc, int page)
    {
        hint.SetActive(false); window.SetActive(true);
        title.text = npc.displayName + "  /  " + npc.roleName;
        stripe.color = npc.accent; title.color = npc.accent;
        body.text = npc.dialogue != null && page < npc.dialogue.Length ? npc.dialogue[page] : "반가워요.";
        int count = Mathf.Max(1, npc.dialogue == null ? 0 : npc.dialogue.Length);
        progress.text = "하이드아웃 주민   ·   " + (page + 1) + " / " + count;
        next.text = page + 1 < count ? "다음 [E]" : "대화 마치기 [E]";
    }
    public void Hide() { window.SetActive(false); hint.SetActive(false); }
    public void SetPrompt(HideoutNpc npc)
    {
        hint.SetActive(npc && !owner.IsOpen);
        if (npc) prompt.text = "[E] 대화: " + npc.displayName + "  ·  " + npc.roleName;
    }

    RectTransform Panel(Transform parent, string name, float width, float height, float bottom)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0);
        rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(0, bottom);
        go.GetComponent<Image>().color = new Color(.055f, .075f, .09f, .98f); return rect;
    }
    Text Label(Transform parent, string name, float x, float y, float width, float height, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, height);
        var text = go.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = new Color(.94f, .93f, .86f);
        text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
        return text;
    }
    Text Button(Transform parent, string name, string caption, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, height); go.GetComponent<Image>().color = new Color(.18f, .26f, .25f);
        var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>(); button.onClick.AddListener(action);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var text = Label(go.transform, "Label", 0, 0, width, height, 17); text.alignment = TextAnchor.MiddleCenter; text.text = caption; return text;
    }
    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
