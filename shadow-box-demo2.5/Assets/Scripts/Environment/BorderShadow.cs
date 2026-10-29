using UnityEngine;

/// <summary>
/// Fakes the shadow a shadow-box border piece casts onto the layer behind it.
///
/// Put this on a border piece (a Unity Quad scaled into a strip) and drag the next
/// layer back into "Receiver". The script builds a soft, semi-transparent strip that
/// sits just in front of the receiver and fades out toward the middle of the screen.
/// Because it is depth tested and hugs the receiver:
///   - the receiver gets darkened, and
///   - anything floating between the two layers (like a gear) stays in front of
///     the shadow and is NOT darkened.
///
/// The strip is lined up with where this piece's inner edge appears on screen,
/// so it starts exactly at the edge even with the perspective camera.
///
/// The strip is regenerated automatically and never saved into the scene,
/// so you only ever edit the settings on this component.
///
/// Assumes the piece uses the built-in Quad mesh and that its parent (BorderFrame)
/// has a scale of 1, which is how the Clock Room is set up.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class BorderShadow : MonoBehaviour
{
    public enum InwardDirection { Right, Left, Up, Down }

    [Tooltip("Which way points toward the middle of the screen?\n" +
             "Left piece = Right, Right piece = Left, Bottom piece = Up, Top piece = Down.")]
    [SerializeField] private InwardDirection inward = InwardDirection.Right;

    [Tooltip("The layer this shadow falls on (e.g. Layer_2_Left for Layer_1_Left). " +
             "Leave empty on the back-most layer and use 'Depth Offset' instead.")]
    [SerializeField] private Transform receiver;

    [Tooltip("How far in front of the receiver the shadow floats. Just enough to avoid flickering.")]
    [SerializeField, Min(0.001f)] private float gapFromReceiver = 0.02f;

    [Tooltip("Only used when Receiver is empty: how far behind this piece the shadow sits.")]
    [SerializeField, Min(0f)] private float depthOffset = 0.05f;

    [Tooltip("How far the shadow reaches inward from the edge, in world units.")]
    [SerializeField, Min(0.001f)] private float width = 0.15f;

    [Tooltip("Shadow tint. Alpha is how dark the shadow is right at the edge.")]
    [SerializeField] private Color color = new Color(0.12f, 0.07f, 0.14f, 0.6f);

    [Tooltip("Strength across the shadow: left of the graph is the edge, right is the inner tip.")]
    [SerializeField]
    private AnimationCurve falloff = new AnimationCurve(
        new Keyframe(0f, 1f, -2f, -2f),
        new Keyframe(1f, 0f, 0f, 0f));

    [Tooltip("Material for the shadow. Use one with the 'Sprites/Default' shader. " +
             "Leave empty to use a temporary one (fine for testing, but assign one before building).")]
    [SerializeField] private Material material;

    private const int Segments = 16;

    private GameObject shadowObject;
    private MeshRenderer shadowRenderer;
    private Mesh mesh;
    private bool dirty = true;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastScale;
    private Vector3 lastReceiverPosition;

    private static Material fallbackMaterial;

    private void OnEnable()
    {
        CreateShadowObject();
        dirty = true;
        Rebuild();
    }

    private void OnDisable()
    {
        if (shadowObject != null)
        {
            if (Application.isPlaying) Destroy(shadowObject);
            else DestroyImmediate(shadowObject);
        }
        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
        shadowObject = null;
        mesh = null;
    }

    // Inspector changes land here. Just flag a rebuild; creating objects
    // inside OnValidate makes Unity throw warnings.
    private void OnValidate()
    {
        dirty = true;
    }

    private void LateUpdate()
    {
        if (shadowObject == null) CreateShadowObject();

        Vector3 receiverPosition = receiver != null ? receiver.position : Vector3.zero;
        if (transform.localPosition != lastPosition ||
            transform.localRotation != lastRotation ||
            transform.localScale != lastScale ||
            receiverPosition != lastReceiverPosition)
        {
            dirty = true;
        }

        Rebuild();
    }

    private void CreateShadowObject()
    {
        if (shadowObject != null) return;

        shadowObject = new GameObject(name + "_Shadow");
        shadowObject.hideFlags = HideFlags.DontSave; // regenerated every time, never saved
        shadowObject.layer = gameObject.layer;

        // Sibling of the piece (child of BorderFrame), so it follows the camera like the frame does
        shadowObject.transform.SetParent(transform.parent, false);
        shadowObject.transform.localPosition = Vector3.zero;
        shadowObject.transform.localRotation = Quaternion.identity;
        shadowObject.transform.localScale = Vector3.one;

        mesh = new Mesh { name = name + "_ShadowMesh", hideFlags = HideFlags.DontSave };

        shadowObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        shadowRenderer = shadowObject.AddComponent<MeshRenderer>();
        shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shadowRenderer.receiveShadows = false;
        shadowRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        shadowRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void Rebuild()
    {
        if (!dirty || mesh == null) return;
        dirty = false;

        lastPosition = transform.localPosition;
        lastRotation = transform.localRotation;
        lastScale = transform.localScale;
        lastReceiverPosition = receiver != null ? receiver.position : Vector3.zero;

        shadowRenderer.sharedMaterial = material != null ? material : GetFallbackMaterial();

        // The Quad mesh spans -0.5..0.5. Find the inner edge (in the piece's own space)
        // and the direction pointing inward.
        Vector3 edgeA, edgeB, inwardAxis;
        switch (inward)
        {
            case InwardDirection.Right:
                edgeA = new Vector3(0.5f, -0.5f, 0f); edgeB = new Vector3(0.5f, 0.5f, 0f); inwardAxis = Vector3.right; break;
            case InwardDirection.Left:
                edgeA = new Vector3(-0.5f, -0.5f, 0f); edgeB = new Vector3(-0.5f, 0.5f, 0f); inwardAxis = Vector3.left; break;
            case InwardDirection.Up:
                edgeA = new Vector3(-0.5f, 0.5f, 0f); edgeB = new Vector3(0.5f, 0.5f, 0f); inwardAxis = Vector3.up; break;
            default: // Down
                edgeA = new Vector3(-0.5f, -0.5f, 0f); edgeB = new Vector3(0.5f, -0.5f, 0f); inwardAxis = Vector3.down; break;
        }

        // Everything below is in the parent's (BorderFrame's) space, where the shadow mesh lives
        Vector3 a = ToParentSpace(edgeA);
        Vector3 b = ToParentSpace(edgeB);

        // Depth the shadow sits at: just in front of the receiver, or just behind this piece
        float shadowZ;
        if (receiver != null)
        {
            Vector3 r = transform.parent != null
                ? transform.parent.InverseTransformPoint(receiver.position)
                : receiver.position;
            shadowZ = r.z - gapFromReceiver;
        }
        else
        {
            shadowZ = transform.localPosition.z + depthOffset;
        }

        // Push the edge back along the camera's sight lines so that, on screen,
        // the shadow starts exactly where this piece's inner edge appears
        Vector3 cameraPoint = GetCameraPointInParentSpace();
        a = ProjectToDepth(a, cameraPoint, shadowZ);
        b = ProjectToDepth(b, cameraPoint, shadowZ);

        Vector3 step = transform.localRotation * inwardAxis * width;

        var vertices = new Vector3[(Segments + 1) * 2];
        var colors = new Color[vertices.Length];
        var triangles = new int[Segments * 6];

        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            Color c = color;
            c.a = color.a * Mathf.Clamp01(falloff.Evaluate(t));

            vertices[i * 2] = a + step * t;
            vertices[i * 2 + 1] = b + step * t;
            colors[i * 2] = c;
            colors[i * 2 + 1] = c;
        }

        for (int i = 0; i < Segments; i++)
        {
            int v = i * 2;
            int tri = i * 6;
            triangles[tri] = v;
            triangles[tri + 1] = v + 1;
            triangles[tri + 2] = v + 2;
            triangles[tri + 3] = v + 1;
            triangles[tri + 4] = v + 3;
            triangles[tri + 5] = v + 2;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private Vector3 ToParentSpace(Vector3 pieceLocalPoint)
    {
        return transform.localPosition +
               transform.localRotation * Vector3.Scale(transform.localScale, pieceLocalPoint);
    }

    // The camera the frame is attached to, expressed in the parent's space
    private Vector3 GetCameraPointInParentSpace()
    {
        Camera cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) return Vector3.zero;

        return transform.parent != null
            ? transform.parent.InverseTransformPoint(cam.transform.position)
            : cam.transform.position;
    }

    // Slide a point along the line from the camera through it until it reaches depth z
    private static Vector3 ProjectToDepth(Vector3 point, Vector3 cameraPoint, float z)
    {
        float denom = point.z - cameraPoint.z;
        if (Mathf.Abs(denom) < 0.0001f) return new Vector3(point.x, point.y, z);

        float scale = (z - cameraPoint.z) / denom;
        return cameraPoint + (point - cameraPoint) * scale;
    }

    private static Material GetFallbackMaterial()
    {
        if (fallbackMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("BorderShadow: couldn't find the 'Sprites/Default' shader. Assign a material on the component.");
                return null;
            }
            fallbackMaterial = new Material(shader) { name = "BorderShadow_Fallback", hideFlags = HideFlags.DontSave };
        }
        return fallbackMaterial;
    }
}