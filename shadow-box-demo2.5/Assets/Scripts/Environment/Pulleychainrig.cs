using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A pulley wheel with a chain draped over it and a counterweight on each end.
/// While the switch of its color is on, Player 2's held left / right input spins the wheel,
/// feeding chain from one side to the other so one weight rises and the other drops.
/// The hanging chain is a small rope simulation, so the weights sway and settle on their own.
///
/// Reads input from SwitchChannels (written by Player2Controller), exactly like Player2Rotatable.
/// Left (+1) spins the wheel counter-clockwise: chain feeds down the LEFT side, left weight drops.
///
/// Hierarchy:
///   PulleyRig        (this script, at the wheel's axle, scale 1)
///     Wheel          (SpriteRenderer: Pulley)
///     Weight_Left    (SpriteRenderer: CounterWeight)
///     Weight_Right   (SpriteRenderer: CounterWeight)
/// Chain links are spawned at runtime as children of PulleyRig.
/// </summary>
public class PulleyChainRig : MonoBehaviour
{
    [Header("Switch")]
    [Tooltip("Which switch color lets Player 2 move this pulley.")]
    [SerializeField] private SwitchColor switchColor = SwitchColor.Grey;
    [Tooltip("Flip which button lowers which weight.")]
    [SerializeField] private bool invert = false;

    [Header("Parts")]
    [SerializeField] private SpriteRenderer wheel;
    [SerializeField] private Transform weightLeft;
    [SerializeField] private Transform weightRight;

    [Header("Chain Links")]
    [Tooltip("Link seen face-on (ChainLinkWide).")]
    [SerializeField] private Sprite linkFace;
    [Tooltip("Link seen edge-on (ChainLinkSkinn).")]
    [SerializeField] private Sprite linkEdge;
    [Tooltip("Scale applied to every link.")]
    [SerializeField] private float linkScale = 1f;
    [Tooltip("Distance between link centers. 0 = automatic (about 65% of a link's height, so they interlock).")]
    [SerializeField] private float linkSpacing = 0f;
    [Tooltip("Draw links behind the wheel so the chain looks like it sits in the wheel's groove.")]
    [SerializeField] private bool linksBehindWheel = true;

    [Header("Dimensions (world units, measured from this object)")]
    [Tooltip("Where the chain rides on the wheel. The yellow circle in the Scene view shows it.")]
    [SerializeField] private float wheelRadius = 1.4f;
    [Tooltip("Length of each hanging side when the weights are level.")]
    [SerializeField] private float restLength = 4f;
    [Tooltip("Shortest a side's chain may get. Keeps the raised weight from running into the wheel. " +
             "The red tick in the Scene view shows how high the chain end can go.")]
    [SerializeField, Min(0.1f)] private float minChainLength = 2.2f;
    [Tooltip("How far one weight can rise (and the other drop) from level. " +
             "Can't exceed Rest Length - Min Chain Length.")]
    [SerializeField] private float maxTravel = 2f;
    [Tooltip("Starting offset. Positive = left weight starts lower.")]
    [SerializeField] private float startTravel = 0f;

    [Header("Weights")]
    [Tooltip("Hang each weight by the top of its sprite instead of its pivot.")]
    [SerializeField] private bool hookAtSpriteTop = true;
    [Tooltip("Pulls the chain down into the weight's hook loop.")]
    [SerializeField] private float hookInset = 0.1f;

    [Header("Movement")]
    [Tooltip("Chain speed in units per second at full input.")]
    [SerializeField, Min(0f)] private float maxSpeed = 1.5f;
    [Tooltip("How quickly the wheel spins up and down. Lower = heavier.")]
    [SerializeField, Min(0.01f)] private float acceleration = 5f;
    [Tooltip("Ease into the ends of the range instead of stopping dead (which causes a big jolt).")]
    [SerializeField] private bool softStop = true;

    [Header("Sway")]
    [Tooltip("How much of each change in chain speed turns into sideways swing. 0 = no sway.")]
    [SerializeField, Range(0f, 2f)] private float swayAmount = 0.6f;
    [Tooltip("Hard limit on how far the weights can swing, in degrees from straight down.")]
    [SerializeField, Range(0f, 45f)] private float maxSwingAngle = 8f;
    [Tooltip("Seconds for a swing (or bounce) to mostly die out. Lower = settles faster.")]
    [SerializeField, Min(0.1f)] private float settleTime = 3f;
    [Tooltip("How heavy a weight is compared to one piece of chain.")]
    [SerializeField, Min(1f)] private float weightMassRatio = 10f;
    [SerializeField, Range(4, 30)] private int segments = 12;
    [SerializeField, Range(4, 40)] private int iterations = 20;

