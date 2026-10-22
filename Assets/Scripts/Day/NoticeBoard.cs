using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The notice board: click it to open the board (NoticeBoardUI), with today's important papers pinned up
/// (the day's events, rooms opening, pickups, deliveries, popular books, the Dean's goal) and a few of the
/// joke notices below. It glows each morning until you've read it.
/// </summary>
public class NoticeBoard : ClickInteractable
{
    [TextArea(1, 3)]
    public string[] notices =
    {
        // General library notices
        "The guy wearing a hat has been seen near the restricted section again.",
        "If you see a book staring back at you, no you didn't.",
        "The library ghost has been promoted to Senior Librarian.",
        "Whoever keeps moving the chairs, we know what you did.",
        "Sleeping in the library does not count as studying. Probably.",
        "Whoever left 17 books on one table, please seek help.",
        "Group study is not a competition to see who can talk the loudest.",
        "Your assignment is due tomorrow. This is your sign.",
        "The library is not a dating app. Please stop making eye contact across the shelves.",
        "If you hear whispering from the basement, please ignore it.",
        "Today's weather forecast predicts a 90% chance of academic suffering.",
        // BRAC University notices
        "Advising season has begun. May the odds be ever in your favor.",
        "Someone said, \"This trimester will be easy.\" They have been reported missing.",
        "The person who said \"just one more course\" is wanted dead or alive.",
        "Please stop calculating your CGPA during library hours. The calculators are scared.",
        "The group project has one active member. Investigations are ongoing.",
        "Someone has reserved a study space and disappeared for three hours.",
        "Turnitin has entered the chat.",
        "All the good sections are full. Have a wonderful day.",
        "\"I'll start tonight\" has once again been recorded as the most popular academic fiction.",
        "The exam was fair. Your preparation was a creative choice.",
        "Whoever clogged the toilet, please use your problem-solving skills next time.",
        "The cat (Bagha) has been banned from the library. Again.",
    };
    [Tooltip("Joke notices pinned up each day (around the important papers).")]
    public int jokesPerDay = 4;
    [Tooltip("The morning glow's colour (until you read the board).")]
    public Color glowColor = new Color(1f, 0.78f, 0.3f, 1f);

    protected override string DefaultVerb => "Read the notices";

    int readDay = -1;
    Material glowMaterial;
    bool glowing;

    static GameManager GM => GameManager.Instance;

    public override void Interact(GameObject player)
    {
        if (GM != null) readDay = GM.day; // read: the glow goes until tomorrow
        NoticeBoardUI.Open(this);
    }

    // ---------------- The morning glow ----------------

    void Update()
    {
        var gm = GM;
        bool want = gm != null && readDay != gm.day && gm.CurrentPhase != GameManager.Phase.Closed
                    && !Tutorial.HoldsClock && !MainMenuUI.Active;
        if (want && glowMaterial == null)
        {
            var ic = FindAnyObjectByType<InteractionController>();
            if (ic == null || ic.outlineMaterial == null) return;
            glowMaterial = new Material(ic.outlineMaterial) { name = "M_Outline_NoticeGlow" };
        }
        if (glowMaterial != null && glowMaterial.HasProperty("_OutlineColor"))
        {
            // A slow pulse (brighter, dimmer), so it reads as "new today" rather than "hovered".
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            var c = Color.Lerp(glowColor * 0.55f, glowColor, t);
            c.a = 1f;
            glowMaterial.SetColor("_OutlineColor", c);
        }
        if (want == glowing) return;
        glowing = want;
        HoverOutline.SetGlow(this, want, glowMaterial);
    }

    void OnDestroy() { if (glowMaterial != null) Destroy(glowMaterial); }

    // ---------------- What's pinned up today ----------------

    /// <summary>A paper on the board: a heading and its text (rich text).</summary>
    public struct Paper
    {
        public string title, body;
        public bool main;
        public Paper(string title, string body, bool main = false) { this.title = title; this.body = body; this.main = main; }
    }

    static string H(string s) => BookRequest.H(s);

