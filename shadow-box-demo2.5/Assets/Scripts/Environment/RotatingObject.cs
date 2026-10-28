using UnityEngine;

public class RotatingObject : MonoBehaviour
{
    [Tooltip("Degrees per second. Positive = counter-clockwise, negative = clockwise.")]
    [SerializeField] private float degreesPerSecond = 10f;

    private void Update()
    {
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}