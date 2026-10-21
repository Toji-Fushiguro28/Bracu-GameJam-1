using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The line at the front desk. Place it where the first customer should stand,
/// facing the desk; the queue grows backwards from there, along <see cref="path"/> if it has points
/// (straight out from the desk, then along the front wall, so a long line stays out of the doorway).
/// Also holds the door points customers walk in from and leave to.
/// </summary>
public class CounterQueue : MonoBehaviour
{
    public static CounterQueue Instance { get; private set; }

    public Transform entrance;
    public Transform exit;
    public float spacing = 0.9f;
    [Tooltip("Where the line bends, in order after the front spot (e.g. a corner, then the end of the line). " +
             "Spots are laid along these points; a longer line carries on past the last one. Empty = straight back from the desk.")]
    public Transform[] path;

    readonly List<Customer> line = new List<Customer>();

    public int Count => line.Count;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Join(Customer c)
    {
        if (!line.Contains(c)) line.Add(c);
    }

    public void Leave(Customer c) => line.Remove(c);

    /// <summary>Back in line at the front: for someone still standing at the desk (a second request after being
    /// served). Joining at the back would let the next person walk onto their spot.</summary>
    public void JoinFront(Customer c)
    {
        line.Remove(c);
        line.Insert(0, c);
    }

    /// <summary>Swaps this customer with the one just ahead (they're still on their way; this one is already here).</summary>
    public void MoveUp(Customer c)
    {
        int i = line.IndexOf(c);
        if (i <= 0) return;
        (line[i - 1], line[i]) = (line[i], line[i - 1]);
    }


    public int IndexOf(Customer c) => line.IndexOf(c);
    public Customer At(int i) => i >= 0 && i < line.Count ? line[i] : null;

    /// <summary>Spot number index (0 = at the desk), spacing apart along the path.</summary>
    public Vector3 SlotPosition(int index)
    {
        float d = index * spacing;
        Vector3 from = transform.position, dir = -transform.forward;
        if (path != null)
            foreach (var p in path)
            {
                if (p == null) continue;
                Vector3 seg = p.position - from;
                seg.y = 0f;
                float len = seg.magnitude;
                if (len < 0.01f) continue;
                dir = seg / len;
                if (d <= len) return from + dir * d;
                d -= len;
                from = new Vector3(p.position.x, from.y, p.position.z);
            }
        return from + dir * d; // past the end: carry on the same way
    }

    /// <summary>How many spots fit before the end of the path (more just carry on past it).</summary>
    public int PathSlots
    {
        get
        {
            float total = 0f;
            Vector3 from = transform.position;
            if (path != null)
                foreach (var p in path)
                {
                    if (p == null) continue;
                    var seg = p.position - from; seg.y = 0f;
                    total += seg.magnitude;
                    from = p.position;
                }
            return Mathf.FloorToInt(total / spacing) + 1;
        }
    }

    /// <summary>Direction a waiting customer faces (toward the desk).</summary>
    public Quaternion FacingDesk => transform.rotation;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.3f);
        int n = path != null && path.Length > 0 ? PathSlots : 4;
        for (int i = 0; i < n; i++) Gizmos.DrawWireSphere(SlotPosition(i) + Vector3.up * 0.1f, 0.25f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, transform.forward);
        // The path itself.
        Vector3 from = transform.position;
        if (path != null)
            foreach (var p in path)
            {
                if (p == null) continue;
                Gizmos.DrawLine(from + Vector3.up * 0.1f, p.position + Vector3.up * 0.1f);
                from = p.position;
            }
    }
}
