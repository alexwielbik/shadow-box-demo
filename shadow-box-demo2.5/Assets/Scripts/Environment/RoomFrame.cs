using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public enum RoomSide { Top, Bottom, Left, Right }

/// <summary>An opening in one side of the room frame.</summary>
[Serializable]
public class RoomGap
{
    public RoomSide side = RoomSide.Top;

    [Tooltip("Middle of the opening, measured from the room's center: X for Top/Bottom, Y for Left/Right.")]
    public float center = 0f;

    [Tooltip("Width of the innermost opening (Layer 3), in gameplay-plane units. " +
             "Each layer further out is one band wider on each side (scene 1: 3 → 3.5 → 4). " +
             "The collider opening matches the collider layer (3.5).")]
    [Min(0.5f)] public float width = 3f;

    [Tooltip("Top/Bottom gaps only: put a LevelDoor in this gap. Its scene link is set on the door itself (under Room/Doors).")]
    public bool door = true;

    [Tooltip("Name of the door. Other scenes point at it with their Target Door Id.")]
    public string doorId = "Top";
}

/// <summary>
/// Builds a whole room frame from a size and a list of gaps:
///   - the 3 stepped paper border layers (with BorderShadow + correct texture tiling),
///   - the barrier colliders (floor, ceiling, walls) with matching openings,
///   - the CameraBounds box,
///   - the background paper wall, sized to everything the camera can see,
///   - a LevelDoor in each Top/Bottom gap that asks for one.
///
/// Put it on an empty GameObject at z = 0 (not inside a prefab). Move the GameObject to move the room.
/// Change Size or Gaps and everything rebuilds. Everything under "Generated" is thrown away and remade,
/// so don't edit things in there by hand. "Doors" and "CameraBounds" are kept and updated in place,
/// so settings you type on a LevelDoor survive rebuilds.
///
/// Size = the CameraBounds box, the same thing scene 1's CameraBounds is (24 x 32 there).
/// The playable inside is one collider-inset smaller on every side (0.5 with the default style).
/// </summary>
[DisallowMultipleComponent]
public class RoomFrame : MonoBehaviour
{
    [SerializeField] private RoomFrameStyle style;

    [Tooltip("Width and height of the area the camera may show (the CameraBounds box). " +
             "Smallest useful size is one screen: about 24 x 13.5.")]
    [SerializeField] private Vector2 size = new Vector2(24f, 32f);

    [SerializeField] private List<RoomGap> gaps = new List<RoomGap>();

    [Tooltip("Rebuild automatically whenever a value here (or in the style) changes.")]
    [SerializeField] private bool autoRebuild = true;

    [SerializeField, HideInInspector] private BoxCollider2D cameraBounds;
    [SerializeField, HideInInspector] private string builtSignature;

    private const string GeneratedName = "Generated";
    private const string DoorsName = "Doors";
    private const string BoundsName = "CameraBounds";

    public RoomFrameStyle Style => style;
    public Vector2 Size => size;
    public BoxCollider2D CameraBounds => cameraBounds;
    public IReadOnlyList<RoomGap> Gaps => gaps;

    /// <summary>Distance from the CameraBounds edge to the collider faces (0.5 by default).</summary>
    public float ColliderInset => style != null ? style.bandWidth * (ColliderLayerIndex + 1) : 0.5f;

    /// <summary>How much wider the collider opening is than a gap's Width (0.25 each side by default).</summary>
    public float ColliderGapGrow =>
        style != null ? style.bandWidth * (style.layers.Length - 1 - ColliderLayerIndex) : 0.25f;

    private int ColliderLayerIndex =>
        style == null || style.layers.Length == 0 ? 0 : Mathf.Clamp(style.colliderLayer, 0, style.layers.Length - 1);

    // ------------------------------------------------------------------ camera view math

