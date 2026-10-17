using System;
using UnityEngine.UIElements;

/// <summary>
/// PC "Audit Log" screen: every request heard, one day at a time (today first, newest at the top).
/// </summary>
public static class PCAuditView
{
    static int shownDay;

    public static void Wire(VisualElement root, Action back)
    {
        root.Q<Button>("back-button")?.RegisterCallback<ClickEvent>(_ => back());
        var gm = GameManager.Instance;
        shownDay = gm != null ? gm.day : 1;
        var days = root.Q("audit-days");
        if (days != null)
            for (int d = 1; d <= shownDay; d++)
            {
                int day = d;
                var b = new Button(() => { shownDay = day; Refresh(root); }) { name = "audit-day-" + d, text = "Day " + d };
                b.AddToClassList("pc-button");
                b.AddToClassList("audit-day");
                days.Add(b);
            }
        Refresh(root);
    }

    static void Refresh(VisualElement root)
    {
        root.Query<Button>(className: "audit-day").ForEach(b => b.EnableInClassList("selected", b.name == "audit-day-" + shownDay));
        var list = root.Q<ScrollView>("audit-list");
        if (list == null) return;
        list.Clear();
        var all = RequestLog.All;
        for (int i = all.Count - 1; i >= 0; i--)
            if (all[i].day == shownDay) list.Add(RequestLog.BuildRow(all[i]));
        if (list.childCount > 0) return;
        var empty = new Label("Nothing logged on this day.");
        empty.AddToClassList("audit-empty");
        list.Add(empty);
    }
}
