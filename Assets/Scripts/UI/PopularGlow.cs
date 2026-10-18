using UnityEngine.UIElements;

/// <summary>
/// Today's popular books glow gold (a slow pulse) with a "Popular" tag, on the computer, the shelves
/// and the end-of-day shop. The styles are .popular / .glow-on / .popular-badge in PC.uss and GameUI.uss.
/// </summary>
public static class PopularGlow
{
    const long PulseMs = 900;

    /// <summary>Glows this row (one that's built fresh for a popular book).</summary>
    public static void Mark(VisualElement row)
    {
        row.AddToClassList("popular");
        Pulse(row);
    }

    /// <summary>Starts the pulse (once per element). Harmless on rows that aren't glowing (reused list rows).</summary>
    public static void Pulse(VisualElement row)
    {
        if (row.ClassListContains("pulsing")) return;
        row.AddToClassList("pulsing");
        row.schedule.Execute(() =>
        {
            bool glowing = row.ClassListContains("popular") || row.ClassListContains("ready");
            row.EnableInClassList("glow-on", glowing && !row.ClassListContains("glow-on"));
        }).Every(PulseMs);
    }

    /// <summary>Glows a tab or tile while there's something to do behind it (an upgrade or hire you can afford).</summary>
    public static void Ready(VisualElement e, bool on)
    {
        if (e == null) return;
        e.EnableInClassList("ready", on);
        if (on) Pulse(e);
        else e.RemoveFromClassList("glow-on");
    }

    public static Label Badge()
    {
        var b = new Label("Popular");
        b.AddToClassList("popular-badge");
        b.pickingMode = PickingMode.Ignore;
        return b;
    }

    /// <summary>The title as is, or (for a popular book) the title with a Popular tag, and the row glowing.</summary>
    public static VisualElement TitleRow(Label title, bool popular, VisualElement row)
    {
        if (!popular) return title;
        Mark(row);
        var line = new VisualElement();
        line.AddToClassList("popular-title-row");
        line.AddToClassList("title-row");
        title.style.flexShrink = 1;
        line.Add(title);
        line.Add(Badge());
        return line;
    }
}