    /// <summary>Current offset from level. Positive = left weight lower. Read this for puzzles.</summary>
    public float Travel => travel;
    /// <summary>Travel remapped to 0 (right weight fully down) ... 1 (left weight fully down).</summary>
    public float Travel01 => Mathf.InverseLerp(-maxTravel, maxTravel, travel);

    private float travel, prevTravel, speed;
    private Vector2[] ropeL, oldL, ropeR, oldR;   // current step and Verlet history
    private Vector2[] snapL, snapR;                // state at the previous step, for smooth rendering
    private Vector2[] drawL, drawR;                // interpolated state used for drawing
    private float[] invMass;
    private Transform[] links;
    private float spacing, hookLeft, hookRight;
    private float weightZLeft, weightZRight;
    private Quaternion wheelBaseRotation;
    private Rigidbody2D rbLeft, rbRight;

    private void Start()
    {
        travel = prevTravel = Mathf.Clamp(startTravel, -maxTravel, maxTravel);

        ropeL = BuildRope(-1, restLength + travel);
        ropeR = BuildRope(+1, restLength - travel);
        oldL = (Vector2[])ropeL.Clone(); snapL = (Vector2[])ropeL.Clone(); drawL = (Vector2[])ropeL.Clone();
        oldR = (Vector2[])ropeR.Clone(); snapR = (Vector2[])ropeR.Clone(); drawR = (Vector2[])ropeR.Clone();

        invMass = new float[segments + 1];
        for (int i = 0; i < invMass.Length; i++) invMass[i] = 1f;
        invMass[0] = 0f;                          // pinned to the wheel
        invMass[segments] = 1f / weightMassRatio; // the weight

        if (wheel) wheelBaseRotation = wheel.transform.localRotation;

        hookLeft = HookOffset(weightLeft);
        hookRight = HookOffset(weightRight);
        if (weightLeft) weightZLeft = weightLeft.localPosition.z;
        if (weightRight) weightZRight = weightRight.localPosition.z;

        // Weights that Player 1 can stand on need a kinematic Rigidbody2D + collider
        rbLeft = SetUpBody(weightLeft);
        rbRight = SetUpBody(weightRight);

        EnsureSortingGroup();
        SpawnLinks();
    }

    private Vector2[] BuildRope(int side, float length)
    {
        var r = new Vector2[segments + 1];
        float seg = length / segments;
        for (int i = 0; i <= segments; i++) r[i] = new Vector2(side * wheelRadius, -i * seg);
        return r;
    }

    private float HookOffset(Transform weight)
    {
        if (!weight || !hookAtSpriteTop) return -hookInset;
        var sr = weight.GetComponent<SpriteRenderer>();
        float top = sr && sr.sprite ? sr.sprite.bounds.max.y * weight.localScale.y : 0f;
        return top - hookInset;
    }

    private static Rigidbody2D SetUpBody(Transform weight)
    {
        if (!weight) return null;
        var rb = weight.GetComponent<Rigidbody2D>();
        if (rb)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
        return rb;
    }

    // The links use sortingOrder (wheel - 1) to tuck behind the wheel. Without a
    // Sorting Group that -1 is compared against the WHOLE scene, so every other
    // transparent sprite at order 0 (e.g. the stained glass backs) draws on top of
    // the links and tints them. A Sorting Group on the rig makes the rig sort as one
    // unit by distance, and the -1 only matters inside the rig.
    private void EnsureSortingGroup()
    {
        var group = GetComponent<SortingGroup>();
        if (!group)
        {
            group = gameObject.AddComponent<SortingGroup>();
            if (wheel)
            {
                group.sortingLayerID = wheel.sortingLayerID;
                group.sortingOrder = wheel.sortingOrder;
            }
        }
    }

