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

    [Tooltip("Seconds for one full back-and-forth. 2 = one tick per second, like a grandfather clock.")]
    [Min(0.1f)]
    [SerializeField] private float period = 2f;

    [Tooltip("Where in the swing it starts (0-1). 0 = center moving right, 0.25 = far right, 0.5 = center moving left, 0.75 = far left.")]
    [Range(0f, 1f)]
    [SerializeField] private float startPhase = 0.25f;

    [Tooltip("Keep swinging while the game is paused (Time.timeScale = 0).")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Events")]
    [Tooltip("Fires each time the pendulum reaches the end of a swing (twice per period). Hook a tick sound here later.")]
    [SerializeField] private UnityEvent onTick;

    private Quaternion restRotation;
    private float elapsed;
    private int lastSwingSide;

    /// <summary>Current angle from straight down, in degrees (+ = counter-clockwise).</summary>
    public float CurrentAngle { get; private set; }

    private void Awake()
    {
        restRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        elapsed = 0f;
        lastSwingSide = 0;
        ApplyAngle();
    }

    private void Update()
    {
        elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        ApplyAngle();
    }

    private void ApplyAngle()
    {
        float phase = (elapsed / period + startPhase) * Mathf.PI * 2f;

        // Negative so phase 0.25 swings to the right (clockwise) on screen
        CurrentAngle = -amplitude * Mathf.Sin(phase);
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, CurrentAngle);

        // Moving direction flips at each end of the swing: that's a "tick"
        int side = Mathf.Cos(phase) >= 0f ? 1 : -1;
        if (lastSwingSide != 0 && side != lastSwingSide)
            onTick?.Invoke();
        lastSwingSide = side;
    }

    private void OnDrawGizmosSelected()
    {
        // Show the swing range: two lines from the hinge at +/- amplitude
        float length = 2f;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
            length = sr.sprite.bounds.size.y * transform.lossyScale.y;

        Quaternion rest = Application.isPlaying && transform.parent != null
            ? transform.parent.rotation * restRotation
            : (Application.isPlaying ? restRotation : transform.rotation);

        Vector3 pivot = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot, pivot + rest * Quaternion.Euler(0f, 0f, amplitude) * Vector3.down * length);
        Gizmos.DrawLine(pivot, pivot + rest * Quaternion.Euler(0f, 0f, -amplitude) * Vector3.down * length);
    }
}