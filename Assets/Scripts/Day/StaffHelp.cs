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

    // By StaffRole: Repairer, Cleaner, RareAttendant. Eight each; no repeats in the same day.
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
            P("The spine on this atlas has given up completely. It's more of a suggestion now.", "Order bookbinding thread", 9, "Proper waxed thread. Lovely.",
              "Use string from the post room", "String. Well, it holds. Mostly."),
            P("The desk lamp's blown. I'm squinting at page numbers like a mole.", "Buy a new bulb", 4, "Let there be light.",
              "Work by the window for now", "The window it is. My neck will hate me by four o'clock."),
            P("Silverfish. In the Archive boxes. I'm fairly sure one waved at me.", "Get proper storage boxes", 12, "Acid-free and bug-free. Thank you.",
              "Shake the boxes out and hope", "They looked offended, but they've gone. For now."),
            P("My scalpel's gone blunt. I'm sawing at this binding like it owes me money.", "Buy fresh blades", 6, "Sharp as anything. Mind your fingers, everyone.",
              "Sharpen it on the front step", "A scrape on the stone and... close enough."),
            P("Someone returned a book with toast as a bookmark. Buttered side down.", "Buy cleaning wipes", 5, "Toast removed. Dignity restored.",
              "Scrape it off very carefully", "Done. I may never eat toast again."),
        },
        new[]
        {
            P("The mop bucket's sprung a leak, it's all over the History aisle!", "Buy a new bucket", 8, "Brand new! Back to it, boss.",
              "Patch it with tape", "Tape... right. Let me mop up the mop water first."),
            P("Someone's stuck chewing gum under every reading table. Every. One.", "Get a scraper set", 6, "Scrape scrape. Leave it to me.",
              "Try ice cubes from the staff room", "Freeze it off. Clever. Takes a while, mind."),
            P("A pigeon got in. It's sitting on the chandelier, judging me.", "Call the warden", 5, "He's caught it! Pigeon's gone home.",
              "Open a window and wait", "Shoo... shoo... it's thinking about it."),
            P("The vacuum's eaten a bookmark and now it's making a noise like a goose.", "Get it repaired", 8, "Good as new. No more goose.",
              "Shake it about a bit", "It's quieter. Still a bit goose-y."),
            P("Someone's spilled a whole cocoa down the stairs. Every step.", "Buy a proper mop head", 6, "This thing drinks cocoa. Back in a jiffy.",
              "Use the old rags", "Rags it is. I'll be a while."),
            P("We're out of bin bags. I'm carrying rubbish around like a baby.", "Order bin bags", 4, "Bags! Freedom!",
              "Use a cardboard box", "Box it is. It's a very full box."),
            P("There's a mouse behind the radiator. It's tiny. I'm not scared. I'm a bit scared.", "Call someone to catch it", 10, "Caught and let go in the park. It looked pleased about it.",
              "Leave some cheese by the back door", "Cheese is out. The mouse and I have an understanding."),
            P("The front mat's soaked through. Everyone's walking puddles over my clean floor.", "Buy a new doormat", 7, "Dry feet for all. Ahh.",
              "Lay down old newspaper", "Done. Today's headlines are getting walked on."),
        },
        new[]
        {
            P("A scholar wants to touch a 400-year-old map with bare hands!", "Buy cotton gloves for guests", 8, "Gloves for everyone. Crisis averted.",
              "Go and have a word with them", "They listened to you. Back to my post."),
            P("The Rare Books room is too warm. The parchment's starting to curl.", "Buy a little fan", 10, "Ah, a breeze. The books thank you.",
              "Prop the door open for a while", "Better. It'll take a bit to cool down."),
            P("Someone asked to borrow a first edition. Borrow! Take it home!", "Print a 'reading room only' sign", 5, "Nobody argues with a sign.",
              "Explain the rules yourself", "Politely put. I'll take it from here."),
            P("The display case lock has jammed. The scholars are peering in like it's an aquarium.", "Call the locksmith", 12, "Opened with a click. Very satisfying.",
              "Jiggle it with a hairpin", "A hairpin and patience. And a little swearing, I'm afraid."),
            P("A professor's fallen asleep face down on a 17th-century herbal. Snoring on it.", "Buy a reading stand and cushions", 9, "Upright reading only now. Comfier, too.",
              "Wake them gently", "Woken. Mortified. The herbal survived."),
            P("The magnifying lamp keeps flickering. Three professors are squinting at one footnote.", "Buy a new lamp", 10, "Bright as noon. They found the footnote.",
              "Lend them the window seat", "Natural light. Very scholarly. Slower, mind."),
            P("Someone's written notes in the margin of a 1740 almanac. In pencil! Notes!", "Buy a soft eraser and a sign", 7, "Gently erased, and a sign so it never happens again.",
              "Have a stern word", "A stern word was had. They've gone quite pale."),
            P("The damp gauge says the room's wet. The vellum's going wavy.", "Order a dehumidifier", 14, "Hum hum. The vellum's settling down.",
              "Open the windows for a bit", "Windows open. Chilly, but drier. Slowly."),
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
    float timer, remind, waited;
    /// <summary>Problems already had today (no repeats in the same day).</summary>
    readonly System.Collections.Generic.HashSet<int> usedToday = new System.Collections.Generic.HashSet<int>();
    /// <summary>You helped or chatted with them today (anyone left alone all day gets grumpy).</summary>
    public bool attentionToday;
    /// <summary>A paid fix today: they come in rested tomorrow.</summary>
    public bool paidToday;
    const float QuickHelp = 20f; // seconds of game time: sorting it out this fast cheers them up

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
        // Still stuck: remind you now and then.
        if (Open != null)
        {
            waited += GameManager.Dt;
            remind -= GameManager.Dt;
            if (remind <= 0f && !Reporting && talk != null && WorldPrompts.Instance != null)
            {
                // Still waiting for you: a reminder, and they get a little more fed up each time.
                gm.ChangeStaffMood(role, -0.15f);
                WorldPrompts.Instance.ShowBubble(talk, Open.line, 3.5f);
                remind = 30f;
            }
            return;
        }
        if (Reporting || gm.CurrentPhase != GameManager.Phase.Open || Tutorial.HoldsClock) return; // no problems mid-tutorial
        timer -= GameManager.Dt;
        if (timer <= 0f) Raise();
    }

    /// <summary>Stops work with a random problem for their job.</summary>
    public void Raise()
    {
        var list = Problems[(int)role];
        if (usedToday.Count >= list.Length) usedToday.Clear(); // had every one today: start over
        int pick;
        do pick = Random.Range(0, list.Length); while (usedToday.Contains(pick));
        usedToday.Add(pick);
        Open = list[pick];
        waited = 0f;
        remind = 30f;
        worker.SetHold(true);
        ShowPointer("Help!");
        Tutorial.Show("staffhelp", "A staff member needs help! Click them: a paid fix keeps them happy and faster; a free one slows them down.");
        if (talk != null && WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(talk, Open.line, 4f);
        StartCoroutine(Ring()); // its own sound, three rings (the counter bell means a visitor)
    }

    /// <summary>The "staff help" ring, three times (stops early if you've already sorted it out).</summary>
    System.Collections.IEnumerator Ring()
    {
        for (int i = 0; i < 3 && Open != null; i++)
        {
            GameAudio.Play(a => a.staffHelp != null ? a.staffHelp : a.impatient, 0.8f);
            yield return new WaitForSecondsRealtime(0.7f);
        }
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
        start.choices.Add(new DialogueAsset.Choice { text = afford ? $"{Open.paid} ({Open.cost} coins: happier, faster tomorrow)" : $"{Open.paid} (need {Open.cost})", next = afford ? "paid" : "start" });
        start.choices.Add(new DialogueAsset.Choice { text = $"{Open.free} (free, but slower today, and they won't love it)", next = "free" });
        d.lines.Add(start);
        d.lines.Add(new DialogueAsset.Line { id = "paid", text = Open.paidReply + " <color=#D67840>Thanks for doing it properly.</color>", next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "free", text = Open.freeReply, next = "end" });
        return d;
    }

    /// <summary>Called with the branch the player took ("paid" or "free").</summary>
    public void Resolve(string how)
    {
        if (Open == null || how != "paid" && how != "free") return;
        var gm = GameManager.Instance;
        if (how == "paid" && (gm == null || !gm.TrySpend(Open.cost))) return;
        if (how == "paid") RequestLog.Money(GameManager.StaffPerson[(int)role], "Paid to fix a problem straight away", "Staff help", -Open.cost);
        Open = null;
        helpedToday++;
        attentionToday = true;
        HidePointer();
        GameAudio.Play(a => a.served, 0.5f);
        worker.SetHold(false);
        // What they'll remember: a proper fix cheers them up and they come in rested tomorrow; a makeshift one
        // slows them down for the rest of today and grates a little. Coming over quickly always helps.
        float mood = how == "paid" ? 0.5f : -0.3f;
        if (waited < QuickHelp) mood += 0.2f;
        if (gm != null)
        {
            gm.ChangeStaffMood(role, mood);
            if (how == "paid") { gm.staffRestedDay[(int)role] = gm.day + 1; paidToday = true; }
        }
        if (how == "free") worker.Slow = true; // makeshift fix: they work, but slower for the rest of the day
        ResetTimer();
    }

    // ---------------- End of the day ----------------

    /// <summary>Closing time: wait for the librarian with a report pointer.</summary>
    public void WaitToReport()
    {
        if (Open != null) { Open = null; worker.SetHold(false); }
        Reporting = true;
        ShowPointer("Report");
    }

    public void ReportDone() { Reporting = false; HidePointer(); }

    /// <summary>A new day: fresh counters, no pointer.</summary>
    public void NewDay()
    {
        Open = null; Reporting = false; helpedToday = 0;
        usedToday.Clear(); attentionToday = false; paidToday = false;
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
