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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => owners.Clear();

    public static void Lock(object owner)
    {
        if (owners.Add(owner) && owners.Count == 1) Apply(true);
    }

    public static void Unlock(object owner)
    {
        if (owners.Remove(owner) && owners.Count == 0) Apply(false);
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
