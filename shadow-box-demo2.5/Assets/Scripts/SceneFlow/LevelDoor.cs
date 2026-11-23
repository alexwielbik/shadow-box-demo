using System.Collections;
using UnityEngine;

/// <summary>
/// A door to another scene through a gap in the ceiling, floor or a side wall.
///
/// Top door
///   Leaving:  jump up near the gap and the player is pulled up through it, then the scene changes.
///   Arriving: the player drops in from above the gap and gets control back once they land.
/// Bottom door
///   Leaving:  fall into the gap and the scene changes.
///   Arriving: the player pops up out of the gap and lands beside it, then gets control back.
/// Left / Right door
///   Leaving:  move into the opening toward the outside and the player keeps going out on their own
///             while the screen fades.
///   Arriving: the player comes in from outside the wall to Walk In Distance and gets control back.
///
/// The GameObject sits in the middle of the gap, ON the surface (ceiling underside, floor top, or the
/// inside face of the wall). RoomFrame creates and places these for you (Room/Doors/Door_&lt;id&gt;).
/// In a hand-built room like scene 1, add one yourself: empty GameObject + this component, positioned at the gap.
///
/// The link: a Top door with Target Scene "Level_02" and Target Door Id "Bottom" sends the player to the
/// door called "Bottom" in Level_02. That Bottom door's own Target Scene / Target Door Id lead back.
///
/// Unfinished doors: while Target Scene is empty the door builds a solid block across its opening at runtime
/// (Block When No Target), so the player can't leave the room through a gap that doesn't go anywhere yet.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class LevelDoor : MonoBehaviour
{
    // Top/Bottom keep their old values (0/1) so existing doors don't change when this enum grows
    public enum DoorSide { Top, Bottom, Left, Right }
    public enum LandSide { Left, Right }

    [Header("Link")]
    [Tooltip("This door's name in this scene. (If RoomFrame made this door, it copies the gap's Door Id here.)")]
    [SerializeField] private string doorId = "Top";
    [Tooltip("Scene to load when the player leaves through this door. Must be in File > Build Profiles > Scene List. " +
             "Empty = arrive-only door.")]
    [SerializeField] private string targetScene = "";
    [Tooltip("Door Id in the target scene where the player comes out.")]
    [SerializeField] private string targetDoorId = "Bottom";
    [Tooltip("While Target Scene is empty, put a solid block across the opening when the game runs, " +
             "so the player can't leave through a door that doesn't lead anywhere yet.")]
    [SerializeField] private bool blockWhenNoTarget = true;

    [Header("Shape (RoomFrame sets these)")]
    [SerializeField] private DoorSide side = DoorSide.Top;
    [Tooltip("Width of the opening between the colliders (its height, for Left/Right doors).")]
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

    [Header("Left / Right doors (walk through)")]
    [Tooltip("Sideways speed while walking out or in. 0 = the player's own Move Speed.")]
    [SerializeField, Min(0f)] private float walkSpeed = 0f;
    [Tooltip("Leaving: the player must be moving toward the outside at least this fast to be caught.")]
    [SerializeField, Min(0f)] private float minWalkOutSpeed = 0.5f;
    [Tooltip("Arriving: how far outside the wall the player starts (hidden behind the border).")]
    [SerializeField, Min(0.5f)] private float startOutside = 1.5f;
    [Tooltip("Arriving: how far inside the wall the player walks before getting control back.")]
    [SerializeField, Min(0.5f)] private float walkInDistance = 0.5f;
    [Tooltip("Arriving: pause after the scene appears before the player walks in.")]
    [SerializeField, Min(0f)] private float walkInDelay = 0.15f;

    [Header("Testing")]
    [Tooltip("Editor only: pressing Play in THIS scene starts the player at this door, as if they'd just come through it.")]
    [SerializeField] private bool arriveHereWhenTesting = false;

    private BoxCollider2D trigger;
    private Collider2D blocker;
    private bool leaving;

    public string DoorId => doorId;
    public DoorSide Side => side;

    private bool IsSideDoor => side == DoorSide.Left || side == DoorSide.Right;

    /// <summary>+1 for a Right door (outside is to the right), -1 for Left. 0 for Top/Bottom.</summary>
    private int Outward => side == DoorSide.Right ? 1 : side == DoorSide.Left ? -1 : 0;

    /// <summary>The door in the loaded scene(s) with this id, or null.</summary>
    public static LevelDoor Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (LevelDoor d in FindObjectsByType<LevelDoor>())
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

    // Top:    a box from pullReach below the ceiling to a little above it.
    // Bottom: a box down inside the floor gap, so walking along the floor next to it never triggers it.
    // Sides:  a box filling the opening, from just inside the wall face to well outside it.
    private void ApplyTriggerShape()
    {
        if (trigger == null) trigger = GetComponent<BoxCollider2D>();
        if (trigger == null) return;

        trigger.isTrigger = true;
        float w = Mathf.Max(0.2f, openingWidth - 0.2f);
        switch (side)
        {
            case DoorSide.Top:
                {
                    const float above = 1f;
                    trigger.size = new Vector2(w, pullReach + above);
                    trigger.offset = new Vector2(0f, (above - pullReach) * 0.5f);
                    break;
                }
            case DoorSide.Bottom:
                {
                    const float top = -0.4f, bottom = -2.5f;
                    trigger.size = new Vector2(w, top - bottom);
                    trigger.offset = new Vector2(0f, (top + bottom) * 0.5f);
                    break;
                }
            default:
                {
                    const float inside = 0.3f, outside = 2f;
                    trigger.size = new Vector2(inside + outside, w);
                    trigger.offset = new Vector2(Outward * (outside - inside) * 0.5f, 0f);
                    break;
                }
        }
    }

    private void Awake()
    {
        trigger = GetComponent<BoxCollider2D>();
        ApplyTriggerShape();
        if (blockWhenNoTarget && string.IsNullOrEmpty(targetScene)) BuildBlocker();
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

    // ------------------------------------------------------------------ unfinished doors

    // A solid box just outside the opening, the same thickness as the room's barrier,
    // so the wall/ceiling/floor reads as closed until this door has somewhere to go.
    private void BuildBlocker()
    {
        RoomFrame frame = GetComponentInParent<RoomFrame>();
        RoomFrameStyle style = frame != null ? frame.Style : null;

        int layer = LayerMask.NameToLayer(style != null ? style.groundLayerName : "Ground");
        float thickness = style != null ? style.barrierThickness : 1f;
        float along = openingWidth + 0.1f; // overlap the barrier pieces on both sides a little

        var go = new GameObject("Blocker (no Target Scene)") { layer = layer >= 0 ? layer : 0 };
        go.transform.SetParent(transform, false);
        var box = go.AddComponent<BoxCollider2D>();
        if (style != null) box.sharedMaterial = style.barrierMaterial;

        switch (side)
        {
            case DoorSide.Top: box.size = new Vector2(along, thickness); box.offset = new Vector2(0f, thickness * 0.5f); break;
            case DoorSide.Bottom: box.size = new Vector2(along, thickness); box.offset = new Vector2(0f, -thickness * 0.5f); break;
            default: box.size = new Vector2(thickness, along); box.offset = new Vector2(Outward * thickness * 0.5f, 0f); break;
        }
        blocker = box;
    }

    // ------------------------------------------------------------------ leaving

    private void OnTriggerStay2D(Collider2D other)
    {
        if (leaving || SceneTransition.IsBusy || string.IsNullOrEmpty(targetScene)) return;

        Rigidbody2D body = other.attachedRigidbody;
        PlayerController player = body != null ? body.GetComponent<PlayerController>() : null;
        if (player == null || player.ControlLocked || body.bodyType != RigidbodyType2D.Dynamic) return;

        Vector2 v = body.linearVelocity;
        switch (side)
        {
            case DoorSide.Top:
                if (v.y >= minRiseSpeed) StartCoroutine(PullUp(player, v.y));
                break;
            case DoorSide.Bottom:
                if (v.y <= 0.1f) StartCoroutine(FallIn(player));
                break;
            default:
                if (v.x * Outward >= minWalkOutSpeed) StartCoroutine(WalkOut(player));
                break;
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

    // Input off but physics still on: the player keeps rolling outward at a steady speed
    // (and falls normally if they jumped in), while the screen fades out over it.
    private IEnumerator WalkOut(PlayerController player)
    {
        leaving = true;
        int dir = Outward;
        float speed = WalkSpeedFor(player);
        Rigidbody2D rb = player.Body;

        player.SetControlLocked(true);
        player.Face(dir);
        SceneTransition.Go(targetScene, targetDoorId);

        // Runs until the new scene replaces this one (which destroys this door and stops the coroutine)
        do
        {
            Vector2 v = rb.linearVelocity;
            v.x = dir * speed;
            rb.linearVelocity = v;
            yield return new WaitForFixedUpdate();
        } while (SceneTransition.IsBusy);

        // Still here: the scene failed to load (SceneTransition already logged why). Hand control back.
        player.SetControlLocked(false);
        leaving = false;
    }

    // ------------------------------------------------------------------ arriving

    /// <summary>Bring the player in through this door (SceneTransition calls this after loading).</summary>
    public void BeginEntry(PlayerController player)
    {
        StopAllCoroutines();
        leaving = false;

        IEnumerator entry = side switch
        {
            DoorSide.Bottom => PopUp(player),
            DoorSide.Top => DropIn(player),
            _ => WalkIn(player),
        };
        StartCoroutine(Enter(entry));
    }

    // Opens an arrive-only door's blocker while the player comes through it, then closes it again
    private IEnumerator Enter(IEnumerator entry)
    {
        if (blocker != null) blocker.enabled = false;
        yield return StartCoroutine(entry);
        if (blocker != null) blocker.enabled = true;
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

    // Kinematic (passes through the wall) and moved by its velocity, so the roll animation
    // plays at the right speed. Starts outside the wall at sill height and rolls in.
    private IEnumerator WalkIn(PlayerController player)
    {
        Rigidbody2D rb = player.Body;
        Vector2 door = transform.position;
        int inward = -Outward;
        float speed = WalkSpeedFor(player);
        float bottom = player.BottomOffset;

        player.BeginScripted();

        // Ground height at the doorway: probe straight down just inside the wall
        float half = openingWidth * 0.5f;
        float y = door.y - half + bottom; // fallback: bottom of the opening
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(door.x + inward * 0.5f, door.y + half - 0.1f),
                                             Vector2.down, openingWidth + 3f, player.GroundLayers);
        if (hit.collider != null) y = hit.point.y + bottom;

        var start = new Vector2(door.x - inward * startOutside, y);
        Teleport(player, start);
        player.Face(inward);
        SnapCamera();

        if (walkInDelay > 0f) yield return new WaitForSeconds(walkInDelay);

        float duration = (startOutside + walkInDistance) / speed;
        rb.linearVelocity = new Vector2(inward * speed, 0f);
        for (float t = 0f; t < duration; t += Time.fixedDeltaTime)
            yield return new WaitForFixedUpdate();

        // Back to normal physics, still moving: with no input the player eases to a stop by themselves
        player.EndScripted();
        rb.linearVelocity = new Vector2(inward * speed, 0f);
    }

    private float WalkSpeedFor(PlayerController player) =>
        walkSpeed > 0f ? walkSpeed : Mathf.Max(1f, player.MoveSpeed);

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

        // Orange = working door, red = no Target Scene yet (blocked when Block When No Target is on)
        bool unfinished = string.IsNullOrEmpty(targetScene);
        Gizmos.color = unfinished ? new Color(1f, 0.25f, 0.2f, 0.9f) : new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(transform.position + (Vector3)box.offset, box.size);

        if (side == DoorSide.Bottom)
        {
            // Where the player lands when arriving
            int dir = landSide == LandSide.Right ? 1 : -1;
            Vector3 land = transform.position + new Vector3(dir * landDistance, 0.6f, 0f);
            Gizmos.DrawWireSphere(land, 0.5f);
            Gizmos.DrawLine(transform.position, land);
        }
        else if (IsSideDoor)
        {
            // Roughly where the player stops after walking in (should sit on the floor)
            Vector3 stop = transform.position + new Vector3(-Outward * walkInDistance, -openingWidth * 0.5f + 0.6f, 0f);
            Gizmos.DrawWireSphere(stop, 0.5f);
            Gizmos.DrawLine(transform.position, stop);
        }
    }
}