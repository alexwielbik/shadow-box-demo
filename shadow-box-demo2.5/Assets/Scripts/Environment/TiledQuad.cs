using UnityEngine;

/// <summary>
/// Gives one renderer its own texture tiling / offset while it keeps using a SHARED material.
///
/// Before: every border piece needed its own duplicated material just so its tiling could match
/// its size (Paper_Layer_1_TopLeft, Paper_Layer_1_TopRight, ...).
/// Now: all pieces of a layer share one material, and this component overrides the tiling for
/// this renderer only (through a MaterialPropertyBlock, which is never saved into the material).
///
/// RoomFrame adds and fills this automatically. You can also put it on any hand-placed quad:
/// type in the Tiling / Offset you would have typed into a duplicated material.
///
/// Note: whatever Tiling / Offset the shared material has is ignored on this renderer.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class TiledQuad : MonoBehaviour
{
    [Tooltip("Texture property to override. URP Lit / Simple Lit use _BaseMap.")]
    [SerializeField] private string textureProperty = "_BaseMap";

    [SerializeField] private Vector2 tiling = Vector2.one;
    [SerializeField] private Vector2 offset = Vector2.zero;

    private static MaterialPropertyBlock block;

    public Vector2 Tiling => tiling;
    public Vector2 Offset => offset;

    /// <summary>Change the tiling and offset from code (RoomFrame uses this).</summary>
    public void Set(Vector2 newTiling, Vector2 newOffset)
    {
        tiling = newTiling;
        offset = newOffset;
        Apply();
    }

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    private void OnDisable()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null) r.SetPropertyBlock(null);
    }

    private void Apply()
    {
        Renderer r = GetComponent<Renderer>();
        if (r == null) return;

        if (block == null) block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetVector(textureProperty + "_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
        r.SetPropertyBlock(block);
    }
}
