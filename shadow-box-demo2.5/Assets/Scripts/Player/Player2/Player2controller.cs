using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player 2 (the "environment" player). Reads the left / right buttons and hands the result
/// to every switch color that is currently active. Objects of that color then react.
/// Put one of these in the scene (e.g. on an empty "Player2" GameObject).
/// </summary>
public class Player2Controller : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Left mouse button / left trigger. Turns things counter-clockwise (to the left).")]
    [SerializeField] private InputActionReference rotateLeftAction;
    [Tooltip("Right mouse button / right trigger. Turns things clockwise (to the right).")]
    [SerializeField] private InputActionReference rotateRightAction;

    private const int Left = 1;    // counter-clockwise, matches RotatingObject.RotationDirection
    private const int Right = -1;  // clockwise

    private SwitchColor[] colors;
    private int heldDir;           // what Player 2 is holding right now

    private void Awake()
    {
        colors = (SwitchColor[])System.Enum.GetValues(typeof(SwitchColor));
    }

    private void OnEnable()
    {
        rotateLeftAction.action.Enable();
        rotateRightAction.action.Enable();
    }

    private void OnDisable()
    {
        rotateLeftAction.action.Disable();
        rotateRightAction.action.Disable();
    }

    private void Update()
    {
        InputAction left = rotateLeftAction.action;
        InputAction right = rotateRightAction.action;

        // A fresh press this frame = Player 2 chose a new direction
        int pressedDir = 0;
        if (left.WasPressedThisFrame()) pressedDir = Left;
        if (right.WasPressedThisFrame()) pressedDir = Right;

        // Held direction: the most recent press wins if both are held.
        // When that button is released, fall back to the other one if it's still held.
        if (pressedDir != 0) heldDir = pressedDir;
        else if (heldDir == Left && !left.IsPressed()) heldDir = right.IsPressed() ? Right : 0;
        else if (heldDir == Right && !right.IsPressed()) heldDir = left.IsPressed() ? Left : 0;

        // Player 2 only affects colors whose switch is on
        foreach (SwitchColor c in colors)
        {
            if (!SwitchChannels.IsActive(c))
            {
                SwitchChannels.SetHeldInput(c, 0);
                continue;
            }

            if (pressedDir != 0) SwitchChannels.SetDirection(c, pressedDir);
            SwitchChannels.SetHeldInput(c, heldDir);
        }
    }
}