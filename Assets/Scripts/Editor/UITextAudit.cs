using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Tools > Check UI Text Overflow (in Play mode): lists every visible label or button whose text
/// doesn't fit, or that pokes out of the box it sits in. Open a screen, run it, read the console.
/// </summary>
public static class UITextAudit
{
    [MenuItem("Tools/Check UI Text Overflow")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        var issues = new List<string>();
        foreach (var doc in Object.FindObjectsByType<UIDocument>())
        {
            var root = doc.rootVisualElement;
            if (root == null || !Shown(root)) continue;
            root.Query<TextElement>().ForEach(t =>
            {
                if (string.IsNullOrEmpty(t.text) || !Shown(t)) return;
                var r = t.contentRect;
                if (r.width < 1f) return;
                string why = null;
                bool wraps = t.resolvedStyle.whiteSpace == WhiteSpace.Normal || t.resolvedStyle.whiteSpace == WhiteSpace.PreWrap;
                bool ellipsis = t.resolvedStyle.textOverflow == TextOverflow.Ellipsis;
                if (wraps)
                {
                    var need = t.MeasureTextSize(t.text, r.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                    if (need.y > r.height + 3f) why = $"needs {need.y:0}px tall, has {r.height:0}";
                }
                else if (!ellipsis)
                {
                    var need = t.MeasureTextSize(t.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    if (need.x > r.width + 3f) why = $"needs {need.x:0}px wide, has {r.width:0}";
                }
                // Poking out of its box (ignoring scroll views, whose content is meant to be bigger).
                var box = t.parent;
                while (box != null && box.resolvedStyle.width < 1f) box = box.parent;
                if (why == null && box != null && !(box.parent is ScrollView) && !(box is ScrollView))
                {
                    var a = t.worldBound; var b = box.worldBound;
                    if (a.xMax > b.xMax + 3f || a.xMin < b.xMin - 3f) why = "pokes out sideways";
                    else if (a.yMax > b.yMax + 3f || a.yMin < b.yMin - 3f) why = "pokes out top/bottom";
                }
                if (why != null)
                {
                    string text = t.text.Replace("\n", " / ");
                    issues.Add($"[{doc.name}] {t.GetType().Name} '{t.name}' \"{(text.Length > 60 ? text.Substring(0, 60) : text)}\": {why}");
                }
            });
        }
        return $"UI text check: {issues.Count} problem(s)\n" + string.Join("\n", issues);
    }

    static bool Shown(VisualElement e)
    {
        for (; e != null; e = e.parent)
            if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden || e.resolvedStyle.opacity < 0.02f
                || (e.layout.width < 1f && e.layout.height < 1f && e.childCount > 0)) return false; // collapsed panels count as hidden
        return true;
    }
}