    /// <summary>What the camera can see, in the room's local space (room center = 0,0; gameplay plane z = 0).</summary>
    private struct View
    {
        public float distance;     // camera distance to the gameplay plane
        public float halfH, halfW; // half the view on the gameplay plane
        public float camXMin, camXMax, camYMin, camYMax; // where the camera center can go

        // An offset at depth z looks k times as big on the gameplay plane (k > 1 in front of it)
        public float K(float z) => distance / (distance + z);
    }

    private View GetView()
    {
        float distance = style != null ? style.cameraDistance : 25.19f;
        float fov = style != null ? style.fieldOfView : 30f;
        float aspect = style != null ? style.aspect : 16f / 9f;

        Camera cam = Camera.main;
        if (cam != null && !cam.orthographic)
        {
            distance = Mathf.Abs(cam.transform.position.z - transform.position.z);
            fov = cam.fieldOfView;
        }

        var v = new View { distance = distance };
        v.halfH = distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        v.halfW = v.halfH * aspect;

        // Same rule as CameraFollow: if the room is smaller than the view on an axis, the camera centers.
        float rangeX = Mathf.Max(0f, size.x * 0.5f - v.halfW);
        float rangeY = Mathf.Max(0f, size.y * 0.5f - v.halfH);
        v.camXMin = -rangeX; v.camXMax = rangeX;
        v.camYMin = -rangeY; v.camYMax = rangeY;
        return v;
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------ rebuilding (editor only)

    [NonSerialized] private bool rebuildQueued;

    private void OnValidate()
    {
        if (autoRebuild) RequestRebuild();
    }

    /// <summary>Rebuild on the next editor tick, but only if something actually changed.</summary>
    public void RequestRebuild()
    {
        if (Application.isPlaying || rebuildQueued || style == null) return;
        rebuildQueued = true;
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            rebuildQueued = false;
            if (Application.isPlaying) return;
            if (Signature() != builtSignature) Rebuild();
        };
    }

    // Everything the result depends on. Stored after a build so opening the scene doesn't rebuild it.
    private string Signature()
    {
        View v = GetView();
        string styleJson = style != null ? EditorJsonUtility.ToJson(style) : "none";
        string mine = JsonUtility.ToJson(new SignatureData { size = size, gaps = gaps });
        return Hash128.Compute($"{mine}|{styleJson}|{v.distance:F3}|{v.halfH:F3}").ToString();
    }

    [Serializable]
    private class SignatureData
    {
        public Vector2 size;
        public List<RoomGap> gaps;
    }