    private void SpawnLinks()
    {
        if (!linkFace) { Debug.LogWarning($"{name}: no chain link sprite assigned.", this); return; }

        float linkHeight = linkFace.bounds.size.y * linkScale;
        float targetSpacing = linkSpacing > 0f ? linkSpacing : linkHeight * 0.65f;

        // Use a whole number of links and spread them evenly over the full chain,
        // so both weights get the same half-link at their hook (no leftover gap on the right).
        float totalLength = restLength * 2f + Mathf.PI * wheelRadius;
        int count = Mathf.Max(1, Mathf.RoundToInt(totalLength / targetSpacing));
        spacing = totalLength / count;
        links = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Link_{i:00}");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * linkScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = (i % 2 == 1 && linkEdge) ? linkEdge : linkFace;

            // Copy the wheel's look so links match its material, lighting and shadows
            if (wheel)
            {
                sr.sharedMaterial = wheel.sharedMaterial;
                sr.sortingLayerID = wheel.sortingLayerID;
                sr.sortingOrder = wheel.sortingOrder + (linksBehindWheel ? -1 : 1);
                sr.shadowCastingMode = wheel.shadowCastingMode;
                sr.receiveShadows = wheel.receiveShadows;
                sr.color = wheel.color;
            }
            links[i] = go.transform;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Remember this step's starting state so LateUpdate can blend smoothly between steps
        System.Array.Copy(ropeL, snapL, ropeL.Length);
        System.Array.Copy(ropeR, snapR, ropeR.Length);
        prevTravel = travel;

        // 1. Wheel drive. GetHeldInput is already 0 whenever the switch is off.
        int held = SwitchChannels.GetHeldInput(switchColor);
        if (invert) held = -held;

        float prevSpeed = speed;
        speed = Mathf.MoveTowards(speed, held * maxSpeed, acceleration * dt);

        if (softStop)
        {
            // Never go faster than what can still brake to zero before the end of the range
            float room = speed > 0f ? maxTravel - travel : maxTravel + travel;
            float limit = Mathf.Sqrt(2f * acceleration * Mathf.Max(0f, room));
            speed = Mathf.Clamp(speed, -limit, limit);
        }

        travel += speed * dt;
        if (Mathf.Abs(travel) > maxTravel)
        {
            travel = Mathf.Clamp(travel, -maxTravel, maxTravel);
            speed = 0f;
        }

        // 2. Sway: change the weights' sideways VELOCITY in proportion to the change in chain speed.
        //    (Moving a Verlet point adds velocity, so the push is scaled by dt to be a velocity change.)
        float push = (speed - prevSpeed) * swayAmount * dt;
        ropeL[segments].x += push;
        ropeR[segments].x -= push;

        // 3. Rope physics in this object's local XY plane
        Vector2 g = transform.InverseTransformDirection(Physics2D.gravity);
        float keep = Mathf.Exp(-3f * dt / settleTime); // ~95% of motion gone after settleTime
        Simulate(ropeL, oldL, restLength + travel, -1, g, dt, keep);
        Simulate(ropeR, oldR, restLength - travel, +1, g, dt, keep);

        // 4. Physics-driven weights move here so Player 1 rides them correctly
        if (rbLeft) MoveBody(rbLeft, ropeL, hookLeft);
        if (rbRight) MoveBody(rbRight, ropeR, hookRight);
    }

    private void Simulate(Vector2[] r, Vector2[] old, float length, int side, Vector2 g, float dt, float keep)
    {
        Vector2 pin = new Vector2(side * wheelRadius, 0f); // where the chain leaves the wheel

        // Verlet integration with damping
        for (int i = 1; i < r.Length; i++)
        {
            Vector2 v = (r[i] - old[i]) * keep;
            old[i] = r[i];
            r[i] += v + g * (dt * dt);
        }

        // Distance constraints, so the chain bends naturally along its length
        float seg = length / segments;
        for (int it = 0; it < iterations; it++)
        {
            r[0] = pin;
            for (int i = 0; i < segments; i++)
            {
                Vector2 d = r[i + 1] - r[i];
                float dist = d.magnitude;
                float w = invMass[i] + invMass[i + 1];
                if (dist < 1e-5f || w <= 0f) continue;
                Vector2 corr = d * ((dist - seg) / (dist * w));
                r[i] += corr * invMass[i];
                r[i + 1] -= corr * invMass[i + 1];
            }
        }
        r[0] = pin;

        // Swing limit: keep the weight inside a cone below the pin
        int n = segments;
        float drop = pin.y - r[n].y;
        if (drop > 0f)
        {
            float maxSide = drop * Mathf.Tan(maxSwingAngle * Mathf.Deg2Rad);
            float dx = r[n].x - pin.x;
            if (Mathf.Abs(dx) > maxSide)
            {
                float clamped = Mathf.Sign(dx) * maxSide;
                old[n].x += clamped - dx; // shift history too, so the clamp also removes outward speed
                r[n].x = pin.x + clamped;
            }
        }

        // Final pass from the pin down: no link may be farther than one segment from the one above.
        // This makes the chain unstretchable, which removes the springy up-and-down bounce.
        for (int i = 1; i <= n; i++)
        {
            Vector2 d = r[i] - r[i - 1];
            float dist = d.magnitude;
            if (dist > seg) r[i] = r[i - 1] + d * (seg / dist);
        }
    }

