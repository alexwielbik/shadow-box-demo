using UnityEngine;

/// <summary>
/// Spins an object, optionally powered by a switch color and steered by Player 2.
///
/// Two ways to move:
///   - No Rigidbody2D: rotates the Transform in Update (background / decor gears).
///   - Kinematic Rigidbody2D on the same object: drives the body's angularVelocity in
///     FixedUpdate. Use this for any gear the player stands on, so physics knows how fast
///     the surface is moving and PlayerController can carry the player with it.
/// </summary>
[DefaultExecutionOrder(-50)] // set this step's spin before PlayerController reads it (same as MovingPlatform)
public class RotatingObject : MonoBehaviour
{
    public enum RotationDirection
    {
        CounterClockwise = 1,
        Clockwise = -1
    }

    [Tooltip("Rotation speed in degrees per second. Always treated as positive; use Direction to flip it.")]
    [Min(0f)]
    [SerializeField] private float degreesPerSecond = 10f;

    [Tooltip("Which way the object spins, as seen from the game camera. " +
             "If it follows Player 2, this is only used until Player 2 presses a direction.")]
    [SerializeField] private RotationDirection direction = RotationDirection.CounterClockwise;

    [Header("Switch Control")]
    [Tooltip("Off = always spins (background gears). On = only spins while the chosen switch color is on.")]
    [SerializeField] private bool controlledBySwitch = false;
    [Tooltip("Spin when ANY switch is on, regardless of color. Switch Color is ignored when this is ticked.")]
    [SerializeField] private bool anySwitchColor = false;
    [Tooltip("Which switch color powers this object.")]
    [SerializeField] private SwitchColor switchColor = SwitchColor.Grey;
    [Tooltip("Spin in the last direction Player 2 pressed for this color.")]
    [SerializeField] private bool followPlayer2Direction = true;
    [Tooltip("Spin opposite to Player 2's direction. Use on every other gear in a meshed chain.")]
    [SerializeField] private bool invertPlayer2Direction = false;
    [Tooltip("Only spin while Player 2 is holding a button (like the clock hands). Off = keeps spinning on its own while powered.")]
    [SerializeField] private bool onlyWhilePlayer2Holds = false;
    [Tooltip("Seconds to go from stopped to full speed. 0 = instant.")]
    [Min(0f)]
    [SerializeField] private float spinUpTime = 0.4f;

    private float currentSpeed; // signed degrees per second actually being applied
    private Rigidbody2D body;   // kinematic body to drive, or null for Transform mode

    /// <summary>Signed degrees per second right now (+ = counter-clockwise).</summary>
    public float CurrentSpeed => currentSpeed;

    private bool UsesPhysics => body != null && body.bodyType == RigidbodyType2D.Kinematic;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        if (body != null && body.bodyType != RigidbodyType2D.Kinematic)
            Debug.LogWarning($"{name}: RotatingObject only drives Kinematic Rigidbody2Ds. " +
                             "Falling back to rotating the Transform.", this);
    }

    private void Start()
    {
        // Always-on objects start at full speed; switch-controlled ones start wherever the switch says
        currentSpeed = TargetSpeed();
        if (UsesPhysics) body.angularVelocity = currentSpeed;
    }

    private void Update()
    {
        if (UsesPhysics) return;

        UpdateSpeed(Time.deltaTime);
        if (!Mathf.Approximately(currentSpeed, 0f))
            transform.Rotate(0f, 0f, currentSpeed * Time.deltaTime, Space.Self);
    }

    private void FixedUpdate()
    {
        if (!UsesPhysics) return;

        UpdateSpeed(Time.fixedDeltaTime);
        // Kinematic bodies rotate by their own angularVelocity (degrees / second), which also
        // lets GetPointVelocity report how fast any point on the surface is moving.
        body.angularVelocity = currentSpeed;
    }

    private void OnDisable()
    {
        if (UsesPhysics) body.angularVelocity = 0f;
    }

    private void UpdateSpeed(float dt)
    {
        float target = TargetSpeed();

        if (spinUpTime <= 0f)
        {
            currentSpeed = target;
        }
        else
        {
            // Accelerate toward the target, so starting, stopping and reversing all look mechanical
            float accel = Mathf.Abs(degreesPerSecond) / spinUpTime;
            currentSpeed = Mathf.MoveTowards(currentSpeed, target, accel * dt);
        }
    }

    private float TargetSpeed()
    {
        int dir = (int)direction;

        if (controlledBySwitch)
        {
            bool powered = anySwitchColor
                ? SwitchChannels.IsAnyActive()
                : SwitchChannels.IsActive(switchColor);
            if (!powered) return 0f;

            if (onlyWhilePlayer2Holds)
            {
                int held = anySwitchColor
                    ? SwitchChannels.GetAnyHeldInput()
                    : SwitchChannels.GetHeldInput(switchColor);
                if (held == 0) return 0f;

                // While holding, the held button IS the direction
                if (followPlayer2Direction) dir = invertPlayer2Direction ? -held : held;
                return Mathf.Abs(degreesPerSecond) * dir;
            }

            if (followPlayer2Direction)
            {
                int p2 = anySwitchColor
                    ? SwitchChannels.GetAnyDirection()
                    : SwitchChannels.GetDirection(switchColor);
                if (p2 != 0) dir = invertPlayer2Direction ? -p2 : p2;
            }
        }

        return Mathf.Abs(degreesPerSecond) * dir;
    }

    /// <summary>Flip the spin direction at runtime (e.g., from a switch or trigger).</summary>
    public void ReverseDirection()
    {
        direction = direction == RotationDirection.Clockwise
            ? RotationDirection.CounterClockwise
            : RotationDirection.Clockwise;
    }

    /// <summary>Set the spin direction at runtime.</summary>
    public void SetDirection(RotationDirection newDirection)
    {
        direction = newDirection;
    }
}