    /// <summary>Throw away the generated frame and build it again from the current settings.</summary>
    public void Rebuild()
    {
        if (Application.isPlaying) return;

        if (style == null || style.layers == null || style.layers.Length == 0)
        {
            Debug.LogWarning($"{name}: RoomFrame needs a Room Frame Style with at least one layer.", this);
            return;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
        {
            Debug.LogWarning($"{name}: RoomFrame can't rebuild inside a prefab instance. " +
                             "Right-click it > Prefab > Unpack Completely, then rebuild.", this);
            return;
        }

        // The math assumes an unrotated, unscaled room on the gameplay plane
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        Vector3 p = transform.position;
        if (!Mathf.Approximately(p.z, 0f)) transform.position = new Vector3(p.x, p.y, 0f);

        View v = GetView();

        Transform old = transform.Find(GeneratedName);
        if (old != null) DestroyImmediate(old.gameObject);
        Transform generated = NewChild(GeneratedName, transform);

        BuildBorders(generated, v);
        BuildBarrier(generated);
        BuildBackground(generated, v);
        BuildCameraBounds();
        BuildDoors();

        builtSignature = Signature();
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    // ------------------------------------------------------------------ borders

    private void BuildBorders(Transform generated, View v)
    {
        Transform borders = NewChild("Borders", generated);
        var sideRoots = new Dictionary<RoomSide, Transform>();
        foreach (RoomSide s in new[] { RoomSide.Left, RoomSide.Bottom, RoomSide.Top, RoomSide.Right })
            sideRoots[s] = NewChild(s.ToString(), borders);

        int count = style.layers.Length;
        float o = style.edgeOverhang;

        // Build back to front so each layer's shadow can use the layer behind it as its receiver
        var behind = new Dictionary<RoomSide, Transform>();
        for (int i = count - 1; i >= 0; i--)
        {
            RoomFrameStyle.PaperLayer layer = style.layers[i];
            float z = layer.depth;
            float k = v.K(z);
            float inner = style.bandWidth * (i + 1); // on-screen distance from screen edge to this layer's inner edge
            float grow = style.bandWidth * (count - 1 - i); // how much wider this layer's gaps are than Gap.width

            // Outer edges: past the edge of the screen when the camera is as far that way as it goes
            float xOutL = v.camXMin - (v.halfW + o) / k;
            float xOutR = v.camXMax + (v.halfW + o) / k;
            float yOutB = v.camYMin - (v.halfH + o) / k;
            float yOutT = v.camYMax + (v.halfH + o) / k;

            // Inner edges: where this layer's band ends on screen at that same camera position
            float xInL = v.camXMin - (v.halfW - inner) / k;
            float xInR = v.camXMax + (v.halfW - inner) / k;
            float yInB = v.camYMin - (v.halfH - inner) / k;
            float yInT = v.camYMax + (v.halfH - inner) / k;

            var built = new Dictionary<RoomSide, Transform>();

            // Top and Bottom own the corners; the sides run between them.
            // Every layer reaches out to the screen edge (hidden behind the layers in front),
            // so openings never show a hole behind them.
            built[RoomSide.Top] = BuildStrip(sideRoots[RoomSide.Top], RoomSide.Top, i, z, k, grow, v,
                xOutL, xOutR, yInT, yOutT, behind);
            built[RoomSide.Bottom] = BuildStrip(sideRoots[RoomSide.Bottom], RoomSide.Bottom, i, z, k, grow, v,
                xOutL, xOutR, yOutB, yInB, behind);
            built[RoomSide.Left] = BuildStrip(sideRoots[RoomSide.Left], RoomSide.Left, i, z, k, grow, v,
                yInB, yInT, xOutL, xInL, behind);
            built[RoomSide.Right] = BuildStrip(sideRoots[RoomSide.Right], RoomSide.Right, i, z, k, grow, v,
                yInB, yInT, xInR, xOutR, behind);

            behind = built;
        }
    }

    /// <summary>
    /// One side of one layer, split into pieces around any gaps on that side.
    /// along0/along1 run along the strip (X for Top/Bottom, Y for Left/Right); across0/across1 is its thickness.
    /// Returns one of the pieces (used as the shadow receiver for the layer in front).
    /// </summary>
    private Transform BuildStrip(Transform parent, RoomSide side, int layerIndex, float z, float k, float grow,
        View v, float along0, float along1, float across0, float across1, Dictionary<RoomSide, Transform> behind)
    {
        bool horizontal = side == RoomSide.Top || side == RoomSide.Bottom;

        // Where the camera sits (along this side) when a gap's middle is as centered as it can get
        float camMin = horizontal ? v.camXMin : v.camYMin;
        float camMax = horizontal ? v.camXMax : v.camYMax;

        var holes = new List<Vector2>();
        foreach (RoomGap g in gaps)
        {
            if (g.side != side) continue;
            float half = g.width * 0.5f + grow;
            float camRef = Mathf.Clamp(g.center, camMin, camMax);
            // Gap edges are given on the gameplay plane; push them to this layer's depth
            holes.Add(new Vector2(camRef + (g.center - half - camRef) / k,
                                  camRef + (g.center + half - camRef) / k));
        }

        List<Vector2> segments = Subtract(along0, along1, holes);
        RoomFrameStyle.PaperLayer layer = style.layers[layerIndex];
        behind.TryGetValue(side, out Transform receiver);

        Transform first = null;
        for (int s = 0; s < segments.Count; s++)
        {
            float a0 = segments[s].x, a1 = segments[s].y;
            string pieceName = $"Layer_{layerIndex + 1}_{side}" + (segments.Count > 1 ? $"_{s + 1}" : "");

            Vector3 pos;
            Vector2 scale;
            if (horizontal)
            {
                pos = new Vector3((a0 + a1) * 0.5f, (across0 + across1) * 0.5f, z);
                scale = new Vector2(a1 - a0, across1 - across0);
            }
            else
            {
                pos = new Vector3((across0 + across1) * 0.5f, (a0 + a1) * 0.5f, z);
                scale = new Vector2(across1 - across0, a1 - a0);
            }

            GameObject quad = MakeQuad(pieceName, parent, pos, scale, layer.material);

            // Texture tiling anchored to the room, so pieces line up and resizing never stretches the paper
            Vector2 tile = horizontal
                ? new Vector2(layer.longTileSize, layer.shortTileSize)
                : new Vector2(layer.shortTileSize, layer.longTileSize);
            Vector2 min = new Vector2(pos.x - scale.x * 0.5f, pos.y - scale.y * 0.5f);
            quad.AddComponent<TiledQuad>().Set(
                new Vector2(scale.x / tile.x, scale.y / tile.y),
                new Vector2(min.x / tile.x, min.y / tile.y));

            if (style.addShadows) AddShadow(quad, side, receiver);
            if (first == null) first = quad.transform;
        }
        return first;
    }

    private void AddShadow(GameObject piece, RoomSide side, Transform receiver)
    {
        BorderShadow shadow = piece.AddComponent<BorderShadow>();
        var so = new SerializedObject(shadow);

        // BorderShadow.InwardDirection: Right, Left, Up, Down
        int inward = side switch
        {
            RoomSide.Left => 0,
            RoomSide.Right => 1,
            RoomSide.Bottom => 2,
            _ => 3,
        };
        so.FindProperty("inward").enumValueIndex = inward;
        so.FindProperty("receiver").objectReferenceValue = receiver;
        so.FindProperty("width").floatValue = style.shadowWidth;
        so.FindProperty("color").colorValue = style.shadowColor;
        if (style.shadowMaterial != null)
            so.FindProperty("material").objectReferenceValue = style.shadowMaterial;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ barrier colliders

    private void BuildBarrier(Transform generated)
    {
        Transform barrier = NewChild("Barrier", generated);

        int layer = LayerMask.NameToLayer(style.groundLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"{name}: layer '{style.groundLayerName}' doesn't exist; barrier uses Default.", this);
            layer = 0;
        }

        float inset = ColliderInset;
        float grow = ColliderGapGrow;
        float t = style.barrierThickness;

        float left = -size.x * 0.5f + inset;
        float right = size.x * 0.5f - inset;
        float floor = -size.y * 0.5f + inset;
        float ceiling = size.y * 0.5f - inset;

        // Floor and ceiling run the full width (past the walls); walls run the full height
        MakeColliderStrip(barrier, "Floor", RoomSide.Bottom, layer, grow, left - t, right + t, floor - t, floor);
        MakeColliderStrip(barrier, "Ceiling", RoomSide.Top, layer, grow, left - t, right + t, ceiling, ceiling + t);
        MakeColliderStrip(barrier, "Wall_Left", RoomSide.Left, layer, grow, floor - t, ceiling + t, left - t, left);
        MakeColliderStrip(barrier, "Wall_Right", RoomSide.Right, layer, grow, floor - t, ceiling + t, right, right + t);
    }

    private void MakeColliderStrip(Transform parent, string baseName, RoomSide side, int layer, float grow,
        float along0, float along1, float across0, float across1)
    {
        bool horizontal = side == RoomSide.Top || side == RoomSide.Bottom;

        // The gameplay plane is z = 0, so openings are exactly Gap.width + grow on each side
        var holes = new List<Vector2>();
        foreach (RoomGap g in gaps)
        {
            if (g.side != side) continue;
            float half = g.width * 0.5f + grow;
            holes.Add(new Vector2(g.center - half, g.center + half));
        }

        List<Vector2> segments = Subtract(along0, along1, holes);
        for (int s = 0; s < segments.Count; s++)
        {
            float a0 = segments[s].x, a1 = segments[s].y;
            var go = new GameObject(baseName + (segments.Count > 1 ? $"_{s + 1}" : "")) { layer = layer };
            go.transform.SetParent(parent, false);

            var box = go.AddComponent<BoxCollider2D>();
            box.sharedMaterial = style.barrierMaterial;
            if (horizontal)
            {
                go.transform.localPosition = new Vector3((a0 + a1) * 0.5f, (across0 + across1) * 0.5f, 0f);
                box.size = new Vector2(a1 - a0, across1 - across0);
            }
            else
            {
                go.transform.localPosition = new Vector3((across0 + across1) * 0.5f, (a0 + a1) * 0.5f, 0f);
                box.size = new Vector2(across1 - across0, a1 - a0);
            }
        }
    }

    // ------------------------------------------------------------------ background wall

    private void BuildBackground(Transform generated, View v)
    {
        if (style.backgroundMaterial == null) return;

        float z = style.backgroundDepth;
        float k = v.K(z);
        float m = style.backgroundMargin;

        // Everything the camera can see at that depth, over its whole range of movement
        float x0 = v.camXMin - v.halfW / k - m;
        float x1 = v.camXMax + v.halfW / k + m;
        float y0 = v.camYMin - v.halfH / k - m;
        float y1 = v.camYMax + v.halfH / k + m;

        var pos = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z);
        var scale = new Vector2(x1 - x0, y1 - y0);
        GameObject quad = MakeQuad("Background_Paper", generated, pos, scale, style.backgroundMaterial);
        quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector2 tile = style.backgroundTileSize;
        quad.AddComponent<TiledQuad>().Set(
            new Vector2(scale.x / tile.x, scale.y / tile.y),
            new Vector2(x0 / tile.x, y0 / tile.y));
    }

    // ------------------------------------------------------------------ camera bounds + doors (kept between rebuilds)

    private void BuildCameraBounds()
    {
        if (cameraBounds == null)
        {
            Transform existing = transform.Find(BoundsName);
            if (existing != null) cameraBounds = existing.GetComponent<BoxCollider2D>();
        }
        if (cameraBounds == null)
        {
            var go = new GameObject(BoundsName);
            go.transform.SetParent(transform, false);
            cameraBounds = go.AddComponent<BoxCollider2D>();
        }

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0) cameraBounds.gameObject.layer = ignoreRaycast;
        cameraBounds.transform.localPosition = Vector3.zero;
        cameraBounds.transform.localRotation = Quaternion.identity;
        cameraBounds.transform.localScale = Vector3.one;
        cameraBounds.isTrigger = true;
        cameraBounds.offset = Vector2.zero;
        cameraBounds.size = size;
    }

