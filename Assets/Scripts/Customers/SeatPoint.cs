using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Somewhere a reader can sit: a chair (1 seat), armchair (1) or sofa (2).
/// Seat offsets are local to the furniture; readers face the furniture's forward.
/// </summary>
public class SeatPoint : MonoBehaviour
{
    [Tooltip("Local positions of each seat (sofas have two).")]
    public Vector3[] seats = { new Vector3(0f, 0f, 0.05f) };
    [Tooltip("How far in front of the seat the reader walks to before sitting down.")]
    public float approachDistance = 0.65f;
    [Tooltip("Only VIP readers (Dean, professors) may sit here, e.g. in Rare Books.")]
    public bool rareBooksOnly;

    static readonly List<SeatPoint> all = new List<SeatPoint>();
    Customer[] taken;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    void OnEnable()
    {
        taken = new Customer[seats.Length];
        all.Add(this);
    }

    void OnDisable() => all.Remove(this);

    public Vector3 SeatPosition(int i) => transform.TransformPoint(seats[i]);
    public Quaternion SeatRotation => Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up));
    public Vector3 ApproachPosition(int i) => SeatPosition(i) + SeatRotation * Vector3.forward * approachDistance;

    /// <summary>Reserves a random free seat. rareRoom = the reader wants a Rare Books seat.</summary>
    public static bool TryReserve(Customer who, bool rareRoom, out SeatPoint seat, out int index)
    {
        var free = new List<(SeatPoint, int)>();
        foreach (var s in all)
        {
            if (s.rareBooksOnly != rareRoom) continue;
            if (RoomGate.IsLockedAt(s.transform.position)) continue; // that room isn't open yet
            for (int i = 0; i < s.taken.Length; i++)
                if (s.taken[i] == null) free.Add((s, i));
        }
        if (free.Count == 0) { seat = null; index = -1; return false; }
        (seat, index) = free[Random.Range(0, free.Count)];
        seat.taken[index] = who;
        return true;
    }

    public void Release(Customer who)
    {
        for (int i = 0; i < taken.Length; i++)
            if (taken[i] == who) taken[i] = null;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int i = 0; i < seats.Length; i++)
        {
            Gizmos.DrawWireSphere(transform.TransformPoint(seats[i]) + Vector3.up * 0.4f, 0.15f);
            Gizmos.DrawLine(transform.TransformPoint(seats[i]), ApproachPosition(i));
        }
    }
}
