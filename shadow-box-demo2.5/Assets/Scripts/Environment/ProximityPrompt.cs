using System.Collections;
using UnityEngine;

/// <summary>
/// A world-space "press E" hint that can be shown and hidden any number of times
/// (unlike InteractPrompt, which is a one-time tutorial hint).
/// Something else decides when: e.g. TimeTrialStarter calls Show() when the player
/// walks into range and Hide() when they leave or the trial starts.
///
/// Put this on an empty parent; every SpriteRenderer under it (paper, letter...) fades together.
/// Use a transparent material (Sprites-Default) on those renderers so the fade works.
/// </summary>
public class ProximityPrompt : MonoBehaviour
{
    [Header("Pop")]
    [SerializeField, Min(0.01f)] private float showTime = 0.3f;
    [SerializeField, Min(0.01f)] private float hideTime = 0.2f;

    [Header("Hover")]
    [Tooltip("How far it bobs up and down (world units).")]
    [SerializeField, Min(0f)] private float bobHeight = 0.08f;
    [Tooltip("Seconds for one full bob.")]
    [SerializeField, Min(0.1f)] private float bobPeriod = 1.6f;

    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Vector3 homePosition;
    private Vector3 baseScale;
    private float bobTime;

    private float alpha;   // 0..1, current fade
    private float scale;   // multiplier on baseScale
    private bool visible;
    private Coroutine routine;

    public bool IsVisible => visible;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;

        homePosition = transform.localPosition;
        baseScale = transform.localScale;

        SetVisuals(0f, 0f);   // hidden until told to show
    }

    /// <summary>Pop the prompt in. Safe to call every frame.</summary>
    public void Show()
    {
        if (visible) return;
        visible = true;
        Restart(ShowRoutine());
    }

    /// <summary>Pop the prompt away. It can be shown again later.</summary>
    public void Hide()
    {
        if (!visible) return;
        visible = false;
        Restart(HideRoutine());
    }

    /// <summary>Hide with no animation.</summary>
    public void HideInstant()
    {
        visible = false;
        StopRoutine();
        SetVisuals(0f, 0f);
    }

    private void Update()
    {
        if (alpha <= 0.001f) return;
        bobTime += Time.deltaTime;
        float y = Mathf.Sin(bobTime / bobPeriod * Mathf.PI * 2f) * bobHeight;
        transform.localPosition = homePosition + Vector3.up * y;
    }

    private IEnumerator ShowRoutine()
    {
        float startAlpha = alpha;
        float startScale = scale;

        for (float t = 0f; t < showTime; t += Time.deltaTime)
        {
            float p = t / showTime;
            // fades in fast, overshoots in size, starting from wherever a half-finished hide left it
            SetVisuals(Mathf.Lerp(startAlpha, 1f, Mathf.Clamp01(p * 2f)),
                       Mathf.LerpUnclamped(startScale, 1f, EaseOutBack(p, 2f)));
            yield return null;
        }
        SetVisuals(1f, 1f);
        routine = null;
    }

    private IEnumerator HideRoutine()
    {
        float startAlpha = alpha;
        float startScale = scale;

        for (float t = 0f; t < hideTime; t += Time.deltaTime)
        {
            float p = t / hideTime;
            SetVisuals(Mathf.Lerp(startAlpha, 0f, p), Mathf.Lerp(startScale, 0.4f, p));
            yield return null;
        }
        SetVisuals(0f, 0f);
        bobTime = 0f;
        routine = null;
    }

    private void SetVisuals(float a, float s)
    {
        alpha = a;
        scale = s;
        for (int i = 0; i < renderers.Length; i++)
        {
            Color c = baseColors[i];
            c.a *= a;
            renderers[i].color = c;
            renderers[i].enabled = a > 0.001f;
        }
        transform.localScale = baseScale * Mathf.Max(s, 0f);
    }

    private void Restart(IEnumerator next)
    {
        StopRoutine();
        if (isActiveAndEnabled) routine = StartCoroutine(next);
        else SetVisuals(visible ? 1f : 0f, visible ? 1f : 0f);
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