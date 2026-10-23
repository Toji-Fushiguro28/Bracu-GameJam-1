using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The staff interview room. Candidates for one job come in one at a time and sit across the desk.
/// You get a few questions each, and every answer fills in their notes. Then you choose who to hire.
/// The pick is saved to PlayerPrefs ("Hired_<Role>" = name, "HiredStats_<Role>" = speed,care,charm,wage).
/// Two ways in: the new-game intro (its own scene, then MainGame), or a morning interview the library
/// loads on top of itself (set Queue first; Finished gets the roles you filled).
/// </summary>
public class StaffInterview : MonoBehaviour
{
    public class Candidate
    {
        public string name, prefab, background, hello, why;
        public int wage, speed, care, charm;
        public bool flexible;
        // What you've found out so far.
        public bool knowSpeed, knowCare, knowCharm, knowWage;
        // Pay: "wage" is their price (paid once, when hired). Each has their own lowest price.
        public int floor;
        public bool payTalked, agreed, knowFloor;
    }

    // Speed, Care, Manner, and "why us?" (which shows whether they'll bargain on pay).
    enum Q { Experience, Accident, Grumpy, Why }

    public const string SceneName = "InterviewScene";
    /// <summary>Jobs to interview for this morning (set by the library before loading this scene).</summary>
    public static List<GameManager.StaffRole> Queue;
    /// <summary>Called with the roles you hired for, once the room has faded out.</summary>
    public static System.Action<List<GameManager.StaffRole>> Finished;
    bool Morning => Queue != null && Queue.Count > 0;
    /// <summary>The story's ending: the Dean comes back to thank you (set by the library, with its numbers).</summary>
    public static bool Ending;
    public static int EndingDays, EndingMembers, EndingReputation, EndingGoal, EndingTier = 2;
    /// <summary>The board's goal for the second week (by Day 10), which the Dean passes on.</summary>
    public static int EndingNextGoal;
    /// <summary>Real time played so far, for the End page.</summary>
    public static float EndingPlaySeconds;
    public static List<string> EndingStaff = new List<string>();
    /// <summary>Set when the player picks "Back to main menu" on the End page: the day is saved, then the title screen.</summary>
    public static bool EndingToMenu;
    /// <summary>Loaded on top of the library (morning interviews and/or the ending). Only when the library is
    /// actually there: on its own (the new-game intro) this scene is always the Dean's interview.</summary>
    bool Overlay => (Morning || Ending) && GameManager.Instance != null && Finished != null;
    /// <summary>This scene is open on top of the library right now (morning interviews or the ending).</summary>
    public static bool OverlayOpen { get; private set; }

    // Statics survive between plays in the editor (and a failed load): always start clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Queue = null; Finished = null; Ending = false; EndingToMenu = false; Coins = int.MaxValue; OverlayOpen = false; }

    /// <summary>The library's purse (morning interviews): you can only hire someone whose fee you can pay.</summary>
    public static int Coins = int.MaxValue;


    public GameManager.StaffRole role = GameManager.StaffRole.Cleaner;
    [Tooltip("New-game intro: Dean Whitmore interviews you first, then you interview the cleaners.")]
    public bool prologue = true;
    [Tooltip("Character prefabs, found by name for each candidate.")]
    public GameObject[] characters;
    public Transform door, candidateSeat, librarianSeat;
    public CharacterAnimator librarian;
    public int questionsPerCandidate = 3;
    public float walkSpeed = 1.3f;
    [Tooltip("Scene to load after hiring (empty = stay here).")]
    public string returnScene = "MainGame";
    public AudioClip doorSound, hireSound, clickSound, music;
    [Tooltip("Key press for the name box (the intro has no GameAudio of its own).")]
    public AudioClip typingSound;

    DialogueUI ui;
    AudioSource sfx;
    VisualElement resume, pick, fade, ending;
    Button skip;
    Label resumeName, resumeRole, resumeBio, resumeNotes, resumeLeft;
    readonly List<Candidate> pool = new List<Candidate>();
    Candidate current;
    int questionsLeft;

    string RoleName => GameManager.StaffNames[(int)role];

    void OnDestroy() => OverlayOpen = false;

