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

    [Tooltip("Which way the object spins, as seen from the game camera.")]
    [SerializeField] private RotationDirection direction = RotationDirection.CounterClockwise;

    private void Update()
    {
        float step = Mathf.Abs(degreesPerSecond) * (int)direction * Time.deltaTime;
        transform.Rotate(0f, 0f, step, Space.Self);
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