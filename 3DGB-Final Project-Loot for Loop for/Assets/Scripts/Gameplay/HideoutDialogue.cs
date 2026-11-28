using UnityEngine;
using UnityEngine.InputSystem;

// Resolve E before inventory input. The closing frame also stays consumed.
[DefaultExecutionOrder(-200)]
[RequireComponent(typeof(TopDownPlayer))]
public sealed class HideoutDialogue : MonoBehaviour
{
    public Font koreanFont;
    public HideoutNpc CurrentNpc { get; private set; }
    public HideoutNpc NearbyNpc { get; private set; }
    public bool IsOpen => CurrentNpc;
    public bool IsBlocking => IsOpen || closedFrame == Time.frameCount;
    public int Page { get; private set; }
    TopDownPlayer player;
    PlayerInventory inventory;
    HideoutNpc[] npcs;
    HideoutDialogueView view;
    int closedFrame = -1;
    bool cursorWasVisible;
    CursorLockMode cursorWasLocked;

    void Start()
    {
        player = GetComponent<TopDownPlayer>();
        inventory = GetComponent<PlayerInventory>();
        npcs = FindObjectsByType<HideoutNpc>(FindObjectsSortMode.None);
        view = gameObject.AddComponent<HideoutDialogueView>();
        view.Initialize(this, koreanFont);
    }

    void Update()
    {
        if (!view) return;
        if (view.WindowVisible && !CurrentNpc) { Close(); return; }
        var health = GetComponent<RaidHealth>();
        bool available = player.InputEnabled && (!health || health.health > 0);
        var keyboard = Keyboard.current;
        if (IsOpen)
        {
            if (!available || !CurrentNpc.CanTalk(player)) { Close(); return; }
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) Close();
                else if (keyboard.eKey.wasPressedThisFrame) Advance();
            }
            return;
        }
        NearbyNpc = null;
        if (available && (!inventory || !inventory.IsOpen) && !IsBlocking)
        {
            float nearest = float.PositiveInfinity;
            foreach (var npc in npcs)
            {
                if (!npc || !npc.CanTalk(player)) continue;
                float distance = (npc.interactionPoint.position - player.transform.position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; NearbyNpc = npc; }
            }
        }
        view.SetPrompt(NearbyNpc);
        if (NearbyNpc && keyboard != null && keyboard.eKey.wasPressedThisFrame) Open(NearbyNpc);
    }

    public bool Open(HideoutNpc npc)
    {
        if (!view || IsBlocking || !player.InputEnabled || (inventory && inventory.IsOpen) || !npc || !npc.CanTalk(player)) return false;
        var health = GetComponent<RaidHealth>();
        if (health && health.health <= 0) return false;
        CurrentNpc = npc; NearbyNpc = npc; Page = 0;
        cursorWasVisible = Cursor.visible; cursorWasLocked = Cursor.lockState;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        view.Show(npc, Page);
        return true;
    }

    public void Advance()
    {
        if (!IsOpen) return;
        if (!player.InputEnabled || !CurrentNpc.CanTalk(player)) { Close(); return; }
        if (++Page >= (CurrentNpc.dialogue == null ? 0 : CurrentNpc.dialogue.Length)) { Close(); return; }
        view.Show(CurrentNpc, Page);
    }

    public void Close()
    {
        if (!CurrentNpc && (view == null || !view.WindowVisible)) return;
        CurrentNpc = null; NearbyNpc = null; closedFrame = Time.frameCount;
        Cursor.lockState = cursorWasLocked; Cursor.visible = cursorWasVisible;
        if (view) view.Hide();
    }

    void OnDisable() => Close();
}
