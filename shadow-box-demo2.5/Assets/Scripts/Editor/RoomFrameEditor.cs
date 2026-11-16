using UnityEditor;
using UnityEngine;

/// <summary>Inspector for RoomFrame: shows the playable size and adds a Rebuild button.</summary>
[CustomEditor(typeof(RoomFrame))]
public class RoomFrameEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var frame = (RoomFrame)target;
        float inset = frame.ColliderInset;
        Vector2 inside = frame.Size - Vector2.one * (2f * inset);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            $"Playable inside (collider faces): {inside.x:0.##} x {inside.y:0.##}\n" +
            $"Floor top at y {frame.transform.position.y - frame.Size.y * 0.5f + inset:0.##}, " +
            $"ceiling at y {frame.transform.position.y + frame.Size.y * 0.5f - inset:0.##}\n" +
            "One screen is about 24 x 13.5. Smaller than that on an axis and the camera stays centered on it.\n" +
            "Gap Center is measured from the room's center.",
            MessageType.Info);

        if (frame.Style == null)
            EditorGUILayout.HelpBox("Assign a Room Frame Style (Create > Shadow Box > Room Frame Style).", MessageType.Warning);

        if (PrefabUtility.IsPartOfPrefabInstance(frame.gameObject))
            EditorGUILayout.HelpBox("This is inside a prefab instance, so it can't add or remove pieces. " +
                                    "Unpack it (right-click > Prefab > Unpack Completely).", MessageType.Warning);

        if (GUILayout.Button("Rebuild Now", GUILayout.Height(28)))
            frame.Rebuild();
    }
}
