using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Swings this object back and forth around its pivot, like a clock pendulum.
/// The sprite's pivot must be at the hinge (top of the rod).
/// Uses a sine wave, which is how a real pendulum moves at small angles.
/// </summary>
[DisallowMultipleComponent]
public class PendulumSwing : MonoBehaviour
{
    [Header("Swing")]
    [Tooltip("How far it swings to each side of straight down, in degrees.")]
    [Range(0f, 60f)]
    [SerializeField] private float amplitude = 12f;

    [Tooltip("Seconds for one full back-and-forth at Swing Speed 1. 2 = one tick per second, like a grandfather clock.")]
    [Min(0.1f)]
    [SerializeField] private float period = 2f;

    [Tooltip("Multiplier on how fast it swings. 1 = normal, 2 = twice as fast, 0.5 = half speed, 0 = frozen in place. " +
             "Safe to change while playing: it speeds up or slows down without jumping.")]
    [Min(0f)]
    [SerializeField] private float swingSpeed = 1f;

    [Tooltip("Seconds to ease into a new Swing Speed when it's changed while playing. 0 = instant.")]
    [Min(0f)]
    [SerializeField] private float speedChangeTime = 0.5f;

    [Tooltip("Where in the swing it starts (0-1). 0 = center moving right, 0.25 = far right, 0.5 = center moving left, 0.75 = far left.")]
    [Range(0f, 1f)]
    [SerializeField] private float startPhase = 0.25f;

    [Tooltip("Keep swinging while the game is paused (Time.timeScale = 0).")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Events")]
    [Tooltip("Fires each time the pendulum reaches the end of a swing (twice per cycle). Hook a tick sound here later.")]
    [SerializeField] private UnityEvent onTick;

    private Quaternion restRotation;
    private float cycle;          // how far through the swing we are, in full cycles (0-1 wraps)
    private float currentSpeed;   // swing speed actually being applied (eases toward swingSpeed)
    private int lastSwingSide;

    /// <summary>Current angle from straight down, in degrees (+ = counter-clockwise).</summary>
    public float CurrentAngle { get; private set; }

    /// <summary>Swing speed multiplier. Set from other scripts or UnityEvents (e.g. a switch).</summary>
    public float SwingSpeed
    {
        get => swingSpeed;
        set => swingSpeed = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        restRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        cycle = startPhase;
        currentSpeed = swingSpeed;
        lastSwingSide = 0;
        ApplyAngle();
    }

    private void Update()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        // Ease toward the target speed so changing it never looks like a jerk
        if (speedChangeTime <= 0f)
            currentSpeed = swingSpeed;
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, swingSpeed,
                Mathf.Max(1f, Mathf.Abs(swingSpeed - currentSpeed)) / speedChangeTime * dt);

        // Advance through the cycle instead of using total time, so a speed change
        // continues from the current angle rather than jumping to a new one
        cycle += dt * currentSpeed / period;
        cycle -= Mathf.Floor(cycle);

        ApplyAngle();
    }

    private void ApplyAngle()
    {
        float phase = cycle * Mathf.PI * 2f;

        // Negative so phase 0.25 swings to the right (clockwise) on screen
        CurrentAngle = -amplitude * Mathf.Sin(phase);
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, CurrentAngle);

        // Moving direction flips at each end of the swing: that's a "tick"
        int side = Mathf.Cos(phase) >= 0f ? 1 : -1;
        if (lastSwingSide != 0 && side != lastSwingSide)
            onTick?.Invoke();
        lastSwingSide = side;
    }

    /// <summary>Set the swing speed multiplier (for UnityEvents, e.g. a LeverSwitch's onTurnedOn).</summary>
    public void SetSwingSpeed(float newSpeed)
    {
        SwingSpeed = newSpeed;
    }

    private void OnDrawGizmosSelected()
    {
        // Show the swing range: two lines from the hinge at +/- amplitude
        float length = 2f;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
            length = sr.sprite.bounds.size.y * transform.lossyScale.y;

        Quaternion rest = Application.isPlaying
            ? (transform.parent != null ? transform.parent.rotation * restRotation : restRotation)
            : transform.rotation;

        Vector3 pivot = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot, pivot + rest * Quaternion.Euler(0f, 0f, amplitude) * Vector3.down * length);
        Gizmos.DrawLine(pivot, pivot + rest * Quaternion.Euler(0f, 0f, -amplitude) * Vector3.down * length);
    }
}