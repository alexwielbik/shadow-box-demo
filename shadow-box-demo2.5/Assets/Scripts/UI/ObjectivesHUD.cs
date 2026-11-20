using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Always-on HUD that appears after the Level Objectives menu is closed for the first time:
///   - Top left: a small paper showing a switch picture for every color that is ON right now.
///     Pictures pop in when their color turns on and shrink away when it turns off.
///   - Top right: the Q key. It plays a press animation every time the menu opens or closes.
///
/// "Revealed" is remembered across scene loads (reset when Play starts), so later rooms show the
/// HUD straight away. If the ObjectivesMenu has Start Unlocked ticked (template / later levels
/// tested on their own), the HUD reveals itself at Start too.
///
/// Picture only: every CanvasGroup here ignores the mouse (mouse clicks are Player 2's controls).
/// This GameObject must stay active; the CanvasGroups hide things.
/// </summary>
public class ObjectivesHud : MonoBehaviour
{
    [Serializable]
    public class SwitchSlot
    {
        [Tooltip("Just a label for the Inspector.")]
        public string label = "Grey switch";
        public SwitchColor color = SwitchColor.Grey;
        [Tooltip("The slot's root (e.g. Lever_Grey, holding Lever + Base). Shown while this color is on. " +
                 "A CanvasGroup is added automatically if it doesn't have one.")]
        public RectTransform root;

        [NonSerialized] public CanvasGroup group;
        [NonSerialized] public Vector3 fullScale;
        [NonSerialized] public bool shown;
        [NonSerialized] public Coroutine routine;
    }

    [Header("Menu")]
    [Tooltip("The Level Objectives menu. Found automatically if left empty.")]
    [SerializeField] private ObjectivesMenu menu;

    [Header("Switch Paper (top left)")]
    [Tooltip("CanvasGroup on the small paper (SwitchPaper). Hidden until the HUD is revealed.")]
    [SerializeField] private CanvasGroup paper;
    [Tooltip("Where the paper waits off screen, relative to where you placed it (UI units).")]
    [SerializeField] private Vector2 paperSlideOffset = new Vector2(-450f, 0f);
    [SerializeField, Min(0.01f)] private float paperShowTime = 0.45f;
    [SerializeField] private SwitchSlot[] slots = Array.Empty<SwitchSlot>();
    [SerializeField, Min(0.01f)] private float slotShowTime = 0.3f;
    [SerializeField, Min(0.01f)] private float slotHideTime = 0.2f;

    [Header("Q Key (top right)")]
    [Tooltip("CanvasGroup on the Q key HUD object. Hidden until the HUD is revealed.")]
    [SerializeField] private CanvasGroup qGroup;
    [Tooltip("The Q picture that gets pressed. Usually the Image on the same object as Q Group.")]
    [SerializeField] private Image qImage;
    [SerializeField] private Vector2 qSlideOffset = new Vector2(250f, 0f);
    [Tooltip("Delay after the paper starts sliding in before the Q key follows.")]
    [SerializeField, Min(0f)] private float qShowDelay = 0.15f;
    [Tooltip("How small the key gets at the bottom of the press.")]
    [SerializeField, Range(0.5f, 1f)] private float pressScale = 0.82f;
    [Tooltip("Tint at the bottom of the press (multiplies the key's color).")]
    [SerializeField] private Color pressTint = new Color(0.7f, 0.7f, 0.7f, 1f);
    [SerializeField, Min(0.01f)] private float pressTime = 0.18f;

    // Survives scene loads, so the HUD stays up in every room once it has appeared
    private static bool revealed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => revealed = false;

    private RectTransform paperRect, qRect;
    private Vector2 paperHome, qHome;
    private Vector3 qScale;
    private Color qColor;
    private bool shownHere;      // revealed in THIS scene's HUD
    private Coroutine pressRoutine;

    public static bool IsRevealed => revealed;

    private void Awake()
    {
        if (menu == null) menu = FindAnyObjectByType<ObjectivesMenu>(FindObjectsInactive.Include);
        if (menu == null) Debug.LogWarning($"{name}: ObjectivesHud can't find an ObjectivesMenu.", this);

        if (paper != null)
        {
            paperRect = (RectTransform)paper.transform;
            paperHome = paperRect.anchoredPosition;
            HideGroup(paper);
            paperRect.anchoredPosition = paperHome + paperSlideOffset;
        }
        else Debug.LogWarning($"{name}: ObjectivesHud has no Paper CanvasGroup.", this);

        if (qGroup != null)
        {
            qRect = (RectTransform)qGroup.transform;
            qHome = qRect.anchoredPosition;
            HideGroup(qGroup);
            qRect.anchoredPosition = qHome + qSlideOffset;
        }
        if (qImage != null)
        {
            qScale = qImage.rectTransform.localScale;
            qColor = qImage.color;
        }

        foreach (SwitchSlot s in slots)
        {
            if (s.root == null) continue;
            s.group = s.root.GetComponent<CanvasGroup>();
            if (s.group == null) s.group = s.root.gameObject.AddComponent<CanvasGroup>();
            s.group.interactable = false;
            s.group.blocksRaycasts = false;
            s.fullScale = s.root.localScale;
            SnapSlot(s, false);
        }
    }

    private void OnEnable()
    {
        if (menu == null) return;
        menu.onClosed.AddListener(OnMenuClosed);
        menu.Toggled += OnMenuToggled;
    }

    private void OnDisable()
    {
        if (menu == null) return;
        menu.onClosed.RemoveListener(OnMenuClosed);
        menu.Toggled -= OnMenuToggled;
    }