    private void LateUpdate()
    {
        if (ropeL == null) return;

        // Blend between the last two physics steps so the chain is smooth at any frame rate
        float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        for (int i = 0; i < drawL.Length; i++)
        {
            drawL[i] = Vector2.Lerp(snapL[i], ropeL[i], alpha);
            drawR[i] = Vector2.Lerp(snapR[i], ropeR[i], alpha);
        }
        float t = Mathf.Lerp(prevTravel, travel, alpha);

        if (wheel)
            wheel.transform.localRotation = wheelBaseRotation * Quaternion.Euler(0f, 0f, t / wheelRadius * Mathf.Rad2Deg);

        if (weightLeft && !rbLeft) PlaceWeight(weightLeft, drawL, hookLeft, weightZLeft);
        if (weightRight && !rbRight) PlaceWeight(weightRight, drawR, hookRight, weightZRight);

        if (links == null) return;
        // Each link sits at a fixed distance from the left weight, measured along the chain
        // (up the left side, over the wheel, down the right). Total length never changes,
        // so links travel with the weights and roll over the wheel instead of stretching.
        for (int i = 0; i < links.Length; i++)
        {
            Vector2 pos = SampleChain((i + 0.5f) * spacing, t, out Vector2 dir);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            links[i].localPosition = new Vector3(pos.x, pos.y, 0f);
            links[i].localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    // ----- Weights -----

    private void WeightPose(Vector2[] r, float hook, out Vector2 localPos, out float angle)
    {
        Vector2 end = r[segments];
        Vector2 up = r[segments - 1] - end;
        angle = Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg - 90f;
        // Offset from the chain's end down to the weight's pivot, tilted with the weight
        localPos = end - (Vector2)(Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, hook, 0f));
    }

    private void PlaceWeight(Transform weight, Vector2[] r, float hook, float z)
    {
        WeightPose(r, hook, out Vector2 p, out float angle);
        weight.localPosition = new Vector3(p.x, p.y, z);
        weight.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void MoveBody(Rigidbody2D rb, Vector2[] r, float hook)
    {
        WeightPose(r, hook, out Vector2 p, out float angle);
        rb.MovePosition(transform.TransformPoint(p));
        rb.MoveRotation(transform.eulerAngles.z + angle);
    }

    // ----- Chain sampling -----

    /// <summary>Point and forward direction at distance u along the chain from the left weight.</summary>
    private Vector2 SampleChain(float u, float t, out Vector2 dir)
    {
        float lenL = restLength + t;
        float lenR = restLength - t;
        float arc = Mathf.PI * wheelRadius;

        if (u < lenL) return SampleRope(drawL, u, out dir); // left side, heading up

        u -= lenL;
        if (u < arc) // over the top of the wheel, left to right
        {
            float th = Mathf.PI - u / wheelRadius;
            dir = new Vector2(Mathf.Sin(th), -Mathf.Cos(th));
            return new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * wheelRadius;
        }

        u -= arc; // right side, heading down
        Vector2 p = SampleRope(drawR, Mathf.Max(0f, lenR - u), out Vector2 upDir);
        dir = -upDir; // keep the direction continuous so links don't flip 180 degrees
        return p;
    }

    /// <summary>Point at a distance up the rope from the weight end, plus the upward direction there.</summary>
    private static Vector2 SampleRope(Vector2[] r, float distFromWeight, out Vector2 upDir)
    {
        float d = distFromWeight;
        for (int i = r.Length - 1; i > 0; i--)
        {
            Vector2 a = r[i], b = r[i - 1];
            float l = (b - a).magnitude;
            if (d <= l || i == 1)
            {
                upDir = b - a;
                return Vector2.Lerp(a, b, l > 0f ? Mathf.Clamp01(d / l) : 0f);
            }
            d -= l;
        }
        upDir = Vector2.up;
        return r[0];
    }

    // ----- Editor helpers -----

    private void OnValidate()
    {
        restLength = Mathf.Max(0.5f, restLength);
        minChainLength = Mathf.Clamp(minChainLength, 0.1f, restLength);
        maxTravel = Mathf.Clamp(maxTravel, 0f, restLength - minChainLength);
        startTravel = Mathf.Clamp(startTravel, -maxTravel, maxTravel);
        wheelRadius = Mathf.Max(0.05f, wheelRadius);
    }

    // Scene view guide: yellow = chain path at rest, cyan = travel range of each chain end,
    // red = the highest a chain end is allowed to go (Min Chain Length)
    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = Color.yellow;
        const int steps = 32;
        Vector3 prev = new Vector3(wheelRadius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            Vector3 p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * wheelRadius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        Gizmos.DrawLine(new Vector3(-wheelRadius, 0f), new Vector3(-wheelRadius, -restLength));
        Gizmos.DrawLine(new Vector3(wheelRadius, 0f), new Vector3(wheelRadius, -restLength));

        foreach (float side in new[] { -wheelRadius, wheelRadius })
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(side - 0.2f, -restLength + maxTravel), new Vector3(side + 0.2f, -restLength + maxTravel));
            Gizmos.DrawLine(new Vector3(side - 0.2f, -restLength - maxTravel), new Vector3(side + 0.2f, -restLength - maxTravel));
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(side - 0.3f, -minChainLength), new Vector3(side + 0.3f, -minChainLength));
        }
    }
}