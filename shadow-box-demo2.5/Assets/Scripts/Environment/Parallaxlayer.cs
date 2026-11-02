using UnityEngine;

/// <summary>
/// Moves this object along with the camera by a fraction of the camera's movement.
///
///   Follow = 1  -> the object moves exactly with the camera, so it stays in the same
///                  spot on screen the whole time (like it's infinitely far away).
///   Follow = 0  -> the object never moves; it behaves like any normal world object.
///   In between  -> classic parallax. 0.85 - 0.95 keeps it *almost* still but gives a
///                  little drift that sells the depth.
///
/// Put this on an empty parent object and drag the background pieces under it.
/// Lay those pieces out in the Scene view with the Main Camera at its saved scene
/// position: that camera position is the "home" view this layer is measured from.
/// </summary>
[DefaultExecutionOrder(50)] // after CameraFollow (0), so we use the camera's final position this frame
[DisallowMultipleComponent]
public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("The camera to follow. Leave empty to use the Main Camera.")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("How much of the camera's horizontal movement to copy. 1 = fixed on screen.")]
    [SerializeField, Range(0f, 1f)] private float followX = 1f;

    [Tooltip("How much of the camera's vertical movement to copy. 1 = fixed on screen.")]
    [SerializeField, Range(0f, 1f)] private float followY = 1f;

    private Vector3 startPosition;
    private Vector3 cameraStartPosition;

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Awake runs before CameraFollow.Start snaps the camera to the player,
        // so this records the camera exactly where it was saved in the scene.
        startPosition = transform.position;
        if (cameraTransform != null)
            cameraStartPosition = cameraTransform.position;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        Vector3 cameraMoved = cameraTransform.position - cameraStartPosition;

        transform.position = startPosition + new Vector3(
            cameraMoved.x * followX,
            cameraMoved.y * followY,
            0f); // depth never changes
    }
}