using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A box over a locked room that darkens what's inside (multiply shader), so you can just
/// make it out. Fades away when the room opens. Also marks the room's bounds for RoomGate.IsLockedAt.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class RoomDarkness : MonoBehaviour
{
    public static readonly List<RoomDarkness> All = new List<RoomDarkness>();

    public RoomGate.Room room;
    [Range(0f, 1f)] public float strength = 1f;
    public float fadeSeconds = 2f;

    static readonly int StrengthId = Shader.PropertyToID("_Strength");
    MeshRenderer mr;
    MaterialPropertyBlock block;
    float current = -1f;

    void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        block = new MaterialPropertyBlock();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public bool Contains(Vector3 p)
    {
        var local = transform.InverseTransformPoint(p);
        return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f && local.y >= -0.5f && local.y <= 0.5f;
    }

    void Update()
    {
        float target = RoomGate.IsOpen(room) && !RoomReveal.IsHeld(room) ? 0f : strength;
        if (Mathf.Approximately(current, target)) return;
        current = current < 0f ? target : Mathf.MoveTowards(current, target, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        mr.enabled = current > 0.001f;
        mr.GetPropertyBlock(block);
        block.SetFloat(StrengthId, current);
        mr.SetPropertyBlock(block);
    }
}
