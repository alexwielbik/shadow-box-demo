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

    private Camera cam;
    private Vector3 velocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();
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

        Vector3 desired = Clamp(Desired());

        Vector3 next = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime)
            : desired;

        // Clamp again so the view can never leave the bounds, even while smoothing
        transform.position = Clamp(next);
    }

    private Vector3 Desired()
    {
        return new Vector3(
            target.position.x + offset.x,
            target.position.y + offset.y,
            transform.position.z);
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
}