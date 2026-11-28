using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class TopDownPlayer : MonoBehaviour
{
    [SerializeField] float moveSpeed = 4.5f;
    [SerializeField] float sprintSpeed = 7f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] Animator animator;
    CharacterController controller;
    float verticalSpeed;
    [SerializeField, Min(0)] float groundSnapDistance = 0.35f;
    readonly RaycastHit[] groundHits = new RaycastHit[16];
    PlayerInventory inventory;
    HideoutDialogue dialogue;
    PlayerWeaponController weapons;
    public bool InputEnabled { get; set; } = true;
    public Animator Animator { set => animator = value; }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        inventory = GetComponent<PlayerInventory>();
        if (!inventory) inventory = gameObject.AddComponent<PlayerInventory>();
        dialogue = GetComponent<HideoutDialogue>();
        weapons = GetComponent<PlayerWeaponController>();
        if (!weapons) weapons = gameObject.AddComponent<PlayerWeaponController>();
    }
    void Update()
    {
        var keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool sprint = false;
        bool canControl = InputEnabled && !inventory.IsBlocking && (!dialogue || !dialogue.IsBlocking);
        if (canControl && keyboard != null)
        {
            input.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            input.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }
        Vector3 direction = new Vector3(input.x, 0, input.y).normalized;
        Vector3 facing = weapons && weapons.IsAiming ? Vector3.ProjectOnPlane(weapons.AimDirection, Vector3.up) : direction;
        if (facing.sqrMagnitude > 0)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(facing), turnSpeed * Time.deltaTime);
        bool wasGrounded = controller.isGrounded;
        if (wasGrounded && verticalSpeed < 0) verticalSpeed = -2f;
        verticalSpeed = Mathf.Max(verticalSpeed + Physics.gravity.y * Time.deltaTime, -30f);
        controller.Move((direction * (sprint ? sprintSpeed : moveSpeed)
            + Vector3.up * verticalSpeed) * Time.deltaTime);
        if (wasGrounded && verticalSpeed <= 0) SnapToGround();
        if (animator)
        {
            bool moving = direction.sqrMagnitude > .01f;
            bool shooting = canControl && weapons && weapons.IsShooting;
            animator.SetBool("Moving", moving);
            animator.SetBool("Sprinting", moving && sprint);
            animator.SetBool("Shooting", shooting);
            // Run_Shoot has its own cadence; only accelerate the original unarmed run clip.
            animator.speed = moving && sprint && !shooting ? sprintSpeed / moveSpeed : 1f;
        }
    }
    // Keep contact over small downward steps without pulling the player off a cliff.
    void SnapToGround()
    {
        const float lift = 0.1f;
        float radius = controller.radius * 0.9f;
        Vector3 foot = transform.position + controller.center - Vector3.up * (controller.height * 0.5f);
        int count = Physics.SphereCastNonAlloc(foot + Vector3.up * (radius + lift), radius,
            Vector3.down, groundHits, lift + groundSnapDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        float minimumNormalY = Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < minimumNormalY) continue;
            closest = Mathf.Min(closest, hit.distance);
        }
        float drop = closest - lift;
        if (drop > 0.01f && drop <= groundSnapDistance)
            controller.Move(Vector3.down * drop);
    }}

