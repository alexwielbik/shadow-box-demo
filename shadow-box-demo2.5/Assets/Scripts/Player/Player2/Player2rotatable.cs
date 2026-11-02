using UnityEngine;

/// <summary>
/// An object Player 2 can spin by holding left / right, but only while the switch of
/// its color is on. Use it for clock hands (and anything else that turns on Player 2's input).
/// The object rotates around its own pivot, so place it on a pivot at the clock's center.
/// </summary>
public class Player2Rotatable : MonoBehaviour
{
    [Header("Switch")]
    [Tooltip("Which switch color unlocks this object for Player 2.")]
    [SerializeField] private SwitchColor switchColor = SwitchColor.Grey;

    [Header("Rotation")]
    [Tooltip("How fast Player 2 can spin it, in degrees per second.")]
    [Min(0f)]
    [SerializeField] private float degreesPerSecond = 90f;
    [Tooltip("Flip which button turns it which way.")]
    [SerializeField] private bool invert = false;

    public SwitchColor SwitchColor => switchColor;

    private void Update()
    {
        if (!SwitchChannels.IsActive(switchColor)) return;

        int dir = SwitchChannels.GetHeldInput(switchColor);
        if (dir == 0) return;

        if (invert) dir = -dir;
        transform.Rotate(0f, 0f, dir * degreesPerSecond * Time.deltaTime, Space.Self);
    }
}