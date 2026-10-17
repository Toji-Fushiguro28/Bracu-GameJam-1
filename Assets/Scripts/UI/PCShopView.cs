using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Shop" screen: buy book copies, buy upgrades, hire staff. Same terminal look as the catalogue.
/// </summary>
public static class PCShopView
{
    static int tab;

    public static void Wire(VisualElement root, Action back)
    {
        root.Q<Button>("back-button")?.RegisterCallback<ClickEvent>(_ => back());
        string[] names = { "tab-books", "tab-upgrades", "tab-staff" };
        for (int i = 0; i < names.Length; i++)
        {
            int t = i;
            root.Q<Button>(names[i])?.RegisterCallback<ClickEvent>(_ => { tab = t; Refresh(root); });
        }
        Refresh(root);
    }

    static void Refresh(VisualElement root)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        root.Q("tab-books")?.EnableInClassList("selected", tab == 0);
        root.Q("tab-upgrades")?.EnableInClassList("selected", tab == 1);
        root.Q("tab-staff")?.EnableInClassList("selected", tab == 2);
        root.Q<Label>("shop-coins").text = $"{gm.Coins} coins";

        var list = root.Q<ScrollView>("shop-list");
        float scroll = list.scrollOffset.y;
        list.Clear();
        var note = root.Q<Label>("shop-note");
        if (tab == 0)
        {
            note.text = $"Order: arrives tomorrow morning. Express (+50%): arrives in {GameManager.ExpressHours:0} hours. Deliveries go to the book cart.";
            // Reservations first: these people are counting on you.
            if (gm.reservations.Count > 0)
            {
                list.Add(Header("Reservations"));
                foreach (var r in gm.reservations)
                {
                    var rb = gm.Books.Find(x => x.Title == r.title);
                    bool coming = rb != null && (gm.OnOrder(rb) > 0 || BookRequest.InStock(rb) || (DeliveryCart.Instance != null && DeliveryCart.Instance.Has(rb)));
                    var l = new Label($"{r.name}: \"{r.title}\" - {(coming ? "ready or on its way" : "NOT ORDERED YET")}");
                    l.AddToClassList("shop-reserve");
                    l.EnableInClassList("warn", !coming);
                    list.Add(l);
                }
                list.Add(Header("Order books"));
            }
            foreach (var b in gm.Books)
            {
                if (BookRequest.IsRare(b) || !BookRequest.Available(b)) continue; // locked rooms can't be stocked yet
                var book = b;
                int price = GameManager.OrderPrice(book, false), express = GameManager.OrderPrice(book, true);
                int onOrder = gm.OnOrder(book);
                string sub = $"{book.FormatName} · {book.Subject} · {ShelfInteractable.NameOf(book.ShelfLocation)} · in stock {book.GoodCopies + book.WornCopies}";
                if (onOrder > 0) sub += $" · on order {onOrder}";
                if (gm.reservations.Exists(r => r.title == book.Title)) sub += " · RESERVED";
                Row(list, book.Title, sub, null, $"Order {price}", gm.Coins >= price,
                    () => { if (gm.OrderBook(book, false)) GameAudio.Play(a => a.buy); }, root,
                    gm.CurrentPhase == GameManager.Phase.Closed ? null : $"Express {express}", gm.Coins >= express,
                    () => { if (gm.OrderBook(book, true)) GameAudio.Play(a => a.buy); });
            }
        }
        else if (tab == 1)
        {
            note.text = "Upgrades last forever and are saved with your library.";
            for (int i = 0; i < GameManager.UpgradeNames.Length; i++)
            {
                var kind = (GameManager.UpgradeKind)i;
                int lvl = gm.Upgrade(kind);
                bool maxed = lvl >= GameManager.MaxUpgrade;
                int cost = GameManager.UpgradeCost(lvl);
                Row(list, GameManager.UpgradeNames[i], GameManager.UpgradeInfo[i], lvl,
                    maxed ? "Max" : $"Buy {cost}", !maxed && gm.Coins >= cost,
                    () => { if (gm.BuyUpgrade(kind)) GameAudio.Play(a => a.buy); }, root, icon: i);
            }
        }
        else
        {
            note.text = gm.DailyWages > 0 ? $"Wages are paid each morning: {gm.DailyWages} coins a day." : "Staff are paid each morning. If you can't pay, they quit.";
            foreach (var role in GameManager.StaffOrder)
            {
                int i = (int)role;
                bool hired = gm.Hired(role), open = gm.StaffUnlocked(role);
                Row(list, GameManager.StaffNames[i] + (hired ? "  (hired)" : ""),
                    $"{GameManager.StaffInfo[i]}  Wage {GameManager.StaffWage[i]} a day.", null,
                    hired ? "Let go" : open ? "Hire" : $"Day {GameManager.StaffUnlockDay[i]}", hired || open,
                    () => gm.ToggleStaff(role), root);
            }
        }
        list.schedule.Execute(() => list.scrollOffset = new Vector2(0, scroll));
    }

    static Label Header(string text)
    {
        var l = new Label(text);
        l.AddToClassList("shop-header");
        return l;
    }

    static void Row(ScrollView list, string title, string sub, int? level, string button, bool enabled, Action onClick, VisualElement root,
                    string button2 = null, bool enabled2 = false, Action onClick2 = null, int icon = -1)
    {
        var row = new VisualElement();
        row.AddToClassList("shop-row");
        if (icon >= 0)
        {
            var ic = new VisualElement();
            ic.AddToClassList("upgrade-icon");
            ic.AddToClassList("upg-" + icon); // kit icon set in PC.uss
            row.Add(ic);
        }
        var text = new VisualElement();
        text.AddToClassList("shop-row-text");
        text.Add(new Label(title) { name = "book-title" });
        text.Add(new Label(sub) { name = "book-sub" });
        if (level.HasValue)
        {
            var pips = new VisualElement();
            pips.AddToClassList("shop-level");
            for (int i = 0; i < GameManager.MaxUpgrade; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("shop-pip");
                pip.EnableInClassList("on", i < level.Value);
                pips.Add(pip);
            }
            text.Add(pips);
        }
        row.Add(text);
        var b = new Button(() => { onClick(); Refresh(root); }) { text = button };
        b.AddToClassList("pc-button");
        b.SetEnabled(enabled);
        row.Add(b);
        if (button2 != null)
        {
            var b2 = new Button(() => { onClick2?.Invoke(); Refresh(root); }) { text = button2 };
            b2.AddToClassList("pc-button");
            b2.AddToClassList("second");
            b2.SetEnabled(enabled2);
            row.Add(b2);
        }
        list.Add(row);
    }
}