    private void Start()
    {
        // Switches publish their state in Awake, so it's ready here
        foreach (SwitchSlot s in slots)
            if (s.root != null) SnapSlot(s, SwitchChannels.IsActive(s.color));

        if (revealed) RevealInstant();                              // came from an earlier room
        else if (menu != null && menu.StartsUnlocked) Reveal();     // testing a later level directly
    }

    private void Update()
    {
        // Polling (not events): SwitchChannels.ResetState on scene change fires no events
        foreach (SwitchSlot s in slots)
        {
            if (s.root == null) continue;
            bool want = SwitchChannels.IsActive(s.color);
            if (want == s.shown) continue;

            if (shownHere) AnimateSlot(s, want);
            else SnapSlot(s, want);     // paper is hidden anyway; just be right when it appears
        }
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Slides the paper and Q key in (does nothing if already shown).</summary>
    public void Reveal()
    {
        if (shownHere) return;
        revealed = shownHere = true;
        if (paper != null) StartCoroutine(SlideIn(paper, paperRect, paperHome, paperSlideOffset, 0f));
        if (qGroup != null) StartCoroutine(SlideIn(qGroup, qRect, qHome, qSlideOffset, qShowDelay));
    }

    /// <summary>Plays the Q press animation (called whenever the menu opens or closes).</summary>
    public void PressQ()
    {
        if (qImage == null || !shownHere) return;
        if (pressRoutine != null) StopCoroutine(pressRoutine);
        pressRoutine = StartCoroutine(PressButton());
    }

    // ---------------------------------------------------------------- menu hooks

    private void OnMenuClosed() => Reveal();               // first close reveals; later ones do nothing
    private void OnMenuToggled(bool opening) => PressQ();

    // ---------------------------------------------------------------- reveal

    private void RevealInstant()
    {
        revealed = shownHere = true;
        if (paper != null) { paperRect.anchoredPosition = paperHome; paper.alpha = 1f; }
        if (qGroup != null) { qRect.anchoredPosition = qHome; qGroup.alpha = 1f; }
    }

    private IEnumerator SlideIn(CanvasGroup g, RectTransform r, Vector2 home, Vector2 offset, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Vector2 start = home + offset;
        for (float t = 0f; t < paperShowTime; t += Time.deltaTime)
        {
            float p = t / paperShowTime;
            r.anchoredPosition = Vector2.LerpUnclamped(start, home, EaseOutBack(p, 1.2f));
            g.alpha = Mathf.Clamp01(p * 2f);
            yield return null;
        }
        r.anchoredPosition = home;
        g.alpha = 1f;
    }

    private static void HideGroup(CanvasGroup g)
    {
        g.alpha = 0f;
        g.interactable = false;
        g.blocksRaycasts = false;
    }

    // ---------------------------------------------------------------- switch slots

    private void SnapSlot(SwitchSlot s, bool show)
    {
        if (s.routine != null) { StopCoroutine(s.routine); s.routine = null; }
        s.shown = show;
        s.group.alpha = show ? 1f : 0f;
        s.root.localScale = s.fullScale;
    }

    private void AnimateSlot(SwitchSlot s, bool show)
    {
        if (s.routine != null) StopCoroutine(s.routine);
        s.shown = show;
        s.routine = StartCoroutine(show ? ShowSlot(s) : HideSlot(s));
    }

    // Pops in with an overshoot. Starts from wherever it is, so a quick on/off/on doesn't jump.
    private IEnumerator ShowSlot(SwitchSlot s)
    {
        Vector3 startScale = s.group.alpha <= 0f ? Vector3.zero : s.root.localScale;
        float startAlpha = s.group.alpha;

        for (float t = 0f; t < slotShowTime; t += Time.deltaTime)
        {
            float p = t / slotShowTime;
            s.root.localScale = Vector3.LerpUnclamped(startScale, s.fullScale, EaseOutBack(p, 2f));
            s.group.alpha = Mathf.Lerp(startAlpha, 1f, Mathf.Clamp01(p * 3f));
            yield return null;
        }
        s.root.localScale = s.fullScale;
        s.group.alpha = 1f;
        s.routine = null;
    }

    // Shrinks a little and fades out
    private IEnumerator HideSlot(SwitchSlot s)
    {
        Vector3 startScale = s.root.localScale;
        float startAlpha = s.group.alpha;

        for (float t = 0f; t < slotHideTime; t += Time.deltaTime)
        {
            float k = Smooth(t / slotHideTime);
            s.root.localScale = Vector3.Lerp(startScale, s.fullScale * 0.6f, k);
            s.group.alpha = Mathf.Lerp(startAlpha, 0f, k);
            yield return null;
        }
        s.group.alpha = 0f;
        s.root.localScale = s.fullScale;
        s.routine = null;
    }

    // ---------------------------------------------------------------- Q press (same feel as the menu's Q)

    private IEnumerator PressButton()
    {
        RectTransform tr = qImage.rectTransform;
        Color dark = qColor * pressTint;
        float down = pressTime * 0.4f;
        float up = pressTime - down;

        for (float t = 0f; t < down; t += Time.deltaTime)
        {
            float k = Smooth(t / down);
            tr.localScale = Vector3.Lerp(qScale, qScale * pressScale, k);
            qImage.color = Color.Lerp(qColor, dark, k);
            yield return null;
        }
        for (float t = 0f; t < up; t += Time.deltaTime)
        {
            float k = EaseOutBack(t / up, 2f);
            tr.localScale = Vector3.LerpUnclamped(qScale * pressScale, qScale, k);
            qImage.color = Color.Lerp(dark, qColor, Mathf.Clamp01(k));
            yield return null;
        }
        tr.localScale = qScale;
        qImage.color = qColor;
        pressRoutine = null;
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