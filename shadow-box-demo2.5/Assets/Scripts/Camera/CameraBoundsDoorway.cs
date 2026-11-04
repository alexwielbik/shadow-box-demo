using UnityEngine;

/// <summary>
/// A trigger placed inside a gap in a wall. When Player 1 starts to pass through it,
/// the camera slides over to the bounds on the far side. If the player backs out the
/// way they came, the camera slides back.
///
/// Setup: put this on an object with a BoxCollider2D (Is Trigger) that fills the gap
/// (same thickness as the wall). Assign the bounds box for each side.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class CameraBoundsDoorway : MonoBehaviour
{
    [Tooltip("The camera to switch. Leave empty to use the Main Camera's CameraFollow.")]
    [SerializeField] private CameraFollow cameraFollow;

    [Tooltip("Camera bounds for the area on the LEFT of this doorway.")]
    [SerializeField] private BoxCollider2D leftBounds;

    [Tooltip("Camera bounds for the area on the RIGHT of this doorway.")]
    [SerializeField] private BoxCollider2D rightBounds;

    [Tooltip("Seconds for the camera slide. Negative = use the camera's Default Transition Time.")]
    [SerializeField] private float transitionTime = -1f;

    private BoxCollider2D trigger;

    private float CenterX => trigger.bounds.center.x;

    private void Awake()
    {
        trigger = GetComponent<BoxCollider2D>();
        if (!trigger.isTrigger)
        {
            Debug.LogWarning($"{name}: CameraBoundsDoorway's BoxCollider2D should be a trigger. Turning Is Trigger on.", this);
            trigger.isTrigger = true;
        }

        if (cameraFollow == null && Camera.main != null)
            cameraFollow = Camera.main.GetComponent<CameraFollow>();

        if (cameraFollow == null || leftBounds == null || rightBounds == null)
            Debug.LogWarning($"{name}: CameraBoundsDoorway is missing its camera or a bounds box.", this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Transform player = GetPlayer(other);
        if (player == null) return;

        // Coming in from the right → heading left, and the other way round.
        // Switching on entry makes the camera start moving as soon as the player starts through.
        bool cameFromRight = player.position.x > CenterX;
        Switch(cameFromRight ? leftBounds : rightBounds);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Transform player = GetPlayer(other);
        if (player == null) return;

        // Whichever side they actually left on wins (handles backing out halfway through)
        bool leftOnLeftSide = player.position.x < CenterX;
        Switch(leftOnLeftSide ? leftBounds : rightBounds);
    }

    private void Switch(BoxCollider2D bounds)
    {
        if (cameraFollow != null && bounds != null)
            cameraFollow.SetBounds(bounds, transitionTime);
    }

    // Only Player 1 counts (it has the PlayerController on its Rigidbody object)
    private static Transform GetPlayer(Collider2D other)
    {
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return null;
        return rb.GetComponent<PlayerController>() != null ? rb.transform : null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawCube(box.bounds.center, box.bounds.size);

        Gizmos.color = Color.cyan;
        if (leftBounds != null) Gizmos.DrawWireCube(leftBounds.bounds.center, leftBounds.bounds.size);
        if (rightBounds != null) Gizmos.DrawWireCube(rightBounds.bounds.center, rightBounds.bounds.size);
    }
#endif
}