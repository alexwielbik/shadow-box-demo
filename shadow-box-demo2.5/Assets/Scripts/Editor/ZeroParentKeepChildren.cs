using UnityEditor;
using UnityEngine;

// Moves the selected parent(s) to local (0,0,0) while keeping every child
// exactly where it is in the world. Undo (Ctrl+Z) reverts it.
public static class ZeroParentKeepChildren
{
    [MenuItem("Tools/Zero Parent Position (Keep Children)")]
    private static void ZeroSelected()
    {
        foreach (Transform parent in Selection.transforms)
        {
            int childCount = parent.childCount;
            Transform[] children = new Transform[childCount];
            Vector3[] worldPositions = new Vector3[childCount];

            for (int i = 0; i < childCount; i++)
            {
                children[i] = parent.GetChild(i);
                worldPositions[i] = children[i].position;
            }

            Undo.RecordObject(parent, "Zero Parent Position");
            foreach (Transform child in children)
                Undo.RecordObject(child, "Zero Parent Position");

            parent.localPosition = Vector3.zero;

            for (int i = 0; i < childCount; i++)
                children[i].position = worldPositions[i];
        }
    }

    [MenuItem("Tools/Zero Parent Position (Keep Children)", true)]
    private static bool ValidateZeroSelected() => Selection.transforms.Length > 0;
}