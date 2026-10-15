using System;
using UnityEngine;

public class ClockHands : MonoBehaviour
{
    public Transform minuteHand;
    public Transform hourHand;
    public Vector3 axis = Vector3.up;
    [Tooltip("Flip if the hands run counter-clockwise.")]
    public bool invert;

    const float HourRestAngle = 90f;

    Quaternion minuteRest, hourRest;

    void Awake()
    {
        if (minuteHand) minuteRest = minuteHand.localRotation;
        if (hourHand) hourRest = hourHand.localRotation;
    }

    void Update()
    {
        var now = DateTime.Now;
        float minutes = now.Minute + now.Second / 60f;
        float hours = now.Hour % 12 + minutes / 60f;
        float sign = invert ? -1f : 1f;
        if (minuteHand) minuteHand.localRotation = minuteRest * Quaternion.AngleAxis(sign * minutes * 6f, axis);
        if (hourHand) hourHand.localRotation = hourRest * Quaternion.AngleAxis(sign * (hours * 30f - HourRestAngle), axis);
    }
}
