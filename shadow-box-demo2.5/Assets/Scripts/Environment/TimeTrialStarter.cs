using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The object Player 1 presses Interact (E) at to start a coin time trial.
/// - Shows its ProximityPrompt while the player is in its trigger and the trial can be started.
/// - On Interact: arms every TrialCoin under Coins Root and starts the BurningPaperTimer.
/// - All coins collected before the paper burns → success (onSucceeded).
/// - Paper burns up first → fail (onFailed); coins reset and the trial can be started again.
///
/// Needs a trigger Collider2D on this GameObject (the "range"), like LeverSwitch.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TimeTrialStarter : MonoBehaviour, IInteractable
{
    [Header("Parts")]
    [Tooltip("The floating E hint. Usually a child of this object.")]
    [SerializeField] private ProximityPrompt prompt;
    [Tooltip("The burning paper in the HUD. Leave empty to find the one in the scene (UI prefab).")]
    [SerializeField] private BurningPaperTimer timer;
    [Tooltip("Parent of this trial's coins. Must NOT be this object or a child of it.")]
    [SerializeField] private Transform coinsRoot;

    [Header("Rules")]
    [Tooltip("Seconds the paper takes to burn for this trial.")]
    [SerializeField, Min(0.5f)] private float timeLimit = 20f;
    [Tooltip("How visible the coins are while no run is going. 0 = hidden, 1 = fully visible.")]
    [SerializeField, Range(0f, 1f)] private float idleCoinAlpha = 0.35f;
    [Tooltip("Allow running the trial again after winning it.")]
    [SerializeField] private bool repeatableAfterWin = false;

    [Header("Events")]
    public UnityEvent onStarted;
    public UnityEvent onSucceeded;
    public UnityEvent onFailed;
    [Tooltip("Collected count, total coins. Handy for a counter or a sound.")]
    public UnityEvent<int, int> onCoinCollected;

    private TrialCoin[] coins = new TrialCoin[0];
    private int collectedCount;
    private bool running;
    private bool completed;
    private int playerContacts;   // player colliders currently inside our trigger

    public bool IsRunning => running;
    public bool IsCompleted => completed;

    public bool CanInteract => !running && (!completed || repeatableAfterWin) && timer != null;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;

        if (timer == null)
            timer = FindAnyObjectByType<BurningPaperTimer>(FindObjectsInactive.Include);
        if (timer == null)
            Debug.LogWarning($"{name}: no BurningPaperTimer found. Add TimeTrialTimer to the UI.", this);

        if (coinsRoot != null)
        {
            if (coinsRoot == transform || coinsRoot.IsChildOf(transform))
                Debug.LogWarning($"{name}: Coins Root is under the starter, so touching a coin would count as " +
                                 "standing at the starter. Move the coins to a sibling object.", this);
            coins = coinsRoot.GetComponentsInChildren<TrialCoin>(true);
        }
        if (coins.Length == 0)
            Debug.LogWarning($"{name}: no TrialCoins found under Coins Root.", this);
    }

    private void OnEnable()
    {
        foreach (var c in coins) c.Collected += OnCoinCollected;
        if (timer != null) timer.onTimeUp.AddListener(OnTimeUp);
    }

    private void OnDisable()
    {
        foreach (var c in coins) c.Collected -= OnCoinCollected;
        if (timer != null) timer.onTimeUp.RemoveListener(OnTimeUp);
        playerContacts = 0;
    }

    private void Start()
    {
        foreach (var c in coins) c.Disarm(idleCoinAlpha);
        if (prompt != null) prompt.HideInstant();
    }

    // ---------------- IInteractable ----------------

    public void Interact(GameObject interactor)
    {
        if (!CanInteract) return;
        StartTrial();
    }

    // ---------------- Trial flow ----------------

    public void StartTrial()
    {
        running = true;
        collectedCount = 0;

        foreach (var c in coins) c.Arm();

        timer.Duration = timeLimit;
        timer.StartTimer();

        RefreshPrompt();
        onStarted?.Invoke();

        if (coins.Length == 0) Succeed();
    }

    private void OnCoinCollected(TrialCoin coin)
    {
        if (!running) return;
        collectedCount++;
        onCoinCollected?.Invoke(collectedCount, coins.Length);

        if (collectedCount >= coins.Length) Succeed();
    }

    private void Succeed()
    {
        running = false;
        completed = true;
        timer.StopTimer();

        // Winning and allowed again → put the faded coins back so the next run has something to collect
        if (repeatableAfterWin)
            foreach (var c in coins) c.Disarm(idleCoinAlpha);

        RefreshPrompt();
        onSucceeded?.Invoke();
    }

    private void OnTimeUp()
    {
        if (!running) return;
        running = false;

        foreach (var c in coins) c.Disarm(idleCoinAlpha);   // collected coins come back for the retry

        RefreshPrompt();
        onFailed?.Invoke();
    }

    // ---------------- Prompt ----------------

    private void RefreshPrompt()
    {
        if (prompt == null) return;
        if (playerContacts > 0 && CanInteract) prompt.Show();
        else prompt.Hide();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        playerContacts++;
        RefreshPrompt();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        playerContacts = Mathf.Max(0, playerContacts - 1);
        RefreshPrompt();
    }
}