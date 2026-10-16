using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// PC "Members" screen: the card list (access, hearts, visits) and the New Card form,
/// filled by picking what the waiting visitor told you (no typing, so it works on WebGL).
/// </summary>
public static class PCMembersView
{
    static string pickName, pickDob;
    static AccessLevel? pickAccess;
    static Customer formFor;
    static string[] nameOptions, dobOptions;

    public static void Wire(VisualElement root, Action back)
    {
        root.Q<Button>("back-button")?.RegisterCallback<ClickEvent>(_ => back());
        root.Q<Button>("card-issue")?.RegisterCallback<ClickEvent>(_ => Issue(root));
        BuildList(root);
        BuildForm(root);
    }

    static void BuildList(VisualElement root)
    {
        var list = root.Q<ScrollView>("members-list");
        if (list == null) return;
        list.Clear();
        if (Members.All.Count == 0)
            list.Add(new Label("No members yet. Visitors who ask for a library card can sign up here.") { name = "member-empty" });
        foreach (var m in Members.All)
        {
            var row = new VisualElement();
            row.AddToClassList("pc-book");
            row.Add(new Label(m.name) { name = "book-title" });
            row.Add(new Label($"Born {m.dob}  ·  {m.access} access  ·  {m.visits} visits  ·  spent {m.spent}") { name = "book-sub" });
            var hearts = new VisualElement();
            hearts.AddToClassList("member-hearts");
            for (int i = 0; i < 5; i++)
            {
                var h = new VisualElement();
                h.AddToClassList("member-heart");
                h.EnableInClassList("empty", i >= m.Hearts);
                hearts.Add(h);
            }
            row.Add(hearts);
            list.Add(row);
        }
    }

    static void BuildForm(VisualElement root)
    {
        var who = Customer.WaitingForCard;
        root.Q("card-empty")?.EnableInClassList("hidden", who != null);
        root.Q("card-body")?.EnableInClassList("hidden", who == null);
        if (who == null) return;

        if (formFor != who)
        {
            formFor = who;
            pickName = pickDob = null;
            pickAccess = null;
            nameOptions = Members.Options(who.profileKey, false);
            dobOptions = Members.Options(who.profileKey, true);
            root.Q<Label>("card-feedback").text = "";
        }
        Options(root.Q("card-name"), nameOptions, () => pickName, v => pickName = v, root);
        Options(root.Q("card-dob"), dobOptions, () => pickDob, v => pickDob = v, root);
        Options(root.Q("card-access"), Enum.GetNames(typeof(AccessLevel)), () => pickAccess?.ToString(),
                v => pickAccess = (AccessLevel)Enum.Parse(typeof(AccessLevel), v), root);
        root.Q<Button>("card-issue")?.SetEnabled(pickName != null && pickDob != null && pickAccess != null);
    }

    static void Options(VisualElement box, string[] values, Func<string> get, Action<string> set, VisualElement root)
    {
        if (box == null) return;
        box.Clear();
        foreach (var v in values)
        {
            var value = v;
            var b = new Button(() => { set(value); BuildForm(root); }) { text = value };
            b.AddToClassList("pc-button");
            b.AddToClassList("card-option");
            b.EnableInClassList("picked", get() == value);
            box.Add(b);
        }
    }

    static void Issue(VisualElement root)
    {
        var who = Customer.WaitingForCard;
        if (who == null || pickName == null || pickDob == null || pickAccess == null) return;
        string wrong = who.ReceiveCard(pickName, pickDob, pickAccess.Value);
        if (wrong == null)
        {
            formFor = null;
            root.Q<Label>("card-empty").text = $"Card issued to {pickName}!";
        }
        else root.Q<Label>("card-feedback").text = $"\"{wrong}\"";
        BuildList(root);
        BuildForm(root);
    }
}
