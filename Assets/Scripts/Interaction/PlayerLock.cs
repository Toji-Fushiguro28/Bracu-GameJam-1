using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Freezes the player while any UI owns the screen (dialogue, shelves, repair desk...).
/// Several owners can lock at once; the player moves again when the last one unlocks.
/// Player scripts are found by name so this has no hard dependency on them.
/// </summary>
public static class PlayerLock
{
    static readonly HashSet<object> owners = new HashSet<object>();
    static readonly int SpeedId = Animator.StringToHash("Speed");

    public static bool IsLocked => owners.Count > 0;

    /// <summary>A work screen is open (computer, shelf or cart, repair desk) and nothing freezes time:
    /// the clock and patience run slower, not stopped (see GameManager.TimeScale).</summary>
    public static bool SlowsTime
    {
        get
        {
            if (FreezesTime) return false;
            foreach (var o in owners) if (o is ShelfUI || o is RepairUI || o is PCOverlayTrigger) return true;
            return false;
        }
    }

    /// <summary>True when the game clock should stop: talking, cutscenes, menus. Browsing the PC, shelves or repair desk lets time run.</summary>
    public static bool FreezesTime
    {
        get
        {
            foreach (var o in owners) if (!(o is ShelfUI || o is RepairUI || o is PCOverlayTrigger)) return true;
            return false;
        }
    }

    /// <summary>Frame the last lock was released; the click that closed a UI must not also interact.</summary>
    public static int UnlockedFrame { get; private set; } = -1;

    /// <summary>Drops every lock (scene reload).</summary>
    public static void Reset() => ResetStatics();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        owners.Clear();
        UnlockedFrame = -1;
    }

    public static void Lock(object owner)
    {
        if (!owners.Add(owner)) return;
        ClickGuard.Block(); // a panel just opened: the click that opened it doesn't press anything in it
        if (owners.Count == 1) Apply(true);
    }

    public static void Unlock(object owner)
    {
        if (owners.Remove(owner) && owners.Count == 0)
        {
            UnlockedFrame = Time.frameCount;
            Apply(false);
        }
    }

    static void Apply(bool locked)
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        foreach (var name in new[] { "PlayerMover", "PlayerFootsteps" })
            if (player.GetComponent(name) is Behaviour b) b.enabled = !locked;
        // Stop the walk cycle; the mover is disabled so it won't do it.
        if (locked && player.TryGetComponent(out Animator anim)) anim.SetFloat(SpeedId, 0f);
    }
}
