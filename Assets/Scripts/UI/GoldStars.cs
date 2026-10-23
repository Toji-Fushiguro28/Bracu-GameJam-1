using UnityEngine.UIElements;

/// <summary>
/// A row of the kit's gold stars (IconStar01a) in place of text stars ("***--", "★"), which the web build's fonts
/// can't always draw. Empty ones are faded. Styles: .gold-stars / .gold-star / .gold-star.off in GameUI.uss.
/// </summary>
public static class GoldStars
{
    public static VisualElement Row(int filled, int total = 5, string extraClass = null)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("gold-stars");
        if (extraClass != null) row.AddToClassList(extraClass);
        for (int i = 0; i < total; i++)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.AddToClassList("gold-star");
            s.EnableInClassList("off", i >= filled);
            row.Add(s);
        }
        return row;
    }

    /// <summary>"Speed  ★★★☆☆" as a row: the name, then the stars (or "?" while you don't know yet).</summary>
    public static VisualElement Stat(string name, int value, bool known, string extraClass = null)
    {
        var line = new VisualElement { pickingMode = PickingMode.Ignore };
        line.AddToClassList("gold-stat");
        if (extraClass != null) line.AddToClassList(extraClass);
        var label = new Label(name) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("gold-stat-name");
        line.Add(label);
        if (known) line.Add(Row(value));
        else
        {
            var q = new Label("?") { pickingMode = PickingMode.Ignore };
            q.AddToClassList("gold-stat-unknown");
            line.Add(q);
        }
        return line;
    }
}
