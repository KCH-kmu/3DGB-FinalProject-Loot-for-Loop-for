using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// UI input resolves first; movement/animation consumes this frame's weapon state afterwards.
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class PlayerWeaponController : MonoBehaviour
{
    static WeaponState[] sessionWeapons;
    static int selected;
    TopDownPlayer player;
    PlayerInventory inventory;
    HideoutDialogue dialogue;
    WeaponCatalog catalog;
    Camera view;
    Transform grip, visual;
    readonly LineRenderer[] tracers = new LineRenderer[48];
    readonly float[] tracerUntil = new float[48];
    int tracerIndex;
    bool requireRelease;
    GUIStyle hudStyle;
    public WeaponState Current => sessionWeapons == null ? null : sessionWeapons[selected];
    public WeaponKind SelectedKind => Current == null ? WeaponKind.Revolver : Current.Definition.kind;
    public Vector3 AimDirection { get; private set; } = Vector3.forward;
    public Vector3 AimPoint { get; private set; }
    public bool IsAiming { get; private set; }
    public bool IsShooting => CanControl && Current != null && !Current.IsReloading &&
        (Current.BurstRemaining > 0 || Time.time - Current.LastShotTime < .16f);
    public int LastPelletCount { get; private set; }
    public int LastDamageHits { get; private set; }
    public bool CanControl
    {
        get
        {
            var health = GetComponent<RaidHealth>();
            return isActiveAndEnabled && player && player.InputEnabled && (!health || health.health > 0) &&
                (!inventory || !inventory.IsBlocking) && (!dialogue || !dialogue.IsBlocking);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession() { sessionWeapons = null; selected = 0; }
    void Start()
    {
        player = GetComponent<TopDownPlayer>(); inventory = GetComponent<PlayerInventory>(); dialogue = GetComponent<HideoutDialogue>();
        view = Camera.main; catalog = Resources.Load<WeaponCatalog>("WeaponCatalog");
        if (!catalog || catalog.weapons == null || catalog.weapons.Length != 4) { Debug.LogError("Four-weapon catalog is missing."); enabled = false; return; }
        if (sessionWeapons == null)
        {
            sessionWeapons = new WeaponState[4];
            for (int i = 0; i < 4; i++)
            {
                if (!catalog.weapons[i] || (int)catalog.weapons[i].kind != i) throw new InvalidOperationException("Weapon catalog order must be Revolver/Rifle/SMG/Shotgun.");
                sessionWeapons[i] = new WeaponState(catalog.weapons[i]);
            }
        }
        foreach (var bone in GetComponentsInChildren<Transform>())
            if (bone.name == "Index1.R") { grip = bone; break; }
        if (!grip) grip = transform;
        for (int i = 0; i < tracers.Length; i++)
        {
            var go = new GameObject("ShotTracer_" + i); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2; line.startWidth = .018f; line.endWidth = .005f;
            line.sharedMaterial = catalog.tracerMaterial; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false; line.enabled = false; tracers[i] = line;
        }
        requireRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
        EquipVisual();
    }
    void Update()
    {
        if (Current == null) return;
        var mouse = Mouse.current; var keyboard = Keyboard.current;
        bool held = mouse != null && mouse.leftButton.isPressed;
        bool overUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        if (!held) requireRelease = false;
        if (!CanControl || overUi)
        {
            Current.CancelActions(Time.time); IsAiming = false;
            if (held) requireRelease = true;
            return;
        }
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) SelectWeapon(WeaponKind.Revolver);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectWeapon(WeaponKind.Rifle);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectWeapon(WeaponKind.SubmachineGun);
            if (keyboard.digit4Key.wasPressedThisFrame) SelectWeapon(WeaponKind.Shotgun);
        }
        UpdateAim();
        bool pressed = !requireRelease && mouse != null && mouse.leftButton.wasPressedThisFrame;
        bool fireHeld = held && !requireRelease;
        IsAiming = fireHeld || Current.BurstRemaining > 0 || IsShooting;
        if (Current.Tick(Time.time, pressed, fireHeld, keyboard != null && keyboard.rKey.wasPressedThisFrame))
            FirePellets();
    }
    public bool SelectWeapon(WeaponKind kind)
    {
        int index = (int)kind;
        if (!CanControl || sessionWeapons == null || index < 0 || index >= 4) return false;
        if (selected == index) return true;
        Current.CancelActions(Time.time); selected = index;
        requireRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
        IsAiming = false; EquipVisual(); return true;
    }
    public WeaponState GetWeapon(WeaponKind kind)
    {
        int index = (int)kind;
        return sessionWeapons != null && index >= 0 && index < 4 ? sessionWeapons[index] : null;
    }
    void EquipVisual()
    {
        if (visual) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); }
        if (Current.Definition.modelPrefab)
        {
            visual = Instantiate(Current.Definition.modelPrefab).transform;
            visual.SetParent(transform, false);
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
        }
    }
    void UpdateAim()
    {
        Vector3 origin = transform.position + Vector3.up * 1.1f;
        AimPoint = origin + transform.forward * 30;
        if (!view || Mouse.current == null) { AimDirection = transform.forward; return; }
        var ray = view.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (NearestHit(ray.origin, ray.direction, 250, out var hit))
        {
            var enemy = hit.collider.GetComponentInParent<RaidEnemyHealth>();
            AimPoint = enemy ? enemy.transform.position + Vector3.up * 1.1f :
                hit.point + (hit.normal.y > .5f ? Vector3.up * 1.1f : Vector3.zero);
        }
        else
        {
            var plane = new Plane(Vector3.up, origin);
            if (plane.Raycast(ray, out float distance)) AimPoint = ray.GetPoint(distance);
        }
        Vector3 delta = AimPoint - origin;
        AimDirection = delta.sqrMagnitude > .01f ? delta.normalized : transform.forward;
    }
    public static Vector3 SpreadDirection(Vector3 forward, float halfAngle, Vector2 disk)
    {
        disk = Vector2.ClampMagnitude(disk, 1);
        var right = Vector3.Cross(Mathf.Abs(forward.y) > .99f ? Vector3.forward : Vector3.up, forward).normalized;
        var up = Vector3.Cross(forward, right);
        float radius = Mathf.Tan(Mathf.Clamp(halfAngle, 0, 30) * Mathf.Deg2Rad);
        return (forward.normalized + radius * (right * disk.x + up * disk.y)).normalized;
    }
    public bool NearestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
    {
        closest = default; float best = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, CombatPhysics.RaycastMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.distance >= best) continue;
            best = hit.distance; closest = hit;
        }
        return !float.IsPositiveInfinity(best);
    }
    void FirePellets()
    {
        var definition = Current.Definition;
        Vector3 origin = transform.position + Vector3.up * 1.1f;
        // Ray from the body prevents a muzzle protruding through a wall from bypassing cover.
        Vector3 forward = (AimPoint - origin).normalized;
        Vector3 muzzle = grip.position + forward * definition.muzzleLocalPosition.z;
        LastPelletCount = definition.Pellets; LastDamageHits = 0;
        for (int i = 0; i < definition.Pellets; i++)
        {
            var direction = SpreadDirection(forward, definition.spreadHalfAngle, UnityEngine.Random.insideUnitCircle);
            Vector3 end = origin + direction * definition.range;
            if (NearestHit(origin, direction, definition.range, out var hit))
            {
                end = hit.point;
                var enemy = hit.collider.GetComponentInParent<RaidEnemyHealth>();
                if (enemy && enemy.Health > 0) { enemy.Damage(definition.damage); LastDamageHits++; }
            }
            // Very close cover also shortens the visible tracer.
            var start = Vector3.Distance(origin, end) < Vector3.Distance(origin, muzzle) ? origin : muzzle;
            var line = tracers[tracerIndex];
            line.SetPosition(0, start); line.SetPosition(1, end); line.enabled = true;
            tracerUntil[tracerIndex] = Time.time + .07f; tracerIndex = (tracerIndex + 1) % tracers.Length;
        }
    }
    void LateUpdate()
    {
        for (int i = 0; i < tracers.Length; i++) if (tracers[i] && Time.time >= tracerUntil[i]) tracers[i].enabled = false;
        if (visual && Current != null)
        {
            var definition = Current.Definition;
            Vector3 facing = IsAiming ? AimDirection : transform.forward;
            visual.SetPositionAndRotation(grip.position + Quaternion.LookRotation(facing) * definition.gripOffset,
                Quaternion.LookRotation(facing) * Quaternion.Euler(definition.modelEuler));
        }
    }
    void OnEnable()
    {
        if (visual) visual.gameObject.SetActive(true);
        requireRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
    }
    void OnDisable()
    {
        if (Current != null) Current.CancelActions(Time.time);
        IsAiming = false;
        if (visual) visual.gameObject.SetActive(false);
        foreach (var line in tracers) if (line) line.enabled = false;
    }
    void OnGUI()
    {
        if (!CanControl || Current == null) return;
        if (hudStyle == null)
        {
            var settings = Resources.Load<InventoryUiSettings>("InventoryUiSettings");
            hudStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 10, 8, 8) };
            if (settings && settings.font) hudStyle.font = settings.font;
        }
        string reload = Current.IsReloading ? (SelectedKind == WeaponKind.Shotgun ? "  쉘 장전 중" : "  재장전 중") : "";
        GUI.Box(new Rect(Screen.width - 354, Screen.height - 124, 338, 108),
            Current.Definition.displayName + "   " + Current.Loaded + " / " + Current.Definition.Capacity + reload +
            "\n예비 탄약 " + Current.Reserve + "   ·   R 재장전\n1 리볼버  2 소총  3 기관단총  4 산탄총", hudStyle);
        if (Mouse.current == null) return;
        var p = Mouse.current.position.ReadValue(); float y = Screen.height - p.y;
        var color = GUI.color; GUI.color = Current.Loaded > 0 ? new Color(1, .85f, .35f) : new Color(1, .3f, .3f);
        GUI.DrawTexture(new Rect(p.x - 6, y, 13, 1), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(p.x, y - 6, 1, 13), Texture2D.whiteTexture); GUI.color = color;
    }
}
