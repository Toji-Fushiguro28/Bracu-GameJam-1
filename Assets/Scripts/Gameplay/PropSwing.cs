using UnityEngine;

/// <summary>
/// Swings a prop part back and forth around a local axis (clock pendulum, hanging sign).
/// </summary>
public class PropSwing : MonoBehaviour
{
    public Vector3 axis = Vector3.up;
    public float amplitude = 8f;
    public float period = 2f;

    Quaternion rest;

    void Awake() => rest = transform.localRotation;

    void Update()
    {
        float angle = Mathf.Sin(Time.time * Mathf.PI * 2f / period) * amplitude;
        transform.localRotation = rest * Quaternion.AngleAxis(angle, axis);
    }
}
