using UnityEngine;

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

    private void Start()
    {
        // Always-on objects start at full speed; switch-controlled ones start wherever the switch says
        currentSpeed = TargetSpeed();
    }

    private void Update()
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
            currentSpeed = Mathf.MoveTowards(currentSpeed, target, accel * Time.deltaTime);
        }

        if (!Mathf.Approximately(currentSpeed, 0f))
            transform.Rotate(0f, 0f, currentSpeed * Time.deltaTime, Space.Self);
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