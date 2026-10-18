using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Stops one click doing two things: the click that opens a panel, finishes a line of dialogue or
/// brings up choices must not also press a button that has just appeared under the cursor.
/// UI roots call Attach once; anything that pops new buttons up calls Block.
/// </summary>
public static class ClickGuard
{
    static float blockedUntil;
    static readonly System.Collections.Generic.HashSet<VisualElement> attached = new System.Collections.Generic.HashSet<VisualElement>();

    /// <summary>Ignore button presses for a moment (new buttons just appeared under the cursor).</summary>
    public static void Block(float seconds = 0.25f) => blockedUntil = Mathf.Max(blockedUntil, Time.unscaledTime + seconds);

    public static bool Blocked => Time.unscaledTime < blockedUntil;

    /// <summary>Swallows pointer presses on this UI while blocked (before any button sees them).</summary>
    public static void Attach(VisualElement root)
    {
        if (root == null || !attached.Add(root)) return;
        root.RegisterCallback<PointerDownEvent>(e => { if (Blocked) e.StopPropagation(); }, TrickleDown.TrickleDown);
        root.RegisterCallback<ClickEvent>(e => { if (Blocked) e.StopPropagation(); }, TrickleDown.TrickleDown);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { blockedUntil = 0f; attached.Clear(); }
}
