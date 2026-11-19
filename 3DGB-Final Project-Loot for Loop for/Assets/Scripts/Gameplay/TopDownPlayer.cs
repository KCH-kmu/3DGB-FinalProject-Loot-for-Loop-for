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
    public bool InputEnabled { get; set; } = true;
    public Animator Animator { set => animator = value; }

    void Awake() => controller = GetComponent<CharacterController>();
    void Update()
    {
        var keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool sprint = false;
        if (InputEnabled && keyboard != null)
        {
            input.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            input.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }
        Vector3 direction = new Vector3(input.x, 0, input.y).normalized;
        if (direction.sqrMagnitude > 0)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
        bool wasGrounded = controller.isGrounded;
        if (wasGrounded && verticalSpeed < 0) verticalSpeed = -2f;
        verticalSpeed = Mathf.Max(verticalSpeed + Physics.gravity.y * Time.deltaTime, -30f);
        controller.Move((direction * (sprint ? sprintSpeed : moveSpeed)
            + Vector3.up * verticalSpeed) * Time.deltaTime);
        if (wasGrounded && verticalSpeed <= 0) SnapToGround();
        if (animator)
        {
            animator.SetBool("Moving", direction.sqrMagnitude > 0.01f);
            animator.speed = direction.sqrMagnitude > 0.01f && sprint ? sprintSpeed / moveSpeed : 1f;
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

