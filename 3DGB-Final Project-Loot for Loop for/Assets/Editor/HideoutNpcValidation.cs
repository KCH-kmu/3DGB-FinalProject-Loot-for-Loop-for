using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class HideoutNpcValidation
{
    static HideoutDialogue dialogue;
    static TopDownPlayer player;
    static PlayerInventory inventory;
    static HideoutNpc[] npcs;
    static int index, step;
    static double next, deadline;
    static bool running, oldBackground, oldCursor;
    static CursorLockMode oldLock;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
    static Vector3 start, lockedPosition;
    static readonly StringBuilder Report = new StringBuilder();

    [MenuItem("Tools/Gameplay/Hideout NPCs/Run Play Check")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || running) throw new InvalidOperationException("Start a fresh Hideout_Bunker Play session first.");
        dialogue = Object.FindAnyObjectByType<HideoutDialogue>();
        Require(dialogue, "Dialogue player exists."); player = dialogue.GetComponent<TopDownPlayer>(); inventory = player.GetComponent<PlayerInventory>();
        npcs = Object.FindObjectsByType<HideoutNpc>(FindObjectsSortMode.None).OrderBy(n => n.npcId).ToArray();
        Require(npcs.Length == 8, "Eight residents exist.");
        oldBackground = Application.runInBackground; oldInputBackground = InputSystem.settings.backgroundBehavior;
        oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        oldCursor = Cursor.visible; oldLock = Cursor.lockState;
        Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.EnableDevice(Keyboard.current);
        start = player.transform.position; index = step = 0; next = 0; deadline = EditorApplication.timeSinceStartup + 120;
        Report.Clear(); running = true; Directory.CreateDirectory(HideoutNpcAuthoring.Output);
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!running) return;
        try
        {
            Require(EditorApplication.isPlaying, "Play session remains active.");
            Require(EditorApplication.timeSinceStartup < deadline, "Play check timeout.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .35;
            var npc = npcs[Mathf.Min(index, npcs.Length - 1)];
            switch (step++)
            {
                case 0:
                    Teleport(npc.interactionPoint.position); Keys(); break;
                case 1:
                    Require(dialogue.NearbyNpc == npc, "Nearest prompt targets " + npc.npcId);
                    Keys(Key.E); break;
                case 2:
                    Require(dialogue.CurrentNpc == npc && dialogue.Page == 0 && !inventory.IsOpen, "E opens only NPC dialogue.");
                    Require(!inventory.Open(null), "Inventory cannot open during dialogue.");
                    lockedPosition = player.transform.position; Keys(Key.W, Key.I);
                    break;
                case 3:
                    Require(dialogue.IsOpen && !inventory.IsOpen && Vector3.Distance(lockedPosition, player.transform.position) < .08f, "Movement and inventory stay locked.");
                    Keys();
                    if (npc.npcId == "fox") ScreenCapture.CaptureScreenshot(Path.GetFullPath(HideoutNpcAuthoring.Output + "/dialogue-fox.png"));
                    break;
                case 4: Keys(Key.E); break;
                case 5:
                    Require(dialogue.CurrentNpc == npc && dialogue.Page == 1, "E advances exactly one page."); Keys(); break;
                case 6: Keys(Key.E); break;
                case 7:
                    Require(!dialogue.IsOpen && !inventory.IsOpen, "Final E closes without reopening another UI.");
                    Require(Cursor.visible == oldCursor && Cursor.lockState == oldLock, "Cursor restored."); Keys();
                    Report.AppendLine("PASS " + npc.npcId + ": E opens / advances / closes, movement locked, I blocked, cursor restored.");
                    if (++index < npcs.Length) step = 0;
                    break;
                case 8:
                    Require(dialogue.Open(npc), "Can reopen after conversation."); Keys(Key.Escape); break;
                case 9:
                    Require(!dialogue.IsOpen, "Escape closes conversation."); Keys(); break;
                case 10:
                    Require(dialogue.Open(npc), "Reopen for button test.");
                    var button = GameObject.Find("NextDialogue").GetComponent<Button>();
                    ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                    Require(dialogue.Page == 1, "UI next button advances.");
                    button = GameObject.Find("CloseDialogue").GetComponent<Button>();
                    ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                    Require(!dialogue.IsOpen, "UI close button closes.");
                    break;
                case 11:
                    Teleport(new Vector3(0, .08f, 0)); break;
                case 12:
                    Require(!dialogue.Open(npc) && !dialogue.NearbyNpc, "Out-of-range talk rejected.");
                    Teleport(npc.transform.position - npc.transform.forward * .5f + Vector3.up * .08f); break;
                case 13:
                    Require(!dialogue.Open(npc), "Behind-counter talk rejected.");
                    Teleport(npc.interactionPoint.position); break;
                case 14:
                    Require(dialogue.Open(npc), "Open for range-close check."); Teleport(new Vector3(0, .08f, 0)); break;
                case 15:
                    Require(!dialogue.IsOpen, "Moving away closes conversation."); Keys(Key.I); break;
                case 16:
                    Require(inventory.IsOpen, "Inventory still opens normally away from NPCs."); Keys(); break;
                case 17:
                    inventory.Close(); Teleport(npc.interactionPoint.position); break;
                case 18:
                    Require(dialogue.Open(npc), "Open for disabled-NPC check."); npc.gameObject.SetActive(false); break;
                case 19:
                    Require(!dialogue.IsOpen, "Disabled NPC closes conversation."); npc.gameObject.SetActive(true); break;
                case 20:
                    Require(dialogue.Open(npc), "Open for external-input lock check."); player.InputEnabled = false; break;
                case 21:
                    Require(!dialogue.IsOpen, "Scene transition input lock closes conversation."); player.InputEnabled = true;
                    Report.AppendLine("PASS Escape, next/close buttons, out-of-range and back-side rejection, range/disable/input-lock close, normal inventory recovery.");
                    foreach (var resident in npcs)
                    {
                        var animal = resident.GetComponentInChildren<Animator>();
                        // The disabled/re-enabled resident just restarted its animator.
                        Require(animal.GetCurrentAnimatorStateInfo(0).normalizedTime > (resident == npc ? 0 : 1) && animal.GetCurrentAnimatorStateInfo(0).loop, "Resident idle loops: " + resident.npcId);
                        Require(animal.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m && m.shader.isSupported)), "All animal materials supported.");
                    }
                    Report.AppendLine("PASS all eight looping idle animations and supported materials.");
                    File.WriteAllText(HideoutNpcAuthoring.Output + "/play-checks.txt", Report.ToString());
                    Debug.Log("HIDEOUT_NPC_PLAY_CHECKS: PASS\n" + Report);
                    Cleanup(); EditorApplication.isPlaying = false; break;
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(HideoutNpcAuthoring.Output + "/play-checks.txt", "FAIL step " + (step - 1) + ", NPC " + index + ": " + error);
            Debug.LogException(error); Cleanup();
        }
    }
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
    static void Teleport(Vector3 position)
    {
        var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
        player.transform.position = position; controller.enabled = true; Physics.SyncTransforms();
        var camera = Camera.main; camera.transform.position = position + camera.GetComponent<TopDownFollowCamera>().offset;
    }
    static void Cleanup()
    {
        running = false; EditorApplication.update -= Tick;
        if (Keyboard.current != null) Keys();
        if (dialogue) dialogue.Close();
        if (inventory) inventory.Close();
        if (player) { player.InputEnabled = true; Teleport(start); }
        foreach (var npc in npcs) if (npc) npc.gameObject.SetActive(true);
        Application.runInBackground = oldBackground; InputSystem.settings.backgroundBehavior = oldInputBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        Cursor.visible = oldCursor; Cursor.lockState = oldLock;
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
