using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Now and then a staff member gets stuck and stops work until you come over: a "Help!" pointer shows
/// where they are (pinned to the screen edge when they're off screen). Click them to sort it out.
/// At closing the same pointer says "Report": they wait to tell you about their day.
/// Upgraded staff need help less often. Added to staff characters by StaffManager.
/// </summary>
public class StaffHelp : MonoBehaviour
{
    /// <summary>A problem and two ways to fix it. The first costs coins and fixes it straight away;
    /// the second is free but takes them a little while to get going again.</summary>
    public class Problem { public string line, paid, paidReply, free, freeReply; public int cost; }

    static Problem P(string line, string paid, int cost, string paidReply, string free, string freeReply) =>
        new Problem { line = line, paid = paid, cost = cost, paidReply = paidReply, free = free, freeReply = freeReply };

    // By StaffRole: Repairer, Cleaner, RareAttendant.
    static readonly Problem[][] Problems =
    {
        new[]
        {
            P("We're out of binding glue. I can't mend a single thing.", "Order more glue", 10, "Fresh glue! Back to the books.",
              "Try the old pot in the cupboard", "It's more lumps than glue... give me a minute to stir it."),
            P("The typewriter ribbon snapped in the middle of the catalogue.", "Buy a new ribbon", 6, "Clack clack. Lovely.",
              "Write it up by hand for now", "By hand it is. My poor wrist."),
            P("This book's pages are stuck together. Tea, I think. A lot of tea.", "Buy a page press", 12, "That'll flatten it nicely. Thank you.",
              "Use blotting paper between the pages", "Slow, but it'll work. Leave it with me."),
        },
        new[]
        {
            P("The mop bucket's sprung a leak, it's all over the History aisle!", "Buy a new bucket", 8, "Brand new! Back to it, boss.",
              "Patch it with tape", "Tape... right. Let me mop up the mop water first."),
            P("Someone's stuck chewing gum under every reading table. Every. One.", "Get a scraper set", 6, "Scrape scrape. Leave it to me.",
              "Try ice cubes from the staff room", "Freeze it off. Clever. Takes a while, mind."),
            P("A pigeon got in. It's sitting on the chandelier, judging me.", "Call the warden", 5, "He's caught it! Pigeon's gone home.",
              "Open a window and wait", "Shoo... shoo... it's thinking about it."),
        },
        new[]
        {
            P("A scholar wants to touch a 400-year-old map with bare hands!", "Buy cotton gloves for guests", 8, "Gloves for everyone. Crisis averted.",
              "Go and have a word with them", "They listened to you. Back to my post."),
            P("The Rare Books room is too warm. The parchment's starting to curl.", "Buy a little fan", 10, "Ah, a breeze. The books thank you.",
              "Prop the door open for a while", "Better. It'll take a bit to cool down."),
            P("Someone asked to borrow a first edition. Borrow! Take it home!", "Print a 'reading room only' sign", 5, "Nobody argues with a sign.",
              "Explain the rules yourself", "Politely put. I'll take it from here."),
        },
    };

    public GameManager.StaffRole role;
    /// <summary>The open problem, if any.</summary>
    public Problem Open { get; private set; }
    public bool Reporting { get; private set; }
    /// <summary>Problems you sorted out today.</summary>
    public int helpedToday;

    StaffWorker worker;
    StaffTalk talk;
    WorldPrompts.Follower pointer;
    float timer, holdUntil = -1f;

    void Awake()
    {
        worker = GetComponent<StaffWorker>();
        talk = GetComponent<StaffTalk>();
    }

    void OnEnable() => ResetTimer();
    void OnDisable() => HidePointer();

    void ResetTimer()
    {
        var gm = GameManager.Instance;
        int level = gm != null ? gm.StaffLevel(role) : 0;
        timer = Random.Range(80f, 150f) * (1 + level);
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || worker == null) return;
        // A free fix: back to work after a short while.
        if (holdUntil > 0f) { holdUntil -= GameManager.Dt; if (holdUntil <= 0f) { holdUntil = -1f; worker.SetHold(false); } return; }
        if (Open != null || Reporting || gm.CurrentPhase != GameManager.Phase.Open) return;
        timer -= GameManager.Dt;
        if (timer <= 0f) Raise();
    }

    /// <summary>Stops work with a random problem for their job.</summary>
    public void Raise()
    {
        var list = Problems[(int)role];
        Open = list[Random.Range(0, list.Length)];
        worker.SetHold(true);
        ShowPointer("Help!");
        if (talk != null && WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(talk, Open.line, 4f);
        GameAudio.Play(a => a.impatient, 0.6f);
    }

    /// <summary>The problem dialogue for StaffTalk (null if nothing's wrong).</summary>
    public DialogueAsset ProblemDialogue(string speaker)
    {
        if (Open == null) return null;
        var gm = GameManager.Instance;
        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = speaker;
        var start = new DialogueAsset.Line { id = "start", text = Open.line };
        bool afford = gm != null && gm.Coins >= Open.cost;
        start.choices.Add(new DialogueAsset.Choice { text = afford ? $"{Open.paid} ({Open.cost} coins)" : $"{Open.paid} (need {Open.cost})", next = afford ? "paid" : "start" });
        start.choices.Add(new DialogueAsset.Choice { text = Open.free, next = "free" });
        d.lines.Add(start);
        d.lines.Add(new DialogueAsset.Line { id = "paid", text = Open.paidReply, next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "free", text = Open.freeReply, next = "end" });
        return d;
    }

    /// <summary>Called with the branch the player took ("paid" or "free").</summary>
    public void Resolve(string how)
    {
        if (Open == null || how != "paid" && how != "free") return;
        var gm = GameManager.Instance;
        if (how == "paid" && (gm == null || !gm.TrySpend(Open.cost))) return;
        Open = null;
        helpedToday++;
        HidePointer();
        GameAudio.Play(a => a.served, 0.5f);
        if (how == "paid") worker.SetHold(false);
        else holdUntil = 8f; // the free fix takes them a moment
        ResetTimer();
    }

    // ---------------- End of the day ----------------

    /// <summary>Closing time: wait for the librarian with a report pointer.</summary>
    public void WaitToReport()
    {
        if (Open != null) { Open = null; worker.SetHold(false); }
        holdUntil = -1f;
        Reporting = true;
        ShowPointer("Report");
    }

    public void ReportDone() { Reporting = false; HidePointer(); }

    /// <summary>A new day: fresh counters, no pointer.</summary>
    public void NewDay()
    {
        Open = null; Reporting = false; holdUntil = -1f; helpedToday = 0;
        HidePointer();
        ResetTimer();
    }

    // ---------------- The pointer ----------------

    void ShowPointer(string text)
    {
        HidePointer();
        var wp = WorldPrompts.Instance;
        if (wp == null) return;
        var el = new VisualElement();
        el.AddToClassList("staff-pointer");
        var icon = new VisualElement();
        icon.AddToClassList("staff-pointer-icon");
        el.Add(icon);
        el.Add(new Label(text) { name = "staff-pointer-label" });
        var arrow = new VisualElement();
        arrow.AddToClassList("staff-pointer-arrow");
        el.Add(arrow);
        pointer = wp.Pin(el, arrow, () => transform.position + Vector3.up * 2.1f);
    }

    void HidePointer()
    {
        if (pointer != null && WorldPrompts.Instance != null) WorldPrompts.Instance.Unfollow(pointer);
        pointer = null;
    }
}
