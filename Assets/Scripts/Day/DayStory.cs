using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The story of the first week (Days 1 to 5, until the Dean's verdict). Day 1 is calm; Days 2 to 5 share all six
/// twists in a random order (two days get two): an exam rush, a book thief, a leaky roof, an inspection, a book signing and a power cut.
/// Around it, a handful of story moments: regulars who walk up to the desk with news, letters, and the twist
/// playing out. The morning banner, the notice board and banners tell the player what's going on.
/// After the ending a second week (Days 6-10) brings all six twists again towards the board's next goal, with a
/// letter from the Dean on Day 11; from then on each day rolls its own. Added to the GameManager at runtime.
/// </summary>
public class DayStory : MonoBehaviour
{
    public static DayStory Instance { get; private set; }

    public enum Twist { ExamRush, Thief, Leak, Inspection, Signing, PowerCut }
    static readonly string[] TwistNames = { "Exam rush", "Book thief", "Leaky roof", "Inspection", "Book signing", "Power cut" };
    static readonly string[] Headlines =
    {
        "it's exam week at the university. Expect students!",
        "rumour has it there's a book thief about.",
        "heavy rain is forecast. Mind the roof.",
        "the board is sending an inspector.",
        "an author is coming in for a book signing.",
        "a power cut is planned for lunchtime.",
    };

    /// <summary>A scripted visit: someone walks in, queues, and says their piece when you click them.</summary>
    public class Visit
    {
        public string[] prefabs;    // who comes, first choice first (falls back to whoever is free)
        public string name;         // shown instead of their own name (people who aren't regulars)
        public string teaser;       // their bubble while they wait at the desk
        public bool vip;            // a VIP scholar: gold name card and dialogue box, like any VIP
        public bool atDesk;         // essential (a twist starting, the inspector, the author, a job): they come to the counter.
                                    // Otherwise they only want a chat: they wait around the library with a "?" over their head.
        public Func<Customer, DialogueAsset> talk;    // built when you click, so it can mention how the day is going
        public Action<Customer, string> after;        // gets the id of the last line shown (which choice you picked)
        public float lingerUntil;   // with next: they look around the shelves until this hour...
        public Func<Customer, bool> linger; // ...or do this instead (true = it's taken care of), e.g. the author at the signing table
        public Visit next;          // ...then come back to the desk for this
    }

    static GameManager GM => GameManager.Instance;
    readonly List<Twist> today = new List<Twist>(); // none on Day 1, one or two on Days 2 to 5
    bool storyDay;                                  // Days 1 to 5: the week's story runs (with or without a twist)
    public IReadOnlyList<Twist> Today => today;

    // ---------------- What the twist changes (read by the spawner, requests, patience and lights) ----------------

    const float RushStart = 10f, RushEnd = 13.5f;
    const float SignStart = 14f, SignEnd = 15.5f;
    const float PowerOff = 12f, PowerOn = 14f;

    static bool Is(Twist t) => Instance != null && Instance.today.Contains(t);
    static bool Between(float from, float to) =>
        GM != null && GM.CurrentPhase == GameManager.Phase.Open && GM.Hour >= from && GM.Hour < to;
    static bool Rush => Is(Twist.ExamRush) && Between(RushStart, RushEnd);
    static bool Signing => Is(Twist.Signing) && Between(SignStart, SignEnd);

    /// <summary>The signing's on and this is one of the author's books (a buyer takes it to the signing table).</summary>
    public static bool SigningTitle(BookData b) => Signing && b != null && Instance.signTitles.Contains(b.Title);

    /// <summary>One of the author's books while the signing's on (a fan's own copy), or null.</summary>
    public static BookData SigningBook()
    {
        var s = Instance;
        if (!Signing || s.signTitles.Count == 0) return null;
        return GM.FindTitle(s.signTitles[UnityEngine.Random.Range(0, s.signTitles.Count)]);
    }

    /// <summary>More visitors during the exam rush and the signing.</summary>
    public static float VisitorBoost => Rush ? 1.6f : Signing ? 1.5f : 1f;
    /// <summary>Exam rush: more of them sit down to read.</summary>
    public static float ExtraReaders => Rush ? 0.25f : 0f;
    /// <summary>Exam rush: mostly students come in.</summary>
    public static bool StudentsWanted => Rush;
    /// <summary>Exam rush: room for a few more visitors at once, so the rush actually feels busy.</summary>
    public static int ExtraCap => Rush ? 3 : 0;
    /// <summary>Exam rush: the subject everyone's revising, or null outside the rush.</summary>
    public static string ExamSubject => Rush ? Instance.examSubject : null;
    /// <summary>Stressed students wait less; in the dark, everyone's oddly patient.</summary>
    public static float PatienceFactor => Rush ? 0.9f : PowerOut ? 1.3f : 1f;
    /// <summary>The power cut: no computer.</summary>
    public static bool PowerOut => Instance != null && Instance.powerOut;
    /// <summary>The power cut darkens the library (the night look), 0..1.</summary>
    public static float Darkness => PowerOut ? 0.8f : 0f;

    /// <summary>How much more often a book is asked for today (exam subject, the visiting author's books).</summary>
    public static float RequestWeight(BookData b)
    {
        var s = Instance;
        if (s == null || b == null) return 1f;
        if (Rush && b.Format == BookFormat.Book && b.Subject == s.examSubject) return 4f;
        if (Signing && s.signTitles.Contains(b.Title)) return 3f;
        return 1f;
    }

    // ---------------- Which twist, which day ----------------

    /// <summary>Every day has its story now: the first week's, the second week's events, then random ones.</summary>
    static bool StoryDay(GameManager gm) => gm != null && gm.day >= 1;

    /// <summary>Twists that can share a day without their busy hours or characters clashing. Left out on purpose:
    /// thief + leak (both start at 11:00), exam rush or signing + inspection (crowds during the inspection),
    /// exam rush or inspection + power cut (a rush or an inspector with no computer).</summary>
    static readonly (Twist, Twist)[] SafePairs =
    {
        (Twist.ExamRush, Twist.Signing), (Twist.ExamRush, Twist.Thief), (Twist.ExamRush, Twist.Leak),
        (Twist.Thief, Twist.Inspection), (Twist.Thief, Twist.Signing), (Twist.Thief, Twist.PowerCut),
        (Twist.Leak, Twist.Inspection), (Twist.Leak, Twist.Signing), (Twist.Leak, Twist.PowerCut),
        (Twist.Signing, Twist.PowerCut),
    };
    static bool Safe(List<Twist> g) => g.Count < 2 || SafePairs.Contains((g[0], g[1])) || SafePairs.Contains((g[1], g[0]));

    // How many twists each day gets: the first week (Days 2-5) and the second (Days 6-10). All six happen once in each.
    static readonly int[] WeekOne = { 1, 1, 2, 2 };
    static readonly int[] WeekTwo = { 1, 1, 1, 1, 2 };
    const int WeekTwoSeed = 0x5EED2, EndlessSeed = 31;

    /// <summary>Today's twists. Day 1: none (a calm first day). Days 2-5: all six once (one each on Days 2 and 3,
    /// two each on 4 and 5; the thief stays off Day 4, where its writer and Bernard are busy with the week's story).
    /// Days 6-10: all six again, reshuffled (two on Day 10). After that: 0, 1 or 2 at random each day, never one
    /// that was on yesterday. Pairs are always safe; the order comes from GameManager.storySeed (saved).</summary>
    public static List<Twist> TwistsFor(GameManager gm)
    {
        if (!StoryDay(gm) || gm.day < 2) return new List<Twist>();
        if (gm.storySeed == 0) gm.storySeed = UnityEngine.Random.Range(1, int.MaxValue);
        if (gm.day <= GameManager.EndingDay)
            return new List<Twist>(Schedule(new System.Random(gm.storySeed))[gm.day - 2]);
        if (gm.day <= GameManager.SecondEndingDay)
            return new List<Twist>(Schedule(new System.Random(gm.storySeed ^ WeekTwoSeed), WeekTwo, -1)[gm.day - GameManager.EndingDay - 1]);
        return EndlessDay(gm.day, gm.storySeed);
    }

    /// <summary>The first week: Days 2 to 5 in order.</summary>
    public static List<List<Twist>> Schedule(System.Random rng) => Schedule(rng, WeekOne, 2);

    /// <summary>A week of twists, sizes[i] on its i-th day, all six once. Reshuffles until every pair is safe (and the
    /// thief isn't on day thiefOff, if set); a fixed safe week if it somehow never is.</summary>
    static List<List<Twist>> Schedule(System.Random rng, int[] sizes, int thiefOff)
    {
        var order = ((Twist[])Enum.GetValues(typeof(Twist))).ToList();
        for (int tries = 0; tries < 100; tries++)
        {
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            var week = Split(order, sizes);
            if (week.All(Safe) && (thiefOff < 0 || !week[thiefOff].Contains(Twist.Thief))) return week;
        }
        return Split(new List<Twist> { Twist.Inspection, Twist.Thief, Twist.ExamRush, Twist.Signing, Twist.Leak, Twist.PowerCut }, sizes);
    }

    static List<List<Twist>> Split(List<Twist> order, int[] sizes)
    {
        var week = new List<List<Twist>>();
        for (int d = 0, at = 0; d < sizes.Length; at += sizes[d], d++) week.Add(order.GetRange(at, sizes[d]));
        return week;
    }

    /// <summary>After Day 10: each day rolls 0 (30%), 1 (50%) or 2 (20%) twists, none from yesterday, pairs safe, and
    /// never three quiet days running. Worked out day by day from Day 11 so those rules always hold, the same on
    /// every reload.</summary>
    static List<Twist> EndlessDay(int day, int seed)
    {
        var yesterday = Schedule(new System.Random(seed ^ WeekTwoSeed), WeekTwo, -1)[WeekTwo.Length - 1];
        var today = yesterday;
        int quiet = 0;
        for (int d = GameManager.SecondEndingDay + 1; d <= day; d++)
        {
            today = Roll(new System.Random(Mix(unchecked(seed * EndlessSeed + d * 7919))), yesterday, quiet >= 2);
            quiet = today.Count == 0 ? quiet + 1 : 0;
            yesterday = today;
        }
        return new List<Twist>(today);
    }

    /// <summary>Scrambles a seed, so neighbouring days don't get look-alike random numbers.</summary>
    static int Mix(int x)
    {
        unchecked
        {
            uint h = (uint)x;
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (int)(h & 0x7fffffff);
        }
    }

    static List<Twist> Roll(System.Random rng, List<Twist> notThese, bool notQuiet)
    {
        double r = rng.NextDouble();
        int want = r < 0.3 ? 0 : r < 0.8 ? 1 : 2;
        if (notQuiet && want == 0) want = 1; // two quiet days in a row is enough
        var pool = ((Twist[])Enum.GetValues(typeof(Twist))).Where(t => !notThese.Contains(t)).ToList();
        for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        var picked = new List<Twist>();
        foreach (var t in pool)
        {
            if (picked.Count >= want) break;
            if (Safe(new List<Twist>(picked) { t })) picked.Add(t);
        }
        return picked;
    }

    /// <summary>A line for the morning banner, so the player knows what kind of day it is.</summary>
    public static string MorningLine(GameManager gm)
    {
        var t = TwistsFor(gm);
        return t.Count == 0 ? "" : "\nToday: " + string.Join("\nAnd ", t.Select(x => Headlines[(int)x]));
    }

    /// <summary>Today's pinned notice (the notice board reads it first).</summary>
    public string Notice => today.Count == 0 ? null : string.Join("\n\n", today.Select(t => t switch
    {
        Twist.ExamRush => $"QUIET PLEASE. Second-year {examSubject} exam tomorrow. Panic in a whisper.",
        Twist.Thief => "Watch for a nervous stranger near the shelves. Tell the desk.",
        Twist.Leak => "If the ceiling drips on you, it is not crying. Tell the desk.",
        Twist.Inspection => "Inspection today at 1:00 PM. Please look studious.",
        Twist.Signing => $"Book signing with {author}, 2:00 PM today! Bring a pen. Not a crayon.",
        Twist.PowerCut => "Eastern Electric: no power from 12:00 to 2:00 PM. Bring a jumper.",
        _ => "",
    }));

    /// <summary>The Day Report's lines about today's twists: (twist, how it went).</summary>
    public List<(string key, string value)> ReportRows() => today.Select(t => (TwistNames[(int)t], t switch
    {
        Twist.ExamRush => "the students survived",
        Twist.Thief => thievesSeen == 0 ? "they never showed" : $"caught {thievesCaught}, got away {thievesEscaped}",
        Twist.Leak => soaked > 0 ? $"{soaked} book{(soaked == 1 ? "" : "s")} soaked" : leakShelf != null ? "no books soaked" : "the roof held",
        Twist.Inspection => verdict ?? "no verdict",
        Twist.Signing => $"{signSold} of their books sold",
        _ => "two hours by candlelight",
    })).ToList();

    // ---------------- Running the day ----------------

    class Beat { public float hour; public Func<bool> run; }
    readonly List<Beat> beats = new List<Beat>();
    readonly Queue<Func<Script>> popups = new Queue<Func<Script>>();
    readonly HashSet<string> usedOpeners = new HashSet<string>();

    /// <summary>At this hour, do something once.</summary>
    void Do(float hour, Action run) => beats.Add(new Beat { hour = hour, run = () => { run(); return true; } });
    /// <summary>At this hour, keep trying until it works (e.g. the right person is free to visit).</summary>
    void Try(float hour, Func<bool> run) => beats.Add(new Beat { hour = hour, run = run });
    void VisitAt(float hour, Visit v) => Try(hour, () => Spawn(v, hour));

    /// <summary>Their first choice of character if they're free, then anyone, then give up after an hour and a half.</summary>
    static bool Spawn(Visit v, float hour)
    {
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        return sp.SpawnStory(v, GM.Hour >= hour + 0.75f) != null || GM.Hour >= hour + 1.5f;
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (GM != null) { GM.DayStarted -= SetUpDay; GM.PhaseChanged -= OnPhase; }
    }

    void Start()
    {
        if (GM == null) return;
        GM.DayStarted += SetUpDay;
        GM.PhaseChanged += OnPhase;
        SetUpDay();
    }

    void SetUpDay()
    {
        RequestLog.EndTasks(); // yesterday's to-do list is over
        beats.Clear();
        popups.Clear();
        usedOpeners.Clear();
        ResetTwistState();
        today.Clear();
        storyDay = StoryDay(GM);
        if (!storyDay) return;
        today.AddRange(TwistsFor(GM));
        if (GM.day == GameManager.SecondEndingDay + 1 && GM.endingSeen && !GM.secondEndingSeen) SeasonLetter();
        foreach (var t in today)
            switch (t)
            {
                case Twist.ExamRush: SetUpExamRush(); break;
                case Twist.Thief: SetUpThief(); break;
                case Twist.Leak: SetUpLeak(); break;
                case Twist.Inspection: SetUpInspection(); break;
                case Twist.Signing: SetUpSigning(); break;
                case Twist.PowerCut: SetUpPowerCut(); break;
            }
        SetUpWeek(GM.day);
    }

    /// <summary>The morning after Day 10: a letter from the Dean on how the second week went against the board's
    /// number. Seen once (saved straight away); from then on every day brings whatever it brings.</summary>
    void SeasonLetter()
    {
        var gm = GM;
        gm.secondEndingSeen = true;
        gm.Save();
        int goal = gm.SecondGoal, rep = gm.Reputation, tier = gm.TierFor(goal);
        Popup(() => new Script("Letter from the Dean")
            .Say(tier >= 2 ? $"Two weeks. Reputation {rep}, and the board asked for {goal}. They've stopped asking questions altogether. One of them asked me for a library card."
               : tier == 1 ? $"Reputation {rep}. The board wanted {goal}. They called it \"promising\", which from them is practically a parade."
               : $"Reputation {rep}, and the board wanted {goal}. I won't dress it up. But the doors are open and people keep coming in. That counts for more than they think.")
            .Say($"Margaret kept a tally of my hours at that desk. Forty-one years of them. I'd put yours at {Spoken(gm.playSeconds)} so far. You'll catch me up.")
            .Say("No more numbers from me. The Whitmore is yours to run, for as long as you'll have it. Keep the lamps lit. - E.W.")
            .Me("From here on, every day brings whatever it brings"));
    }

    /// <summary>Playtime the way the Dean would say it: "about half an hour", "about an hour and a quarter",
    /// "a little over three hours".</summary>
    static string Spoken(float seconds)
    {
        int q = Mathf.RoundToInt(seconds / 900f); // to the nearest quarter hour
        if (q <= 1) return "a good few minutes";
        if (q == 2) return "about half an hour";
        if (q == 3) return "about three-quarters of an hour";
        int whole = q / 4, rest = q % 4;
        string hours = whole == 1 ? "an hour" : $"{whole} hours";
        if (whole > 3) return rest == 0 ? $"about {whole} hours" : rest <= 2 ? $"a little over {whole} hours" : $"nearly {whole + 1} hours";
        return rest == 0 ? $"about {hours}" : rest == 1 ? $"about {hours} and a quarter" : rest == 2 ? $"about {hours} and a half" : $"about {hours} and three-quarters";
    }

    /// <summary>Closing time: the rain stops, the power's back, nobody's left half-way through.</summary>
    void OnPhase(GameManager.Phase phase)
    {
        if (phase == GameManager.Phase.Open) return;
        powerOut = false;
        GameAudio.Loop(a => a.rainLoop, false);
        GameAudio.Loop(a => a.candleLoop, false);
        if (leaking) { leaking = false; Unpoint(ref leakPointer); RequestLog.TaskDone("leak", "The rain stopped"); }
        inspecting = false;
    }

    void Update()
    {
        var gm = GM;
        if (gm == null) return;
        if (storyDay && gm.CurrentPhase == GameManager.Phase.Open && gm.Hour >= gm.openHour + 1f / 6f)
            for (int i = 0; i < beats.Count; i++)
                if (gm.Hour >= beats[i].hour && beats[i].run()) { beats.RemoveAt(i); i--; }
        UpdateLeak();
        UpdateInspection();
        if (thief != null && !thief.thief) thief = null;                    // nothing to steal: just a visitor after all
        if (thiefPointer != null && thief == null) Unpoint(ref thiefPointer); // gone (closing, or a reload)
        ShowPendingPopup();
    }

    // ---------------- Talking ----------------

    /// <summary>Builds a short conversation: lines in order, with the odd choice that jumps to a line by id.</summary>
    public class Script
    {
        public readonly DialogueAsset d = ScriptableObject.CreateInstance<DialogueAsset>();
        public Script(string speaker = null) { d.speaker = speaker; }
        /// <summary>Their line. next: the id to go to afterwards ("end" stops), or empty for the next line.</summary>
        public Script Say(string text, string id = null, string next = null) => Add(null, text, id, next);
        /// <summary>A thought of the librarian's (how the game tells you what to do), in italics.</summary>
        public Script Me(string text, string id = null, string next = null) => Add(GameManager.PlayerName, $"<i>({text})</i>", id, next);
        Script Add(string who, string text, string id, string next)
        {
            d.lines.Add(new DialogueAsset.Line { id = id, speaker = who, text = text, next = next });
            return this;
        }
        /// <summary>Choices on the last line: "what you say", "id it jumps to", ...</summary>
        public Script Choose(params string[] textThenId)
        {
            var line = d.lines[d.lines.Count - 1];
            for (int i = 0; i + 1 < textThenId.Length; i += 2)
                line.choices.Add(new DialogueAsset.Choice { text = textThenId[i], next = textThenId[i + 1] });
            return this;
        }
    }

    /// <summary>A scene with nobody at the desk (a letter, a thought): shown as soon as the player is free.</summary>
    void Popup(Func<Script> build) => popups.Enqueue(build);

    void ShowPendingPopup()
    {
        if (popups.Count == 0 || PlayerLock.IsLocked) return;
        var ui = DialogueUI.Instance;
        if (ui == null || ui.IsOpen) return;
        Play(popups.Dequeue()());
    }

    /// <summary>Right now if nothing else is on screen, otherwise next.</summary>
    void PlayNow(Script s)
    {
        var ui = DialogueUI.Instance;
        if (ui != null && !ui.IsOpen) Play(s);
        else popups.Enqueue(() => s);
    }

    static void Play(Script s)
    {
        var ui = DialogueUI.Instance;
        if (ui == null) { Destroy(s.d); return; }
        ui.Play(s.d, () => Destroy(s.d));
    }

    static void Banner(string text, float seconds = 6f) => FindAnyObjectByType<HUD>()?.ShowBanner(text, seconds);

    static void Reward(int coins, int reputation)
    {
        if (coins > 0) { GM.AddCoins(coins); GM.Today.Earn(GameManager.DayStats.Source.Other, coins); RequestLog.Money("Today's event", "A thank-you for today's event", "Reward", coins); }
        if (reputation != 0) GM.AddReputation(reputation);
    }

    /// <summary>A floating label with an arrow that stays on screen (like a staff member calling for help).</summary>
    /// <param name="above">On screen, sit just above this world-layer element (a visitor's bubble) instead of at the point.</param>
    public static WorldPrompts.Follower Point(string text, Func<Vector3> at, bool edgeOnly = false, VisualElement above = null)
    {
        var wp = WorldPrompts.Instance;
        if (wp == null) return null;
        var el = new VisualElement();
        el.AddToClassList("staff-pointer");
        var label = new Label(text == "?" ? "" : text) { name = "staff-pointer-label" };
        label.EnableInClassList("ask", text == "?"); // the pixel question mark, as over their head
        el.Add(label);
        var arrow = new VisualElement();
        arrow.AddToClassList("staff-pointer-arrow");
        el.Add(arrow);
        var f = wp.Pin(el, arrow, at, edgeOnly);
        if (f != null) f.above = above;
        return f;
    }

    public static void Unpoint(ref WorldPrompts.Follower f)
    {
        if (f != null && WorldPrompts.Instance != null) WorldPrompts.Instance.Unfollow(f);
        f = null;
    }

    static string S(int n, string one, string many) => n == 1 ? one : many;

    // Books the story asks for: something on the shelves right now, at random.
    static BookData InStock(Func<BookData, bool> where = null) =>
        GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && BookRequest.Available(b) && BookRequest.InStock(b)
                            && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b) && (where == null || where(b)))
                .OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();

    static BookRequest ByTitle(BookData b, string line) =>
        new BookRequest { Hint = HintType.Title, Target = b, Format = b.Format, Line = line };

    /// <summary>Done talking: they sit down with something from the shelves, or just head off if nothing's in.</summary>
    static void ThenRead(Customer c, Func<BookData, string> line, Func<BookData, bool> where = null)
    {
        var b = InStock(where) ?? InStock();
        if (b != null) c.ContinueWithRequest(ByTitle(b, line(b)), true);
    }

    // Who plays whom (character prefabs). Alternatives step in when the first choice is busy or works here.
    const string Walter = "PF_NPC_Visitor_Walter", Bernard = "PF_NPC_Visitor_Bernard", Gus = "PF_NPC_Visitor_Gus";
    const string Dean = "PF_NPC_Dean_Whitmore";
    static readonly string[] Elders = { Walter, Bernard, Gus };
    static string[] Old(string first) => new[] { first }.Concat(Elders.Where(e => e != first)).ToArray();
    static string[] Student(string first, params string[] more) =>
        new[] { first }.Concat(more).Concat(new[] { "PF_NPC_Student_Ruby", "PF_NPC_Student_Sofia", "PF_NPC_Student_Theo" }).Distinct().ToArray();

    // ---------------- The week's own story (the same every game) ----------------

    void SetUpWeek(int day)
    {
        int rep() => GM.Reputation;
        int goal = GM.ReputationGoal;
        switch (day)
        {
            case 1:
                VisitAt(10.5f, new Visit
                {
                    prefabs = Old(Walter), teaser = "Still smells the same in here.",
                    talk = c =>
                    {
                        bool member = c.Membership != null;
                        return new Script()
                            .Say("Sixty years I've walked past this door. My wife used to say, either go in or stop slowing down outside it.")
                            .Say("She'd have liked you. You look like you know where things are.")
                            .Choose("Come in properly, then.", "come", "Did she ever come in?", "she")
                            .Say("Every Thursday. I waited on the bench across the road with a flask. Daft, really.", "she")
                            .Say(member ? "Go on, then. Find me something to read. I've got my card already, look." : "Go on, then. I'd better have one of those cards, before I lose my nerve.", "come")
                            .d;
                    },
                    after = (c, _) =>
                    {
                        if (c.Membership == null && Members.TryProfile(c.profileKey, out var profile)) c.ContinueAsSignUp();
                        else ThenRead(c, b => $"Something to sit with for an hour. <color=#D67840>\"{b.Title}\"</color>, if it's in.");
                    },
                });
                VisitAt(15.75f, new Visit
                {
                    prefabs = Student("PF_NPC_Student_Arjun"), teaser = "Can I ask you something weird?",
                    talk = c => new Script()
                        .Say("Can I ask you something weird? My aunt says the university wants to turn this place into a car park.")
                        .Choose("Not if I can help it.", "no", "Who told your aunt that?", "who")
                        .Say("Everyone at the bus stop. The bus stop knows everything. So is it true?", "who")
                        .Choose("Not if I can help it.", "no")
                        .Say("Okay. Good. Because I only just found it.", "no")
                        .Me($"The Dean's goal: reputation {goal} by the end of Day {GameManager.EndingDay}. You're on {rep()}")
                        .d,
                });
                break;

            case 2:
                VisitAt(10.25f, new Visit
                {
                    prefabs = new[] { "PF_NPC_Scholar_Tariq", "PF_NPC_Scholar_Omar", "PF_NPC_Student_Leila", "PF_NPC_Student_Leo" },
                    teaser = "Is the Archive really open?",
                    talk = c => new Script()
                        .Say("Is the Archive really open? I've been writing to the university for six years to see the 1890 town records.")
                        .Say("They wrote back twice. Once to say no, and once to ask me to stop writing.")
                        .Choose("It's open. Go and have a look.", "go", "What are you hoping to find?", "find")
                        .Say("My great-grandmother. She ran the bakery on Fenn Street and nobody ever wrote her down anywhere.", "find")
                        .Say("I might cry a bit. Not in a bad way. Don't watch.", "go")
                        .d,
                    after = (c, _) => Reward(0, 1), // word gets round that the Archive's open
                });
                VisitAt(15.5f, new Visit
                {
                    prefabs = Student("PF_NPC_Student_Clara", "PF_NPC_Student_Priya"), teaser = "Has anyone handed in some glasses?",
                    talk = c => new Script()
                        .Say("Has anyone handed in some glasses? Brown frames. Tortoiseshell, the man in the shop called it, which felt a bit much.")
                        .Choose("...Are they on your head?", "head", "Let me check lost property.", "box")
                        .Me("The lost property box holds one glove, a banana, and a library book due back in 1987", "box")
                        .Say("Oh! They're on my head. They've been on my head this whole time. Sorry. Sorry.", null, DialogueAsset.End)
                        .Say("...Right. Okay. We don't need to tell anyone about this.", "head")
                        .d,
                });
                break;

            case 3:
                Popup(() =>
                {
                    bool there = rep() >= goal;
                    return new Script("Letter from the Board")
                        .Say($"To the Acting Librarian. The Board notes that the Whitmore's reputation currently stands at {rep()}.")
                        .Say(there ? $"The Dean suggested {goal} by the end of Day {GameManager.EndingDay}. The Board is surprised. The Board does not enjoy being surprised."
                                   : $"The Dean has suggested a figure of {goal} by the end of Day {GameManager.EndingDay}. The Board wishes to say that it finds this optimistic.")
                        .Me("It's signed by four people. One of them has doodled a small car in the margin");
                });
                VisitAt(15.25f, new Visit
                {
                    prefabs = Student("PF_NPC_Student_Kai", "PF_NPC_Student_Theo", "PF_NPC_Student_Hiro"), teaser = "There's a cat in the Archive.",
                    talk = c => new Script()
                        .Say("There's a cat in the Archive.")
                        .Choose("There's no cat.", "no", "What's it doing?", "doing")
                        .Say("There is definitely a cat. It's orange and it looked at me like I owe it money.", "no", "bagha")
                        .Say("Sitting on the 1923 almanac. I asked it to move. It didn't.", "doing")
                        .Say("The notice board says it's banned. Bagha, it's called. It doesn't seem bothered about being banned.", "bagha")
                        .Choose("I'll let it out later.", "out", "Leave it. It's probably reading.", "reading")
                        .Say("Cool. I'm not going back in there.", "out", DialogueAsset.End)
                        .Say("Honestly? Respect.", "reading")
                        .d,
                });
                break;

            case 4:
                VisitAt(10.5f, new Visit
                {
                    prefabs = new[] { "PF_NPC_Writer_Finn", "PF_NPC_Writer_Ezra" }, teaser = "Do you mind if I write in here?",
                    talk = c => new Script()
                        .Say("Do you mind if I write in here? My flat's too quiet. I need ambient sighing. Pages turning. Someone dropping a pencil and swearing under their breath.")
                        .Say("I'm on chapter nine. Chapter nine has been chapter nine since March.")
                        .Choose("Pick any seat you like.", "seat", "What's it about?", "about")
                        .Say("You're a saint. If it ever gets finished, you're in the acknowledgements. Third paragraph, near the cat.", "seat", "book")
                        .Say("A librarian who... no. I'll jinx it.", "about")
                        .Say("Could you find me something with good sentences? I'm going to steal one. Just one. A small one.", "book")
                        .d,
                    after = (c, _) => ThenRead(c, b => $"Something with good sentences to steal. <color=#D67840>\"{b.Title}\"</color>, if it's in?"),
                });
                VisitAt(16f, new Visit
                {
                    prefabs = Old(Bernard), teaser = "Have you seen the fella outside?",
                    talk = c =>
                    {
                        int left = goal - rep();
                        return new Script()
                            .Say("Have you seen the fella outside? Grey suit, little clicker counter. Counting everyone who comes in.")
                            .Say("When I waved at him he pretended to read the bus timetable. There's no bus on this street. Hasn't been since 1998.")
                            .Choose("That'll be the board.", "board", "Did he count you?", "count")
                            .Say("Thought so. Well, I came in twice today, so that's two for you.", "board", "rep")
                            .Say("Twice. I went out and came back in again. You're welcome.", "count")
                            .Me(left <= 0 ? $"Reputation {rep()}: the Dean's goal of {goal} is already reached. Hold on to it"
                                          : $"Reputation {rep()}. The Dean wants {goal} by the end of tomorrow. {left} to go", "rep")
                            .d;
                    },
                });
                break;

            case 5:
                Popup(() =>
                {
                    bool there = rep() >= goal;
                    return new Script("Note from the Dean")
                        .Say("Tonight's the night. Whatever the number says, I've seen what you've done with the place. The board hasn't. Show them.")
                        .Say("P.S. Don't let Bernard near the biscuit tin. He knows what he did. - E.W.")
                        .Me(there ? $"Reputation {rep()}, and the goal was {goal}. Already there. Keep it that way" : $"Reputation {rep()} of {goal}. Last day");
                });
                VisitAt(15f, new Visit
                {
                    prefabs = Student("PF_NPC_Student_Amara", "PF_NPC_Student_Priya"), teaser = "This is from all of us.",
                    talk = c => new Script()
                        .Say("This is from all of us. It's a card. Everyone signed it. Walter wrote a whole paragraph, so the rest of us had to write really small.")
                        .Say("Kai drew a cat on the back. He says it's the Archive cat. It looks like a potato with ears.")
                        .Choose("Thank you. Really.", "thanks", "It does look like a potato.", "potato")
                        .Say("Don't get weird about it. Okay, get a bit weird about it.", "thanks", DialogueAsset.End)
                        .Say("Don't tell him. He's very proud of it.", "potato")
                        .d,
                    after = (c, _) => { Reward(0, 2); Banner("The regulars made you a card. +2 reputation."); },
                });
                break;
        }
    }

    // ---------------- Exam rush ----------------

    string examSubject = "History";

    void SetUpExamRush()
    {
        var subjects = GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b))
                               .Select(b => b.Subject).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (subjects.Count > 0) examSubject = subjects[UnityEngine.Random.Range(0, subjects.Count)];
        string s = examSubject;
        bool OnSubject(BookData b) => b.Subject == s && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b);

        VisitAt(9.25f, new Visit
        {
            prefabs = Student("PF_NPC_Student_Nadia", "PF_NPC_Student_Leila"), teaser = "Please tell me you're open all day.", atDesk = true,
            talk = c => new Script()
                .Say($"Okay. So. The whole of second year has a {s} exam tomorrow, and the department library's shut for \"carpet reasons\".")
                .Say("Nobody knows what carpet reasons are. But everyone's coming here instead.")
                .Choose("How many is everyone?", "many", "We'll manage.", "manage")
                .Say(s == "Math" ? "Forty? Sixty? Don't ask me, I'm doing Maths. That's the whole problem." : "Forty? Sixty? I can't count when I'm panicking. I can barely spell.", "many", "go")
                .Say("That's the spirit. That's exactly what I said about the exam three weeks ago.", "manage")
                .Say($"They'll all want {s} books, and they'll want them now. Sorry in advance.", "go")
                .Me($"Exam rush from 10:00 to 1:30. Lots of students after {s} books, and they're short on patience")
                .d,
            after = (c, _) =>
            {
                Banner($"Exam rush from 10:00 AM to 1:30 PM: students want {s} books!", 7f);
                RequestLog.Task("exam", "Exam rush", $"Students want {BookRequest.H(s)} books from 10:00 to 1:30. Keep that shelf stocked."); 
                ThenRead(c, b => $"Right, starting with me. Have you got <color=#D67840>\"{b.Title}\"</color>? I need to read it here. Now. Immediately.", OnSubject);
            },
        });
        Do(RushStart, () => { GameAudio.Play(a => a.examStart); Banner($"The exam rush has started! Keep the {s} books coming."); });
        VisitAt(11.75f, new Visit
        {
            prefabs = Student("PF_NPC_Student_Ravi", "PF_NPC_Student_Leo"), teaser = "Is it bad if I've only read the contents page?",
            talk = c => new Script()
                .Say("Is it bad if I've only read the contents page? Like, all of it. Really carefully.")
                .Choose("It's a start.", "start", "The exam's tomorrow.", "tomorrow")
                .Say("That's what I said! My flatmate says it's \"not revision\". My flatmate has never once cleaned the oven.", "start", "ask")
                .Say("I know. I KNOW. I'm aware of time. Time is all I'm aware of right now.", "tomorrow")
                .Say($"Anyway. Have you got anything on {s}? Ideally with pictures. Ideally short.", "ask")
                .d,
            after = (c, _) =>
            {
                var any = GM.Books.FirstOrDefault(b => b.Sellable && b.Format == BookFormat.Book && OnSubject(b) && BookRequest.Available(b));
                if (any != null)
                    c.ContinueWithRequest(new BookRequest { Hint = HintType.Subject, Value = s, Target = any, Format = BookFormat.Book,
                                                            Line = $"Anything on {s}, please. Short. With pictures, if you've got it." }, false);
            },
        });
        Do(RushEnd, () => { GameAudio.Play(a => a.eventDone); Banner("The exam rush is over. Deep breaths."); RequestLog.TaskDone("exam", "The students survived"); });
        VisitAt(15f, new Visit
        {
            prefabs = Student("PF_NPC_Student_Nadia", "PF_NPC_Student_Leila"), teaser = "Update. Big update.",
            talk = c => new Script()
                .Say("They've moved the exam. To Thursday. The department emailed at ten past one with the subject line \"small change\".")
                .Say("Half of them cried. The other half went to the café to be angry with cake.")
                .Say("We had a whip-round for you, though, for not throwing us out. It's mostly coins. There's a button in there. That was Ravi.")
                .Choose("You didn't have to do that.", "thanks", "A button?", "button")
                .Say("We did, a bit. You found Leo a book on a subject he can't pronounce.", "thanks", DialogueAsset.End)
                .Say("He says it's lucky. He also says he'll want it back if he fails.", "button")
                .d,
            after = (c, _) => { Reward(20, 1); Banner("The students left you a whip-round: +20 coins."); },
        });
    }

    // ---------------- Book thief ----------------

    Customer thief;
    WorldPrompts.Follower thiefPointer;
    int thievesSeen, thievesCaught, thievesEscaped;
    const string ThiefName = "Nervous stranger";

    void SetUpThief()
    {
        VisitAt(9.3f, new Visit
        {
            prefabs = Old(Bernard), teaser = "You'll want to hear this.", atDesk = true,
            talk = c => new Script()
                .Say("Keep your eyes peeled today. Somebody's been pinching books. Did the charity shop on Fenn Street on Tuesday and the dentist's waiting room yesterday.")
                .Say("Who nicks from a dentist? Those magazines are from 2009.")
                .Choose("What do they look like?", "look", "Thanks for the warning.", "thanks")
                .Say("A stranger who keeps glancing at the door. Looks at the shelves like they're casing a bank. You'll know them when you see them.", "look", "tip")
                .Say("A nervous stranger, if you see one. Don't be a hero. Well. Be a little bit of a hero.", "thanks")
                .Me("If a stranger heads for the door with a book they haven't checked out, click them before they get out", "tip")
                .d,
            after = (c, _) =>
            {
                Banner("Book thief about! Stop the nervous stranger before they reach the door.", 7f);
                RequestLog.Task("thief", "Book thief", "Watch for a stranger leaving with a book they haven't checked out. Click them before they reach the door.");
            },
        });
        Try(11f, () => SpawnThief(11f));
        VisitAt(12.5f, new Visit
        {
            prefabs = new[] { "PF_NPC_Writer_Ezra", "PF_NPC_Writer_Finn" }, teaser = "I hear you've got a thief.",
            talk = c => new Script()
                .Say("I hear you've got a thief. I write mysteries, so professionally I'm thrilled. Personally, appalled. Mostly thrilled.")
                .Say("Could I sit near the door? For research. I won't tackle anyone. I've got a bad knee and worse timing.")
                .Choose("Sit wherever you like.", "sit", "Please don't tackle anyone.", "tackle")
                .Say("And if you catch them, I want details. What they took. Whether they said anything dramatic. Nobody ever says anything dramatic.", "sit", "book")
                .Say("I literally just told you about the knee.", "tackle")
                .Say("Find me something to read while I keep watch? Something short, in case it all kicks off.", "book")
                .d,
            after = (c, _) => ThenRead(c, b => $"Something to read while I watch the door. <color=#D67840>\"{b.Title}\"</color>, maybe?"),
        });
        Try(14.5f, () => SpawnThief(14.5f));
        VisitAt(16.25f, new Visit
        {
            prefabs = Old(Bernard), teaser = "Heard the news?",
            talk = c =>
            {
                var s = new Script();
                if (thievesSeen == 0)
                    s.Say("Quiet in here today. Maybe word got round you were on guard.")
                     .Say("Or they've moved on to the garden centre. Poor garden centre.");
                else if (thievesEscaped == 0)
                    s.Say("Word's out you caught them! Fenn Street's talking about nothing else.")
                     .Say("Mrs. Adeyemi from the charity shop wants to bake you a cake. She's going to bake it whether you like it or not.");
                else if (thievesCaught > 0)
                    s.Say("Caught one and lost one, I heard. Better than the dentist. The dentist didn't notice for a week.");
                else
                    s.Say("Got away from you, did they? Same as the dentist. Never mind.")
                     .Say("They'll be back. And next time you'll know the face.");
                return s.d;
            },
            after = (c, _) =>
            {
                if (thievesSeen > 0 && thievesEscaped == 0) Reward(0, 1);
                RequestLog.TaskDone("thief", thievesSeen == 0 ? "They never showed" : $"Caught {thievesCaught}, got away {thievesEscaped}");
            },
        });
    }

    bool SpawnThief(float hour)
    {
        if (thief != null) return GM.Hour >= hour + 1.5f; // one at a time
        var sp = CustomerSpawner.Instance;
        if (sp == null) return true;
        var c = sp.SpawnThief(ThiefName);
        if (c != null) thief = c;
        return c != null || GM.Hour >= hour + 1.5f;
    }

    /// <summary>The thief has a book under their coat and is heading for the door.</summary>
    public void ThiefPocketed(Customer c)
    {
        thief = c;
        thievesSeen++;
        Unpoint(ref thiefPointer);
        thiefPointer = Point("Thief!", () => c != null ? c.transform.position + Vector3.up * 2.1f : Vector3.zero);
        GameAudio.Play(a => a.thiefAlert);
        Banner("The nervous stranger just pocketed a book! Catch them before the door!", 6f);
    }

    /// <summary>Clicked in time: the book goes back on the shelf.</summary>
    public void ThiefCaught(Customer c, BookData book)
    {
        thievesCaught++;
        Unpoint(ref thiefPointer);
        GameAudio.Play(a => a.thiefCaught);
        thief = null;
        Reward(0, 1);
        string t = book != null ? $"<color=#D67840>\"{book.Title}\"</color>" : "the book";
        var s = new Script(ThiefName);
        switch (UnityEngine.Random.Range(0, 3))
        {
            case 0:
                s.Say("What? This? I was... going to bring it back.")
                 .Choose("Check it out first next time.", "x")
                 .Say("...Yes. Obviously. That's what I meant.", "x");
                break;
            case 1:
                s.Say("I wasn't stealing it. I was borrowing it in a very quiet way.")
                 .Choose("That's stealing.", "x")
                 .Say("That's a harsh word for it.", "x");
                break;
            default:
                s.Say("Fine! Fine. Have it. It had a sad ending anyway.");
                break;
        }
        s.Me($"They hand over {t} and leave at a brisk, embarrassed walk. It's back on the shelf");
        PlayNow(s);
    }

    /// <summary>Out of the door with it: the copy is gone.</summary>
    public void ThiefGotAway(Customer c, BookData book)
    {
        thievesEscaped++;
        Unpoint(ref thiefPointer);
        if (thief == c) thief = null;
        GM.AddReputation(-1);
        GM.NotifyBooksChanged();
        Banner(book != null ? $"The stranger got away with <color=#D67840>\"{book.Title}\"</color>! -1 reputation." : "The stranger got away! -1 reputation.", 6f);
    }

    // ---------------- Leaky roof ----------------

    ShelfInteractable leakShelf;
    bool leaking;
    float nextSoak;
    int soaked;
    WorldPrompts.Follower leakPointer;
    const float LeakAt = 11f, FirstSoak = 0.5f, SoakEvery = 0.35f;
    const int MaxSoaked = 4;
    string LeakShelfName => leakShelf != null ? leakShelf.displayName : "a";

    void SetUpLeak()
    {
        GameAudio.Loop(a => a.rainLoop, true); // raining from the morning until Gus clears the gutter
        VisitAt(9.3f, new Visit
        {
            prefabs = Old(Gus), teaser = "Chucking it down out there.", atDesk = true,
            talk = c => new Script()
                .Say("It's chucking it down out there. Proper sideways rain. My hood's given up.")
                .Say("Your gutters are full of leaves, by the way. I could see from the bus. Water backs up like that, it finds a way in somewhere.")
                .Choose("I'll keep an eye on the ceiling.", "eye", "Could you fix it?", "fix")
                .Say("Good. Water's patient. More patient than me.", "eye", "tip")
                .Say("I fix boats. A roof's just a boat that's given up. Maybe later, if it stops.", "fix")
                .Me("If the roof starts dripping on a shelf, get there fast and click it to move the books before they're soaked", "tip")
                .d,
            after = (c, _) => Banner("Heavy rain today. Mind the roof!"),
        });
        Do(LeakAt, StartLeak);
        // With a power cut too, Walter comes at 1:00 to talk about that, so he comes about the leak earlier.
        VisitAt(today.Contains(Twist.PowerCut) ? 11.5f : 13f, new Visit
        {
            prefabs = Old(Walter), teaser = "Leaking again, is it?",
            talk = c =>
            {
                string then = leaking ? "still" : DialogueAsset.End;
                var s = new Script()
                    .Say("Leaking again, is it? It did that in '74. They put a bucket under it and called it a renovation.")
                    .Say("Then in '91 they put a different bucket under it. A blue one. That was the big refurbishment.")
                    .Choose("Has it ever actually been fixed?", "fixed", "Ours is the waste bin.", "bin")
                    .Say("Fixed? It's a listed building. You don't fix it. You negotiate with it.", "fixed", then)
                    .Say("Times are hard.", "bin", then);
                if (leaking) s.Say($"It's still going, you know. Over by the {LeakShelfName} shelf. I could hear it from the door.", "still");
                return s.d;
            },
        });
        VisitAt(15.5f, new Visit
        {
            prefabs = Old(Gus), teaser = "Sorted your gutter.",
            talk = c => new Script()
                .Say("Rain's stopped, so I went up and cleared your gutter. No charge. I was up there anyway, if anyone asks.")
                .Say("Don't tell my wife I was on a roof. She thinks I'm at the dentist.")
                .Choose("Thank you. Really.", "end", "Your secret's safe.", "end")
                .d,
            after = (c, _) =>
            {
                GameAudio.Loop(a => a.rainLoop, false); // the rain's stopped
                if (!leaking) return;
                leaking = false;
                Unpoint(ref leakPointer);
                Banner("The leak's stopped.");
                RequestLog.TaskDone("leak", soaked > 0 ? $"{soaked} soaked" : "No books soaked");
            },
        });
    }

    /// <summary>Water starts coming through over a shelf in an open room (one with books on it, if there is one).</summary>
    void StartLeak()
    {
        var shelves = FindObjectsByType<ShelfInteractable>().Where(s => !RoomGate.IsLockedAt(s.transform.position)).ToList();
        if (shelves.Count == 0) return;
        var withBooks = shelves.Where(s => s.Books.Any(b => b.GoodCopies + b.WornCopies > 0)).ToList();
        // Best of all a main hall bookcase with books for sale (not the Archive, Rare Books or the periodicals).
        var main = withBooks.Where(s => !s.shelfId.StartsWith("ARC") && !s.shelfId.StartsWith("RARE")
                                        && s.Books.Any(b => b.Sellable && b.Format == BookFormat.Book && b.GoodCopies + b.WornCopies > 0)).ToList();
        // On an exam rush day, spare the shelf the students need.
        if (today.Contains(Twist.ExamRush))
        {
            var dry = main.Where(s => !s.Books.Any(b => b.Subject == examSubject)).ToList();
            if (dry.Count > 0) main = dry;
        }
        var pool = main.Count > 0 ? main : withBooks.Count > 0 ? withBooks : shelves;
        leakShelf = pool[UnityEngine.Random.Range(0, pool.Count)];
        leaking = true;
        nextSoak = GM.Hour + FirstSoak;
        var shelf = leakShelf;
        leakPointer = Point("Leak!", () => shelf != null ? shelf.TopAnchor + Vector3.up * 0.4f : Vector3.zero);
        Banner($"Leak! Water is dripping on the {LeakShelfName} shelf. Click it to save the books!", 7f);
        RequestLog.Task("leak", "Leaky roof", $"Water is dripping on the {BookRequest.H(LeakShelfName)} shelf. Click the shelf to move the books.");
        Popup(() => new Script()
            .Me($"Plink. ... Plink. Water is dripping onto the {LeakShelfName} shelf!")
            .Me("Click that shelf to move the books and put the bin under the drip. Every few minutes it drips, another book gets soaked"));
    }

    void UpdateLeak()
    {
        if (!leaking || GM.CurrentPhase != GameManager.Phase.Open || GM.Hour < nextSoak) return;
        nextSoak += SoakEvery;
        if (soaked >= MaxSoaked || leakShelf == null) return;
        var dry = leakShelf.Books.Where(b => b.GoodCopies + b.WornCopies > 0).ToList();
        if (dry.Count == 0) return;
        var book = dry[UnityEngine.Random.Range(0, dry.Count)];
        var cond = book.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
        if (!book.TakeCopy(cond)) return;
        book.AddCopy(BookCondition.Damaged);
        soaked++;
        GM.NotifyBooksChanged();
        GameAudio.Play(a => a.soaked);
        Banner($"<color=#D67840>\"{book.Title}\"</color> got soaked!", 3f);
    }

    /// <summary>The player clicked a shelf: if it's the leaking one, they deal with the leak (instead of browsing).</summary>
    public bool FixLeak(ShelfInteractable shelf)
    {
        if (!leaking || shelf != leakShelf) return false;
        leaking = false;
        Unpoint(ref leakPointer);
        RequestLog.TaskDone("leak", soaked > 0 ? $"Fixed, {soaked} soaked" : "Fixed, no books soaked");
        GameAudio.Play(a => a.tidy);
        var s = new Script().Me("You shuffle the books along and wedge the waste bin under the drip. Plink. Plonk. Plonk is better");
        if (soaked > 0) s.Me($"{soaked} {S(soaked, "book", "books")} got soaked. Take {S(soaked, "it", "them")} to the Repair desk to dry out and mend");
        else s.Me("Not a single page got wet");
        PlayNow(s);
        return true;
    }

    // ---------------- Inspection ----------------

    bool inspecting;
    int angryAtStart;
    float messTime, messSum;
    string verdict;
    const float InspectFrom = 13f, InspectUntil = 14.5f;

    void SetUpInspection()
    {
        VisitAt(9.3f, new Visit
        {
            prefabs = new[] { Dean }, name = "Edgar Whitmore", teaser = "A quick word, before it gets busy.", atDesk = true,
            talk = c => new Script()
                .Say("A quick word. The board is sending an inspector at one o'clock. A Mr. Pryce.")
                .Say("He once failed a bakery for having too much flour. In a bakery.")
                .Choose("What does he look for?", "what", "Great. Thanks for the warning.", "great")
                .Say("Mess. Grumbling. People walking out in a huff. Keep the floor tidy and nobody storming off, and he'll have nothing to write.", "what", "go")
                .Say("Sarcasm. Good, you'll need it. Keep the floor tidy and nobody storming off, and he'll have nothing to write.", "great")
                .Say("I'd stay, but if I'm here he'll say I coached you. Which I am doing. Right now.", "go")
                .Me("Inspection from 1:00 to 2:30. Tidy cups and books quickly, and don't let anyone walk out")
                .d,
            after = (c, _) =>
            {
                Banner("Inspection at 1:00 PM: keep it tidy and keep visitors happy until 2:30.", 7f);
                RequestLog.Task("inspection", "Inspection", "Mr. Pryce inspects from 1:00 to 2:30. Keep the floor tidy and don't let anyone walk out.");
            },
        });
        VisitAt(11f, new Visit
        {
            prefabs = Student("PF_NPC_Student_Hiro", "PF_NPC_Student_Kai"), teaser = "Is there really an inspector coming?",
            talk = c => new Script()
                .Say("Is it true there's an inspector coming? Should I hide my crisps? I've got a lot of crisps.")
                .Choose("Just don't eat them over the books.", "books", "Hide them.", "hide")
                .Say("Over the floor, then. Got it.", "books")
                .Me("That is not what you meant", null, DialogueAsset.End)
                .Say("Hiding them in my stomach. Problem solved.", "hide")
                .d,
        });
        var verdictVisit = new Visit
        {
            name = "Mr. Pryce", teaser = "Right. I've seen enough.", atDesk = true,
            talk = c =>
            {
                inspecting = false;
                int walkouts = GM.Today.angry - angryAtStart;
                float mess = messTime > 0f ? messSum / messTime : 0f;
                var s = new Script();
                if (walkouts == 0 && mess < 1.5f)
                {
                    verdict = "satisfactory";
                    s.Say("Floor's tidy. Nobody stormed out. One gentleman told me, unprompted, that you're \"alright, actually\".")
                     .Say("I'll be writing \"satisfactory\". From me, that's practically a love letter.");
                }
                else if (walkouts >= 2 || mess >= 3.5f)
                {
                    verdict = "concerns";
                    s.Say("Cups on the tables, books on the floor, and I watched people walk out muttering.")
                     .Say("I'm writing \"concerns\". The board loves the word \"concerns\".");
                }
                else
                {
                    verdict = "adequate";
                    s.Say(walkouts > 0 ? "Mostly fine. But somebody left in a huff, and I wrote down what they called you." : "Mostly fine. A few things left lying about, and I noticed every one of them.")
                     .Say("\"Adequate\", I'll write. Don't look pleased. Adequate is a warning.");
                }
                return s.d;
            },
            after = (c, _) =>
            {
                int r = verdict == "satisfactory" ? 3 : verdict == "concerns" ? -2 : 1;
                GM.AddReputation(r);
                GameAudio.Play(a => a.eventDone);
                Banner($"Inspection result: {verdict} ({(r > 0 ? "+" : "")}{r} reputation).", 6f);
                RequestLog.TaskDone("inspection", $"Verdict: {verdict}");
            },
        };
        VisitAt(InspectFrom, new Visit
        {
            prefabs = new[] { "PF_NPC_Visitor_Arthur", "PF_NPC_Scholar_Omar", "PF_NPC_Professor_Victor", "PF_NPC_Professor_Edmund" },
            name = "Mr. Pryce", teaser = "Pryce. From the board.", atDesk = true,
            talk = c => new Script()
                .Say("Pryce. Board of Governors. I have a clipboard and ninety minutes.")
                .Say("Carry on as normal. I'll just be looking. Don't mind me. Everyone minds me.")
                .Choose("Can I get you anything?", "get", "Look all you like.", "look")
                .Say("A pencil that hasn't been chewed would be a start.", "get", DialogueAsset.End)
                .Say("I will. That's the job.", "look")
                .d,
            after = (c, _) =>
            {
                inspecting = true;
                GameAudio.Play(a => a.announcement);
                angryAtStart = GM.Today.angry;
                messTime = messSum = 0f;
                Banner("The inspection has started! Keep it tidy until 2:30.");
            },
            lingerUntil = InspectUntil,
            next = verdictVisit,
        });
        Do(16f, () =>
        {
            if (verdict == null) return;
            string v = verdict;
            Popup(() => new Script("Note from the Dean").Say(
                v == "satisfactory" ? "Pryce wrote \"satisfactory\". I have never heard him say that about anything, including his own wedding. Well done. - E.W."
              : v == "adequate" ? "\"Adequate.\" Could be worse. He once called my office \"a fire waiting for a match\". - E.W."
              : "\"Concerns.\" Well. We've had concerns before. Tomorrow is another day. - E.W."));
        });
    }

    /// <summary>While Mr. Pryce looks around: how much mess is lying about, on average.</summary>
    void UpdateInspection()
    {
        if (!inspecting || GM.CurrentPhase != GameManager.Phase.Open) return;
        float dt = GameManager.Dt;
        int dirty = 0;
        foreach (var m in MessSpot.All) if (m != null && m.IsDirty) dirty++;
        messTime += dt;
        messSum += dirty * dt;
    }

    // ---------------- Book signing ----------------

    string author = "the author";
    readonly List<string> signTitles = new List<string>();
    int signSold;
    static readonly HashSet<string> Women = new HashSet<string> { "Clara Bell", "Hazel Grant", "Mara Voss" };

    string TitlesText => signTitles.Count == 0 ? "their books" : string.Join(" and ", signTitles.Select(t => BookRequest.H($"\"{t}\"")));

    string StockText => string.Join(", ", signTitles.Select(t =>
    {
        var b = GM.FindTitle(t);
        int n = b != null ? b.GoodCopies + b.WornCopies : 0;
        return $"{BookRequest.H($"\"{t}\"")} {(n > 0 ? "x" + n : "none")}";
    }));

    /// <summary>What the author pays for her signing table.</summary>
    const int SigningFee = 30;

    void SetUpSigning()
    {
        // An author with books on the main hall shelves (the visiting writer is one of the library's own authors).
        var groups = GM.Books.Where(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsArchive(b) && !BookRequest.IsRare(b)
                                         && !string.IsNullOrEmpty(b.Author) && !b.Author.Contains("Whitmore"))
                             .GroupBy(b => b.Author).OrderByDescending(g => g.Count()).ToList();
        var pick = groups.Where(g => g.Count() >= 2).ToList();
        if (pick.Count == 0) pick = groups;
        if (pick.Count > 0)
        {
            var g = pick[UnityEngine.Random.Range(0, pick.Count)];
            author = g.Key;
            signTitles.AddRange(g.Take(2).Select(b => b.Title));
            foreach (var t in signTitles) GM.PopularToday.Add(t); // they glow on the computer and the shelves
        }
        var who = Women.Contains(author) ? new[] { "PF_NPC_Professor_Ingrid", "PF_NPC_Professor_Hazel" } : new[] { "PF_NPC_Professor_Edmund", "PF_NPC_Professor_Victor" };

        VisitAt(9.3f, new Visit
        {
            prefabs = who, name = author, teaser = "Hello! You must be the librarian.", atDesk = true,
            talk = c => new Script()
                .Say($"Hello! {author}. I'm signing books here at two o'clock. The Dean said it was all arranged.")
                .Say("...He didn't tell you, did he. He does this. He did this at my last launch, and that was in his own house.")
                .Choose("First I've heard of it, but you're welcome.", "kind", "He did not tell me, no.", "no")
                .Say($"You're very kind. Have you got copies of {TitlesText}? People like to buy one to get it signed.", "kind", "stock")
                .Say($"Classic Edgar. Have you at least got copies of {TitlesText}? People like to buy one to get it signed.", "no")
                .Me($"On the shelves: {StockText}. Order more from the Catalogue: express gets here in 2 hours", "stock")
                .d,
            after = (c, _) =>
            {
                Banner($"Book signing at 2:00 PM! {author}'s fans will want {TitlesText}.", 7f);
                RequestLog.Task("signing", "Book signing", $"{BookRequest.H(author)} signs at 2:00 PM. Have {TitlesText} on the shelves (order more in the Catalogue).");
            },
        });
        VisitAt(12.25f, new Visit
        {
            prefabs = Student("PF_NPC_Student_Hiro", "PF_NPC_Student_Arjun"), teaser = "Is this where the signing is?",
            talk = c => new Script()
                .Say("Is this where the signing is? I know it's not till two. I'm early. I'm early to everything. My mum says I was early to my own birth.")
                .Say(signTitles.Count > 0 ? $"Have you got \"{signTitles[0]}\"? I want to read the first chapter so I've got something clever to say." : "I want to buy one of their books so I've got something clever to say.")
                .d,
            after = (c, _) =>
            {
                var b = signTitles.Count > 0 ? GM.FindTitle(signTitles[0]) : null;
                if (b != null) c.ContinueWithRequest(ByTitle(b, $"Could I buy <color=#D67840>\"{b.Title}\"</color>? For the signing."), false);
            },
        });
        var farewell = new Visit
        {
            name = author, teaser = "That's me done!", atDesk = true,
            talk = c =>
            {
                int n = signSold;
                var s = new Script();
                if (n >= 3)
                    s.Say($"{n} copies! I signed one \"to Pip\" and it turned out Pip was a dog. Best signing I've had in years.")
                     .Say("This is for the library. Don't argue, I've already let go of it.");
                else if (n > 0)
                    s.Say($"We sold {n}. Which is {n} more than the garden centre last month. I'll take it.");
                else
                    s.Say("Nobody bought one, but a little girl asked if I was famous. I said \"a bit\", and she said \"which bit\". I'll be thinking about that all week.");
                return s.d;
            },
            after = (c, _) =>
            {
                RequestLog.TaskDone("signing", $"{signSold} sold");
                GameAudio.Play(a => a.eventDone);
                if (signSold >= 3) { Reward(25, 2); Banner($"{author} left a thank-you: +25 coins."); }
                else if (signSold > 0) Reward(0, 1);
            },
        };
        VisitAt(SignStart, new Visit
        {
            prefabs = who, name = author, teaser = "Right! Where do you want me?", atDesk = true,
            talk = c => new Script()
                .Say("Right! Where do you want me? Near the door so people see me, but not so near they feel trapped.")
                .Say($"And here's {BookRequest.H($"{SigningFee} coins")} for the table. I always pay my way. Mostly in coins, once in jam.")
                .Say("I've brought my own pen. Last time somebody lent me one and it was a crayon.")
                .d,
            after = (c, _) =>
            {
                // The author pays the library for her table.
                GM.AddCoins(SigningFee);
                GM.Today.Earn(GameManager.DayStats.Source.Other, SigningFee);
                RequestLog.Money(author, "Paid for the signing table", "Signing fee", SigningFee);
                HUD.SkillPop(c.transform.position + Vector3.up * 2.2f, $"Signing table fee  +{SigningFee}");
                GameAudio.Play(a => a.signingStart);
                Banner($"The book signing has started! {author}'s fans are on their way.");
            },
            lingerUntil = SignEnd,
            linger = c => SigningTable.Instance != null && SigningTable.Instance.SeatAuthor(c, SignEnd), // signs at the table till 3:30
            next = farewell,
        });
    }

    /// <summary>A copy was sold (handed over or stamped out).</summary>
    public void Sold(BookData b)
    {
        if (b != null && today.Contains(Twist.Signing) && signTitles.Contains(b.Title)) signSold++;
    }

    // ---------------- Power cut ----------------

    bool powerOut;

    void SetUpPowerCut()
    {
        Do(9.25f, () =>
        {
            Banner("Power cut from 12:00 to 2:00 PM: no computer. Learn where the books live!", 7f);
            RequestLog.Task("power", "Power cut", "No power from 12:00 to 2:00 PM, so no computer. Find books by their shelves.");
            Popup(() => new Script("Note on the doormat")
                .Say("EASTERN ELECTRIC. Planned maintenance at the Mill Road substation, 12:00 to 2:00 PM. We apologise for any inconvenience.")
                .Me("It's dated three weeks ago. Somebody has been using it as a coaster")
                .Me("No power means no computer from noon until two. You'll have to find books by their shelves"));
        });
        VisitAt(10.75f, new Visit
        {
            prefabs = Student("PF_NPC_Student_Maya", "PF_NPC_Student_Priya", "PF_NPC_Student_Leila"), teaser = "Is it true the power's going off?",
            talk = c => new Script()
                .Say("Is it true the power's going off at lunch? With candles and everything?")
                .Say("This is the best day of my life. I'm not joking. I've wanted a candlelit library since I was nine.")
                .Choose("It's just a power cut.", "just", "Bring a jumper.", "jumper")
                .Say("To you.", "just", "book")
                .Say("I'm wearing two.", "jumper")
                .Say("Can you find me something to read for when it happens? Something old. Old counts as spooky.", "book")
                .d,
            after = (c, _) => ThenRead(c, b => $"Something old for the dark. <color=#D67840>\"{b.Title}\"</color>?"),
        });
        Do(PowerOff, CutPower);
        VisitAt(13f, new Visit
        {
            prefabs = Old(Walter), teaser = "This is nothing.",
            talk = c => new Script()
                .Say("This is nothing. Winter of '63, the power went for three days. We read by candle and nobody complained.")
                .Say("Well. Doris complained. Doris complained about everything. Lovely woman.")
                .Choose("What did you read?", "read", "Three days?", "three")
                .Say("Whatever was nearest. I read a book on beekeeping twice. Still don't keep bees.", "read", DialogueAsset.End)
                .Say("Three days. We played cards by the fire. I lost a bicycle.", "three")
                .d,
        });
        Do(PowerOn, RestorePower);
    }

    void CutPower()
    {
        powerOut = true;
        GameAudio.Play(a => a.powerOff);
        GameAudio.Loop(a => a.candleLoop, true);
        FindAnyObjectByType<PCOverlayTrigger>()?.ForceClose();
        Banner("Power cut! No computer until 2:00 PM.");
        Popup(() => new Script()
            .Me("The lights die with a soft clunk. Somebody gasps. Somebody else says \"ooh\" like it's fireworks")
            .Me("The computer's dead until two. People are being nice about it: they'll wait a little longer than usual"));
    }

    void RestorePower()
    {
        powerOut = false;
        GameAudio.Play(a => a.powerOn);
        GameAudio.Loop(a => a.candleLoop, false);
        Banner("The power's back! The computer works again.");
        RequestLog.TaskDone("power", "Power back");
        Popup(() => new Script()
            .Me("The lights stutter, buzz, and come back on. A small cheer from the reading chairs. Someone by the window looks genuinely disappointed"));
    }

