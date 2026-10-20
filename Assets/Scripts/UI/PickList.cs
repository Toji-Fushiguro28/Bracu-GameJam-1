using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A drop-down list under a button: pick an option in one click instead of cycling through them.
/// It opens on top of the whole UI, closes on a pick or a click outside it, and closes itself
/// if the button is hidden (e.g. the computer is closed while it's open).
/// </summary>
public static class PickList
{
    static VisualElement open;

    const float ItemHeight = 38f;

    /// <summary>Opens the list under <paramref name="button"/>. <paramref name="enabled"/> greys out options that can't be picked,
    /// <paramref name="header"/> turns options into group headings (not pickable), and
    /// <paramref name="maxVisible"/> caps how many rows show before it scrolls (0 = as many as fit).</summary>
    public static void Open(Button button, string[] options, int current, Action<int> pick, bool pcStyle, Func<int, bool> enabled = null, int scrollTo = -1,
                            Func<int, bool> header = null, int maxVisible = 0, Func<int, bool> group = null)
    {
        Close();
        if (button?.panel == null || options == null || options.Length == 0) return;

        // The top of this UI document, plus every style sheet on the way up so the buttons keep their look.
        VisualElement top = button;
        while (top.parent != null && top.parent != button.panel.visualTree) top = top.parent;

        var layer = new VisualElement { pickingMode = PickingMode.Position };
        layer.style.position = Position.Absolute;
        layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
        for (var e = button.parent; e != null; e = e.parent)
            for (int i = 0; i < e.styleSheets.count; i++) layer.styleSheets.Add(e.styleSheets[i]);
        // A click anywhere outside the list closes it (and does nothing else).
        layer.RegisterCallback<PointerDownEvent>(e => { if (e.target == layer) { Close(); e.StopPropagation(); } });

        var list = new ScrollView(ScrollViewMode.Vertical);
        list.AddToClassList(pcStyle ? "pc-scroll" : "page-scroll");
        list.style.position = Position.Absolute;
        var b = top.WorldToLocal(button.worldBound);
        // Stay inside the panel the button sits in (the first big box around it), not just the whole UI.
        VisualElement area = button.parent;
        while (area != null && area != top && area.layout.height < 300f) area = area.parent;
        Rect bounds = area != null ? top.WorldToLocal(area.worldBound) : new Rect(0f, 0f, top.layout.width, top.layout.height);
        float width = Mathf.Max(b.width, 130f);
        int rows = maxVisible > 0 ? Mathf.Min(options.Length, maxVisible) : options.Length;
        float natural = rows * ItemHeight + 12f;
        float below = bounds.yMax - b.yMax - 6f, above = b.yMin - bounds.yMin - 6f;
        // Below the button if it fits, otherwise whichever side has more room (it scrolls if needed).
        bool down = natural <= below || below >= above;
        float height = maxVisible > 0 ? natural : Mathf.Max(80f, Mathf.Min(natural, down ? below : above));
        list.style.left = Mathf.Clamp(b.xMin, bounds.xMin + 4f, Mathf.Max(bounds.xMin + 4f, bounds.xMax - width - 4f));
        list.style.top = down ? b.yMax + 2f : b.yMin - 2f - height;
        list.style.width = width;
        list.style.height = height;
        // The PC is parchment with a dark pixel outline; the book pages keep their own colours.
        list.style.backgroundColor = (Color)(pcStyle ? new Color32(248, 237, 207, 252) : new Color32(244, 228, 193, 252));
        var edge = pcStyle ? new Color32(110, 76, 48, 255) : new Color32(120, 80, 50, 255);
        list.style.borderTopColor = list.style.borderBottomColor = list.style.borderLeftColor = list.style.borderRightColor = (Color)edge;
        list.style.borderTopWidth = list.style.borderBottomWidth = list.style.borderLeftWidth = list.style.borderRightWidth = pcStyle ? 3f : 2f;
        list.style.paddingTop = list.style.paddingBottom = list.style.paddingLeft = list.style.paddingRight = 4f;

        Button picked = null, scrollTarget = null;
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            if (header != null && header(i))
            {
                var head = new Label(options[i]) { pickingMode = PickingMode.Ignore };
                head.AddToClassList("picklist-header");
                head.style.height = ItemHeight - 2f;
                list.Add(head);
                continue;
            }
            var item = new Button(() => { Close(); pick(index); }) { text = options[i] };
            item.AddToClassList(pcStyle ? "pc-button" : "kit-button");
            item.AddToClassList("picklist-item");
            if (group != null && group(i)) item.AddToClassList("picklist-group"); // a whole group: pickable, styled like a heading
            item.style.marginTop = item.style.marginBottom = 1f;
            item.style.marginLeft = item.style.marginRight = 0f;
            item.style.unityTextAlign = TextAnchor.MiddleLeft;
            item.style.whiteSpace = WhiteSpace.NoWrap;
            item.style.height = ItemHeight - 2f;
            if (i == current)
            {
                picked = item;
                item.AddToClassList("picked");
                item.style.backgroundColor = (Color)new Color32(214, 120, 64, 255);
                item.style.color = Color.white;
            }
            if (i == scrollTo) scrollTarget = item;
            if (enabled != null && !enabled(i)) { item.SetEnabled(false); item.style.opacity = 0.3f; }
            list.Add(item);
        }
        layer.Add(list);
        // Wide enough for the longest option (measured once the font is known), but never past the panel.
        float minLeft = bounds.xMin + 4f, maxRight = bounds.xMax - 4f;
        list.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            // Text width plus whatever the list takes around it (padding, borders, scrollbar).
            float need = width;
            foreach (var child in list.Children())
                if (child is Button opt && opt.contentRect.width > 0f)
                {
                    float text = opt.MeasureTextSize(opt.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                    need = Mathf.Max(need, text + (list.resolvedStyle.width - opt.contentRect.width) + 6f);
                }
            need = Mathf.Min(need, maxRight - minLeft);
            if (Mathf.Abs(need - list.resolvedStyle.width) < 1f) return;
            list.style.width = need;
            list.style.left = Mathf.Clamp(b.xMin, minLeft, Mathf.Max(minLeft, maxRight - need));
        });
        top.Add(layer);
        open = layer;
        var show = picked ?? scrollTarget; // open the list where the choice is
        if (show != null) list.schedule.Execute(() => list.ScrollTo(show)).ExecuteLater(30);

        // Gone with its button: the screen changed or the computer was closed.
        layer.schedule.Execute(() => { if (open == layer && !Showing(button)) Close(); }).Every(100);
    }

    /// <summary>Closes the open list, if any.</summary>
    public static void Close()
    {
        open?.RemoveFromHierarchy();
        open = null;
    }

    static bool Showing(VisualElement e)
    {
        if (e.panel == null) return false;
        for (; e != null; e = e.parent)
            if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return false;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => open = null;
}
