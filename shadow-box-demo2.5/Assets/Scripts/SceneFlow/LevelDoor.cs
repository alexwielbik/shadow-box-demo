using System.Collections;
using UnityEngine;

/// <summary>
/// A door to another scene through a gap in the ceiling or floor.
///
/// Top door
///   Leaving:  jump up near the gap and the player is pulled up through it, then the scene changes.
///   Arriving: the player drops in from above the gap and gets control back once they land.
/// Bottom door
///   Leaving:  fall into the gap and the scene changes.
///   Arriving: the player pops up out of the gap and lands beside it, then gets control back.
///
/// The GameObject sits in the middle of the gap, ON the surface (the ceiling's underside or the floor's top).
/// RoomFrame creates and places these for you (Room/Doors/Door_&lt;id&gt;). In a hand-built room like
/// scene 1, add one yourself: empty GameObject + this component, positioned at the gap.
///
/// The link: a Top door with Target Scene "Level_02" and Target Door Id "Bottom" sends the player to the
/// door called "Bottom" in Level_02. That Bottom door's own Target Scene / Target Door Id lead back.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class LevelDoor : MonoBehaviour
{
    public enum DoorSide { Top, Bottom }
    public enum LandSide { Left, Right }

    [Header("Link")]
    [Tooltip("This door's name in this scene. (If RoomFrame made this door, it copies the gap's Door Id here.)")]
    [SerializeField] private string doorId = "Top";
    [Tooltip("Scene to load when the player leaves through this door. Must be in File > Build Profiles > Scene List. " +
             "Empty = arrive-only door.")]
    [SerializeField] private string targetScene = "";
    [Tooltip("Door Id in the target scene where the player comes out.")]
    [SerializeField] private string targetDoorId = "Bottom";

    [Header("Shape (RoomFrame sets these)")]
    [SerializeField] private DoorSide side = DoorSide.Top;
    [Tooltip("Width of the opening between the colliders.")]
    [SerializeField, Min(0.5f)] private float openingWidth = 3.5f;

    [Header("Leaving through a Top door (pull up)")]
    [Tooltip("How far below the ceiling a jump still gets caught by the pull.")]
    [SerializeField, Min(0.1f)] private float pullReach = 2f;
    [Tooltip("The player must be moving up at least this fast (i.e. jumping) to be caught.")]
    [SerializeField, Min(0f)] private float minRiseSpeed = 0.5f;
    [Tooltip("Seconds from being caught to vanishing through the gap.")]
    [SerializeField, Min(0.1f)] private float pullDuration = 0.5f;
    [Tooltip("How far past the ceiling the player is pulled (needs to get them off screen).")]
    [SerializeField, Min(0.5f)] private float pullHeightAbove = 3f;

    [Header("Leaving through a Bottom door (fall in)")]
    [Tooltip("Seconds of falling before the fade starts.")]
    [SerializeField, Min(0f)] private float fallTimeBeforeFade = 0.2f;

    [Header("Arriving through a Bottom door (pop up)")]
    [SerializeField] private LandSide landSide = LandSide.Right;
    [Tooltip("Where the player lands, measured sideways from the middle of the gap.")]
    [SerializeField, Min(0.5f)] private float landDistance = 3f;
    [Tooltip("How high above the floor the pop reaches.")]
    [SerializeField, Min(0.5f)] private float popHeight = 3f;
    [Tooltip("Gravity of the pop arc. Higher = snappier.")]
    [SerializeField, Min(1f)] private float popGravity = 40f;
    [Tooltip("How far below the floor surface the player starts (hidden behind the border).")]
    [SerializeField, Min(0.5f)] private float startDepth = 2f;
    [Tooltip("Pause after the scene appears before the player pops out.")]
    [SerializeField, Min(0f)] private float popDelay = 0.2f;

    [Header("Arriving through a Top door (drop in)")]
    [Tooltip("How far above the ceiling the player starts.")]
    [SerializeField, Min(0.5f)] private float dropStartHeight = 2f;
    [Tooltip("Give control back after this long even if the player hasn't landed.")]
    [SerializeField, Min(0.5f)] private float dropTimeout = 4f;

    [Header("Testing")]
    [Tooltip("Editor only: pressing Play in THIS scene starts the player at this door, as if they'd just come through it.")]
    [SerializeField] private bool arriveHereWhenTesting = false;

    private BoxCollider2D trigger;
    private bool leaving;

    public string DoorId => doorId;
    public DoorSide Side => side;

    /// <summary>The door in the loaded scene(s) with this id, or null.</summary>
    public static LevelDoor Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (LevelDoor d in FindObjectsByType<LevelDoor>(FindObjectsSortMode.None))
            if (d.doorId == id) return d;
        return null;
    }

    /// <summary>Called by RoomFrame when it builds or moves this door's gap.</summary>
    public void ConfigureFromFrame(DoorSide newSide, float newOpeningWidth, string newDoorId)
    {
        side = newSide;
        openingWidth = newOpeningWidth;
        doorId = newDoorId;
        ApplyTriggerShape();
    }

    private void Reset() => ApplyTriggerShape();
    private void OnValidate() => ApplyTriggerShape();

    // Top: a box from pullReach below the ceiling to a little above it.
    // Bottom: a box down inside the floor gap, so walking along the floor next to it never triggers it.
    private void ApplyTriggerShape()
    {
        if (trigger == null) trigger = GetComponent<BoxCollider2D>();
        if (trigger == null) return;

        trigger.isTrigger = true;
        float w = Mathf.Max(0.2f, openingWidth - 0.2f);
        if (side == DoorSide.Top)
        {
            const float above = 1f;
            trigger.size = new Vector2(w, pullReach + above);
            trigger.offset = new Vector2(0f, (above - pullReach) * 0.5f);
        }
        else
        {
            const float top = -0.4f, bottom = -2.5f;
            trigger.size = new Vector2(w, top - bottom);
            trigger.offset = new Vector2(0f, (top + bottom) * 0.5f);
        }
    }

    private void Awake()
    {
        trigger = GetComponent<BoxCollider2D>();
        ApplyTriggerShape();
    }

    private void Start()
    {
#if UNITY_EDITOR
        if (arriveHereWhenTesting && !SceneTransition.IsBusy)
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null) BeginEntry(player);
        }