    void Start()
    {
        if (!Overlay) { Queue = null; Ending = false; } // on its own: the new-game intro, nothing left over
        OverlayOpen = Overlay;
        if (Overlay)
        {
            // Loaded on top of the library: move the room well away from it.
            prologue = false;
            foreach (var go in gameObject.scene.GetRootGameObjects()) go.transform.position += new Vector3(0f, 0f, -300f);
        }
        if (prologue) role = GameManager.StaffRole.Cleaner;
        ui = GetComponentInChildren<DialogueUI>();
        foreach (var d in FindObjectsByType<DialogueUI>()) if (d.gameObject.scene == gameObject.scene) ui = d;
        sfx = gameObject.AddComponent<AudioSource>();
        GameSettings.MarkUi(sfx); // clicks, door, hire: the UI sounds slider
        if (music != null && !Overlay) // the library's music keeps playing in the morning
        {
            var m = gameObject.AddComponent<AudioSource>();
            m.clip = music; m.loop = true; m.volume = 0.25f * GameSettings.MusicScale; m.Play();
            GameSettings.Skip(m); // music: its own slider
        }

        var root = ui.GetComponent<UIDocument>().rootVisualElement;
        resume = root.Q("resume");
        resumeName = root.Q<Label>("resume-name");
        resumeRole = root.Q<Label>("resume-role");
        resumeBio = root.Q<Label>("resume-bio");
        resumeNotes = root.Q<Label>("resume-notes");
        resumeLeft = root.Q<Label>("resume-left");
        pick = root.Q("pick");
        fade = root.Q("fade");
        skip = root.Q<Button>("skip");
        if (skip != null) { skip.clicked += SkipIntro; if (Morning) skip.text = "Skip interview"; skip.EnableInClassList("hidden", Ending || !Overlay); } // intro: shown only after the name box
        ending = root.Q("ending");
        root.Q("name-entry")?.AddToClassList("hidden");
        ending?.AddToClassList("hidden");
        resume.AddToClassList("hidden");
        pick.AddToClassList("hidden");
        if (fade != null) { fade.style.opacity = 1f; fade.AddToClassList("on"); } // start dark, then fade in

        if (librarian != null) SeatLibrarian(prologue ? candidateSeat : librarianSeat);
        StartCoroutine(Run());
    }

    // ---------------- Flow ----------------

    void SeatLibrarian(Transform seat)
    {
        if (librarian == null || seat == null) return;
        librarian.transform.SetPositionAndRotation(seat.position, seat.rotation);
        librarian.SetActivity(CharacterAnimator.Activity.SitIdle);
    }

    IEnumerator Run()
    {
        yield return null;
        yield return Fade(false);
        if (!Overlay) { yield return AskName(); yield return Prologue(); yield return Finish(null); yield break; }

        if (Ending) yield return Farewell();
        if (Morning) skip?.RemoveFromClassList("hidden");

        // Morning interviews: one job after another, then back to the library.
        filled.Clear();
        if (Morning)
        {
            foreach (var r in Queue)
            {
                yield return HireFor(r);
                if (lastHire == null) continue; // nobody suitable (or affordable)
                Save(lastHire);
                filled.Add(r);
                Coins -= lastHire.wage; // the next job's Hire buttons see what's left
            }
            yield return Say(Me, "Right. Time to open the library.");
        }
        yield return Fade(true);
        Finished?.Invoke(filled);
    }

    Candidate lastHire;
    /// <summary>Jobs filled this morning (so Skip only fills the rest).</summary>
    readonly List<GameManager.StaffRole> filled = new List<GameManager.StaffRole>();
    bool leaving; // Skip pressed: ignore further clicks

    /// <summary>Interview both applicants for one job and hire one.</summary>
    IEnumerator HireFor(GameManager.StaffRole r)
    {
        role = r;
        lastHire = null;
        pool.Clear();
        pool.AddRange(CandidatesFor(r));
        yield return new WaitForSeconds(0.6f);
        yield return Say(Me, $"Two people want the {RoleName} job. I get {questionsPerCandidate} questions with each, so I'd better choose them well.");
        foreach (var c in pool) yield return Interview(c);
        yield return Say(Me, "That's everyone. Time to decide.");
        yield return Decide();
    }

