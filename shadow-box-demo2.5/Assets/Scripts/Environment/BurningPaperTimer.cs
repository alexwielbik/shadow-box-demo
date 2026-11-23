using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>
/// HUD timer: a strip of paper that burns from left to right.
/// Drives the PaperBurn (Canvas Shader Graph) material on the Paper Image and moves
/// a UIFlameEmitter along the burning edge. Slides in when started, out when stopped.
///
/// Put this on the container (it adds a CanvasGroup); the paper Image is a child.
/// The container must stay ACTIVE in the scene; it hides itself with its CanvasGroup.
/// </summary>
public class BurningPaperTimer : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("The paper Image. Its Material must use the PaperBurn shader.")]
    [SerializeField] private Image paper;
    [Tooltip("Child of the paper that rides along the burning edge (holds the UIFlameEmitter).")]
    [SerializeField] private RectTransform flameAnchor;
    [SerializeField] private UIFlameEmitter flames;

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float duration = 20f;
    [Tooltip("When this fraction of time is left, the fire starts burning harder.")]
    [SerializeField, Range(0f, 1f)] private float lowTimeThreshold = 0.25f;
    [Tooltip("Flame emission multiplier at the very end.")]
    [SerializeField, Min(1f)] private float lowTimeFlameBoost = 2.5f;

    [Header("Burn edge")]
    [Tooltip("How many noise 'blobs' fit across the paper's height. Higher = more jagged edge.")]
    [SerializeField, Min(0.1f)] private float noiseScale = 3f;

    [Header("Show / hide")]
    [Tooltip("Where it slides in from, relative to where you placed it.")]
    [SerializeField] private Vector2 hiddenOffset = new Vector2(0f, 160f);
    [SerializeField, Min(0.01f)] private float slideTime = 0.35f;
    [Tooltip("Seconds to stay on screen after it stops (so the player sees the result).")]
    [SerializeField, Min(0f)] private float hideDelay = 0.6f;

    [Header("Events")]
    public UnityEvent onTimeUp;

    private static readonly int BurnId = Shader.PropertyToID("_Burn");
    private static readonly int UVRectId = Shader.PropertyToID("_UVRect");
    private static readonly int NoiseTilingId = Shader.PropertyToID("_NoiseTiling");
    private static readonly int CharWidthId = Shader.PropertyToID("_CharWidth");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");

    private RectTransform rt;
    private CanvasGroup group;
    private Vector2 shownPosition;
    private Material mat;
    private float elapsed;
    private bool running;
    private Coroutine slide;

    /// <summary>1 = all the time left, 0 = burned up.</summary>
    public float Remaining01 => 1f - Mathf.Clamp01(elapsed / duration);
    public bool IsRunning => running;
    public float Duration { get => duration; set => duration = Mathf.Max(0.1f, value); }

    private void Awake()
    {
        rt = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        shownPosition = rt.anchoredPosition;

        if (paper == null) paper = GetComponentInChildren<Image>(true);
        if (paper.material == null || paper.material == Graphic.defaultGraphicMaterial)
            Debug.LogWarning($"{name}: the paper Image has no PaperBurn material, so it won't burn.", this);

        // Own copy, so tweaking it at runtime never edits the asset
        mat = new Material(paper.material);
        paper.material = mat;

        // The paper comes from a sprite sheet: tell the shader which part of the sheet it is
        if (paper.sprite != null)
            mat.SetVector(UVRectId, DataUtility.GetOuterUV(paper.sprite));

        Rect r = paper.rectTransform.rect;
        float aspect = r.width / Mathf.Max(1f, r.height);
        mat.SetVector(NoiseTilingId, new Vector4(noiseScale * aspect, noiseScale, 0f, 0f));

        ApplyBurn(0f);
        SetShown(0f);
    }

    private void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }

    /// <summary>Fresh paper, slide in, start burning.</summary>
    public void StartTimer()
    {
        elapsed = 0f;
        running = true;
        ApplyBurn(0f);

        if (flames != null)
        {
            flames.Clear();
            flames.SetIntensity(1f);
            flames.SetEmitting(true);
        }
        Slide(true, 0f);
    }

    /// <summary>Stop where it is (e.g. all coins collected) and slide away.</summary>
    public void StopTimer()
    {
        running = false;
        if (flames != null) flames.SetEmitting(false);
        Slide(false, hideDelay);
    }

    /// <summary>Stop and hide immediately, no events.</summary>
    public void ResetTimer()
    {
        running = false;
        elapsed = 0f;
        ApplyBurn(0f);
        if (flames != null) { flames.SetEmitting(false); flames.Clear(); }
        if (slide != null) StopCoroutine(slide);
        slide = null;
        SetShown(0f);
    }

    private void Update()
    {
        if (!running) return;

        elapsed += Time.deltaTime;
        float burn = Mathf.Clamp01(elapsed / duration);
        ApplyBurn(burn);

        if (flames != null)
        {
            float left = Remaining01;
            float boost = left < lowTimeThreshold && lowTimeThreshold > 0f
                ? Mathf.Lerp(lowTimeFlameBoost, 1f, left / lowTimeThreshold)
                : 1f;
            flames.SetIntensity(boost);
        }

        if (burn >= 1f)
        {
            running = false;
            if (flames != null) flames.SetEmitting(false);
            Slide(false, hideDelay);
            onTimeUp?.Invoke();
        }
    }

    private void ApplyBurn(float burn)
    {
        mat.SetFloat(BurnId, burn);
        if (flameAnchor == null) return;

        // Same math as the shader, so the flames sit on the visible edge:
        // threshold B runs from -CharWidth to 1; the edge is where lerp(u, noise≈0.5, s) == B
        float charW = mat.GetFloat(CharWidthId);
        float s = Mathf.Clamp(mat.GetFloat(NoiseStrengthId), 0f, 0.95f);
        float b = Mathf.Lerp(-charW, 1f, burn);
        float u = Mathf.Clamp01((b - 0.5f * s) / (1f - s));

        Rect r = paper.rectTransform.rect;
        Vector3 p = flameAnchor.localPosition;
        flameAnchor.localPosition = new Vector3(Mathf.Lerp(r.xMin, r.xMax, u), r.center.y, p.z);
    }

    // ---------------- Slide in / out ----------------

    private void Slide(bool show, float delay)
    {
        if (slide != null) StopCoroutine(slide);
        slide = StartCoroutine(SlideRoutine(show ? 1f : 0f, delay));
    }

    private IEnumerator SlideRoutine(float target, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float start = group.alpha;
        for (float t = 0f; t < slideTime; t += Time.deltaTime)
        {
            float p = Mathf.SmoothStep(0f, 1f, t / slideTime);
            SetShown(Mathf.Lerp(start, target, p));
            yield return null;
        }
        SetShown(target);
        if (target <= 0f && flames != null) flames.Clear();
        slide = null;
    }

    private void SetShown(float amount)
    {
        group.alpha = amount;
        rt.anchoredPosition = shownPosition + hiddenOffset * (1f - amount);
    }
}