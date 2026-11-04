using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// A tutorial "paper" that slides onto the screen, waits for the player to use each listed
/// input once, ticks each one off (checkmark pops in, icon fades), then slides away.
///
/// Reuse it for later instructions: give each paper its own steps, untick Show On Start,
/// and call Show() from a UnityEvent (e.g. the previous paper's On Completed, or a trigger).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class InstructionPaper : MonoBehaviour
{
    [Serializable]
    public class Step
    {
        [Tooltip("Just a label for the Inspector.")]
        public string label = "Move";
        [Tooltip("The same action the player uses (e.g. Player/Move, Player/Jump).")]
        public InputActionReference action;
        [Tooltip("The key picture. Fades out when this step is done.")]
        public Image icon;
        [Tooltip("The green checkmark. Hidden until this step is done.")]
        public Image checkmark;

        [NonSerialized] public bool done;
        [NonSerialized] public Color iconColor;
        [NonSerialized] public Vector3 checkScale;
    }

    [Header("Steps")]
    [SerializeField] private Step[] steps = Array.Empty<Step>();

    [Header("Show")]
    [SerializeField] private bool showOnStart = true;
    [Tooltip("Seconds after the scene starts before the paper appears.")]
    [SerializeField, Min(0f)] private float showDelay = 0.5f;
    [Tooltip("Where the paper starts, relative to where you placed it (UI units). " +
             "(0, 500) drops it in from above; (0, -500) slides it up from below.")]
    [SerializeField] private Vector2 slideOffset = new Vector2(0f, 500f);
    [SerializeField, Min(0.01f)] private float showTime = 0.45f;

    [Header("Hide")]
    [Tooltip("Seconds to wait after the last checkmark before the paper leaves.")]
    [SerializeField, Min(0f)] private float hideDelay = 1f;
    [SerializeField, Min(0.01f)] private float hideTime = 0.4f;

    [Header("Completed Step")]
    [Tooltip("How see-through the icon gets once done (1 = no change, 0 = invisible).")]
    [Range(0f, 1f)]
    [SerializeField] private float completedIconAlpha = 0.35f;
    [SerializeField, Min(0.01f)] private float iconFadeTime = 0.3f;
    [SerializeField, Min(0.01f)] private float checkPopTime = 0.3f;

    [Header("Events")]
    public UnityEvent onShown;
    public UnityEvent<int> onStepCompleted;
    [Tooltip("Fires after the paper has left the screen. Use it to show the next paper.")]
    public UnityEvent onCompleted;

    private CanvasGroup group;
    private RectTransform rect;
    private Vector2 homePosition;
    private bool listening;   // counting input (from the moment the paper starts appearing)
    private bool finishing;
    private bool shown;

    public bool IsShowing => shown && !finishing;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        rect = (RectTransform)transform;
        homePosition = rect.anchoredPosition;

        // Tutorial is just a picture: never block clicks or UI navigation
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        foreach (Step s in steps)
        {
            if (s.icon != null) s.iconColor = s.icon.color;
            if (s.checkmark != null)
            {
                s.checkScale = s.checkmark.rectTransform.localScale;
                s.checkmark.enabled = false;
            }
        }
    }

    private void Start()
    {
        if (showOnStart) Show();
    }

    /// <summary>Slide the paper in and start watching for input. Safe to call from UnityEvents.</summary>
    public void Show()
    {
        if (shown) return;
        shown = true;
        gameObject.SetActive(true);
        StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        rect.anchoredPosition = homePosition + slideOffset;
        group.alpha = 0f;

        if (showDelay > 0f) yield return new WaitForSeconds(showDelay);

        // PlayerController enables these too; we only make sure they're on, and never turn them off
        foreach (Step s in steps)
            if (s.action != null && !s.action.action.enabled) s.action.action.Enable();

        listening = true;

        for (float t = 0f; t < showTime; t += Time.deltaTime)
        {
            float k = EaseOutBack(t / showTime, 1.2f);
            rect.anchoredPosition = Vector2.LerpUnclamped(homePosition + slideOffset, homePosition, k);
            group.alpha = Mathf.Clamp01(t / (showTime * 0.5f));
            yield return null;
        }

        rect.anchoredPosition = homePosition;
        group.alpha = 1f;
        onShown?.Invoke();
    }

    private void Update()
    {
        if (!listening || finishing) return;

        bool allDone = true;
        for (int i = 0; i < steps.Length; i++)
        {
            Step s = steps[i];
            if (s.done) continue;

            // IsPressed works for buttons (Space) and for the WASD composite
            // (true once the stick/keys push past the press point).
            if (s.action != null && s.action.action.IsPressed())
                CompleteStep(i);
            else
                allDone = false;
        }

        if (allDone && steps.Length > 0)
        {
            finishing = true;
            listening = false;
            StartCoroutine(HideRoutine());
        }
    }

    /// <summary>Tick a step off by index (also usable for steps that aren't a single input).</summary>
    public void CompleteStep(int index)
    {
        if (index < 0 || index >= steps.Length || steps[index].done) return;

        Step s = steps[index];
        s.done = true;
        if (s.icon != null) StartCoroutine(FadeIcon(s));
        if (s.checkmark != null) StartCoroutine(PopCheck(s));
        onStepCompleted?.Invoke(index);
    }

    private IEnumerator FadeIcon(Step s)
    {
        Color from = s.iconColor;
        Color to = new Color(from.r, from.g, from.b, from.a * completedIconAlpha);
        for (float t = 0f; t < iconFadeTime; t += Time.deltaTime)
        {
            s.icon.color = Color.Lerp(from, to, Smooth(t / iconFadeTime));
            yield return null;
        }
        s.icon.color = to;
    }

    private IEnumerator PopCheck(Step s)
    {
        Image check = s.checkmark;
        Transform tr = check.rectTransform;
        Color c = check.color;

        check.enabled = true;
        for (float t = 0f; t < checkPopTime; t += Time.deltaTime)
        {
            float p = t / checkPopTime;
            tr.localScale = s.checkScale * EaseOutBack(p, 2.5f);   // overshoots, then settles
            check.color = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(p * 3f));
            yield return null;
        }
        tr.localScale = s.checkScale;
        check.color = c;
    }

    private IEnumerator HideRoutine()
    {
        // Let the last checkmark finish and be seen
        yield return new WaitForSeconds(Mathf.Max(hideDelay, checkPopTime));

        Vector2 start = rect.anchoredPosition;
        Vector2 end = homePosition + slideOffset;
        for (float t = 0f; t < hideTime; t += Time.deltaTime)
        {
            float p = t / hideTime;
            rect.anchoredPosition = Vector2.Lerp(start, end, p * p);   // ease in: accelerates away
            group.alpha = 1f - Smooth(p);
            yield return null;
        }

        group.alpha = 0f;
        onCompleted?.Invoke();
        gameObject.SetActive(false);
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // 0 → 1 with a little overshoot past 1 before settling (overshoot ≈ 1.7 is the classic value)
    private static float EaseOutBack(float t, float overshoot)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + t * t * ((overshoot + 1f) * t + overshoot);
    }
}