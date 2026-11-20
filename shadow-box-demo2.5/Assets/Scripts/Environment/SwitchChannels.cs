using System;
using System.Collections.Generic;
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
///   EverActive    - has this color EVER been turned on? Never goes back to false.
///                   Used by the Level Objectives menu.
///
/// The state belongs to one room (scene). When the player leaves, SceneTransition saves it with
/// SaveRoom and loads the next room's saved state with EnterRoom, so every room remembers its
/// switches for the rest of the play session.
/// </summary>
public static class SwitchChannels
{
    private static readonly int ColorCount = Enum.GetValues(typeof(SwitchColor)).Length;

    private static bool[] active = new bool[ColorCount];
    private static int[] direction = new int[ColorCount];
    private static int[] heldInput = new int[ColorCount];
    private static bool[] everActive = new bool[ColorCount];
    private static int anyDirection; // last direction Player 2 pressed on ANY active color

    // Saved state of every room the player has left this play session, by scene name
    private struct RoomState
    {
        public bool[] active;
        public int[] direction;
        public bool[] everActive;
        public int anyDirection;
    }
    private static Dictionary<string, RoomState> rooms = new Dictionary<string, RoomState>();

    /// <summary>
    /// True while the current room's state came from a save (the player has been here before).
    /// Switches then ignore their own starting "Is On" and just match the saved state.
    /// </summary>
    public static bool IsRestoredRoom { get; private set; }

    /// <summary>Fires when a color turns on or off.</summary>
    public static event Action<SwitchColor, bool> ActiveChanged;

    /// <summary>Fires once per color, the first time that color is turned on.</summary>
    public static event Action<SwitchColor> FirstActivated;

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
        everActive = new bool[ColorCount];
        anyDirection = 0;
        rooms = new Dictionary<string, RoomState>();
        IsRestoredRoom = false;
        ActiveChanged = null;
        FirstActivated = null;
        DirectionChanged = null;
    }

    /// <summary>
    /// Clear every color's state but keep event listeners (a room seen for the first time).
    /// </summary>
    public static void ResetState()
    {
        active = new bool[ColorCount];
        direction = new int[ColorCount];
        heldInput = new int[ColorCount];
        everActive = new bool[ColorCount];
        anyDirection = 0;
        IsRestoredRoom = false;
    }

    /// <summary>Remember the current state as this room's. Called by SceneTransition when leaving.</summary>
    public static void SaveRoom(string room)
    {
        if (string.IsNullOrEmpty(room)) return;
        rooms[room] = new RoomState
        {
            active = (bool[])active.Clone(),
            direction = (int[])direction.Clone(),
            everActive = (bool[])everActive.Clone(),
            anyDirection = anyDirection
        };
    }

    /// <summary>
    /// Load a room's saved state, or start fresh if the player hasn't been there yet.
    /// Called by SceneTransition just BEFORE the scene loads, so switches see it in Awake.
    /// Fires no events: everything that cares either polls or snaps to the state in Start.
    /// </summary>
    public static void EnterRoom(string room)
    {
        ResetState();
        if (string.IsNullOrEmpty(room) || !rooms.TryGetValue(room, out RoomState s)) return;

        Array.Copy(s.active, active, ColorCount);
        Array.Copy(s.direction, direction, ColorCount);
        Array.Copy(s.everActive, everActive, ColorCount);
        anyDirection = s.anyDirection;
        IsRestoredRoom = true;   // heldInput stays 0: Player 2 re-presses in the new room
    }

    public static bool HasSavedRoom(string room) => !string.IsNullOrEmpty(room) && rooms.ContainsKey(room);

    /// <summary>Forget every room (e.g. a future "restart game" button).</summary>
    public static void ForgetAllRooms() => rooms.Clear();

    public static bool IsActive(SwitchColor color) => active[(int)color];
    public static int GetDirection(SwitchColor color) => direction[(int)color];
    public static int GetHeldInput(SwitchColor color) => heldInput[(int)color];

    /// <summary>True once this color has been turned on at least once (stays true after it's turned off).</summary>
    public static bool HasEverBeenActive(SwitchColor color) => everActive[(int)color];

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

        if (on && !everActive[i])
        {
            everActive[i] = true;
            FirstActivated?.Invoke(color);
        }
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