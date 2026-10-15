using UnityEngine;

public class PropSpin : MonoBehaviour
{
    public Vector3 axis = Vector3.up;
    public float degreesPerSecond = 15f;

    void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
}
