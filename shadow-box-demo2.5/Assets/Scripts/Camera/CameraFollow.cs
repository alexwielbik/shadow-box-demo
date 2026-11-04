using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector2 offset = Vector2.zero;

    [Header("Bounds")]
    [Tooltip("A BoxCollider2D (Is Trigger) marking the area the camera view may cover.")]
    [SerializeField] private BoxCollider2D bounds;
    [Tooltip("World Z of the plane the bounds refer to (the gameplay plane).")]
    [SerializeField] private float boundsPlaneZ = 0f;

    [Header("Smoothing")]
    [Tooltip("0 = locked to the player. Higher = looser follow.")]
    [SerializeField] private float smoothTime = 0.1f;

    [Header("Keep Target In View")]
    [Tooltip("The target is never allowed closer than this to the edge of the view " +
             "(world units on the gameplay plane). Smoothing still works inside this zone; " +
             "the camera only gets 'pulled' when the target reaches it.")]
    [SerializeField] private Vector2 edgeMargin = new Vector2(3f, 2.5f);

    [Header("Bounds Transition")]
    [Tooltip("Seconds the camera takes to slide over when SetBounds is called without a time.")]
    [Min(0f)]
    [SerializeField] private float defaultTransitionTime = 0.8f;

    private Camera cam;
    private Vector3 velocity;

    // Slide between two sets of bounds
    private bool transitioning;
    private Vector3 transitionFrom;
    private float transitionTimer;
    private float transitionDuration;

    /// <summary>The bounds the camera is currently kept inside.</summary>
    public BoxCollider2D Bounds => bounds;

    /// <summary>
    /// Switch to a different bounds box and slide the camera over to it.
    /// transitionTime &lt; 0 uses Default Transition Time; 0 cuts instantly.
    /// Calling it again mid-slide (e.g. the player backs out) slides smoothly from wherever the camera is.
    /// </summary>
    public void SetBounds(BoxCollider2D newBounds, float transitionTime = -1f)
    {
        if (newBounds == null || newBounds == bounds) return;

        bounds = newBounds;
        transitionDuration = transitionTime < 0f ? defaultTransitionTime : transitionTime;

        if (transitionDuration <= 0f || target == null)
        {
            transitioning = false;
            velocity = Vector3.zero;
            if (target != null) transform.position = Clamp(Desired());
            return;
        }

        transitionFrom = transform.position;
        transitionTimer = 0f;
        transitioning = true;
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Sort sprites / Sorting Groups by depth (z) only, not by 3D distance.
        // With a perspective camera, distance sorting makes objects at different
        // heights swap draw order as the camera moves.
        cam.transparencySortMode = TransparencySortMode.Orthographic;
    }

    private void Start()
    {
        // Snap into position so the camera doesn't swoop in when the scene loads
        if (target != null)
            transform.position = Clamp(Desired());
    }

    private void LateUpdate()
    {
        if (target == null) return;

        if (transitioning)
        {
            UpdateTransition();
            return;
        }

        Vector3 previous = transform.position;
        Vector3 desired = Clamp(Desired());

        Vector3 next = smoothTime > 0f
            ? Vector3.SmoothDamp(previous, desired, ref velocity, smoothTime)
            : desired;

        // Hard limit: if smoothing has fallen too far behind, drag the camera
        // so the target stays inside the view (minus the edge margin).
        Vector3 kept = KeepTargetInView(next);
        if (kept != next && Time.deltaTime > 0f)
        {
            // Keep SmoothDamp's velocity in sync with the forced movement so
            // there's no jolt when the target slows down and smoothing takes over again.
            velocity = (kept - previous) / Time.deltaTime;
        }

        // Clamp last so the view can never leave the bounds
        transform.position = Clamp(kept);
    }

    // Ease from where the camera was to where it will rest inside the new bounds.
    // The end point is recomputed every frame, so it stays correct while the player keeps moving.
    private void UpdateTransition()
    {
        transitionTimer += Time.deltaTime;
        float t = Mathf.Clamp01(transitionTimer / transitionDuration);
        t = t * t * (3f - 2f * t); // smoothstep: eases in and out

        Vector3 to = Clamp(Desired());
        transform.position = Vector3.Lerp(transitionFrom, to, t);

        if (t >= 1f)
        {
            transitioning = false;
            velocity = Vector3.zero; // hand back to normal following with no leftover speed
        }
    }

    private Vector3 Desired()
    {
        return new Vector3(
            target.position.x + offset.x,
            target.position.y + offset.y,
            transform.position.z);
    }

    // Moves the camera the minimum amount needed so the target sits inside the
    // view rectangle shrunk by edgeMargin on each side.
    private Vector3 KeepTargetInView(Vector3 p)
    {
        Vector2 half = ViewHalfSize();
        float allowX = Mathf.Max(0f, half.x - edgeMargin.x);
        float allowY = Mathf.Max(0f, half.y - edgeMargin.y);

        // Where the camera "wants" to be relative to the target (includes offset)
        float tx = target.position.x + offset.x;
        float ty = target.position.y + offset.y;

        p.x = Mathf.Clamp(p.x, tx - allowX, tx + allowX);
        p.y = Mathf.Clamp(p.y, ty - allowY, ty + allowY);
        return p;
    }

    private Vector3 Clamp(Vector3 p)
    {
        if (bounds == null) return p;

        Vector2 half = ViewHalfSize();
        Bounds b = bounds.bounds;

        p.x = ClampAxis(p.x, b.min.x + half.x, b.max.x - half.x, b.center.x);
        p.y = ClampAxis(p.y, b.min.y + half.y, b.max.y - half.y, b.center.y);
        return p;
    }

    // If the bounds are smaller than the view on an axis, center on the bounds
    private static float ClampAxis(float value, float min, float max, float center)
    {
        return min > max ? center : Mathf.Clamp(value, min, max);
    }

    // Half the width and height of what the camera sees on the bounds plane
    private Vector2 ViewHalfSize()
    {
        float halfHeight;
        if (cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
        }
        else
        {
            float distance = Mathf.Abs(transform.position.z - boundsPlaneZ);
            halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
        return new Vector2(halfHeight * cam.aspect, halfHeight);
    }

#if UNITY_EDITOR
    // Green box = the zone the target is kept inside (select the camera to see it)
    private void OnDrawGizmosSelected()
    {
        if (cam == null) cam = GetComponent<Camera>();
        Vector2 half = ViewHalfSize();
        Vector3 size = new Vector3(
            Mathf.Max(0f, half.x - edgeMargin.x) * 2f,
            Mathf.Max(0f, half.y - edgeMargin.y) * 2f,
            0f);
        Vector3 center = new Vector3(transform.position.x - offset.x,
                                     transform.position.y - offset.y,
                                     boundsPlaneZ);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size);
    }
#endif
}