#endif
    }

    // ------------------------------------------------------------------ leaving

    private void OnTriggerStay2D(Collider2D other)
    {
        if (leaving || SceneTransition.IsBusy || string.IsNullOrEmpty(targetScene)) return;

        Rigidbody2D body = other.attachedRigidbody;
        PlayerController player = body != null ? body.GetComponent<PlayerController>() : null;
        if (player == null || player.ControlLocked || body.bodyType != RigidbodyType2D.Dynamic) return;

        float vy = body.linearVelocity.y;
        if (side == DoorSide.Top)
        {
            if (vy >= minRiseSpeed) StartCoroutine(PullUp(player, vy));
        }
        else
        {
            if (vy <= 0.1f) StartCoroutine(FallIn(player));
        }
    }

    private IEnumerator PullUp(PlayerController player, float startRiseSpeed)
    {
        leaving = true;
        Rigidbody2D rb = player.Body;
        Vector2 gap = transform.position;
        Vector2 start = rb.position;
        player.BeginScripted();

        // y(t) = start + v0*t + a*t^2/2, chosen to arrive at the end height at exactly pullDuration.
        // Starting with the jump's own speed means no hitch; a > 0 makes it feel like a pull that speeds up.
        float T = pullDuration;
        float endY = gap.y + pullHeightAbove;
        float dy = Mathf.Max(0.1f, endY - start.y);
        float v0 = Mathf.Min(startRiseSpeed, dy / T);
        float a = 2f * (dy - v0 * T) / (T * T);

        for (float t = 0f; t < T;)
        {
            yield return new WaitForFixedUpdate();
            t = Mathf.Min(t + Time.fixedDeltaTime, T);

            // Center on the gap in the first 60% so the player never clips the gap's edges
            float u = Mathf.Clamp01(t / (T * 0.6f));
            u = 1f - (1f - u) * (1f - u);
            float x = Mathf.Lerp(start.x, gap.x, u);
            float y = start.y + v0 * t + 0.5f * a * t * t;
            rb.MovePosition(new Vector2(x, y));
        }

        SceneTransition.Go(targetScene, targetDoorId);
    }

    private IEnumerator FallIn(PlayerController player)
    {
        leaving = true;
        player.SetControlLocked(true); // keep falling under normal gravity, just no input
        if (fallTimeBeforeFade > 0f) yield return new WaitForSeconds(fallTimeBeforeFade);
        SceneTransition.Go(targetScene, targetDoorId);
    }

    // ------------------------------------------------------------------ arriving

    /// <summary>Bring the player in through this door (SceneTransition calls this after loading).</summary>
    public void BeginEntry(PlayerController player)
    {
        StopAllCoroutines();
        leaving = false;
        StartCoroutine(side == DoorSide.Bottom ? PopUp(player) : DropIn(player));
    }

    private IEnumerator PopUp(PlayerController player)
    {
        Rigidbody2D rb = player.Body;
        Vector2 gap = transform.position;
        float floorTop = gap.y;
        float bottom = player.BottomOffset; // pivot height above the bottom of the collider
        int dir = landSide == LandSide.Right ? 1 : -1;

        player.BeginScripted();
        var start = new Vector2(gap.x, floorTop - startDepth);
        Teleport(player, start);
        player.Face(dir);
        SnapCamera();

        if (popDelay > 0f) yield return new WaitForSeconds(popDelay);

        // Find the ground where we'll land (falls back to the floor height)
        float landX = gap.x + dir * landDistance;
        float landY = floorTop + bottom;
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(landX, floorTop + popHeight + 1f), Vector2.down,
                                             popHeight + 3f, player.GroundLayers);
        if (hit.collider != null) landY = hit.point.y + bottom;

        // A simple projectile arc: straight up through the gap, then over to the landing spot
        float g = popGravity;
        float apex = Mathf.Max(floorTop + bottom + popHeight, landY + 0.5f);
        float tUp = Mathf.Sqrt(2f * (apex - start.y) / g);
        float tDown = Mathf.Sqrt(2f * (apex - landY) / g);
        float total = tUp + tDown;
        float v0 = g * tUp;

        // Moment the player's bottom clears the floor; sideways movement starts only after that
        float clearY = floorTop + bottom;
        float disc = v0 * v0 - 2f * g * (clearY - start.y);
        float tClear = disc > 0f ? (v0 - Mathf.Sqrt(disc)) / g : tUp;
        tClear = Mathf.Min(tClear, total * 0.9f);

        for (float t = 0f; t < total;)
        {
            yield return new WaitForFixedUpdate();
            t = Mathf.Min(t + Time.fixedDeltaTime, total);

            float y = start.y + v0 * t - 0.5f * g * t * t;
            float side01 = t <= tClear ? 0f : (t - tClear) / (total - tClear);
            float x = Mathf.Lerp(gap.x, landX, side01);
            rb.MovePosition(new Vector2(x, y));
        }

        yield return new WaitForFixedUpdate();
        player.EndScripted();
    }

    private IEnumerator DropIn(PlayerController player)
    {
        Vector2 gap = transform.position;

        player.EndScripted(unlock: false);  // normal physics, no input
        player.SetControlLocked(true);
        Teleport(player, new Vector2(gap.x, gap.y + dropStartHeight));
        player.Body.linearVelocity = new Vector2(0f, -2f);
        SnapCamera();

        // Give the ground check a couple of steps to notice we're in the air
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        for (float t = 0f; t < dropTimeout && !player.IsGrounded; t += Time.fixedDeltaTime)
            yield return new WaitForFixedUpdate();

        player.SetControlLocked(false);
    }

    private static void Teleport(PlayerController player, Vector2 position)
    {
        Transform tr = player.transform;
        tr.position = new Vector3(position.x, position.y, tr.position.z);
        player.Body.position = position;
        player.Body.linearVelocity = Vector2.zero;
    }

    private static void SnapCamera()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.TryGetComponent(out CameraFollow follow))
            follow.SnapToTarget();
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmos()
    {
        BoxCollider2D box = trigger != null ? trigger : GetComponent<BoxCollider2D>();
        if (box == null) return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(transform.position + (Vector3)box.offset, box.size);

        if (side == DoorSide.Bottom)
        {
            // Where the player lands when arriving
            int dir = landSide == LandSide.Right ? 1 : -1;
            Vector3 land = transform.position + new Vector3(dir * landDistance, 0.6f, 0f);
            Gizmos.DrawWireSphere(land, 0.5f);
            Gizmos.DrawLine(transform.position, land);
        }
    }
}