    IEnumerator Interview(Candidate c)
    {
        current = c;
        questionsLeft = questionsPerCandidate;
        var go = Spawn(c);
        var anim = go.GetComponent<CharacterAnimator>();
        Play(doorSound);
        yield return Walk(go.transform, anim, candidateSeat.position);
        go.transform.SetPositionAndRotation(candidateSeat.position, candidateSeat.rotation);
        if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitIdle); }
        ShowResume();
        yield return Say(c.name, c.hello);

        var asked = new HashSet<Q>();
        while (questionsLeft > 0)
        {
            Q? chosen = null;
            bool done = false;
            var d = Line(Me, $"<color=#D67840>Questions left: {questionsLeft}</color>  What should I ask?");
            foreach (Q q in System.Enum.GetValues(typeof(Q)))
                if (!asked.Contains(q)) d.lines[0].choices.Add(new DialogueAsset.Choice { text = QuestionText(q), next = q.ToString() });
            d.lines[0].choices.Add(new DialogueAsset.Choice { text = "That's all, thank you.", next = "done" });
            foreach (Q q in System.Enum.GetValues(typeof(Q))) d.lines.Add(new DialogueAsset.Line { id = q.ToString(), text = QuestionText(q), next = "end" });
            d.lines.Add(new DialogueAsset.Line { id = "done", text = "I think I've heard enough.", next = "end" });
            System.Action<string> seen = id => { if (id == "done") done = true; else if (System.Enum.TryParse(id, out Q q)) chosen = q; };
            ui.LineShown += seen;
            yield return PlayDialogue(d);
            ui.LineShown -= seen;
            if (done || chosen == null) break;

            asked.Add(chosen.Value);
            questionsLeft--;
            yield return Say(c.name, Answer(c, chosen.Value));
            ShowResume();
            yield return Say(Me, $"<color=#D67840>{Thought(c, chosen.Value)}</color>");
        }

        // After the questions: what would they cost?
        yield return Say(Me, "Before you go, let's talk about pay.");
        yield return PayTalk(c);
        yield return Say(c.name, Pick("Thank you for your time!", "Lovely to meet you. I hope to hear from you.", "Thanks! Fingers crossed."));
        resume.AddToClassList("hidden");
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        yield return new WaitForSeconds(0.4f);
        yield return Walk(go.transform, anim, door.position);
        Play(doorSound);
        Destroy(go);
        yield return new WaitForSeconds(0.5f);
    }

    IEnumerator Decide()
    {
        Candidate hired = null;
        bool nobody = false;
        var list = pick.Q("pick-list");
        list.Clear();
        foreach (var c in pool)
        {
            var row = new VisualElement();
            row.AddToClassList("pick-row");
            var text = new VisualElement();
            text.AddToClassList("pick-text");
            text.Add(new Label(c.name) { name = "pick-name" });
            text.Add(Stats(c));
            text.Add(new Label(PriceLine(c)) { name = "pick-notes" });
            row.Add(text);
            var cc = c;
            int price = FinalPrice(c);
            var b = new Button(() => { hired = cc; Play(clickSound); }) { text = Coins >= price ? $"Hire ({price})" : $"Need {price}" };
            b.SetEnabled(Coins >= price);
            b.AddToClassList("kit-button");
            row.Add(b);
            list.Add(row);
        }
        // Always a way out: maybe nobody fits, or the purse is too light today.
        var none = new Button(() => { nobody = true; Play(clickSound); }) { text = "Hire no one" };
        none.AddToClassList("kit-button");
        none.AddToClassList("wide");
        list.Add(none);
        ClickGuard.Block(); // the click that closed the last line mustn't press Hire
        pick.RemoveFromClassList("hidden");
        while (hired == null && !nobody) yield return null;
        pick.AddToClassList("hidden");
        if (hired != null) { hired.wage = FinalPrice(hired); hired.agreed = true; }
        if (nobody)
        {
            bool broke = true;
            foreach (var c in pool) if (Coins >= c.wage) broke = false;
            yield return Say(Me, broke ? "I can't afford either of them yet. Maybe another day." : "Neither of them is quite right for us. Maybe another time.");
            yield break;
        }

        // The new hire pops back in to say thanks.
        var go = Spawn(hired);
        var anim = go.GetComponent<CharacterAnimator>();
        Play(doorSound);
        Vector3 spot = candidateSeat.position + candidateSeat.forward * -0.9f;
        yield return Walk(go.transform, anim, spot);
        var look = librarian != null ? librarian.transform.position - go.transform.position : -go.transform.forward;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(look);
        if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.Wave); }
        Play(hireSound);
        yield return Say(hired.name, hired.agreed ? $"Really? Thank you! {hired.wage} coins, just as we said. I can start right away!"
                                                  : $"Really? Thank you! {hired.wage} coins it is, then. I can start right away!");
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        yield return Say(Me, $"Welcome to the Whitmore Library, {hired.name.Split(' ')[0]}.");
        lastHire = hired;
        // Off they go to get started.
        yield return Walk(go.transform, anim, door.position);
        Play(doorSound);
        Destroy(go);
    }

    // ---------------- The new-game intro ----------------

    const string Dean = "Dean Whitmore";
    /// <summary>The player's name in the dialogue box.</summary>
    static string Me => GameManager.PlayerName;

    /// <summary>The Dean interviews you for the job. Every answer is a good one: you're hired either way.</summary>
    IEnumerator Prologue()
    {
        var deanGo = Spawn(new Candidate { prefab = "Dean_Whitmore" });
        deanGo.transform.SetPositionAndRotation(librarianSeat.position, librarianSeat.rotation);
        var dean = deanGo.GetComponent<CharacterAnimator>();
        if (dean) dean.SetActivity(CharacterAnimator.Activity.SitIdle);
        yield return new WaitForSeconds(1f);

        yield return Say(Dean, $"Ah, {Me}, you came! Sit, sit. I'm Dean Whitmore, and I've looked after this library for forty-one years.");
        yield return Ask("So tell me, why the Whitmore Library?",
            ("I love books. All of them.", "Ha! So do I. Even the boring ones. Especially the boring ones."),
            ("Honestly? I need the job.", "Honest! Good. Honesty matters more than a degree in this place."),
            ("I've always dreamed of running a library.", "Then you've come to the right door."));
        yield return Ask("A visitor can't remember the title. Only that the book was 'about the sea, sort of sad'. What do you do?",
            ("Search the computer by subject and tags.", "Clever. That old computer is smarter than it looks."),
            ("Ask them about the story until it clicks.", "Patience. That's half of this job."),
            ("Bring them every sad sea book we have.", "Ha! Thorough. Your arms will be very strong."));
        yield return Ask("Some days it's chaos. A queue at the desk, books to mend, mess everywhere. How do you cope?",
            ("One thing at a time.", "Wise. The books aren't going anywhere."),
            ("Get some help in.", "Exactly what I was hoping you'd say. Hold that thought."),
            ("Tea. A great deal of tea.", "A librarian after my own heart."));

        yield return Say(Dean, "I'll be honest with you. I'd decided before you sat down.");
        yield return Say(Dean, "My knees can't manage the Archive stairs any more, and my doctor says it's time I rested. So the library needs someone new to run it.");
        yield return Say(Dean, "It's yours to look after now. Don't worry, I'll still pop in as a visitor. I'll even pay my fees.");
        yield return Say(Dean, "When it gets busy, hire some help on the computer. Applicants come in the next morning, before the doors open, and you'll interview them right here.");
        yield return Say(Dean, "Ask good questions, and talk about pay kindly. People remember how they were treated.");
        // The new-game intro runs before the library loads: use the mode picked on the title screen.
        int goal = GameManager.Instance != null ? GameManager.Instance.ReputationGoal
                 : GameManager.ReputationGoals[(int)(GameManager.PendingDifficulty ?? GameManager.Difficulty.Standard)];
        yield return Say(Dean, $"One more thing. The university board wants to see the town loves this place again. Get the library's <color=#D67840>reputation to {goal}</color> by the end of <color=#D67840>Day {GameManager.EndingDay}</color>, and they'll stop asking questions.");
        yield return Say(Dean, "Serve people well and they'll talk. Hand them the wrong book, or let them walk out, and they'll talk too.");
        yield return Say(Dean, "Now go on. The library opens at nine, and the books won't shelve themselves.");
    }

    // ---------------- The ending ----------------

    /// <summary>The Dean sits where you sat on your first day, says thank you, and The End page shows.
    /// Then it's back to work: the game carries on.</summary>
    IEnumerator Farewell()
    {
        var deanGo = Spawn(new Candidate { prefab = "Dean_Whitmore" });
        var dean = deanGo.GetComponent<CharacterAnimator>();
        Play(doorSound);
        yield return Walk(deanGo.transform, dean, candidateSeat.position);
        deanGo.transform.SetPositionAndRotation(candidateSeat.position, candidateSeat.rotation);
        if (dean) { dean.SetSpeed(0f); dean.SetActivity(CharacterAnimator.Activity.SitIdle); }

        string staffLine = EndingStaff.Count == 0 ? "And you did it all on your own, you stubborn thing."
                         : EndingStaff.Count == 1 ? $"{EndingStaff[0]} tells me you're the best boss they've had."
                         : $"{string.Join(", ", EndingStaff.GetRange(0, EndingStaff.Count - 1))} and {EndingStaff[EndingStaff.Count - 1]} tell me you're the best boss they've had.";

        yield return Say(Dean, "Don't get up! Ha, look at us. You in my old chair, and me in the visitor's seat.");
        yield return Say(Dean, "I've been coming in every day, you know. Sitting in the corner with a book I wasn't really reading. Watching.");
        yield return Say(Dean, $"<color=#D67840>{EndingMembers} members</color> carry a Whitmore card now. " +
                               (EndingMembers > 3 ? "When I left there were far fewer, and most of those were my bridge club." : "A small start. My bridge club started smaller."));
        yield return Say(Dean, staffLine);
        // How the week went against the goal he set.
        if (EndingTier >= 2)
            yield return Say(Dean, $"And the reputation! <color=#D67840>{EndingReputation}</color>. I asked for {EndingGoal}. The board read the number twice, then signed the papers. The Whitmore is yours, for good.");
        else if (EndingTier == 1)
            yield return Say(Dean, $"Reputation <color=#D67840>{EndingReputation}</color>. Not quite the {EndingGoal} I asked for, but close. The board wants one more season to be sure. I told them they'd be fools to wait.");
        else
            yield return Say(Dean, $"I won't pretend. Reputation <color=#D67840>{EndingReputation}</color>, and I asked for {EndingGoal}. The board is worried. I've asked them for one more season. Show them what I see in you.");
        yield return Ask("So tell me honestly. How has it been?",
            ("I couldn't have done it without your library.", "Nonsense. A library is only bricks and paper until someone opens the doors. You opened them."),
            ("Honestly? Some days were chaos.", "Ha! The good days always are. A quiet library is a lonely one."),
            ("I was terrified the whole time.", "So was I, my first year. I hid in the Archive and pretended to catalogue things. Nobody noticed. You never hid."));
        yield return Say(Dean, "My Margaret used to say a library is a house where stories wait for someone to come home.");
        yield return Say(Dean, "For years, hardly anybody came home. I kept the lamps lit anyway. Forty-one years, I kept them lit.");
        yield return Say(Dean, "I was so afraid that when I stepped down, they'd finally go out.");
        yield return Say(Dean, EndingTier >= 1 ? $"They didn't. Because of you, {Me}." : $"They're flickering, {Me}. But they haven't gone out. Keep them lit.");
        yield return Say(Me, "...Thank you, Dean.");
        // The second week: the board's next number, due at the end of Day 10.
        if (EndingNextGoal > 0)
            yield return Say(Dean, EndingTier >= 2
                ? $"Mind you, the board likes to see things last. <color=#D67840>Reputation {EndingNextGoal}</color> by the end of <color=#D67840>Day {GameManager.SecondEndingDay}</color>. Humour them. I'll write to you then."
                : $"One more season, then. <color=#D67840>Reputation {EndingNextGoal}</color> by the end of <color=#D67840>Day {GameManager.SecondEndingDay}</color>. I'll write to you when it's done.");
        yield return Say(Dean, $"Go on, {Me}. Enough of an old man's sniffling. The doors open at nine, and someone has to keep the lamps lit.");

        // The End page.
        Play(hireSound);
        var stats = ending.Q<Label>("ending-stats");
        if (stats != null) stats.text = $"{EndingDays} days at the Whitmore\n{EndingMembers} members\nReputation {EndingReputation} of {EndingGoal}" +
                                         (EndingTier >= 2 ? " - goal reached!" : EndingTier == 1 ? " - so close" : " - goal missed") +
                                         (EndingStaff.Count > 0 ? $"\nStaff: {string.Join(", ", EndingStaff)}" : "") +
                                         (EndingPlaySeconds >= 60f ? $"\nPlayed for {GameManager.FormatPlaytime(EndingPlaySeconds)}" : "");
        bool go = false;
        var cont = ending.Q<Button>("ending-continue");
        if (cont != null) cont.clicked += () => { go = true; Play(clickSound); };
        var menu = ending.Q<Button>("ending-menu");
        if (menu != null) menu.clicked += () => { EndingToMenu = go = true; Play(clickSound); };
        ClickGuard.Block();
        ending.RemoveFromClassList("hidden");
        while (!go) yield return null;
        ending.AddToClassList("hidden");
        if (EndingToMenu) yield break; // off to the title screen: no need to watch him leave

        // He heads off; the library is yours.
        if (dean) dean.SetActivity(CharacterAnimator.Activity.None);
        yield return new WaitForSeconds(0.4f);
        yield return Walk(deanGo.transform, dean, door.position);
        Play(doorSound);
        Destroy(deanGo);
    }

    /// <summary>The name box: type your name, press OK (or Enter). Saved for this and later games.</summary>
    IEnumerator AskName()
    {
        var root = ui.GetComponent<UIDocument>().rootVisualElement;
        var panel = root.Q("name-entry");
        var field = root.Q<TextField>("name-field");
        var ok = root.Q<Button>("name-ok");
        if (panel == null || field == null || ok == null) yield break;
        GameAudio.UseIntroKey(typingSound);
        GameAudio.HookTyping(root); // key presses as you type your name
        field.maxLength = 16;
        field.value = ""; // a new game starts with an empty box; the name is saved once you press OK
        bool done = false;
        // Plain letters only: no rich-text tags sneaking into the dialogue.
        string Clean(string v) => (v ?? "").Replace("<", "").Replace(">", "").Trim();
        void Submit() { if (Clean(field.value).Length == 0) return; done = true; }
        ok.clicked += Submit;
        field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); });
        field.RegisterValueChangedCallback(e => ok.SetEnabled(Clean(e.newValue).Length > 0));
        ok.SetEnabled(Clean(field.value).Length > 0);
        panel.RemoveFromClassList("hidden");
        skip?.AddToClassList("hidden"); // name first; then you may skip the rest
        field.Focus();
        while (!done) yield return null;
        ok.clicked -= Submit;
        panel.AddToClassList("hidden");
        skip?.RemoveFromClassList("hidden");
        Play(clickSound);
        PlayerPrefs.SetString("PlayerName", Clean(field.value));
        PlayerPrefs.Save();
    }

    /// <summary>One question from the Dean with a reply for each answer.</summary>
    IEnumerator Ask(string question, params (string answer, string reply)[] options)
    {
        var d = Line(Dean, question);
        for (int i = 0; i < options.Length; i++)
        {
            d.lines[0].choices.Add(new DialogueAsset.Choice { text = options[i].answer, next = "r" + i });
            d.lines.Add(new DialogueAsset.Line { id = "r" + i, text = options[i].reply, next = "end" });
        }
        yield return PlayDialogue(d);
    }

    IEnumerator Fade(bool toBlack)
    {
        if (fade == null) yield break;
        fade.style.opacity = StyleKeyword.Null;
        fade.EnableInClassList("on", toBlack);
        yield return new WaitForSeconds(0.7f);
    }

    /// <summary>Saves the hire and heads into the library (Day 1).</summary>
    IEnumerator Finish(Candidate hired)
    {
        if (hired != null) Save(hired);
        if (prologue)
        {
            // A fresh library: nobody is hired yet.
            foreach (GameManager.StaffRole r in System.Enum.GetValues(typeof(GameManager.StaffRole)))
                foreach (var key in new[] { "Hired_", "HiredPrefab_", "HiredStats_" }) PlayerPrefs.DeleteKey(key + r);
            PlayerPrefs.Save();
        }
        if (string.IsNullOrEmpty(returnScene)) yield break;
        yield return Fade(true);
        MainMenuUI.SkipOnce = true; // straight into Day 1, no title screen
        SceneManager.LoadScene(returnScene);
    }

    void Save(Candidate hired)
    {
        PlayerPrefs.SetString("Hired_" + role, hired.name);
        PlayerPrefs.SetString("HiredPrefab_" + role, hired.prefab);
        PlayerPrefs.SetString("HiredStats_" + role, $"{hired.speed},{hired.care},{hired.charm},{hired.wage}");
        PlayerPrefs.Save();
    }

    /// <summary>Skip button: hires the usual person (Sam, Omar or Arthur) for every job still to fill.</summary>
    public void SkipIntro()
    {
        if (leaving) return;
        leaving = true;
        skip?.SetEnabled(false);
        StopAllCoroutines();
        ClearSpawned();
        resume?.AddToClassList("hidden");
        Play(clickSound);
        if (!Morning) { StartCoroutine(Finish(null)); return; }
        StartCoroutine(SkipMorning());
    }

    IEnumerator SkipMorning()
    {
        foreach (var r in Queue)
        {
            if (filled.Contains(r)) continue;
            role = r;
            var c = CandidatesFor(r)[0];
            if (Coins < c.wage) continue; // can't pay: that job stays open
            Save(c);
            filled.Add(r);
            Coins -= c.wage;
        }
        yield return Fade(true);
        Finished?.Invoke(new List<GameManager.StaffRole>(filled));
    }

    // ---------------- Characters ----------------

    readonly List<GameObject> spawned = new List<GameObject>();

    GameObject Spawn(Candidate c)
    {
        GameObject prefab = null;
        foreach (var p in characters) if (p != null && p.name.Contains(c.prefab)) { prefab = p; break; }
        var go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = door.position;
        // Into the interview's own scene (not the library's, which is the active one in the morning), so nobody
        // can be left behind in the chair when the interview scene closes.
        if (go.scene != gameObject.scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        spawned.Add(go);
        return go;
    }

    /// <summary>Skip stops the interview mid-way, before the applicant walks out: send everyone home at once.</summary>
    void ClearSpawned()
    {
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();
    }

    IEnumerator Walk(Transform t, CharacterAnimator anim, Vector3 to)
    {
        // Walk on the floor (seats are lifted onto the chair).
        t.position = new Vector3(t.position.x, 0f, t.position.z);
        to.y = 0f;
        while ((t.position - to).sqrMagnitude > 0.0025f)
        {
            var dir = to - t.position;
            t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(dir), 540f * Time.deltaTime);
            t.position = Vector3.MoveTowards(t.position, to, walkSpeed * Time.deltaTime);
            if (anim) anim.SetSpeed(0.5f);
            yield return null;
        }
        if (anim) anim.SetSpeed(0f);
    }

    // ---------------- Dialogue ----------------

    DialogueAsset Line(string speaker, string text)
    {
        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = speaker;
        d.voicePitch = speaker == Me ? 1f : 0.85f + (speaker.Length % 5) * 0.08f;
        d.lines.Add(new DialogueAsset.Line { id = "start", text = text, next = "end" });
        return d;
    }

    IEnumerator Say(string speaker, string text) => PlayDialogue(Line(speaker, text));

    IEnumerator PlayDialogue(DialogueAsset d)
    {
        bool closed = false;
        ui.Play(d, () => closed = true);
        if (!ui.IsOpen) closed = true;
        while (!closed) yield return null;
        Destroy(d);
    }

    int Fee => GameManager.StaffHireFee[(int)role];

    // ---------------- Talking about pay ----------------

    static int Round5(float v) => Mathf.Max(5, Mathf.RoundToInt(v / 5f) * 5);

    /// <summary>What hiring them costs: the agreed price, or the lowest price they told you, or what they asked.</summary>
    static int FinalPrice(Candidate c) => c.agreed ? c.wage : c.knowFloor ? c.floor : c.wage;

    /// <summary>They name their price; you offer. Too low, and they politely say the lowest they can go
    /// (different for everyone). Agree and that's what you'll pay if you hire them.</summary>
    IEnumerator PayTalk(Candidate c)
    {
        c.payTalked = true;
        int ask = c.wage;
        yield return Say(c.name, Pick($"For this job I was hoping for {ask} coins.", $"I'd be asking {ask} coins, if that's all right.", $"My price would be {ask} coins, please."));
        int low = Round5(ask * 0.6f), mid = Round5(ask * 0.8f);
        yield return Choose(Me, "What do I offer?", out var pick1, ($"Offer {low}", "low"), ($"Offer {mid}", "mid"), ($"{ask} sounds fair", "ask"));
        string p = pick1();
        int offer = p == "low" ? low : p == "mid" ? mid : ask;
        if (offer >= c.floor)
        {
            c.wage = offer; c.agreed = true;
            yield return Say(c.name, offer == ask ? Pick("Wonderful, thank you!", "Thank you, that's very kind.")
                                                  : Pick($"{offer}? That's fair. I can do that.", $"{offer} works for me. Thank you for asking so kindly.", $"Alright, {offer}. I'd be happy with that."));
            ShowResume();
            yield break;
        }
        c.knowFloor = true;
        yield return Say(c.name, Pick($"That's a little low for me, I'm afraid. The lowest I could go is {c.floor}.",
                                      $"I appreciate the offer, but I couldn't manage {offer}. {c.floor} is really as low as I can go.",
                                      $"Hmm, {offer} would be hard for me. Would {c.floor} work for you?"));
        yield return Choose(Me, "Hmm...", out var pick2, ($"{c.floor} it is", "yes"), ("Let me think about it", "no"));
        if (pick2() == "yes")
        {
            c.wage = c.floor; c.agreed = true;
            yield return Say(c.name, Pick("Thank you. That's very fair.", "Deal! I won't let you down."));
        }
        else yield return Say(c.name, Pick("Of course. Take your time.", "That's all right, I understand."));
        ShowResume();
    }

    /// <summary>A line with choices; <paramref name="result"/> gives the chosen id once it's closed.</summary>
    IEnumerator Choose(string speaker, string text, out System.Func<string> result, params (string label, string id)[] options)
    {
        string chosen = null;
        result = () => chosen;
        var d = Line(speaker, text);
        foreach (var o in options)
        {
            d.lines[0].choices.Add(new DialogueAsset.Choice { text = o.label, next = o.id });
            d.lines.Add(new DialogueAsset.Line { id = o.id, text = o.label, next = "end" });
        }
        System.Action<string> seen = id => { foreach (var o in options) if (o.id == id) chosen = id; };
        ui.LineShown += seen;
        return PlayAndUnhook(d, seen);
    }

    IEnumerator PlayAndUnhook(DialogueAsset d, System.Action<string> seen)
    {
        yield return PlayDialogue(d);
        ui.LineShown -= seen;
    }

    /// <summary>What you make of their answer, with the stat it shows.</summary>
    static string Thought(Candidate c, Q q)
    {
        switch (q)
        {
            case Q.Experience: return Tier(c.speed, "(Slow and steady.", "(Keeps a decent pace.", "(Quick on their feet!") + ")"; // the stars go on the card
            case Q.Accident: return Tier(c.care, "(Hmm, a bit careless.", "(Careful enough.", "(Very careful with books.") + ")";
            case Q.Grumpy: return Tier(c.charm, "(Not great with people.", "(Polite enough.", "(Lovely with visitors.") + ")";
            default: return c.flexible ? "(They really want this job. They might be open about the price.)" : "(They know what they're worth.)";
        }
    }

    string QuestionText(Q q)
    {
        switch (q)
        {
            case Q.Experience: return "Tell me about your last job.";
            case Q.Accident:
                return role == GameManager.StaffRole.Cleaner ? "Ink's spilt across a reading table. What do you do?"
                     : role == GameManager.StaffRole.Repairer ? "A first edition comes in with a torn spine. Walk me through it."
                     : "A scholar wants to touch a 400-year-old map with bare hands. What do you say?";
            case Q.Grumpy: return "A visitor is shouting at the desk. How do you handle it?";
            default: return "Why the Whitmore Library?";
        }
    }

    string Answer(Candidate c, Q q)
    {
        switch (q)
        {
            case Q.Experience:
                c.knowSpeed = true;
                return Tier(c.speed, "I like to take my time. Rushing is how mistakes happen, isn't it?",
                                     "I kept up fine. Nobody ever told me I was slow.",
                                     "I'm quick! My last boss called me 'the whirlwind'.");
            case Q.Accident:
                c.knowCare = true;
                if (role == GameManager.StaffRole.Cleaner)
                    return Tier(c.care, "Wipe it with my sleeve? It's only a table.",
                                        "Blot it up with a rag, then a bit of polish.",
                                        "Blot, never rub. Then salt for the stain, and I'd check the books nearby too.");
                if (role == GameManager.StaffRole.Repairer)
                    return Tier(c.care, "A strip of sticky tape down the back. Good as new!",
                                        "Glue the spine, clamp it overnight, done.",
                                        "Lift the old cloth, line it with Japanese tissue, wheat-starch paste. Gentle and reversible.");
                return Tier(c.care, "If they're a scholar, I suppose they know best.",
                                    "I'd ask them nicely to use gloves.",
                                    "Gloves on, map on the cradle, and I'd turn it for them. Politely, but firmly.");
            case Q.Grumpy:
                c.knowCharm = true;
                return Tier(c.charm, "Shout back? No, no... I'd probably hide in the stacks.",
                                     "Stay calm and fetch whoever's in charge.",
                                     "Offer them a seat and a cup of tea. Nobody stays cross with a biscuit in hand.");
            default:
                c.knowWage = true;
                return c.why;
        }
    }

    static string Tier(int v, string low, string mid, string high) => v <= 2 ? low : v >= 4 ? high : mid;
    static string Pick(params string[] s) => s[Random.Range(0, s.Length)];

    // ---------------- Notes ----------------

    void ShowResume()
    {
        var c = current;
        resumeName.text = c.name;
        resumeRole.text = $"Applying for: {RoleName}";
        resumeBio.text = c.background;
        // Speed, Care and Manner as gold stars, above the price line.
        var stats = resume.Q("resume-stats");
        if (stats == null) { stats = new VisualElement { name = "resume-stats", pickingMode = PickingMode.Ignore }; resume.Insert(resume.IndexOf(resumeNotes), stats); }
        stats.Clear();
        stats.Add(Stats(c));
        resumeNotes.text = PriceLine(c);
        resumeLeft.text = $"Questions left: {questionsLeft}";
        resume.RemoveFromClassList("hidden");
        // A little bump so you notice the card change after an answer.
        resume.AddToClassList("bump");
        resume.schedule.Execute(() => resume.RemoveFromClassList("bump")).ExecuteLater(180);
    }

    static VisualElement Stats(Candidate c)
    {
        var box = new VisualElement { pickingMode = PickingMode.Ignore };
        box.Add(GoldStars.Stat("Speed", c.speed, c.knowSpeed));
        box.Add(GoldStars.Stat("Care", c.care, c.knowCare));
        box.Add(GoldStars.Stat("Manner", c.charm, c.knowCharm));
        return box;
    }

    static string PriceLine(Candidate c) =>
        $"Price   {c.wage} coins{(c.agreed ? " (agreed)" : c.knowFloor ? $" (lowest {c.floor})" : " (asking)")}";

    void Play(AudioClip clip) { if (clip != null) sfx.PlayOneShot(clip, 0.8f); }

    // ---------------- Candidates ----------------

    static List<Candidate> CandidatesFor(GameManager.StaffRole r)
    {
        int basePrice = GameManager.StaffHireFee[(int)r];
        Candidate C(string name, string prefab, string bio, string hello, string why, int dPrice, int speed, int care, int charm, bool flexible)
        {
            int price = Round5(basePrice * (1f + dPrice * 0.05f)); // e.g. -2 = 10% cheaper than usual
            // How low they'll go: everyone's different, and keen people go lower.
            int floor = Mathf.Min(price, Round5(price * (flexible ? Random.Range(0.55f, 0.8f) : Random.Range(0.8f, 0.95f))));
            return new Candidate { name = name, prefab = prefab, background = bio, hello = hello, why = why, wage = price, floor = floor, speed = speed, care = care, charm = charm, flexible = flexible };
        }

        if (r == GameManager.StaffRole.Cleaner) return new List<Candidate>
        {
            C("Sam Hollis", "Janitor_Sam", "Twelve years caretaking at the old town hall.", "Afternoon. Sam Hollis. I've brought my own mop, if that helps.",
              "Quiet place, good people, and the floors here have character. I like a floor with character.", 0, 4, 3, 3, true),
            C("Finn Marlow", "Writer_Finn", "Writer between novels. Cleaned a cafe for two summers.", "Hi! Finn. I'm technically a novelist, but I'm very good with a broom.",
              "Honestly? Inspiration. And the rent.", -2, 3, 2, 5, false),
        };
        if (r == GameManager.StaffRole.Repairer) return new List<Candidate>
        {
            C("Omar Haddad", "Scholar_Omar", "Archivist and bookbinder. Trained in Cairo.", "Good morning. Omar Haddad. Forgive the glue on my sleeve.",
              "Your Archive is a treasure no one has catalogued properly. I couldn't stay away.", 0, 3, 5, 3, false),
            C("Kai Tanaka", "Student_Kai", "Art student. Fixes comics and old records for friends.", "Hi, Kai! I fix stuff. Comics mostly, but books are just bigger comics, right?",
              "I need the job, and I really like the smell of old paper.", -4, 5, 2, 3, true),
        };
        return new List<Candidate>
        {
            C("Arthur Pembroke", "Visitor_Arthur", "Forty years a museum guard. Knows a little Latin.", "Arthur Pembroke, at your service. I've polished my shoes for the occasion.",
              "I've visited the Rare Books room every Sunday for thirty years. I'd like to look after it.", 0, 2, 5, 4, false),
            C("Clara Bell", "Student_Clara", "History student. Volunteers at the county museum.", "Hello! Clara Bell. I've read about every book in your Rare Books room. Twice.",
              "It's my dream job. Sorry, is that too keen?", -2, 4, 3, 4, true),
        };
    }
}
