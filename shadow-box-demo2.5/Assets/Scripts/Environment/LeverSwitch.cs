using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A two-state lever. The base stays still; the lever child rotates around its pivot
/// between an Off angle (tilted left) and an On angle (tilted right).
/// </summary>
public class LeverSwitch : MonoBehaviour, IInteractable
{
    [Header("State")]
    [Tooltip("Current state. Changing this in the Inspector previews the lever angle in the Scene view.")]
    [SerializeField] private bool isOn = false;
    [Tooltip("Which color channel this switch powers.")]
    [SerializeField] private SwitchColor color = SwitchColor.Grey;

    [Header("Lever")]
    [Tooltip("The child Transform holding the lever sprite. Its pivot must be at the hinge.")]
    [SerializeField] private Transform lever;
    [Tooltip("Z rotation when Off. Positive = tilted to the left (counter-clockwise).")]
    [SerializeField] private float offAngle = 45f;
    [Tooltip("Z rotation when On. Negative = tilted to the right (clockwise).")]
    [SerializeField] private float onAngle = -45f;
    [Tooltip("Seconds for the lever to swing from one side to the other.")]
    [SerializeField, Min(0f)] private float flipDuration = 0.2f;

    [Header("Behaviour")]
    [Tooltip("If off, the switch can only be turned on once (one-way switch).")]
    [SerializeField] private bool canToggleBack = true;

    [Header("Events")]
    [Tooltip("Fires after each flip with the new state (true = On).")]
    public UnityEvent<bool> onToggled;
    public UnityEvent onTurnedOn;
    public UnityEvent onTurnedOff;

    private Coroutine flipRoutine;

    /// <summary>Read this from other scripts to check the switch state.</summary>
    public bool IsOn => isOn;
    public SwitchColor SwitchColor => color;

    public bool CanInteract => flipRoutine == null && (canToggleBack || !isOn);

    private void Awake()
    {
        SnapLever();
        // Publish the starting state so a switch that begins On powers its objects immediately
        SwitchChannels.SetActive(color, isOn);
    }

    // Keeps the Scene view in sync when you edit isOn or the angles in the Inspector
    private void OnValidate()
    {
        if (!Application.isPlaying) SnapLever();
    }

    public void Interact(GameObject interactor)
    {
        Toggle();
    }

    /// <summary>Flip to the opposite state.</summary>
    [ContextMenu("Toggle")]
    public void Toggle()
    {
        SetState(!isOn);
    }

    /// <summary>Set a specific state, animating the lever.</summary>
    public void SetState(bool on)
    {
        if (on == isOn) return;
        isOn = on;

        if (!Application.isPlaying) { SnapLever(); return; }

        if (flipRoutine != null) StopCoroutine(flipRoutine);
        flipRoutine = StartCoroutine(FlipLever(isOn ? onAngle : offAngle));

        // Power (or un-power) every object listening to this color
        SwitchChannels.SetActive(color, isOn);

        onToggled?.Invoke(isOn);
        if (isOn) onTurnedOn?.Invoke();
        else onTurnedOff?.Invoke();
    }

    private IEnumerator FlipLever(float targetAngle)
    {
        float startAngle = lever.localEulerAngles.z;
        float t = 0f;

        while (t < flipDuration)
        {
            t += Time.deltaTime;
            // SmoothStep eases in and out so the lever "snaps" into place instead of moving robotically
            float k = Mathf.SmoothStep(0f, 1f, t / flipDuration);
            float angle = Mathf.LerpAngle(startAngle, targetAngle, k);
            lever.localRotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }

        lever.localRotation = Quaternion.Euler(0f, 0f, targetAngle);
        flipRoutine = null;
    }

    private void SnapLever()
    {
        if (lever == null) return;
        lever.localRotation = Quaternion.Euler(0f, 0f, isOn ? onAngle : offAngle);
    }
}