using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A world-space "press E" prompt that hovers above something (e.g. a switch).
/// Pops in after an InstructionPaper finishes (or at start), bobs gently,
/// and pops away for good the first time the linked switch is flipped.
///
/// Put this on an empty parent; every SpriteRenderer under it (paper, letter...) fades together.
/// </summary>
public class InteractPrompt : MonoBehaviour
{
    [Header("When")]
    [Tooltip("Appear after this paper has left the screen. Leave empty to appear at the start.")]
    [SerializeField] private InstructionPaper showAfter;
    [Tooltip("Extra seconds to wait before appearing.")]
    [SerializeField, Min(0f)] private float showDelay = 0.3f;
    [Tooltip("Disappear (for good) the first time this switch flips.")]
    [SerializeField] private LeverSwitch hideWhenFlipped;

    [Header("Pop")]
    [SerializeField, Min(0.01f)] private float showTime = 0.35f;
    [SerializeField, Min(0.01f)] private float hideTime = 0.25f;

    [Header("Hover")]
    [Tooltip("How far it bobs up and down (world units).")]
    [SerializeField, Min(0f)] private float bobHeight = 0.08f;
    [Tooltip("Seconds for one full bob.")]
    [SerializeField, Min(0.1f)] private float bobPeriod = 1.6f;

    [Header("Events")]
    [Tooltip("Fires once when the prompt goes away for good (also if the switch was flipped " +
             "before the prompt ever appeared). Use it to show the next InstructionPaper.")]
    public UnityEvent onHidden;

    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Vector3 homePosition;
    private Vector3 baseScale;
    private float bobTime;

    private bool visible;
    private bool dismissed;
    private bool waitingForPaper;
    private Coroutine routine;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;

        homePosition = transform.localPosition;
        baseScale = transform.localScale;

        SetVisuals(0f, 0f);   // hidden until told to show
    }

    private void OnEnable()
    {
        if (showAfter != null) showAfter.onCompleted.AddListener(OnPaperDone);
        if (hideWhenFlipped != null) hideWhenFlipped.onToggled.AddListener(OnSwitchFlipped);
    }

    private void OnDisable()
    {
        if (showAfter != null) showAfter.onCompleted.RemoveListener(OnPaperDone);
        if (hideWhenFlipped != null) hideWhenFlipped.onToggled.RemoveListener(OnSwitchFlipped);
    }

    private void Start()
    {
        if (hideWhenFlipped == null)
            Debug.LogWarning($"{name}: InteractPrompt has no switch in Hide When Flipped, so it will never go away.", this);

        if (showAfter == null) Show();
    }

    private void OnPaperDone()
    {
        if (waitingForPaper) FinishHidden();
        else Show();
    }

    private void OnSwitchFlipped(bool on) => Hide();

    /// <summary>Pop the prompt in (ignored once it has been dismissed).</summary>
    public void Show()
    {
        if (dismissed || visible) return;
        visible = true;
        Restart(ShowRoutine());
    }

    /// <summary>Pop the prompt away for good.</summary>
    public void Hide()
    {
        if (dismissed) return;
        dismissed = true;

        if (visible) { Restart(HideRoutine()); return; }

        // Flipped before the prompt ever appeared: never show it.
        StopRoutine();
        if (PaperStillUp)
            waitingForPaper = true;   // hold onHidden until the paper leaves, so papers never overlap
        else
            FinishHidden();
    }

    // The paper we wait for deactivates itself once it has left the screen
    private bool PaperStillUp => showAfter != null && showAfter.gameObject.activeInHierarchy;

    private void FinishHidden()
    {
        waitingForPaper = false;
        onHidden?.Invoke();
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!visible) return;
        bobTime += Time.deltaTime;
        float y = Mathf.Sin(bobTime / bobPeriod * Mathf.PI * 2f) * bobHeight;
        transform.localPosition = homePosition + Vector3.up * y;
    }

    private IEnumerator ShowRoutine()
    {
        if (showDelay > 0f) yield return new WaitForSeconds(showDelay);

        for (float t = 0f; t < showTime; t += Time.deltaTime)
        {
            float p = t / showTime;
            SetVisuals(Mathf.Clamp01(p * 2f), EaseOutBack(p, 2f));   // fades in fast, overshoots in size
            yield return null;
        }
        SetVisuals(1f, 1f);
        routine = null;
    }

    private IEnumerator HideRoutine()
    {
        float startAlpha = renderers.Length > 0 ? renderers[0].color.a / Mathf.Max(baseColors[0].a, 0.0001f) : 1f;

        for (float t = 0f; t < hideTime; t += Time.deltaTime)
        {
            float p = t / hideTime;
            // Quick swell, then shrink away while fading
            float scale = p < 0.3f ? Mathf.Lerp(1f, 1.15f, p / 0.3f) : Mathf.Lerp(1.15f, 0.4f, (p - 0.3f) / 0.7f);
            SetVisuals(startAlpha * (1f - p), scale);
            yield return null;
        }

        SetVisuals(0f, 0f);
        visible = false;
        routine = null;
        FinishHidden();
    }

    private void SetVisuals(float alpha, float scale)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Color c = baseColors[i];
            c.a *= alpha;
            renderers[i].color = c;
            renderers[i].enabled = alpha > 0.001f;
        }
        transform.localScale = baseScale * scale;
    }

    private void Restart(IEnumerator next)
    {
        StopRoutine();
        routine = StartCoroutine(next);
    }

    private void StopRoutine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    // 0 → 1 with a little overshoot past 1 before settling
    private static float EaseOutBack(float t, float overshoot)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + t * t * ((overshoot + 1f) * t + overshoot);
    }
}