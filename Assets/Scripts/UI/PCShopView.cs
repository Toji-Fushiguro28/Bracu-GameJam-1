using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Shop": Upgrades (cards with a pixel icon, level pips and price) and Staff (one card per role).
/// Books are ordered from the Catalogue now.
/// </summary>
public static class PCShopView
{
    static int tab;

    /// <summary>Upgrade icon classes in PC.uss, in GameManager.UpgradeKind order (kit icons and ones drawn to match).</summary>
    static readonly string[] UpgradeIcons = { "hammer", "glue", "heart", "shoe", "lamp", "armchair", "flyer", "bag", "energy" };

    /// <summary>Upgrades page order: your own first (shoes, bag, stamina), then visitors, tips and fees, repairs.
    /// Ones not unlocked yet move to the end (Refresh).</summary>
    static readonly GameManager.UpgradeKind[] UpgradeOrder =
    {
        GameManager.UpgradeKind.RunningShoes, GameManager.UpgradeKind.BookBag, GameManager.UpgradeKind.Stamina,
        GameManager.UpgradeKind.ComfyQueue, GameManager.UpgradeKind.TownFlyers, GameManager.UpgradeKind.CozyLamps,
        GameManager.UpgradeKind.ReadingNooks, GameManager.UpgradeKind.RepairTools, GameManager.UpgradeKind.RestorationKit,
    };

