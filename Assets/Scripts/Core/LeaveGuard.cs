using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Web build only: the browser asks "Leave site?" before the tab closes (Plugins/WebGL/LeaveGuard.jslib).
/// Players holding Ctrl while walking hit Ctrl+W, the browser's close-tab shortcut, which a page can't block;
/// the question lets them cancel instead of losing the day (the game only saves at the start of each day).
/// </summary>
public static class LeaveGuard
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void LeaveGuard_Enable();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Enable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        LeaveGuard_Enable();
#endif
    }
}