#if UNITY_EDITOR
    // ---------------- Dev Tools only ----------------

    /// <summary>Adds a twist to today and sets it up; its moments then happen by the clock.</summary>
    public void DevStartTwist(Twist t)
    {
        if (today.Contains(t)) return;
        storyDay = true;
        today.Add(t);
        switch (t)
        {
            case Twist.ExamRush: SetUpExamRush(); break;
            case Twist.Thief: SetUpThief(); break;
            case Twist.Leak: SetUpLeak(); break;
            case Twist.Inspection: SetUpInspection(); break;
            case Twist.Signing: SetUpSigning(); break;
            case Twist.PowerCut: SetUpPowerCut(); break;
        }
    }

    public void DevLeakNow() { if (!leaking) StartLeak(); }
    public void DevPower(bool on) { if (on) RestorePower(); else CutPower(); }
    public void DevThiefNow() => SpawnThief(GM.Hour);
#endif

    void ResetTwistState()
    {
        Unpoint(ref thiefPointer);
        Unpoint(ref leakPointer);
        thief = null; thievesSeen = thievesCaught = thievesEscaped = 0;
        leakShelf = null; leaking = false; soaked = 0;
        inspecting = false; verdict = null; messTime = messSum = 0f;
        author = "the author"; signTitles.Clear(); signSold = 0;
        examSubject = "History";
        powerOut = false;
        GameAudio.Loop(a => a.rainLoop, false);
        GameAudio.Loop(a => a.candleLoop, false);
    }

    // ---------------- Small talk ----------------

    static readonly string[] Openers =
    {
        "Sorry, I've just run from the bus. Give me a second. ...Okay.",
        "I practised what to say in the queue and now I've forgotten all of it.",
        "My cat sat on my last library book for a week. It's fine. It's mostly fine.",
        "Is it always this quiet? I can hear my own stomach. It's very loud.",
        "I told my mum I'd be at the library, and for once it's actually true.",
        "I've had three coffees, so if I talk fast, that's why.",
        "Don't mind the umbrella. It's broken, but I'm emotionally attached to it.",
        "I tried the library in town first. They've put a café where the books used to be.",
        "My book club meets tonight and I haven't read the book. Again.",
        "Is that clock right? It's been ten past something all day.",
        "I've parked on a yellow line, so I'll be quick. Ish.",
        "Somebody's in my chair by the window. I'm not bitter. I'm a little bit bitter.",
        "My son says nobody reads any more. I'm here to prove him wrong, out of spite.",
        "Lovely in here. Smells like my grandad's coat. That's a compliment.",
        "I dropped my phone in the bath, so all I can do is read. It's been brilliant, honestly.",
        "Is it alright if I whisper? I feel like I should whisper.",
        "I walked past four times trying to remember what I came in for. Got it now.",
        "Sorry, I've had a day. A dog ate my sandwich. Not my dog. A stranger's dog.",
        "Your floor is very shiny. I nearly went over by the door.",
        "First time in. Is there a password or anything?",
        "My doctor says I need a hobby that isn't arguing online.",
        "I nearly bought this online, then I thought, no. Go and talk to a person.",
        "I've got twenty minutes before my bus, which round here means an hour.",
        "My flatmate's learning the trumpet. I'll be staying as long as you'll have me.",
        "Does anyone actually bring books back on time? Asking for a friend. The friend is me.",
        "I got told off for reading at the dinner table. So. Here I am.",
        "Is it weird to come to the library on my day off? Don't answer that.",
        "The man at the bus stop recommended you. He also recommended a fish shop, so we'll see.",
    };

    static readonly string[][] TwistOpeners =
    {
        new[] { "I've had four hours' sleep, and two of them were on the bus.", "If anyone asks, I've been revising since Monday.",
                "Everyone from my course is in here. It's like a party where nobody's allowed to enjoy themselves." },
        new[] { "Did you hear about the thief? My aunt says he's seven foot tall. My aunt says a lot of things.",
                "I'm keeping my bag on my lap today. No offence.", "Someone said the thief only takes hardbacks. Bit picky, isn't it?" },
        new[] { "Sorry, I'm dripping on your floor. It's absolutely chucking it down.", "My shoes are making a squelching noise. I'm going to pretend they aren't.",
                "I came in to get out of the rain. And for a book. Mostly the rain." },
        new[] { "Is there really an inspector coming today? Should I sit up straighter?", "I heard the inspector once failed a bakery. For flour.",
                "I've ironed my shirt in case the inspector looks at me. Nobody ever looks at me." },
        new[] { "Is the author here yet? I've been practising being casual about it.", "My sister loves this author. I'm here so she doesn't have to talk to anyone.",
                "My aunt once queued two hours for this author. I've brought snacks, just in case." },
        new[] { "My whole street's talking about the power cut. My neighbour's bought forty candles. Forty.",
                "I've brought a torch, just in case. And a spare torch for the torch.", "Is it true about the power? I've charged everything I own." },
    };

    /// <summary>Something a visitor says about their day before getting to the point, the first time you talk
    /// (or null: plenty of people just get on with it). Often about the day's twist, otherwise one of that day's
    /// share of everyday lines (each of five days has its own; each new week takes them in a different order).</summary>
    public static string Opener(string profileKey)
    {
        var s = Instance;
        if (s == null || !s.storyDay || GM == null || Tutorial.HoldsClock) return null; // mid-tutorial: no small talk
        IEnumerable<string> pool = null;
        if (s.today.Count > 0 && UnityEngine.Random.value < 0.5f)
        {
            bool student = profileKey != null && profileKey.Contains("Student");
            var twist = s.today[UnityEngine.Random.Range(0, s.today.Count)];
            if (twist != Twist.ExamRush || student) pool = TwistOpeners[(int)twist];
        }
        // 28 lines in five shares of 5 or 6; week w starts w shares along, so Day 6 doesn't repeat Day 1.
        int d = Mathf.Max(1, GM.day) - 1, share = (d + d / GameManager.EndingDay) % GameManager.EndingDay;
        if (pool == null && UnityEngine.Random.value < 0.35f)
            pool = Openers.Where((l, i) => i % GameManager.EndingDay == share);
        if (pool == null) return null;
        var fresh = pool.Where(l => !s.usedOpeners.Contains(l)).ToList();
        if (fresh.Count == 0) return null;
        string line = fresh[UnityEngine.Random.Range(0, fresh.Count)];
        s.usedOpeners.Add(line);
        return line;
    }
}