    /// <summary>The next time the Shop opens, start on Upgrades (0) or Staff (1).</summary>
    public static void OpenOn(int subTab) => tab = subTab;
    /// <summary>The sub-tab showing: Upgrades (0) or Staff (1).</summary>
    public static int Tab => tab;

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
            foreach (var k in UpgradeOrder) if (gm.UpgradeUnlocked(k)) grid.Add(UpgradeCard(root, gm, k));
            foreach (var k in UpgradeOrder) if (!gm.UpgradeUnlocked(k)) grid.Add(UpgradeCard(root, gm, k)); // locked: last
            list.Add(grid);
        }
        else
        {
            note.text = "Booking an interview pays the hire price now. Two applicants come the next morning; whatever you save by haggling comes back.";
            var grid = new VisualElement();
            grid.AddToClassList("ugrid");
            foreach (var role in GameManager.StaffOrder) grid.Add(StaffCard(root, gm, role));
            list.Add(grid);
        }
        list.schedule.Execute(() => list.scrollOffset = new Vector2(0, scroll));
    }

    static VisualElement UpgradeCard(VisualElement root, GameManager gm, GameManager.UpgradeKind kind)
    {
        int i = (int)kind;
        int lvl = gm.Upgrade(kind);
        bool maxed = lvl >= GameManager.MaxUpgrade;
        bool unlocked = gm.UpgradeUnlocked(kind);
        int cost = GameManager.UpgradeCost(kind, lvl);
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
        b.SetEnabled(canBuy && !Tutorial.HoldsClock); // the tutorial only shows the Shop: no buying in practice
        foot.Add(b);
        card.Add(foot);
        return card;
    }

    /// <summary>Staff icons in GameManager.StaffRole order (Archivist, Cleaner, attendant).</summary>
    static readonly string[] StaffIcons = { "books", "broom", "key" };

    /// <summary>One staff member's card: who they are, what they do, what the next level brings, and the button
    /// (Interview with the hire price, Upgrade, or why not yet).</summary>
    static VisualElement StaffCard(VisualElement root, GameManager gm, GameManager.StaffRole role)
    {
        int i = (int)role;
        bool hired = gm.Hired(role), booked = gm.InterviewBooked(role), unlocked = gm.StaffUnlocked(role);
        int lvl = gm.StaffLevel(role), cost = gm.StaffUpgradeCost(role), fee = GameManager.StaffHireFee[i];

        var card = new VisualElement();
        card.AddToClassList("ucard");
        card.AddToClassList("scard");
        card.EnableInClassList("locked", !unlocked && !hired);
        card.EnableInClassList("ready", gm.StaffActionReady(role));

        var top = new VisualElement();
        top.AddToClassList("ucard-top");
        top.Add(Icon(StaffIcons[i]));
        var text = new VisualElement();
        text.AddToClassList("ucard-text");
        text.Add(Text(hired ? GameManager.StaffPerson[i] : GameManager.StaffNames[i], "ucard-name", true));
        text.Add(Text(hired ? GameManager.StaffNames[i] : booked ? "Interview tomorrow morning" : unlocked ? "Not hired yet" : $"Opens on Day {GameManager.StaffUnlockDay[i]}", "ucard-info"));
        top.Add(text);
        card.Add(top);

        if (hired)
        {
            var row = new VisualElement();
            row.AddToClassList("scard-level");
            var pips = new VisualElement();
            pips.AddToClassList("pips");
            for (int p = 0; p <= GameManager.StaffUpgradeCosts[i].Length; p++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                pip.EnableInClassList("on", p <= lvl);
                pips.Add(pip);
            }
            row.Add(pips);
            row.Add(Text($"Mood: {GameManager.MoodWord(gm.staffMood[i])}" + (gm.StaffRested(role) ? ", rested" : ""), "scard-mood"));
            card.Add(row);
        }
        else
        {
            // The price, up top where it's easy to see.
            var price = new VisualElement();
            price.AddToClassList("scard-price");
            var coin = new VisualElement();
            coin.AddToClassList("coin-icon");
            price.Add(coin);
            price.Add(Text(booked ? $"{gm.hirePaid[i]} set aside" : $"Hire: {fee}", "scard-price-text"));
            card.Add(price);
        }

        card.Add(Text("What they do", "scard-head"));
        foreach (var line in GameManager.StaffDoes[i]) card.Add(Line(line));
        if (GameManager.StaffCarries(role)) card.Add(Line($"bag|Carries {Books(gm.StaffCarry(role))} per trip"));

        if (hired && cost >= 0)
        {
            var next = new VisualElement();
            next.AddToClassList("scard-next");
            next.Add(Text($"Next: Level {lvl + 2}", "scard-head"));
            next.Add(Line("energy|Works 35% faster"));
            next.Add(Line("ask|Needs your help less often"));
            if (GameManager.StaffCarries(role)) next.Add(Line($"bag|Carries {Books(gm.StaffCarry(role) + 1)} per trip"));
            card.Add(next);
        }
        else if (!hired)
        {
            var later = new VisualElement();
            later.AddToClassList("scard-next");
            later.AddToClassList("later");
            later.Add(Text("Upgrades later", "scard-head"));
            later.Add(Text(string.Join("  ·  ", GameManager.StaffUpgradeCosts[i].Select((c, n) => $"Level {n + 2}: {c}")), "scard-text"));
            later.Add(Text("Each: faster, needs less help" + (GameManager.StaffCarries(role) ? ", +1 book per trip" : ""), "scard-text"));
            card.Add(later);
        }

        var spacer = new VisualElement();
        spacer.AddToClassList("scard-spacer");
        card.Add(spacer);
        var foot = new VisualElement();
        foot.AddToClassList("ucard-foot");
        Action click = () => { gm.ToggleStaff(role); Refresh(root); };
        Button b;
        if (hired) b = cost < 0 ? PlainButton("Max level") : PriceButton("Upgrade", cost, click);
        else if (booked) b = PlainButton("Booked");
        else if (!unlocked) b = PlainButton($"Day {GameManager.StaffUnlockDay[i]}");
        else b = PriceButton("Interview", fee, click);
        b.SetEnabled(!Tutorial.HoldsClock && (hired ? cost >= 0 && gm.Coins >= cost : !booked && unlocked && gm.Coins >= fee));
        foot.Add(b);
        card.Add(foot);
        return card;
    }

    static string Books(int n) => n == 1 ? "1 book" : $"{n} books";

    /// <summary>"icon|text": a small pixel icon (.si-NAME) and a line of text.</summary>
    static VisualElement Line(string iconAndText)
    {
        int bar = iconAndText.IndexOf('|');
        var row = new VisualElement();
        row.AddToClassList("scard-line");
        var icon = new VisualElement();
        icon.AddToClassList("si");
        icon.AddToClassList("si-" + iconAndText.Substring(0, bar));
        row.Add(icon);
        row.Add(Text(iconAndText.Substring(bar + 1), "scard-text"));
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
