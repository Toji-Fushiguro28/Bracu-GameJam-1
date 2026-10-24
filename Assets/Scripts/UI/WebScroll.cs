using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Browser mouse wheels report a notch as ~100 pixels and UI Toolkit multiplies that by its own scroll step (18), so
/// on the web one notch flung every list from the top straight to the bottom. On WebGL this catches the wheel on
/// every UI panel first and moves the list under the mouse by a sensible step instead (about a row a notch; small
/// trackpad flicks move a little). The date wheels on the library card keep their own wheel handling.
/// Made at runtime (no scene edits); does nothing outside WebGL unless <see cref="ForceOn"/> is set.
/// </summary>
public class WebScroll : MonoBehaviour
{
    /// <summary>Testing in the editor: turn the web scrolling on outside WebGL.</summary>
    public static bool ForceOn;

    readonly HashSet<IPanel> hooked = new HashSet<IPanel>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("WebScroll");
        DontDestroyOnLoad(go);
        go.AddComponent<WebScroll>();
    }

    void Update()
    {
        if (!(Application.platform == RuntimePlatform.WebGLPlayer || ForceOn) || Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f; // documents come and go (scenes, screens): look for new panels twice a second
        foreach (var doc in FindObjectsByType<UIDocument>())
        {
            var panel = doc.rootVisualElement?.panel;
            if (panel == null || !hooked.Add(panel)) continue;
            panel.visualTree.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }
        hooked.RemoveWhere(p => p == null || p.visualTree == null || p.visualTree.panel == null);
    }

    static void OnWheel(WheelEvent e)
    {
        if (!(Application.platform == RuntimePlatform.WebGLPlayer || ForceOn) || e.delta.y == 0f) return;
        var el = e.target as VisualElement;
        // The library card's date wheels step one notch at a time on their own.
        for (var p = el; p != null; p = p.parent) if (p.ClassListContains("wheel")) return;
        var sv = el as ScrollView ?? el?.GetFirstAncestorOfType<ScrollView>();
        if (sv == null) return;
        float step = Mathf.Sign(e.delta.y) * Mathf.Clamp(Mathf.Abs(e.delta.y) * 0.5f, 18f, 60f);
        sv.scrollOffset += new Vector2(0f, step); // ScrollView keeps it within the list
        e.StopPropagation();
    }
}
