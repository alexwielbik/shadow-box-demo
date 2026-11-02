using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A two-state lever. The base stays still; the lever child rotates around its pivot
/// between an Off angle (tilted left) and an On angle (tilted right).
///
/// Every switch of the same color is linked: flipping one flips all of them.
/// They stay in sync by listening to SwitchChannels, so no switch needs a reference to another.
/// </summary>
public class LeverSwitch : MonoBehaviour, IInteractable
{
    [Header("State")]
    [Tooltip("Current state. Changing this in the Inspector previews the lever angle in the Scene view.")]
    [SerializeField] private bool isOn = false;
    [Tooltip("Which color channel this switch powers. All switches of this color flip together.")]
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
    [Tooltip("If off, Player 1 can't turn this switch back off once it's on. " +
             "It still follows other switches of its color if one of those turns the color off.")]
    [SerializeField] private bool canToggleBack = true;

    [Header("Events")]
    [Tooltip("Fires after each flip with the new state (true = On). " +
             "Also fires when this switch flips because another switch of its color was flipped.")]
    public UnityEvent<bool> onToggled;
    public UnityEvent onTurnedOn;
    public UnityEvent onTurnedOff;

    private Coroutine flipRoutine;
    private bool started;

    /// <summary>Read this from other scripts to check the switch state.</summary>
    public bool IsOn => isOn;
    public SwitchColor SwitchColor => color;

    public bool CanInteract => flipRoutine == null && (canToggleBack || !isOn);

    private void Awake()
    {
        SnapLever();
        // A switch that starts On turns its color on. Switches that start Off don't publish
        // anything, so the order Awake runs in can't let an Off switch cancel an On one.
        if (isOn) SwitchChannels.SetActive(color, true);
    }

    private void OnEnable()
    {
        SwitchChannels.ActiveChanged += OnChannelChanged;

        // If this switch was disabled while its color changed, catch up instantly
        if (started) SyncToChannel();
    }

    private void OnDisable()
    {
        SwitchChannels.ActiveChanged -= OnChannelChanged;
    }

    private void Start()
    {
        // Every switch's Awake has run by now, so the channel holds the final starting state.
        // Any switch that started On has turned the color on, so every switch of that color matches it.
        started = true;
        SyncToChannel();
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

    /// <summary>Flip to the opposite state (and every other switch of this color with it).</summary>
    [ContextMenu("Toggle")]
    public void Toggle()
    {
        SetState(!isOn);
    }

    /// <summary>Set a specific state, animating the lever. Every switch of this color follows.</summary>
    public void SetState(bool on)
    {
        if (on == isOn) return;

        if (!Application.isPlaying) { isOn = on; SnapLever(); return; }

        ApplyState(on, animate: true, notify: true);

        // Power (or un-power) the color. This fires ActiveChanged, which flips every
        // other switch of this color. This switch ignores it because it already matches.
        SwitchChannels.SetActive(color, isOn);
    }

    /// <summary>Another switch changed this color's state: follow it.</summary>
    private void OnChannelChanged(SwitchColor changedColor, bool on)
    {
        if (changedColor != color) return;
        ApplyState(on, animate: true, notify: true);
    }

    /// <summary>Snap to the color's current state without animating or firing events.</summary>
    private void SyncToChannel()
    {
        ApplyState(SwitchChannels.IsActive(color), animate: false, notify: false);
    }

    /// <summary>Changes this switch's own state and visuals. Does NOT write to SwitchChannels.</summary>
    private void ApplyState(bool on, bool animate, bool notify)
    {
        if (on == isOn) return;
        isOn = on;

        if (flipRoutine != null) StopCoroutine(flipRoutine);
        flipRoutine = null;

        if (animate) flipRoutine = StartCoroutine(FlipLever(isOn ? onAngle : offAngle));
        else SnapLever();

        if (!notify) return;
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