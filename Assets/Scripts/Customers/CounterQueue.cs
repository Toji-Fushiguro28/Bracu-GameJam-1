using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The line at the front desk. Place it where the first customer should stand,
/// facing the desk; the queue grows backwards from there.
/// Also holds the door points customers walk in from and leave to.
/// </summary>
public class CounterQueue : MonoBehaviour
{
    public static CounterQueue Instance { get; private set; }

    public Transform entrance;
    public Transform exit;
    public float spacing = 0.9f;

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

    public int IndexOf(Customer c) => line.IndexOf(c);

    public Vector3 SlotPosition(int index) => transform.position - transform.forward * spacing * index;

    /// <summary>Direction a waiting customer faces (toward the desk).</summary>
    public Quaternion FacingDesk => transform.rotation;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.3f);
        for (int i = 0; i < 4; i++) Gizmos.DrawWireSphere(SlotPosition(i) + Vector3.up * 0.1f, 0.25f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, transform.forward);
    }
}
