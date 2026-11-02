using System;
using UnityEngine;

/// <summary>The switch colors in the game. Each color is its own independent channel.</summary>
public enum SwitchColor
{
    Grey,
    Gold,
    Blue
}

/// <summary>
/// Shared state for every switch color. Switches write to it, Player 2 writes to it,
/// and anything in the scene can read from it. Nothing needs a direct reference to anything else.
///
/// Per color it stores:
///   IsActive      - is a switch of this color on?
///   Direction     - the last direction Player 2 pressed (+1 = counter-clockwise / left,
///                   -1 = clockwise / right, 0 = Player 2 hasn't pressed anything yet)
///   HeldInput     - what Player 2 is holding right now (+1, -1 or 0). Always 0 while inactive.
/// </summary>
public static class SwitchChannels
{
    private static readonly int ColorCount = Enum.GetValues(typeof(SwitchColor)).Length;

    private static bool[] active = new bool[ColorCount];
    private static int[] direction = new int[ColorCount];
    private static int[] heldInput = new int[ColorCount];
    private static int anyDirection; // last direction Player 2 pressed on ANY active color

    /// <summary>Fires when a color turns on or off.</summary>
    public static event Action<SwitchColor, bool> ActiveChanged;

    /// <summary>Fires when Player 2 picks a new direction for a color.</summary>
    public static event Action<SwitchColor, int> DirectionChanged;

    // Static data survives between Play sessions when Domain Reload is disabled,
    // so wipe it every time Play mode starts.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAll()
    {
        active = new bool[ColorCount];
        direction = new int[ColorCount];
        heldInput = new int[ColorCount];
        anyDirection = 0;
        ActiveChanged = null;
        DirectionChanged = null;
    }

    public static bool IsActive(SwitchColor color) => active[(int)color];
    public static int GetDirection(SwitchColor color) => direction[(int)color];
    public static int GetHeldInput(SwitchColor color) => heldInput[(int)color];

    /// <summary>True if at least one switch of any color is on.</summary>
    public static bool IsAnyActive()
    {
        for (int i = 0; i < ColorCount; i++)
            if (active[i]) return true;
        return false;
    }

    /// <summary>The last direction Player 2 pressed on any active color (0 = never pressed).</summary>
    public static int GetAnyDirection() => anyDirection;

    /// <summary>What Player 2 is holding right now on any active color (+1, -1, or 0).</summary>
    public static int GetAnyHeldInput()
    {
        for (int i = 0; i < ColorCount; i++)
            if (heldInput[i] != 0) return heldInput[i];
        return 0;
    }

    public static void SetActive(SwitchColor color, bool on)
    {
        int i = (int)color;
        if (active[i] == on) return;

        active[i] = on;
        if (!on) heldInput[i] = 0;
        ActiveChanged?.Invoke(color, on);
    }

    public static void SetDirection(SwitchColor color, int dir)
    {
        int i = (int)color;
        dir = Math.Sign(dir);
        if (dir == 0) return;

        anyDirection = dir; // track this even if this color's direction didn't change
        if (direction[i] == dir) return;

        direction[i] = dir;
        DirectionChanged?.Invoke(color, dir);
    }

    public static void SetHeldInput(SwitchColor color, int dir)
    {
        int i = (int)color;
        heldInput[i] = active[i] ? Math.Sign(dir) : 0;
    }
}