    /// <summary>Today's papers: the main one first, then the important ones, then the jokes.</summary>
    public List<Paper> TodaysPapers()
    {
        var papers = new List<Paper>();
        var gm = GM;
        int day = gm != null ? gm.day : 1;

        // The main paper: today's events (DayStory's notices), or a plain welcome.
        string events = DayStory.Instance != null ? DayStory.Instance.Notice : null;
        if (!string.IsNullOrEmpty(events))
            papers.Add(new Paper($"TODAY  ·  Day {day}", string.Join("\n\n", events.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries)), true));
        else if (gm != null && gm.GoalActive)
            papers.Add(new Paper($"TODAY  ·  Day {day}", day == 1
                ? $"Welcome to the Whitmore. Doors open {H("9:00")}, close at {H("5:30")}. Be kind to the books."
                : "A quiet day, by the look of it. Don't say that out loud.", true));
        else
            papers.Add(new Paper("LIBRARY NEWS", "Business as usual. Mind the cat.", true));

        if (gm == null) return AddJokes(papers, day);

        // Rooms opening today.
        if (day == GameManager.ArchiveDay) papers.Add(new Paper("NOW OPEN", $"The {H("Archive")} opens today. Readers will ask for its old books and papers."));
        if (day == GameManager.RareDay) papers.Add(new Paper("NOW OPEN", $"{H("Rare Books")} opens today. Watch for VIP scholars."));

        // Coming in for reserved books.
        var pickups = gm.reservations.Where(r => r.day == day).Take(3).ToList();
        if (pickups.Count > 0)
            papers.Add(new Paper("PICKUPS TODAY", string.Join("\n", pickups.Select(r =>
                $"{r.name}: {H($"\"{r.title}\"")}{(r.urgent ? $" by {GameManager.FormatHour(r.dueHour)}" : "")}"))));

        // Deliveries: on the cart already, and express copies still on their way.
        var onCart = DeliveryCart.Instance != null ? DeliveryCart.Instance.Copies.Select(b => b.Title).Distinct().ToList() : new List<string>();
        var coming = gm.ArrivingToday.Select(a => $"{H($"\"{a.book.Title}\"")} at {GameManager.FormatHour(a.hour)}").ToList();
        if (onCart.Count > 0 || coming.Count > 0)
        {
            var lines = new List<string>();
            if (onCart.Count > 0) lines.Add($"On the cart: {string.Join(", ", onCart.Take(3).Select(t => H($"\"{t}\"")))}{(onCart.Count > 3 ? " and more" : "")}");
            lines.AddRange(coming.Take(2));
            papers.Add(new Paper("DELIVERIES", string.Join("\n", lines)));
        }

        // What everyone's asking for.
        if (gm.PopularToday.Count > 0)
            papers.Add(new Paper("POPULAR TODAY", string.Join("\n", gm.PopularToday.Take(4).Select(t => H($"\"{t}\"")))));

        // The Dean's goal.
        if (gm.GoalActive && day >= 1)
        {
            int left = gm.ReputationGoal - gm.Reputation;
            papers.Add(new Paper("THE DEAN'S GOAL", left <= 0
                ? $"Reputation {H(gm.Reputation.ToString())} of {gm.ReputationGoal}. Goal reached. Keep it there."
                : day >= gm.GoalDay
                    ? $"Last day! Reputation {H(gm.Reputation.ToString())} of {gm.ReputationGoal}. {left} to go."
                    : $"Reputation {H(gm.Reputation.ToString())} of {gm.ReputationGoal} by the end of Day {gm.GoalDay}. {left} to go."));
        }

        return AddJokes(papers, day);
    }

    int[] order;

    /// <summary>A few joke notices, different each day (a fixed shuffle, each day further along it).</summary>
    List<Paper> AddJokes(List<Paper> papers, int day)
    {
        if (notices == null || notices.Length == 0) return papers;
        if (order == null || order.Length != notices.Length)
        {
            order = Enumerable.Range(0, notices.Length).ToArray();
            var rng = new System.Random(489); // same order every playthrough
            for (int i = order.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        }
        // Room for 7 papers in all: the jokes fill what the important ones leave (at least 3, at most jokesPerDay).
        int jokes = Mathf.Clamp(NoticeBoardUI.Slots - papers.Count, Mathf.Min(3, jokesPerDay), jokesPerDay);
        for (int k = 0; k < jokes; k++)
            papers.Add(new Paper("NOTICE", notices[order[(Mathf.Max(0, day - 1) * jokesPerDay + k) % order.Length]]));
        return papers;
    }
}
