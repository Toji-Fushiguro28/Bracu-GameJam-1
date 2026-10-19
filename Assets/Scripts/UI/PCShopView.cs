using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Shop": Upgrades (cards with a pixel icon, level pips and price) and Staff (one row per role).
/// Books are ordered from the Catalogue now.
/// </summary>
public static class PCShopView
{
    static int tab;

    /// <summary>Upgrade icon classes in PC.uss, in GameManager.UpgradeKind order (kit icons and ones drawn to match).</summary>
    static readonly string[] UpgradeIcons = { "hammer", "glue", "heart", "shoe", "lamp", "armchair", "flyer", "bag", "energy" };

    /// <summary>The next time the Shop opens, start on Upgrades (0) or Staff (1).</summary>
    public static void OpenOn(int subTab) => tab = subTab;

    public static void Wire(VisualElement root)
    {
        root.Q<Button>("tab-upgrades")?.RegisterCallback<ClickEvent>(_ => { tab = 0; Refresh(root); });
        root.Q<Button>("tab-staff")?.RegisterCallback<ClickEvent>(_ => { tab = 1; Refresh(root); });
        Refresh(root);
    }

    static void Refresh(VisualElement root)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        var up = root.Q("tab-upgrades");
        var st = root.Q("tab-staff");
        up?.EnableInClassList("selected", tab == 0);
        st?.EnableInClassList("selected", tab == 1);
        up?.EnableInClassList("ready", gm.UpgradeReady && tab != 0);
        st?.EnableInClassList("ready", gm.StaffReady && tab != 1);

        var list = root.Q<ScrollView>("shop-list");
        float scroll = list.scrollOffset.y;
        list.Clear();
        var note = root.Q<Label>("shop-note");
        if (tab == 0)
        {
            gm.upgradesSeenDay = gm.day; // the reminder glow can rest for today
            note.text = "Upgrades last forever and are saved with your library.";
            var grid = new VisualElement();
            grid.AddToClassList("ugrid");
            for (int i = 0; i < GameManager.UpgradeNames.Length; i++) grid.Add(UpgradeCard(root, gm, (GameManager.UpgradeKind)i));
            list.Add(grid);
        }
        else
        {
            note.text = "Press Interview: two applicants come in tomorrow morning. Staff are paid once, when you hire them.";
            foreach (var role in GameManager.StaffOrder) list.Add(StaffRow(root, gm, role));
        }
        list.schedule.Execute(() => list.scrollOffset = new Vector2(0, scroll));
    }

    static VisualElement UpgradeCard(VisualElement root, GameManager gm, GameManager.UpgradeKind kind)
    {
        int i = (int)kind;
        int lvl = gm.Upgrade(kind);
        bool maxed = lvl >= GameManager.MaxUpgrade;
        bool unlocked = gm.UpgradeUnlocked(kind);
        int cost = GameManager.UpgradeCost(lvl);
        bool canBuy = gm.CanBuyUpgrade(kind);

        var card = new VisualElement();
        card.AddToClassList("ucard");
        card.EnableInClassList("locked", !unlocked && !maxed);
        card.EnableInClassList("ready", canBuy);
        var top = new VisualElement();
        top.AddToClassList("ucard-top");
        top.Add(Icon(UpgradeIcons[i]));
        var text = new VisualElement();
        text.AddToClassList("ucard-text");
        text.Add(Text(GameManager.UpgradeNames[i], "ucard-name", true));
        text.Add(Text(GameManager.UpgradeInfo[i] + (unlocked || maxed ? "" : $" Unlocks on Day {GameManager.UpgradeUnlockDay[i]}."), "ucard-info"));
        top.Add(text);
        card.Add(top);

        var foot = new VisualElement();
        foot.AddToClassList("ucard-foot");
        var pips = new VisualElement();
        pips.AddToClassList("pips");
        for (int p = 0; p < GameManager.MaxUpgrade; p++)
        {
            var pip = new VisualElement();
            pip.AddToClassList("pip");
            pip.EnableInClassList("on", p < lvl);
            pips.Add(pip);
        }
        foot.Add(pips);
        var b = maxed ? PlainButton("Max") : !unlocked ? PlainButton($"Day {GameManager.UpgradeUnlockDay[i]}")
                      : PriceButton("Buy", cost, () => { if (gm.BuyUpgrade(kind)) GameAudio.Play(a => a.buy); Refresh(root); });
        b.SetEnabled(canBuy);
        foot.Add(b);
        card.Add(foot);
        return card;
    }

    static VisualElement StaffRow(VisualElement root, GameManager gm, GameManager.StaffRole role)
    {
        var (title, sub, _, enabled) = gm.StaffRow(role);
        var row = new VisualElement();
        row.AddToClassList("srow");
        row.EnableInClassList("ready", gm.StaffActionReady(role));
        string icon = role == GameManager.StaffRole.Cleaner ? "broom" : role == GameManager.StaffRole.Repairer ? "books" : "key";
        row.Add(Icon(icon));
        var text = new VisualElement();
        text.AddToClassList("srow-text");
        text.Add(Text(title, "srow-title", true));
        text.Add(Text(sub, "srow-sub"));
        row.Add(text);

        Action click = () => { gm.ToggleStaff(role); Refresh(root); };
        Button b;
        if (gm.Hired(role))
        {
            int cost = gm.StaffUpgradeCost(role);
            b = cost < 0 ? PlainButton("Max level") : PriceButton("Upgrade", cost, click);
        }
        else if (gm.InterviewBooked(role)) b = PlainButton("Cancel", click);
        else if (!gm.StaffUnlocked(role)) b = PlainButton($"Day {GameManager.StaffUnlockDay[(int)role]}");
        else b = PlainButton("Interview", click);
        b.SetEnabled(enabled);
        row.Add(b);
        return row;
    }

    // ---------------- Pieces ----------------

    /// <summary>A framed pixel icon; the picture and its 3x size come from .uicon-NAME in PC.uss.</summary>
    static VisualElement Icon(string name)
    {
        var box = new VisualElement();
        box.AddToClassList("uico");
        var img = new VisualElement();
        img.AddToClassList("uico-img");
        img.AddToClassList("uicon-" + name);
        box.Add(img);
        return box;
    }

    static Label Text(string text, string cls, bool title = false)
    {
        var l = new Label(text);
        l.AddToClassList(cls);
        if (title) l.AddToClassList("font-title");
        return l;
    }

    static Button PlainButton(string text, Action onClick = null)
    {
        var b = new Button(() => onClick?.Invoke()) { text = text };
        b.AddToClassList("pc-button");
        b.AddToClassList("small");
        return b;
    }

    /// <summary>"Buy -(coin)80": the price with the kit coin.</summary>
    public static Button PriceButton(string verb, int cost, Action onClick)
    {
        var b = new Button(() => onClick?.Invoke());
        b.AddToClassList("pc-button");
        b.AddToClassList("small");
        b.Add(Text(verb, "btn-text"));
        var minus = Text("-", "btn-text");
        minus.AddToClassList("cost-minus");
        b.Add(minus);
        var coin = new VisualElement();
        coin.AddToClassList("coin-icon");
        b.Add(coin);
        b.Add(Text(cost.ToString(), "btn-text"));
        foreach (var child in b.Children()) child.pickingMode = PickingMode.Ignore;
        return b;
    }
}
