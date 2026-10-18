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
    }

    enum Q { Experience, Accident, Grumpy, Wage, Why }

    public const string SceneName = "InterviewScene";
    /// <summary>Jobs to interview for this morning (set by the library before loading this scene).</summary>
    public static List<GameManager.StaffRole> Queue;
    /// <summary>Called with the roles you hired for, once the room has faded out.</summary>
    public static System.Action<List<GameManager.StaffRole>> Finished;
    bool Morning => Queue != null && Queue.Count > 0;
    /// <summary>The story's ending: the Dean comes back to thank you (set by the library, with its numbers).</summary>
    public static bool Ending;
    public static int EndingDays, EndingMembers, EndingReputation;
    public static List<string> EndingStaff = new List<string>();
    /// <summary>Loaded on top of the library (morning interviews and/or the ending).</summary>
    bool Overlay => Morning || Ending;

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

    DialogueUI ui;
    AudioSource sfx;
    VisualElement resume, pick, fade, ending;
    Button skip;
    Label resumeName, resumeRole, resumeBio, resumeNotes, resumeLeft;
    readonly List<Candidate> pool = new List<Candidate>();
    Candidate current;
    int questionsLeft;

    string RoleName => GameManager.StaffNames[(int)role];

    void Start()
    {
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
        if (music != null && !Overlay) // the library's music keeps playing in the morning
        {
            var m = gameObject.AddComponent<AudioSource>();
            m.clip = music; m.loop = true; m.volume = 0.25f; m.Play();
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
        if (skip != null) { skip.clicked += SkipIntro; if (Morning) skip.text = "Skip interview"; skip.EnableInClassList("hidden", Ending); }
        ending = root.Q("ending");
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
        if (prologue) yield return Prologue();
        if (!Overlay) { yield return HireFor(role); yield return Finish(lastHire); yield break; }

        if (Ending) yield return Farewell();
        if (Morning) skip?.RemoveFromClassList("hidden");

        // Morning interviews: one job after another, then back to the library.
        var filled = new List<GameManager.StaffRole>();
        if (Morning)
        {
            foreach (var r in Queue)
            {
                yield return HireFor(r);
                Save(lastHire);
                filled.Add(r);
            }
            yield return Say("You", "Right. Time to open the library.");
        }
        yield return Fade(true);
        Finished?.Invoke(filled);
    }

    Candidate lastHire;

    /// <summary>Interview both applicants for one job and hire one.</summary>
    IEnumerator HireFor(GameManager.StaffRole r)
    {
        role = r;
        pool.Clear();
        pool.AddRange(CandidatesFor(r));
        yield return new WaitForSeconds(0.6f);
        yield return Say("You", $"Two people want the {RoleName} job. I get {questionsPerCandidate} questions with each, so I'd better choose them well.");
        foreach (var c in pool) yield return Interview(c);
        yield return Say("You", "That's everyone. Time to decide.");
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
            var d = Line("You", $"<color=#D67840>Questions left: {questionsLeft}</color>  What should I ask?");
            foreach (Q q in System.Enum.GetValues(typeof(Q)))
                if (!asked.Contains(q)) d.lines[0].choices.Add(new DialogueAsset.Choice { text = QuestionText(q), next = q.ToString() });
            d.lines[0].choices.Add(new DialogueAsset.Choice { text = "That's all, thank you.", next = "done" });
            foreach (Q q in System.Enum.GetValues(typeof(Q))) d.lines.Add(new DialogueAsset.Line { id = q.ToString(), text = QuestionText(q), next = "end" });
            d.lines.Add(new DialogueAsset.Line { id = "done", text = "I think I've heard enough. Thank you for coming in.", next = "end" });
            System.Action<string> seen = id => { if (id == "done") done = true; else if (System.Enum.TryParse(id, out Q q)) chosen = q; };
            ui.LineShown += seen;
            yield return PlayDialogue(d);
            ui.LineShown -= seen;
            if (done || chosen == null) break;

            asked.Add(chosen.Value);
            questionsLeft--;
            yield return Say(c.name, Answer(c, chosen.Value));
            ShowResume();
        }

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
        var list = pick.Q("pick-list");
        list.Clear();
        foreach (var c in pool)
        {
            var row = new VisualElement();
            row.AddToClassList("pick-row");
            var text = new VisualElement();
            text.AddToClassList("pick-text");
            text.Add(new Label(c.name) { name = "pick-name" });
            text.Add(new Label(Notes(c)) { name = "pick-notes" });
            row.Add(text);
            var cc = c;
            var b = new Button(() => { hired = cc; Play(clickSound); }) { text = "Hire" };
            b.AddToClassList("kit-button");
            row.Add(b);
            list.Add(row);
        }
        pick.RemoveFromClassList("hidden");
        while (hired == null) yield return null;
        pick.AddToClassList("hidden");

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
        yield return Say(hired.name, Morning ? $"Really? Thank you! I can start right away. {hired.wage} coins a day, as agreed."
                                             : $"Really? Thank you! I'll be there first thing tomorrow, {hired.wage} coins a day as agreed.");
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        yield return Say("You", $"Welcome to the Whitmore Library, {hired.name.Split(' ')[0]}.");
        lastHire = hired;
        // Off they go to get started.
        yield return Walk(go.transform, anim, door.position);
        Play(doorSound);
        Destroy(go);
    }

    // ---------------- The new-game intro ----------------

    const string Dean = "Dean Whitmore";

    /// <summary>The Dean interviews you for the job. Every answer is a good one: you're hired either way.</summary>
    IEnumerator Prologue()
    {
        var deanGo = Spawn(new Candidate { prefab = "Dean_Whitmore" });
        deanGo.transform.SetPositionAndRotation(librarianSeat.position, librarianSeat.rotation);
        var dean = deanGo.GetComponent<CharacterAnimator>();
        if (dean) dean.SetActivity(CharacterAnimator.Activity.SitIdle);
        yield return new WaitForSeconds(1f);

        yield return Say(Dean, "Ah, you came! Sit, sit. Edgar Whitmore. I've looked after this library for forty-one years.");
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
        yield return Say(Dean, "Now, your first job: you'll need a cleaner. Two applicants are waiting outside.");
        yield return Say(Dean, "Take my chair. Ask good questions: you only get three each. And I'll pay their first day's wage, as a welcome gift.");

        yield return Fade(true);
        Destroy(deanGo);
        SeatLibrarian(librarianSeat);
        yield return Fade(false);
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
        yield return Say(Dean, $"<color=#D67840>{EndingMembers} members</color> carry a Whitmore card now. When I left, it was eleven, and nine of them were my bridge club.");
        yield return Say(Dean, staffLine);
        yield return Ask("So tell me honestly. How has it been?",
            ("I couldn't have done it without your library.", "Nonsense. A library is only bricks and paper until someone opens the doors. You opened them."),
            ("Honestly? Some days were chaos.", "Ha! The good days always are. A quiet library is a lonely one."),
            ("I was terrified the whole time.", "So was I, my first year. I hid in the Archive and pretended to catalogue things. Nobody noticed. You never hid."));
        yield return Say(Dean, "My Margaret used to say a library is a house where stories wait for someone to come home.");
        yield return Say(Dean, "For years, hardly anybody came home. I kept the lamps lit anyway. Forty-one years, I kept them lit.");
        yield return Say(Dean, "I was so afraid that when I stepped down, they'd finally go out.");
        yield return Say(Dean, "They didn't. Because of you.");
        yield return Say("You", "...Thank you, Dean.");
        yield return Say(Dean, "Edgar. Please. Now, enough of an old man's sniffling. The doors open at nine, and someone has to keep the lamps lit.");

        // The End page.
        Play(hireSound);
        var stats = ending.Q<Label>("ending-stats");
        if (stats != null) stats.text = $"{EndingDays} days at the Whitmore\n{EndingMembers} members\nReputation {EndingReputation}" +
                                         (EndingStaff.Count > 0 ? $"\nStaff: {string.Join(", ", EndingStaff)}" : "");
        bool go = false;
        var cont = ending.Q<Button>("ending-continue");
        if (cont != null) cont.clicked += () => { go = true; Play(clickSound); };
        ending.RemoveFromClassList("hidden");
        while (!go) yield return null;
        ending.AddToClassList("hidden");

        // He heads off; the library is yours.
        if (dean) dean.SetActivity(CharacterAnimator.Activity.None);
        yield return new WaitForSeconds(0.4f);
        yield return Walk(deanGo.transform, dean, door.position);
        Play(doorSound);
        Destroy(deanGo);
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
        Save(hired);
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
        if (prologue)
        {
            PlayerPrefs.SetInt("InterviewNewHire", 1); // the library starts Day 1 with them on staff
            // A fresh library: forget who was hired for the other jobs last game.
            foreach (var other in new[] { GameManager.StaffRole.Repairer, GameManager.StaffRole.RareAttendant })
                foreach (var key in new[] { "Hired_", "HiredPrefab_", "HiredStats_" }) PlayerPrefs.DeleteKey(key + other);
        }
        PlayerPrefs.Save();
    }

    /// <summary>Skip button: hires the usual person (Sam, Omar or Arthur) for every job still to fill.</summary>
    public void SkipIntro()
    {
        StopAllCoroutines();
        Play(clickSound);
        if (!Morning) { StartCoroutine(Finish(CandidatesFor(GameManager.StaffRole.Cleaner)[0])); return; }
        StartCoroutine(SkipMorning());
    }

    IEnumerator SkipMorning()
    {
        foreach (var r in Queue) { role = r; Save(CandidatesFor(r)[0]); }
        yield return Fade(true);
        Finished?.Invoke(new List<GameManager.StaffRole>(Queue));
    }

    // ---------------- Characters ----------------

    GameObject Spawn(Candidate c)
    {
        GameObject prefab = null;
        foreach (var p in characters) if (p != null && p.name.Contains(c.prefab)) { prefab = p; break; }
        var go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = door.position;
        return go;
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
        d.voicePitch = speaker == "You" ? 1f : 0.85f + (speaker.Length % 5) * 0.08f;
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
            case Q.Wage: return $"You asked for {current.wage} coins a day. Could you start on a little less?";
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
            case Q.Wage:
                c.knowWage = true;
                if (c.flexible) { c.wage -= 2; c.flexible = false; return $"For a library like this? Alright, {c.wage} coins would do."; }
                return $"Sorry, {c.wage} is the lowest I can go.";
            default:
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
        resumeNotes.text = Notes(c);
        resumeLeft.text = $"Questions left: {questionsLeft}";
        resume.RemoveFromClassList("hidden");
    }

    static string Stars(int v) => new string('*', v) + new string('-', 5 - v);

    static string Notes(Candidate c) =>
        $"Speed   {(c.knowSpeed ? Stars(c.speed) : "?")}\n" +
        $"Care    {(c.knowCare ? Stars(c.care) : "?")}\n" +
        $"Manner  {(c.knowCharm ? Stars(c.charm) : "?")}\n" +
        $"Wage    {c.wage} a day{(c.knowWage ? " (final)" : "")}";

    void Play(AudioClip clip) { if (clip != null) sfx.PlayOneShot(clip, 0.8f); }

    // ---------------- Candidates ----------------

    static List<Candidate> CandidatesFor(GameManager.StaffRole r)
    {
        int baseWage = GameManager.StaffWage[(int)r];
        Candidate C(string name, string prefab, string bio, string hello, string why, int dWage, int speed, int care, int charm, bool flexible) =>
            new Candidate { name = name, prefab = prefab, background = bio, hello = hello, why = why, wage = baseWage + dWage, speed = speed, care = care, charm = charm, flexible = flexible };

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