    private void BuildDoors()
    {
        Transform doors = transform.Find(DoorsName);
        var wanted = new HashSet<string>();

        foreach (RoomGap g in gaps)
        {
            if (!g.door || (g.side != RoomSide.Top && g.side != RoomSide.Bottom)) continue;
            if (string.IsNullOrWhiteSpace(g.doorId))
            {
                Debug.LogWarning($"{name}: a {g.side} gap wants a door but has no Door Id.", this);
                continue;
            }

            string doorName = "Door_" + g.doorId;
            if (!wanted.Add(doorName))
            {
                Debug.LogWarning($"{name}: two gaps use the Door Id '{g.doorId}'. Door Ids must be unique per scene.", this);
                continue;
            }

            if (doors == null) doors = NewChild(DoorsName, transform);

            Transform t = doors.Find(doorName);
            LevelDoor door = t != null ? t.GetComponent<LevelDoor>() : null;
            if (door == null)
            {
                var go = new GameObject(doorName);
                go.transform.SetParent(doors, false);
                go.AddComponent<BoxCollider2D>();
                door = go.AddComponent<LevelDoor>();
            }

            // The door sits on the surface: the ceiling's underside or the floor's top
            float surfaceY = g.side == RoomSide.Top
                ? size.y * 0.5f - ColliderInset
                : -size.y * 0.5f + ColliderInset;
            door.transform.localPosition = new Vector3(g.center, surfaceY, 0f);
            door.ConfigureFromFrame(
                g.side == RoomSide.Top ? LevelDoor.DoorSide.Top : LevelDoor.DoorSide.Bottom,
                g.width + 2f * ColliderGapGrow,
                g.doorId);
            EditorUtility.SetDirty(door);
        }

        // Remove doors whose gap was deleted (only the ones this tool made: Door_*)
        if (doors != null)
        {
            for (int i = doors.childCount - 1; i >= 0; i--)
            {
                Transform child = doors.GetChild(i);
                if (child.name.StartsWith("Door_") && child.GetComponent<LevelDoor>() != null && !wanted.Contains(child.name))
                    DestroyImmediate(child.gameObject);
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Transform NewChild(string childName, Transform parent)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject MakeQuad(string quadName, Transform parent, Vector3 localPos, Vector2 scale, Material mat)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>()); // visual only
        quad.name = quadName;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPos;
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        if (mat != null) quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return quad;
    }
#endif

    /// <summary>The parts of [start, end] not covered by any of the holes (each hole is x..y).</summary>
    private static List<Vector2> Subtract(float start, float end, List<Vector2> holes)
    {
        holes.Sort((a, b) => a.x.CompareTo(b.x));
        var result = new List<Vector2>();
        float cursor = start;
        foreach (Vector2 h in holes)
        {
            if (h.y <= cursor || h.x >= end) continue;
            if (h.x > cursor) result.Add(new Vector2(cursor, h.x));
            cursor = Mathf.Max(cursor, h.y);
        }
        if (end > cursor) result.Add(new Vector2(cursor, end));
        result.RemoveAll(seg => seg.y - seg.x < 0.001f);
        return result;
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmos()
    {
        Vector3 c = transform.position;

        // White: camera bounds. Green: the playable inside (collider faces).
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawWireCube(c, new Vector3(size.x, size.y, 0f));

        float inset = ColliderInset;
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(c, new Vector3(size.x - 2f * inset, size.y - 2f * inset, 0f));

        // Cyan: gap openings (collider width) on the inside edge
        Gizmos.color = Color.cyan;
        float grow = ColliderGapGrow;
        foreach (RoomGap g in gaps)
        {
            float half = g.width * 0.5f + grow;
            Vector3 a, b;
            switch (g.side)
            {
                case RoomSide.Top:
                    a = new Vector3(g.center - half, size.y * 0.5f - inset); b = new Vector3(g.center + half, a.y); break;
                case RoomSide.Bottom:
                    a = new Vector3(g.center - half, -size.y * 0.5f + inset); b = new Vector3(g.center + half, a.y); break;
                case RoomSide.Left:
                    a = new Vector3(-size.x * 0.5f + inset, g.center - half); b = new Vector3(a.x, g.center + half); break;
                default:
                    a = new Vector3(size.x * 0.5f - inset, g.center - half); b = new Vector3(a.x, g.center + half); break;
            }
            Gizmos.DrawLine(c + a, c + b);
            Gizmos.DrawWireSphere(c + (a + b) * 0.5f, 0.15f);
        }
    }
}
