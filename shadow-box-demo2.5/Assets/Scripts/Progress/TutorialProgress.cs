using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers which tutorial pieces (instruction papers, interact prompts) the player has already
/// finished, so they don't come back when the player returns to a room.
///
/// Lives for the whole play session (survives scene loads) and is wiped every time Play starts,
/// so each new run of the game shows the tutorial once.
/// </summary>
public static class TutorialProgress
{
    private static HashSet<string> done = new HashSet<string>();

    // Static data survives between Play sessions when Domain Reload is disabled
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAll() => done = new HashSet<string>();

    public static bool IsDone(string id) => !string.IsNullOrEmpty(id) && done.Contains(id);

    public static void MarkDone(string id)
    {
        if (!string.IsNullOrEmpty(id)) done.Add(id);
    }

    /// <summary>Forget everything (e.g. a future "restart game" button).</summary>
    public static void Clear() => done.Clear();
}