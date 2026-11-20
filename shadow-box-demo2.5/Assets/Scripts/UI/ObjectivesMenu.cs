using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The "Level Objectives" paper. Locked until the tutorial unlocks it; after that the
/// Objectives action (Q) opens and closes it.
///
/// Each objective is "turn a switch color on for the first time" (read from
/// SwitchChannels.HasEverBeenActive). A completed objective is only ANIMATED while the menu is
/// open: on open, every objective completed since the last look gets its checkmark popped into
/// the box and its lever picture faded, one after another. Objectives completed while the menu
/// is already open animate straight away.
///
/// The GameObject stays active the whole time (it has to keep listening for Q);
/// the CanvasGroup hides it.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ObjectivesMenu : MonoBehaviour
{
    [Serializable]
    public class Objective
    {
        [Tooltip("Just a label for the Inspector.")]
        public string label = "Grey switch";
        [Tooltip("Done the first time a switch of this color is turned on.")]
        public SwitchColor color = SwitchColor.Grey;
        [Tooltip("Every picture of this lever (e.g. Base + Lever). They fade together once ticked.")]
        public Image[] leverImages = Array.Empty<Image>();
        [Tooltip("The green checkmark inside the box. Hidden until this objective is shown as done.")]
        public Image checkmark;

        [NonSerialized] public bool ticked;          // the player has seen (or is seeing) this tick
        [NonSerialized] public Color[] leverColors;
        [NonSerialized] public Color checkColor;
        [NonSerialized] public Vector3 checkScale;
    }

    [Header("Input")]
    [Tooltip("Player/Objectives (Keyboard Q). Opens and closes the menu once unlocked.")]
    [SerializeField] private InputActionReference toggleAction;

    [Header("Unlock")]
    [Tooltip("The \"Press Q to view Level Objectives\" paper. When it finishes leaving the screen, " +
             "the menu unlocks and opens by itself. Leave empty to wire it up with UnityEvents instead.")]
    [SerializeField] private InstructionPaper unlockAfter;
    [Tooltip("For testing: Q works from the start, without the tutorial.")]
    [SerializeField] private bool startUnlocked = false;

    [Header("Objectives")]
    [SerializeField] private Objective[] objectives = Array.Empty<Objective>();

    [Header("Q Button (on the menu)")]
    [Tooltip("The Q key picture in the top-right corner. Plays a press animation before closing.")]
    [SerializeField] private Image qButton;
    [Tooltip("How small the key gets at the bottom of the press.")]
    [SerializeField, Range(0.5f, 1f)] private float pressScale = 0.82f;
    [Tooltip("Tint at the bottom of the press (multiplies the key's color).")]
    [SerializeField] private Color pressTint = new Color(0.7f, 0.7f, 0.7f, 1f);
    [SerializeField, Min(0.01f)] private float pressTime = 0.18f;
    [Tooltip("Pause after the press before the paper starts to leave.")]
    [SerializeField, Min(0f)] private float pressHoldTime = 0.08f;

    [Header("Slide")]
    [Tooltip("Where the paper waits off screen, relative to where you placed it (UI units).")]
    [SerializeField] private Vector2 slideOffset = new Vector2(0f, 500f);
    [SerializeField, Min(0.01f)] private float showTime = 0.45f;
    [SerializeField, Min(0.01f)] private float hideTime = 0.4f;

    [Header("Ticking Objectives")]
    [Tooltip("Pause after the paper lands before the first checkmark.")]
    [SerializeField, Min(0f)] private float firstTickDelay = 0.2f;
    [Tooltip("Time between one objective's checkmark and the next one's.")]
    [SerializeField, Min(0f)] private float tickInterval = 0.4f;
    [SerializeField, Min(0.01f)] private float checkPopTime = 0.3f;
    [Tooltip("Pause between the checkmark appearing and the lever starting to fade.")]
    [SerializeField, Min(0f)] private float leverFadeDelay = 0.1f;
    [SerializeField, Min(0.01f)] private float leverFadeTime = 0.3f;
    [Tooltip("How see-through the lever gets once done (1 = no change, 0 = invisible).")]
    [SerializeField, Range(0f, 1f)] private float completedLeverAlpha = 0.35f;

    [Header("Events")]
    public UnityEvent onUnlocked;
    public UnityEvent onOpened;          // paper has landed
    public UnityEvent onClosed;          // paper has left the screen
    [Tooltip("Fires when an objective's checkmark is shown to the player (not when the switch is flipped).")]
    public UnityEvent<SwitchColor> onObjectiveShown;
    [Tooltip("Fires once, when the last objective's checkmark has been shown.")]
    public UnityEvent onAllObjectivesShown;

    private CanvasGroup group;
    private RectTransform rect;
    private Vector2 homePosition;
    private Vector3 qScale;
    private Color qColor;

    private bool unlocked;
    private bool isOpen;        // what the player asked for (true from the moment it starts opening)
    private bool landed;        // fully on screen
    private bool allShownFired;
    private Coroutine tickRoutine;

    // Remembered across scene loads (wiped when Play starts), so Q keeps working when the
    // player walks back into a room whose menu starts locked
    private static bool unlockedThisSession;

    // Which checkmarks the player has already seen, per room (scene name), so coming back to a
    // room shows them ticked instead of replaying the animation
    private static Dictionary<string, bool[]> seenTicks = new Dictionary<string, bool[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        unlockedThisSession = false;
        seenTicks = new Dictionary<string, bool[]>();
    }

    public bool IsUnlocked => unlocked;
    public bool IsOpen => isOpen;
    public bool StartsUnlocked => startUnlocked;

    /// <summary>Fires the moment the menu starts opening (true) or closing (false). Used by the HUD's Q key.</summary>
    public event Action<bool> Toggled;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        rect = (RectTransform)transform;
        homePosition = rect.anchoredPosition;

        // Picture only: never block clicks (mouse clicks are Player 2's controls)
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        rect.anchoredPosition = homePosition + slideOffset;

        if (qButton != null)
        {
            qScale = qButton.rectTransform.localScale;
            qColor = qButton.color;
        }

        if (toggleAction == null)
            Debug.LogWarning($"{name}: ObjectivesMenu has no Toggle Action, so Q will never open it.", this);
        if (objectives.Length == 0)
            Debug.LogWarning($"{name}: ObjectivesMenu has no Objectives. Fill the list in outside Play mode.", this);

        for (int i = 0; i < objectives.Length; i++)
        {
            Objective o = objectives[i];
            if (o.leverImages == null) o.leverImages = Array.Empty<Image>();
            o.leverColors = new Color[o.leverImages.Length];
            for (int j = 0; j < o.leverImages.Length; j++)
                if (o.leverImages[j] != null) o.leverColors[j] = o.leverImages[j].color;

            if (o.checkmark != null)
            {
                o.checkColor = o.checkmark.color;
                o.checkScale = o.checkmark.rectTransform.localScale;
                o.checkmark.enabled = false;
            }
            else Debug.LogWarning($"{name}: Objective {i} ({o.label}) has no Checkmark.", this);
        }

        // Back in a room we've been to: show the checkmarks already seen here as done
        if (seenTicks.TryGetValue(gameObject.scene.name, out bool[] seen))
        {
            for (int i = 0; i < objectives.Length && i < seen.Length; i++)
            {
                if (!seen[i] || !SwitchChannels.HasEverBeenActive(objectives[i].color)) continue;
                objectives[i].ticked = true;
                SnapTicked(objectives[i]);
            }
            if (AllTicked() && objectives.Length > 0) allShownFired = true;
        }
    }

    private void OnEnable()
    {
        if (unlockAfter != null) unlockAfter.onCompleted.AddListener(UnlockAndOpen);
    }

    private void OnDisable()
    {
        if (unlockAfter != null) unlockAfter.onCompleted.RemoveListener(UnlockAndOpen);
    }

    private void Start()
    {
        if (unlockedThisSession) Unlock();          // player already unlocked it in an earlier room
        else if (startUnlocked) Unlock(false);     // testing: doesn't count as the player unlocking it
    }

    private void Update()
    {
        if (unlocked && toggleAction != null && toggleAction.action.WasPressedThisFrame())
            Toggle();

        // While the menu is on screen, tick anything newly completed (on open, or a switch
        // flipped for the first time while the menu is up)
        if (landed && tickRoutine == null && HasUntickedCompleted())
            tickRoutine = StartCoroutine(TickRoutine());
    }

    // ---------------------------------------------------------------- public API (UnityEvents)

    /// <summary>Lets Q open the menu from now on (in this room and every room after it).</summary>
    public void Unlock() => Unlock(true);

    private void Unlock(bool rememberForSession)
    {
        if (rememberForSession) unlockedThisSession = true;
        if (unlocked) return;
        unlocked = true;
        // PlayerController enables the Player map; this just makes sure. Never disabled here.
        if (toggleAction != null && !toggleAction.action.enabled) toggleAction.action.Enable();
        onUnlocked?.Invoke();
    }

    /// <summary>Unlock and show the menu straight away (end of the tutorial). Only opens the first time.</summary>
    public void UnlockAndOpen()
    {
        // Coming back to the tutorial room: the tutorial chain replays silently and ends here,
        // so only open the menu the very first time
        bool firstTime = !unlockedThisSession;
        Unlock();
        if (firstTime) Open();
    }

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        StopEverything();
        StartCoroutine(OpenRoutine());
        Toggled?.Invoke(true);
    }

    /// <summary>Plays the Q press animation, then slides the paper away.</summary>
    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        StopEverything();
        StartCoroutine(CloseRoutine());
        Toggled?.Invoke(false);
    }

    // ---------------------------------------------------------------- open / close

    private IEnumerator OpenRoutine()
    {
        // Starts from wherever the paper is, so reopening mid-close doesn't jump
        Vector2 start = group.alpha <= 0f ? homePosition + slideOffset : rect.anchoredPosition;
        float startAlpha = group.alpha;

        for (float t = 0f; t < showTime; t += Time.deltaTime)
        {
            float p = t / showTime;
            rect.anchoredPosition = Vector2.LerpUnclamped(start, homePosition, EaseOutBack(p, 1.2f));
            group.alpha = Mathf.Lerp(startAlpha, 1f, Mathf.Clamp01(p * 2f));
            yield return null;
        }

        rect.anchoredPosition = homePosition;
        group.alpha = 1f;
        landed = true;           // Update starts ticking from here
        onOpened?.Invoke();
    }

    private IEnumerator CloseRoutine()
    {
        if (qButton != null) yield return PressButton();
        if (pressHoldTime > 0f) yield return new WaitForSeconds(pressHoldTime);

        Vector2 start = rect.anchoredPosition;
        Vector2 end = homePosition + slideOffset;
        float startAlpha = group.alpha;

        for (float t = 0f; t < hideTime; t += Time.deltaTime)
        {
            float p = t / hideTime;
            rect.anchoredPosition = Vector2.Lerp(start, end, p * p);   // ease in: accelerates away
            group.alpha = startAlpha * (1f - Smooth(p));
            yield return null;
        }

        rect.anchoredPosition = end;
        group.alpha = 0f;
        onClosed?.Invoke();
    }

    // Key goes down (smaller + darker) quickly, then springs back up
    private IEnumerator PressButton()
    {
        RectTransform tr = qButton.rectTransform;
        Color dark = qColor * pressTint;
        float down = pressTime * 0.4f;
        float up = pressTime - down;

        for (float t = 0f; t < down; t += Time.deltaTime)
        {
            float k = Smooth(t / down);
            tr.localScale = Vector3.Lerp(qScale, qScale * pressScale, k);
            qButton.color = Color.Lerp(qColor, dark, k);
            yield return null;
        }
        for (float t = 0f; t < up; t += Time.deltaTime)
        {
            float k = EaseOutBack(t / up, 2f);
            tr.localScale = Vector3.LerpUnclamped(qScale * pressScale, qScale, k);
            qButton.color = Color.Lerp(dark, qColor, Mathf.Clamp01(k));
            yield return null;
        }
        ResetButton();
    }

    // Stops every running animation and snaps anything half-animated to its final look,
    // so an objective that started ticking is never left half done (and never replays).
    private void StopEverything()
    {
        StopAllCoroutines();
        tickRoutine = null;
        landed = false;
        ResetButton();
        foreach (Objective o in objectives)
            if (o.ticked) SnapTicked(o);
    }

    private void ResetButton()
    {
        if (qButton == null) return;
        qButton.rectTransform.localScale = qScale;
        qButton.color = qColor;
    }

    // ---------------------------------------------------------------- ticking objectives

    private bool HasUntickedCompleted()
    {
        foreach (Objective o in objectives)
            if (!o.ticked && SwitchChannels.HasEverBeenActive(o.color)) return true;
        return false;
    }

    private IEnumerator TickRoutine()
    {
        if (firstTickDelay > 0f) yield return new WaitForSeconds(firstTickDelay);

        bool first = true;
        for (int i = 0; i < objectives.Length; i++)
        {
            Objective o = objectives[i];
            if (o.ticked || !SwitchChannels.HasEverBeenActive(o.color)) continue;

            if (!first && tickInterval > 0f) yield return new WaitForSeconds(tickInterval);
            first = false;

            o.ticked = true;
            RememberTick(i);
            StartCoroutine(AnimateTick(o));
            onObjectiveShown?.Invoke(o.color);
        }

        if (!allShownFired && objectives.Length > 0 && AllTicked())
        {
            allShownFired = true;
            onAllObjectivesShown?.Invoke();
        }
        tickRoutine = null;
    }

    private void RememberTick(int index)
    {
        string room = gameObject.scene.name;
        if (!seenTicks.TryGetValue(room, out bool[] seen) || seen.Length != objectives.Length)
        {
            bool[] grown = new bool[objectives.Length];
            if (seen != null) Array.Copy(seen, grown, Mathf.Min(seen.Length, grown.Length));
            seenTicks[room] = seen = grown;
        }
        seen[index] = true;
    }

    private bool AllTicked()
    {
        foreach (Objective o in objectives)
            if (!o.ticked) return false;
        return true;
    }

    // Checkmark pops into the box, then the lever fades
    private IEnumerator AnimateTick(Objective o)
    {
        if (o.checkmark != null)
        {
            Image check = o.checkmark;
            Transform tr = check.rectTransform;
            Color c = o.checkColor;

            check.enabled = true;
            for (float t = 0f; t < checkPopTime; t += Time.deltaTime)
            {
                float p = t / checkPopTime;
                tr.localScale = o.checkScale * EaseOutBack(p, 2.5f);   // overshoots, then settles
                check.color = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(p * 3f));
                yield return null;
            }
            tr.localScale = o.checkScale;
            check.color = c;
        }

        if (leverFadeDelay > 0f) yield return new WaitForSeconds(leverFadeDelay);

        for (float t = 0f; t < leverFadeTime; t += Time.deltaTime)
        {
            SetLeverAlpha(o, Mathf.Lerp(1f, completedLeverAlpha, Smooth(t / leverFadeTime)));
            yield return null;
        }
        SetLeverAlpha(o, completedLeverAlpha);
    }

    private void SnapTicked(Objective o)
    {
        if (o.checkmark != null)
        {
            o.checkmark.enabled = true;
            o.checkmark.rectTransform.localScale = o.checkScale;
            o.checkmark.color = o.checkColor;
        }
        SetLeverAlpha(o, completedLeverAlpha);
    }

    private static void SetLeverAlpha(Objective o, float alphaMultiplier)
    {
        for (int i = 0; i < o.leverImages.Length; i++)
        {
            if (o.leverImages[i] == null) continue;
            Color c = o.leverColors[i];
            c.a *= alphaMultiplier;
            o.leverImages[i].color = c;
        }
    }

    // ---------------------------------------------------------------- easing (same as InstructionPaper)

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + t * t * ((overshoot + 1f) * t + overshoot);
    }
}