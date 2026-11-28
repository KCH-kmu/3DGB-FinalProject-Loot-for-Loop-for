using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WeaponPlayValidation
{
    static PlayerWeaponController weapons;
    static TopDownPlayer player;
    static PlayerInventory inventory;
    static HideoutDialogue dialogue;
    static Camera camera;
    static RaidEnemyHealth target;
    static GameObject wall;
    static readonly List<GameObject> fixtures = new List<GameObject>();
    static readonly StringBuilder report = new StringBuilder();
    static int step, before, savedLoaded;
    static bool running, oldBackground;
    static double next, deadline;
    static InputSettings.BackgroundBehavior oldInput;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
    static WeaponState savedWeapon;
    static AsyncOperation travel;
    static Vector3 origin = new Vector3(0, 1000, 0);

    [MenuItem("Tools/Gameplay/Weapons/Run Play Check")]
    public static void Run()
    {
        if (!Application.isPlaying || running) throw new InvalidOperationException("Use a fresh Hideout_Bunker Play session.");
        player = Object.FindAnyObjectByType<TopDownPlayer>(); weapons = player.GetComponent<PlayerWeaponController>();
        inventory = player.GetComponent<PlayerInventory>(); dialogue = player.GetComponent<HideoutDialogue>();
        Require(weapons && weapons.Current != null && dialogue, "Initialized hideout weapon controller");
        Require(weapons.GetWeapon(WeaponKind.Revolver).TotalShots == 0, "Fresh ammunition session");
        oldBackground = Application.runInBackground; oldInput = InputSystem.settings.backgroundBehavior;
        oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        Application.runInBackground = true; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.EnableDevice(Keyboard.current); InputSystem.EnableDevice(Mouse.current);
        camera = Camera.main; camera.GetComponent<TopDownFollowCamera>().offset = new Vector3(6, 8, -10); camera.fieldOfView = 35;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "WeaponQA_Ground";
        ground.transform.position = origin - Vector3.up * .2f; ground.transform.localScale = new Vector3(50, .4f, 50); fixtures.Add(ground);
        Teleport(origin); NewTarget();
        report.Clear(); running = true; step = 0; next = 0; deadline = EditorApplication.timeSinceStartup + 80;
        Directory.CreateDirectory(WeaponAuthoring.Output); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (!running) return;
        try
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup >= deadline) throw new InvalidOperationException("Play validation stopped or timed out.");
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .3;
            switch (step++)
            {
                case 0: Input(false, Key.Digit1); break;
                case 1:
                    Require(weapons.SelectedKind == WeaponKind.Revolver, "1 selects revolver"); Input(true); break;
                case 2:
                    Require(weapons.Current.Loaded == 5 && target.Health == 66, "Revolver spends one round and damages target");
                    Require(weapons.LastPelletCount == 1 && weapons.LastDamageHits == 1, "Single hitscan ray hits enemy");
                    Capture("01-revolver.png"); next += .4; break;
                case 3:
                    Require(weapons.Current.Loaded == 5, "Holding revolver cannot auto-fire"); Input(false); break;
                case 4:
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "WeaponQA_Cover";
                    wall.transform.position = origin + new Vector3(0, 1, 3); wall.transform.localScale = new Vector3(4, 3, .3f); fixtures.Add(wall);
                    Physics.SyncTransforms(); Input(true); break;
                case 5:
                    Require(weapons.Current.Loaded == 4 && target.Health == 66, "Cover blocks damage while shot consumes ammo");
                    Object.Destroy(wall); Input(false, Key.R); next += 1.7; break;
                case 6:
                    Require(weapons.Current.Loaded == 6 && weapons.Current.Reserve == 34, "R refills revolver from reserve");
                    NewTarget(); Input(false, Key.Digit2); break;
                case 7:
                    Require(weapons.SelectedKind == WeaponKind.Rifle, "2 selects rifle"); Input(true); next = EditorApplication.timeSinceStartup + .04; break;
                case 8: Input(false); next += .2; break;
                case 9:
                    Require(weapons.Current.Loaded == 27 && weapons.Current.TotalShots == 3 && target.Health == 40, "Rifle emits exactly three rounds after a quick click");
                    Capture("02-rifle.png"); Input(true); next += .25; break;
                case 10:
                    Require(weapons.Current.Loaded == 24, "Second click starts one additional burst"); before = weapons.Current.TotalShots; next += .3; break;
                case 11:
                    Require(weapons.Current.TotalShots == before, "Holding rifle does not repeat bursts");
                    NewTarget(); Input(false, Key.Digit3); break;
                case 12:
                    Require(weapons.SelectedKind == WeaponKind.SubmachineGun, "3 selects SMG"); Input(true, Key.D); break;
                case 13:
                    Require(weapons.Current.TotalShots >= 2 && weapons.Current.Loaded == 30 - weapons.Current.TotalShots, "SMG fires repeatedly while held");
                    Require(player.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Walk_Shoot"), "Walking fire animation");
                    Input(true, Key.D, Key.LeftShift); break;
                case 14:
                    Require(player.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Run_Shoot"), "Running fire animation");
                    Capture("03-smg.png"); Input(false); break;
                case 15:
                    before = weapons.Current.TotalShots; Input(false, Key.Digit4); Teleport(origin); NewTarget(true); break;
                case 16:
                    Require(weapons.SelectedKind == WeaponKind.Shotgun && weapons.GetWeapon(WeaponKind.SubmachineGun).TotalShots == before, "4 selects shotgun and released SMG stops");
                    Input(true); next += 1; break;
                case 17:
                    Require(weapons.Current.Loaded == 7 && weapons.LastPelletCount == 12 && weapons.Current.TotalPellets == 12, "One shell emits twelve pellets; holding does not repeat");
                    Require(weapons.LastDamageHits == 12, "Twelve pellets independently hit a wide target");
                    Capture("04-shotgun.png"); Input(false); break;
                case 18: NewTarget(); Input(true); break;
                case 19:
                    Require(weapons.Current.Loaded == 6, "Second shell"); Input(false, Key.R); next = EditorApplication.timeSinceStartup + .5; break;
                case 20:
                    Require(weapons.Current.IsReloading && weapons.Current.Loaded == 6, "Shotgun reload has an insertion delay");
                    Input(false); next += .35; break;
                case 21:
                    Require(weapons.Current.Loaded == 7 && weapons.Current.Reserve == 23 && weapons.Current.IsReloading, "One shell inserted; loading continues");
                    Input(true); next = EditorApplication.timeSinceStartup + .2; break;
                case 22:
                    Require(!weapons.Current.IsReloading && weapons.Current.Loaded == 6, "Click interrupts shell loading and fires a shell");
                    Input(false, Key.R); break;
                case 23:
                    Require(inventory.Open(null), "Open inventory during reload"); savedLoaded = weapons.Current.Loaded; before = weapons.Current.TotalShots;
                    Input(true); next += 1; break;
                case 24:
                    Require(!weapons.Current.IsReloading && weapons.Current.Loaded == savedLoaded && weapons.Current.TotalShots == before, "Inventory cancels reload and blocks fire");
                    inventory.Close(); Input(true); break;
                case 25:
                    Require(weapons.Current.TotalShots == before, "Held click cannot leak through inventory closure");
                    Input(false);
                    var npc = Object.FindObjectsByType<HideoutNpc>().First(n => n.npcId == "fox");
                    Teleport(npc.interactionPoint.position); Require(dialogue.Open(npc), "Open dialogue");
                    Input(true); break;
                case 26:
                    Require(weapons.Current.TotalShots == before && !weapons.IsShooting, "NPC dialogue blocks fire");
                    dialogue.Close(); Input(false); break;
                case 27:
                    weapons.enabled = false; Require(!weapons.CanControl, "Disabled weapon component cannot fire");
                    weapons.enabled = true; Require(weapons.CanControl, "Weapon component can resume after enabling");
                    savedWeapon = weapons.Current; savedLoaded = savedWeapon.Loaded;
                    travel = SceneManager.LoadSceneAsync("Game"); break;
                case 28:
                    if (!travel.isDone) { step--; break; }
                    player = Object.FindAnyObjectByType<TopDownPlayer>(); weapons = player ? player.GetComponent<PlayerWeaponController>() : null;
                    if (!weapons || weapons.Current == null) { step--; break; }
                    Require(weapons.Current == savedWeapon && weapons.Current.Loaded == savedLoaded, "Scene transition preserves selected weapon and ammunition");
                    Require(Object.FindObjectsByType<RaidEncounter>().Where(e => !e.isCrate).All(e => e.GetComponent<RaidEnemyHealth>()), "Raid enemies have damage receivers");
                    Require(!weapons.SelectWeapon((WeaponKind)9), "Unknown weapon families rejected");
                    before = weapons.Current.TotalShots; player.GetComponent<RaidHealth>().Damage(1000); Input(true); break;
                case 29:
                    Require(!weapons.CanControl && weapons.Current.TotalShots == before, "Death blocks weapon input");
                    report.AppendLine("PASS real mouse/number/R inputs; semi/burst/auto/pellets; world cover and enemy damage; walk/run shooting; individual shell reload and interruption; UI/death guards; selected weapon/ammo persistence.");
                    Finish(true); break;
            }
        }
        catch (Exception e) { report.AppendLine("FAIL step " + (step - 1) + ": " + e); Finish(false); }
    }
    static void NewTarget(bool wide = false)
    {
        if (target) Object.Destroy(target.gameObject);
        var go = new GameObject("WeaponQA_Target"); go.transform.position = origin + Vector3.forward * 6; fixtures.Add(go);
        var body = GameObject.CreatePrimitive(wide ? PrimitiveType.Cube : PrimitiveType.Capsule);
        body.transform.SetParent(go.transform, false); body.transform.localPosition = Vector3.up;
        if (wide) body.transform.localScale = new Vector3(4, 4, .5f);
        go.AddComponent<RaidEncounter>(); target = go.AddComponent<RaidEnemyHealth>(); Physics.SyncTransforms();
    }
    static void Input(bool fire, params Key[] keys)
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        var point = camera ? camera.WorldToScreenPoint(origin + new Vector3(0, 1, 6)) : new Vector3(30, 30);
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = new Vector2(point.x, point.y) }.WithButton(MouseButton.Left, fire));
    }
    static void Teleport(Vector3 point)
    {
        var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
        player.transform.SetPositionAndRotation(point, Quaternion.identity); controller.enabled = true; Physics.SyncTransforms();
        camera.transform.position = point + camera.GetComponent<TopDownFollowCamera>().offset;
    }
    static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.GetFullPath(WeaponAuthoring.Output + "/" + name));
    static void Finish(bool passed)
    {
        running = false; EditorApplication.update -= Tick; Input(false);
        foreach (var go in fixtures) if (go) Object.Destroy(go); fixtures.Clear();
        Application.runInBackground = oldBackground; InputSystem.settings.backgroundBehavior = oldInput;
        InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        File.WriteAllText(WeaponAuthoring.Output + "/play-checks.txt", report.ToString());
        if (passed) Debug.Log("WEAPON_PLAY_CHECKS: PASS"); else Debug.LogError(report.ToString());
        EditorApplication.isPlaying = false;
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.AppendLine("PASS " + message);
    }
}
