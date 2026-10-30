using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 80f;
    [Range(0f, 1f)]
    [SerializeField] private float airControl = 0.6f;

    [Header("Jump")]
    [Tooltip("How high a full (held) jump goes, in world units.")]
    [SerializeField] private float jumpHeight = 3f;
    [Tooltip("Seconds to reach the top of a full jump. Lower = snappier, less floaty.")]
    [SerializeField, Min(0.05f)] private float timeToApex = 0.38f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [Range(0f, 1f)]
    [SerializeField] private float jumpCutMultiplier = 0.5f;
    [Tooltip("Extra gravity while falling. 1 = same as rising.")]
    [SerializeField] private float fallGravityMultiplier = 1.6f;
    [Tooltip("Downward speed is capped here so long falls stay readable.")]
    [SerializeField] private float maxFallSpeed = 18f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private LayerMask groundLayers;

    [Header("Wall Check")]
    [Tooltip("How vertical a surface must be to count as a wall (1 = perfectly vertical).")]
    [Range(0f, 1f)]
    [SerializeField] private float wallNormalThreshold = 0.7f;

    [Header("Animation")]
    [Tooltip("Check this if the roll frames are drawn rolling to the right.")]
    [SerializeField] private bool spriteRollsRight = true;

    private static readonly int RollSpeedHash = Animator.StringToHash("RollSpeed");

    private Rigidbody2D rb;
    private Animator animator;
    private float baseGravityScale;
    private float jumpVelocity;
    private Vector2 moveInput;
    private bool isGrounded;
    private bool touchingWallLeft;
    private bool touchingWallRight;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool jumpCutRequested;

    private readonly ContactPoint2D[] contacts = new ContactPoint2D[8];

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        CalculateJumpPhysics();
    }

    // Lets you tweak jump values in Play mode and feel the change immediately
    private void OnValidate()
    {
        if (rb != null) CalculateJumpPhysics();
    }

    // Derive gravity and launch speed from the designer-friendly height + time values:
    //   gravity  = 2h / t^2
    //   velocity = 2h / t
    private void CalculateJumpPhysics()
    {
        float gravity = 2f * jumpHeight / (timeToApex * timeToApex);
        jumpVelocity = 2f * jumpHeight / timeToApex;
        baseGravityScale = gravity / Mathf.Abs(Physics2D.gravity.y);
        rb.gravityScale = baseGravityScale;
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        jumpAction.action.Disable();
    }

    private void Update()
    {
        moveInput = moveAction.action.ReadValue<Vector2>();

        if (jumpAction.action.WasPressedThisFrame())
            jumpBufferTimer = jumpBufferTime;
        else
            jumpBufferTimer -= Time.deltaTime;

        if (jumpAction.action.WasReleasedThisFrame())
            jumpCutRequested = true;

        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        // Roll speed is always positive: direction is shown by mirroring, not by playing backward
        if (animator != null)
        {
            float speed01 = Mathf.Abs(rb.linearVelocity.x) / moveSpeed;
            animator.SetFloat(RollSpeedHash, speed01);
        }

        // Mirror the object horizontally based on input direction; keep last facing when the stick is released
        if (Mathf.Abs(moveInput.x) > 0.1f)
        {
            bool movingLeft = moveInput.x < 0f;
            bool mirror = spriteRollsRight ? movingLeft : !movingLeft;

            Vector3 s = transform.localScale;
            s.x = Mathf.Abs(s.x) * (mirror ? -1f : 1f);
            transform.localScale = s;
        }
    }

    private void FixedUpdate()
    {
        CheckGround();
        CheckWalls();
        HandleHorizontal();
        HandleJump();
        HandleGravity();
    }

    private void CheckGround()
    {
        isGrounded = Physics2D.OverlapCircle(
            groundCheck.position, groundCheckRadius, groundLayers) != null;

        if (isGrounded)
            coyoteTimer = coyoteTime;
        else
            coyoteTimer -= Time.fixedDeltaTime;
    }

    // Looks at everything the player is currently touching. A contact normal points
    // from the surface toward the player, so a wall on our right has a normal pointing left.
    private void CheckWalls()
    {
        touchingWallLeft = false;
        touchingWallRight = false;

        int count = rb.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            Vector2 n = contacts[i].normal;
            if (n.x <= -wallNormalThreshold) touchingWallRight = true;
            else if (n.x >= wallNormalThreshold) touchingWallLeft = true;
        }
    }

    private void HandleHorizontal()
    {
        Vector2 v = rb.linearVelocity;
        float targetX = moveInput.x * moveSpeed;

        float rate = Mathf.Abs(targetX) > 0.01f ? acceleration : deceleration;
        if (!isGrounded) rate *= airControl;

        v.x = Mathf.MoveTowards(v.x, targetX, rate * Time.fixedDeltaTime);

        // Never push into a wall. With no sideways force there's nothing for
        // friction to grab, so gravity takes over and the player slides down.
        if (touchingWallRight && v.x > 0f) v.x = 0f;
        if (touchingWallLeft && v.x < 0f) v.x = 0f;

        rb.linearVelocity = v;
    }

    private void HandleJump()
    {
        Vector2 v = rb.linearVelocity;

        // Jump: buffered press + (grounded or within coyote window)
        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            v.y = jumpVelocity;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
        }

        // Variable jump height: releasing early cuts upward speed
        if (jumpCutRequested)
        {
            if (v.y > 0f) v.y *= jumpCutMultiplier;
            jumpCutRequested = false;
        }

        rb.linearVelocity = v;
    }

    private void HandleGravity()
    {
        // Stronger gravity while falling for a snappier arc
        rb.gravityScale = rb.linearVelocity.y < 0f
            ? baseGravityScale * fallGravityMultiplier
            : baseGravityScale;

        // Cap fall speed
        Vector2 v = rb.linearVelocity;
        if (v.y < -maxFallSpeed)
        {
            v.y = -maxFallSpeed;
            rb.linearVelocity = v;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}