using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A coin for a TimeTrialStarter. The trial arms it when the run starts;
/// Player 1 touching an armed coin collects it (pop + fade) and tells the trial.
///
/// Needs a trigger Collider2D (CircleCollider2D) on the same GameObject.
/// Do NOT put coins under the TimeTrialStarter object: PlayerInteractor looks for an
/// IInteractable in the parents of any trigger it touches, so a coin under the starter
/// would let the player start the trial from that coin.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TrialCoin : MonoBehaviour
{
    [Header("Idle")]
    [SerializeField, Min(0f)] private float bobHeight = 0.1f;
    [SerializeField, Min(0.1f)] private float bobPeriod = 1.2f;

    [Header("Collect")]
    [SerializeField, Min(0.01f)] private float collectTime = 0.25f;
    [Tooltip("How far it floats up while popping (world units).")]
    [SerializeField] private float collectRise = 0.4f;
    [SerializeField, Min(1f)] private float collectSwell = 1.4f;

    /// <summary>Fired once each time the coin is collected during a run.</summary>
    public event Action<TrialCoin> Collected;

    private Collider2D col;
    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Vector3 homePosition;
    private Vector3 baseScale;
    private float bobTime;

    private bool armed;
    private bool collected;
    private bool collecting;
    private Coroutine routine;

    public bool IsCollected => collected;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        col.isTrigger = true;

        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;

        homePosition = transform.localPosition;
        baseScale = transform.localScale;
        bobTime = UnityEngine.Random.value * bobPeriod;   // so the coins don't bob in lockstep
    }

    /// <summary>Make the coin collectable (full opacity).</summary>
    public void Arm()
    {
        StopRoutine();
        armed = true;
        collected = false;
        ResetTransform();
        SetAlpha(1f);
        col.enabled = true;   // re-enabling fires OnTriggerEnter2D if the player is already on it
    }

    /// <summary>Make the coin not collectable. idleAlpha 0 hides it; e.g. 0.35 shows a faded "preview".</summary>
    public void Disarm(float idleAlpha)
    {
        StopRoutine();
        armed = false;
        collected = false;
        col.enabled = false;
        ResetTransform();
        SetAlpha(idleAlpha);
    }

    private void Update()
    {
        if (collecting) return;
        bobTime += Time.deltaTime;
        float y = Mathf.Sin(bobTime / bobPeriod * Mathf.PI * 2f) * bobHeight;
        transform.localPosition = homePosition + Vector3.up * y;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!armed || collected) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;   // Player 1 only

        collected = true;
        armed = false;
        col.enabled = false;

        Collected?.Invoke(this);

        StopRoutine();
        routine = StartCoroutine(CollectRoutine());
    }

    private IEnumerator CollectRoutine()
    {
        collecting = true;
        Vector3 start = transform.localPosition;

        for (float t = 0f; t < collectTime; t += Time.deltaTime)
        {
            float p = t / collectTime;
            transform.localPosition = start + Vector3.up * (collectRise * p);
            transform.localScale = baseScale * Mathf.Lerp(1f, collectSwell, Mathf.Sin(p * Mathf.PI * 0.5f));
            SetAlpha(1f - p * p);
            yield return null;
        }

        SetAlpha(0f);
        collecting = false;
        routine = null;
    }

    private void ResetTransform()
    {
        collecting = false;
        transform.localPosition = homePosition;
        transform.localScale = baseScale;
    }

    private void SetAlpha(float a)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Color c = baseColors[i];
            c.a *= a;
            renderers[i].color = c;
            renderers[i].enabled = a > 0.001f;
        }
    }

    private void StopRoutine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }
}