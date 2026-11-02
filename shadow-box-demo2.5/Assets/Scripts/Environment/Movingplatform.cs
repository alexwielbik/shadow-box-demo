using UnityEngine;

/// <summary>
/// A platform that slides between two points (Start and End) when a switch color is on.
/// The two points are offsets from where you place the platform in the scene, so you can
/// move the whole thing around without redoing its path.
///
/// Drag the yellow A / B handles in the Scene view (or type the offsets in the Inspector)
/// to set the range. The outlines at each end show where the platform will stop.
///
/// Uses a kinematic Rigidbody2D driven by velocity, so the player is pushed up correctly
/// and PlayerController can read the platform's speed to ride along with it.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    public enum PathAxis
    {
        Free,        // any direction (diagonals allowed)
        Horizontal,  // offsets keep y = 0
        Vertical     // offsets keep x = 0
    }

    public enum MoveMode
    {
        PingPongWhileOn,  // goes back and forth between A and B for as long as the switch is on
        GoToEndWhileOn,   // switch on = travel to B and stay; switch off = travel back to A
        Player2Controlled // switch on = only moves while Player 2 holds left / right
    }

    [Header("Path (offsets from where the platform sits in the scene)")]
    [Tooltip("Locks the path to one axis. Horizontal / Vertical keep the handles on a straight line.")]
    [SerializeField] private PathAxis axis = PathAxis.Vertical;
    [Tooltip("Point A, relative to the platform's scene position. Usually (0, 0).")]
    [SerializeField] private Vector2 startOffset = Vector2.zero;
    [Tooltip("Point B, relative to the platform's scene position.")]
    [SerializeField] private Vector2 endOffset = new Vector2(0f, 3f);

    [Header("Movement")]
    [Tooltip("Travel speed in world units per second (average, if easing is on).")]
    [Min(0.01f)]
    [SerializeField] private float speed = 2f;
    [Tooltip("0 = constant speed, 1 = slows down smoothly into each end.")]
    [Range(0f, 1f)]
    [SerializeField] private float easing = 0.5f;
    [Tooltip("Seconds to wait at each end before turning around (Ping Pong only).")]
    [Min(0f)]
    [SerializeField] private float pauseAtEnds = 0.5f;

    [Header("Switch Control")]
    [Tooltip("Which switch color powers this platform.")]
    [SerializeField] private SwitchColor switchColor = SwitchColor.Gold;
    [SerializeField] private MoveMode mode = MoveMode.PingPongWhileOn;
    [Tooltip("Ping Pong / Player 2 modes. On = glide back to A when the switch turns off. Off = freeze in place.")]
    [SerializeField] private bool returnToStartWhenOff = false;
    [Tooltip("Ignore the switch and always move. Handy for testing. (Has no effect in Player2Controlled mode: Player 2 input only exists while the switch is on.)")]
    [SerializeField] private bool alwaysOn = false;

    [Header("Player 2 Control (Player2Controlled mode)")]
    [Tooltip("Off: Player 2's RIGHT button moves toward B, LEFT toward A. On: swapped.")]
    [SerializeField] private bool invertPlayer2 = false;
    [Tooltip("Seconds to reach full speed (and to stop) when Player 2 presses or releases. 0 = instant.")]
    [Min(0f)]
    [SerializeField] private float player2RampTime = 0.15f;

    private Rigidbody2D rb;
    private Vector2 home;          // scene position, cached when play starts
    private bool homeCached;
    private float progress;        // 0 = at A, 1 = at B (linear, before easing)
    private int heading = 1;       // +1 = toward B, -1 = toward A
    private float pauseTimer;
    private float player2Speed;    // -1 … +1, smoothed Player 2 input (+1 = toward B)

    /// <summary>The platform's current velocity (what riders should add to their own).</summary>
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;

    /// <summary>0 at point A, 1 at point B.</summary>
    public float Progress => progress;

    /// <summary>Where the offsets are measured from. In edit mode this follows the transform.</summary>
    public Vector2 Home => Application.isPlaying && homeCached ? home : (Vector2)transform.position;

    public Vector2 StartPoint => Home + startOffset;
    public Vector2 EndPoint => Home + endOffset;
    public float PathLength => Vector2.Distance(startOffset, endOffset);
    public PathAxis Axis => axis;

    private void Reset()
    {
        // Sensible Rigidbody settings when the component is first added
        var body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void OnValidate()
    {
        ApplyAxisLock();
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        home = transform.position;
        homeCached = true;

        // Start at A
        progress = 0f;
        heading = 1;
        Vector2 a = PointAt(0f);
        rb.position = a;
        transform.position = new Vector3(a.x, a.y, transform.position.z);
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float length = PathLength;

        if (length < 0.0001f)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        float step = speed / length * dt; // progress per physics step
        bool powered = alwaysOn || SwitchChannels.IsActive(switchColor);

        if (mode == MoveMode.GoToEndWhileOn)
        {
            progress = Mathf.MoveTowards(progress, powered ? 1f : 0f, step);
        }
        else if (mode == MoveMode.Player2Controlled)
        {
            // HeldInput: +1 = left button, -1 = right button, 0 = nothing (always 0 while the switch is off)
            int held = powered ? SwitchChannels.GetHeldInput(switchColor) : 0;
            float inputDir = -held * (invertPlayer2 ? -1f : 1f); // right (-1) -> toward B (+1)

            player2Speed = player2RampTime <= 0f
                ? inputDir
                : Mathf.MoveTowards(player2Speed, inputDir, dt / player2RampTime);

            if (!powered && returnToStartWhenOff)
            {
                player2Speed = 0f;
                progress = Mathf.MoveTowards(progress, 0f, step);
            }
            else
            {
                progress = Mathf.Clamp01(progress + player2Speed * step);
                if (progress <= 0f || progress >= 1f) player2Speed = 0f; // hit an end: stop dead, no wind-up
            }
        }
        else if (powered)
        {
            if (pauseTimer > 0f)
            {
                pauseTimer -= dt;
            }
            else
            {
                progress += heading * step;
                if (progress >= 1f) { progress = 1f; heading = -1; pauseTimer = pauseAtEnds; }
                else if (progress <= 0f) { progress = 0f; heading = 1; pauseTimer = pauseAtEnds; }
            }
        }
        else if (returnToStartWhenOff)
        {
            progress = Mathf.MoveTowards(progress, 0f, step);
            heading = 1;       // next time it powers on, head out toward B
            pauseTimer = 0f;
        }
        // else: unpowered ping-pong just freezes where it is

        // Velocity that lands the body exactly on the target this step.
        // Recomputed from the real position every step, so it never drifts.
        Vector2 target = PointAt(progress);
        rb.linearVelocity = (target - rb.position) / dt;
    }

    private Vector2 PointAt(float t)
    {
        float eased = Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), easing);
        return Home + Vector2.Lerp(startOffset, endOffset, eased);
    }

    private void ApplyAxisLock()
    {
        if (axis == PathAxis.Horizontal) { startOffset.y = 0f; endOffset.y = 0f; }
        else if (axis == PathAxis.Vertical) { startOffset.x = 0f; endOffset.x = 0f; }
    }

    // ---------- Gizmos: the path line + platform outline at each end ----------

    private static readonly Color PathColor = new Color(1f, 0.8f, 0.2f, 1f);

    private void OnDrawGizmos()
    {
        DrawPath(selected: false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawPath(selected: true);
    }

    private void DrawPath(bool selected)
    {
        float z = transform.position.z;
        Vector3 a = new Vector3(StartPoint.x, StartPoint.y, z);
        Vector3 b = new Vector3(EndPoint.x, EndPoint.y, z);

        Color c = PathColor;
        c.a = selected ? 1f : 0.35f;
        Gizmos.color = c;
        Gizmos.DrawLine(a, b);

        if (!selected) return;

        // Platform footprint at each end, taken from its collider(s)
        Collider2D col = GetComponentInChildren<Collider2D>();
        if (col == null)
        {
            Gizmos.DrawWireSphere(a, 0.15f);
            Gizmos.DrawWireSphere(b, 0.15f);
            return;
        }

        Bounds bounds = col.bounds;
        Vector3 centerOffset = bounds.center - transform.position;
        centerOffset.z = 0f;
        Vector3 size = new Vector3(bounds.size.x, bounds.size.y, 0.01f);

        Gizmos.DrawWireCube(a + centerOffset, size);
        Gizmos.DrawWireCube(b + centerOffset, size);
    }
}