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
    [SerializeField] private float jumpHeight = 3f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [Range(0f, 1f)]
    [SerializeField] private float jumpCutMultiplier = 0.5f;
    [SerializeField] private float fallGravityMultiplier = 1.8f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private LayerMask groundLayers;

    [Header("Animation")]
    [Tooltip("Check this if the roll frames are drawn rolling to the right.")]
    [SerializeField] private bool spriteRollsRight = true;

    private static readonly int RollSpeedHash = Animator.StringToHash("RollSpeed");

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private float baseGravityScale;
    private Vector2 moveInput;
    private bool isGrounded;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool jumpCutRequested;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseGravityScale = rb.gravityScale;
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
        // Roll speed is always positive: direction is shown by flipping, not by playing backward
        if (animator != null)
        {
            float speed01 = Mathf.Abs(rb.linearVelocity.x) / moveSpeed;
            animator.SetFloat(RollSpeedHash, speed01);
        }

        // Flip horizontally based on input direction; keep last facing when the stick is released
        if (spriteRenderer != null && Mathf.Abs(moveInput.x) > 0.1f)
        {
            bool movingLeft = moveInput.x < 0f;
            spriteRenderer.flipX = spriteRollsRight ? movingLeft : !movingLeft;
        }
    }

    private void FixedUpdate()
    {
        CheckGround();
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

    private void HandleHorizontal()
    {
        Vector2 v = rb.linearVelocity;
        float targetX = moveInput.x * moveSpeed;

        float rate = Mathf.Abs(targetX) > 0.01f ? acceleration : deceleration;
        if (!isGrounded) rate *= airControl;

        v.x = Mathf.MoveTowards(v.x, targetX, rate * Time.fixedDeltaTime);
        rb.linearVelocity = v;
    }

    private void HandleJump()
    {
        Vector2 v = rb.linearVelocity;

        // Jump: buffered press + (grounded or within coyote window)
        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            float gravity = Mathf.Abs(Physics2D.gravity.y) * baseGravityScale;
            v.y = Mathf.Sqrt(2f * gravity * jumpHeight);
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
        // Stronger gravity while falling for a snappier, less floaty arc
        rb.gravityScale = rb.linearVelocity.y < 0f
            ? baseGravityScale * fallGravityMultiplier
            : baseGravityScale;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}