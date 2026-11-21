using System;
using UnityEngine;

/// <summary>
/// The LOOK of a room frame, shared by every scene that uses it: which material each paper layer
/// uses, how deep the layers sit, how wide each band is, the paper texture density, shadows,
/// background wall and barrier settings.
///
/// Make one per "stage" (Create > Shadow Box > Room Frame Style). When the border changes color
/// for a later stage, duplicate this asset, swap the materials, and point those scenes' RoomFrame at it.
/// Editing this asset rebuilds every RoomFrame in the open scene(s) that uses it.
/// </summary>
[CreateAssetMenu(menuName = "Shadow Box/Room Frame Style", fileName = "RoomFrameStyle")]
public class RoomFrameStyle : ScriptableObject
{
    [Serializable]
    public class PaperLayer
    {
        [Tooltip("Shared material for every piece of this layer. Its own Tiling/Offset are ignored " +
                 "(each piece gets its own through TiledQuad), so any of the old Paper_Layer_N materials works.")]
        public Material material;

        [Tooltip("World Z of this layer. Smaller = closer to the camera (drawn in front).")]
        public float depth;

        [Tooltip("World units per texture tile along the strip's LONG side (keeps the paper the same size everywhere).")]
        public float longTileSize = 9.224f;

        [Tooltip("World units per texture tile across the strip's SHORT side.")]
        public float shortTileSize = 5.897f;
    }

    [Header("Paper layers (front / outermost first)")]
    public PaperLayer[] layers =
    {
        new PaperLayer { depth = -2f,   longTileSize = 9.224f,  shortTileSize = 5.897f },
        new PaperLayer { depth = -0.5f, longTileSize = 9.746f,  shortTileSize = 6.282f },
        new PaperLayer { depth = 1f,    longTileSize = 10.278f, shortTileSize = 6.667f },
    };

    [Tooltip("How wide each layer's band looks on screen, in gameplay-plane units.")]
    [Min(0.01f)] public float bandWidth = 0.25f;

    [Tooltip("Pieces reach this far past the edge of the screen so no hairline ever shows.")]
    [Min(0f)] public float edgeOverhang = 0.1f;

    [Tooltip("The walls/floor/ceiling colliders line up with this layer's inner edge (0 = Layer 1). " +
             "1 = Layer 2, same as scene 1.")]
    [Min(0)] public int colliderLayer = 1;

    [Header("Border shadows")]
    [Tooltip("Real light shadows cast by the paper pieces. Off = same as scene 1, where the " +
             "shadows come only from the BorderShadow strips below.")]
    public bool bordersCastShadows = false;
    public bool addShadows = true;
    [Tooltip("BorderShadow_Mat")]
    public Material shadowMaterial;
    [Min(0.001f)] public float shadowWidth = 0.15f;
    public Color shadowColor = new Color(0.12f, 0.07f, 0.14f, 0.6f);

    [Header("Background wall")]
    [Tooltip("Paper_Background_Mat. Leave empty for no generated background.")]
    public Material backgroundMaterial;
    public float backgroundDepth = 10f;
    [Tooltip("World units per texture tile (X, Y). Scene 1 uses 8.955 x 8.824.")]
    public Vector2 backgroundTileSize = new Vector2(8.955f, 8.824f);
    [Tooltip("Extra coverage past what the camera can ever see.")]
    [Min(0f)] public float backgroundMargin = 1f;

    [Header("Barrier colliders")]
    public string groundLayerName = "Ground";
    public PhysicsMaterial2D barrierMaterial;
    [Min(0.1f)] public float barrierThickness = 1f;

    [Header("Camera (used if there is no Main Camera in the scene)")]
    public float cameraDistance = 25.19f;
    public float fieldOfView = 30f;
    [Tooltip("Always used: the Game view's aspect in the editor can be anything, the build is 16:9.")]
    public float aspect = 16f / 9f;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Rebuild every room in the open scenes that uses this style
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            foreach (RoomFrame frame in FindObjectsByType<RoomFrame>(FindObjectsInactive.Include))
                if (frame.Style == this) frame.RequestRebuild();
        };
    }
#endif
}
