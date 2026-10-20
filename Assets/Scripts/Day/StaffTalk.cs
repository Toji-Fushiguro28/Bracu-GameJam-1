using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Click a staff member for a quick chat. Each has their own voice; lines don't repeat back to back.
/// Added to staff characters by StaffManager.
/// </summary>
public class StaffTalk : ClickInteractable
{
    public GameManager.StaffRole role;

    // line, reply A, reply B, answer to A, answer to B
    static readonly string[][] Sam =
    {
        new[] { "Somebody put a teacup inside the History shelf. Inside. Behind the books.", "That's a new one.", "Was it Bagha?", "Twelve years I've done this. Still new ones every week.", "Bagha doesn't drink tea. Usually." },
        new[] { "My knees say it's going to rain.", "Your knees are never wrong.", "Want to sit for a minute?", "Forty years of mopping. They've earned the right to complain.", "If I sit down, I'm not getting back up. Thanks though." },
        new[] { "Found a love letter under one of the reading tables. Unsigned. Pinned it to the notice board.", "That's very sweet of you.", "Is that allowed?", "Don't tell anyone I'm soft.", "Lost property is lost property. Even hearts." },
        new[] { "Why do people always leave exactly one sip of tea in the cup?", "It's for luck.", "Science can't explain it.", "Lucky for who? Not me.", "I've got a theory, but it's not polite." },
        new[] { "Floor's dry over there now, just so you know. Mostly.", "Mostly?", "Thanks, Sam.", "Mostly. Walk like a cat.", "Anytime, boss." },
        new[] { "Is it me, or is the basement whispering again?", "It's you.", "It's the pipes.", "Good. I'll keep pretending that.", "Pipes don't hum lullabies, but fine." },
        new[] { "You're doing alright, you know. Place hasn't been this busy in years.", "Really?", "Couldn't do it without you.", "Really. The Dean even smiled. I saw it.", "Ha. Tell that to my back." },
        new[] { "Lunch was a cold samosa. Living the dream.", "I'll bring you a hot one tomorrow.", "Very glamorous.", "Now that's a boss worth working for.", "Don't knock it till you've tried it at three o'clock." },
    };

    static readonly string[][] Omar =
    {
        new[] { "This binding is hand-stitched. Someone spent a whole week on it.", "That's beautiful.", "A whole week?", "Isn't it? I almost don't want to fix it.", "Seven days, judging by the knots. I can tell. It's a sickness." },
        new[] { "I've been typing the catalogue since nine. My fingers sound like rain.", "Take a break.", "How far are you?", "Two more drawers. Then a break. Maybe.", "The letter C. It's a very long letter." },
        new[] { "Somebody used a slice of bread as a bookmark. Bread.", "Toasted or plain?", "That's criminal.", "...Plain. Which somehow makes it worse.", "I've drafted a strongly worded note." },
        new[] { "Old newspapers smell like vanilla, you know. It's the paper breaking down.", "That's oddly lovely.", "So the Archive is a bakery?", "Everything old smells a little sweet. Like grandparents.", "A very slow bakery. Nothing's come out of the oven since 1974." },
        new[] { "I found my old school report in Stacks B. Don't ask how.", "What did it say?", "I won't ask.", "'Omar talks to books more than people.' Accurate.", "Thank you. Some mysteries should stay archived." },
        new[] { "Glue's a bit thick today. Weather, probably.", "Can you still work with it?", "You sound like Sam's knees.", "Oh yes. I'll just be slower and grumpier.", "Sam's knees are honestly better than the forecast." },
        new[] { "If anyone asks, the typewriter jammed. I definitely didn't fall asleep on it.", "Your secret's safe.", "You've got key marks on your cheek.", "Much appreciated.", "...Do I? Oh no." },
        new[] { "I like this job. Books don't interrupt you.", "I'm interrupting you right now.", "Same here.", "You're the exception. You bring tea.", "Good. We'll be quiet together, then." },
    };

    static readonly string[][] Arthur =
    {
        new[] { "When I was your age, this room had a gramophone. Same waltz every afternoon.", "What happened to it?", "Sounds lovely.", "The old Dean sat on it. Long story. Lovely man, terrible balance.", "It was. I still hum it when it's quiet." },
        new[] { "A professor held one page up to the light for ten minutes. I didn't have the heart to rush her.", "Good call.", "Ten whole minutes?", "Patience is half this job. The other half is not sneezing.", "Eleven, if I'm honest. I counted the clock." },
        new[] { "Some of these books are older than my grandfather's grandfather. I still say good morning to them.", "Do they answer?", "That's sweet.", "Only on Sundays.", "Manners cost nothing, and they've earned it." },
        new[] { "My wife says I talk about this room more than I talk about her.", "Do you?", "Tell her she's lovely from me.", "Don't tell her, but... possibly.", "I shall. She'll pretend she doesn't care. She'll care." },
        new[] { "Gloves on, always. Fingers are a book's worst enemy. After mice.", "Mice?", "Noted.", "We had one called Gerald. He ate a first edition. Gerald is no longer with us.", "Good. You'll do fine in here." },
        new[] { "A scholar asked me the way to the washroom in Latin. I answered in Latin. We were both very proud.", "Impressive.", "What's washroom in Latin?", "Two years of night classes, finally useful.", "I've forgotten already. Lovely moment, though." },
        new[] { "It's ever so quiet in here. Sometimes I hear the pages settle.", "That's peaceful.", "Is that the ghost?", "Best seat in the library, this.", "The ghost and I have an understanding. He keeps to the Archive." },
        new[] { "In my day we had card catalogues. Now you have a computer. Marvellous, really.", "Want me to show you?", "Do you miss the cards?", "Perhaps on a slow afternoon. Don't let me press anything.", "A little. They smelled better." },
    };

    // Busy replies once you've had a chat; a new chat is ready a few hours later.
    static readonly string[] SamBusy = { "Can't stop, boss, these floors won't mop themselves.", "Later, alright? There's a spill with my name on it.", "Back to work for me. Ask me again after tea." };
    static readonly string[] OmarBusy = { "Sorry, the glue's setting. Can't look away.", "Mid-stitch! Talk later?", "I've got a pile of books glaring at me. Later." };
    static readonly string[] ArthurBusy = { "Forgive me, a scholar is waiting.", "Duty calls, I'm afraid. Another time.", "The Rare Books won't watch themselves, my dear." };
    const float TalkCooldownHours = 3.5f;
    int talkDay = -1;
    float talkHour;

    int lastLine = -1;
    NavMeshAgent agent;

    string Name => GameManager.StaffPerson[(int)role];
    string[][] Lines => role == GameManager.StaffRole.Cleaner ? Sam : role == GameManager.StaffRole.Repairer ? Omar : Arthur;

    protected override string DefaultVerb => "Talk to " + Name;
    public override Vector3 PromptAnchor => transform.position + Vector3.up * 1.0f;

    protected override void Awake()
    {
        base.Awake();
        range = 2.8f;
        agent = GetComponent<NavMeshAgent>();
    }

    public override void Interact(GameObject player)
    {
        var ui = DialogueUI.Instance;
        if (ui == null || ui.IsOpen) return;
        var gm = GameManager.Instance;
        var worker = GetComponent<StaffWorker>();
        var help = GetComponent<StaffHelp>();
        // End of the day: they tell you how it went, then head home.
        if (worker != null && worker.WaitingToReport && help != null && help.Reporting)
        {
            worker.StopToTalk(player.transform.position); // stop where you caught them, then head home
            var r = ScriptableObject.CreateInstance<DialogueAsset>();
            r.speaker = Name;
            // Left alone all day (no help, no chat): it shows.
            bool ignored = !help.attentionToday;
            if (ignored && gm != null) gm.ChangeStaffMood(role, -0.3f);
            r.lines.Add(new DialogueAsset.Line { id = "start", text = Report(worker, help, ignored), next = "end" });
            ui.Play(r, () =>
            {
                Destroy(r);
                help.ReportDone();
                if (worker.earnedToday > 0 && gm != null)
                {
                    int handed = worker.earnedToday - Cut(worker.earnedToday); // they keep their cut
                    gm.Today.staffKept += Cut(worker.earnedToday);
                    if (handed > 0)
                    {
                        gm.AddCoins(handed);
                        gm.Today.Earn(GameManager.DayStats.Source.Staff, handed);
                        RequestLog.Money(GameManager.StaffPerson[(int)role], $"Handed over the day's earnings ({worker.earnedToday} coins, kept {Cut(worker.earnedToday)} as their {CutPercent}% cut)", "Staff report", handed);
                        GameAudio.Play(a => a.coin);
                    }
                    worker.earnedToday = 0;
                }
                StaffManager.Instance?.Dismiss(worker);
            });
            return;
        }
        // Stuck on something: sort it out.
        var problem = help != null ? help.ProblemDialogue(Name) : null;
        if (problem != null)
        {
            string how = null;
            System.Action<string> seen = id => { if (id == "paid" || id == "free") how = id; };
            ui.LineShown += seen;
            ui.Play(problem, () => { ui.LineShown -= seen; Destroy(problem); if (how != null) help.Resolve(how); });
            return;
        }
        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = Name;
        if (gm != null && talkDay == gm.day && gm.Hour - talkHour < TalkCooldownHours)
        {
            var busy = role == GameManager.StaffRole.Cleaner ? SamBusy : role == GameManager.StaffRole.Repairer ? OmarBusy : ArthurBusy;
            // A quick line over their head; the clock keeps going.
            Destroy(d);
            if (WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(this, busy[Random.Range(0, busy.Length)], 3f);
            return;
        }
        if (gm != null) { talkDay = gm.day; talkHour = gm.Hour; }

        // The first chat of the day cheers them up a little (and counts as looking after them).
        if (help != null && !help.attentionToday) { help.attentionToday = true; gm?.ChangeStaffMood(role, 0.15f); }
        var lines = Lines;
        int pick = Random.Range(0, lines.Length);
        if (pick == lastLine) pick = (pick + 1) % lines.Length;
        lastLine = pick;
        var t = lines[pick];

        var start = new DialogueAsset.Line { id = "start", text = t[0] };
        start.choices.Add(new DialogueAsset.Choice { text = t[1], next = "a" });
        start.choices.Add(new DialogueAsset.Choice { text = t[2], next = "b" });
        d.lines.Add(start);
        d.lines.Add(new DialogueAsset.Line { id = "a", text = t[3], next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "b", text = t[4], next = "end" });

        // Stop and turn to face you while you chat (unless they're sitting).
        bool walking = agent != null && agent.enabled;
        if (walking) agent.isStopped = true;
        var look = player.transform.position - transform.position; look.y = 0f;
        if (walking && look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
        ui.Play(d, () =>
        {
            Destroy(d);
            if (agent != null && agent.enabled) agent.isStopped = worker != null && worker.OnHold;
        });
    }

    /// <summary>Staff keep this share of what they earned when they hand it over at the end of the day.</summary>
    const int CutPercent = 25;
    /// <summary>Their cut: at least 1 coin, and they always hand something over (from 2 coins up).</summary>
    static int Cut(int earned) => earned <= 0 ? 0 : earned == 1 ? 1 : Mathf.Clamp(Mathf.RoundToInt(earned * CutPercent / 100f), 1, earned - 1);

    /// <summary>A few words about today's work.</summary>
    string Report(StaffWorker w, StaffHelp help, bool ignored)
    {
        string work = role == GameManager.StaffRole.Cleaner
            ? (w.cleanedToday > 0 ? $"I tidied up <color=#D67840>{w.cleanedToday}</color> messes today." : "Quiet day for mess. I polished the floor instead.")
            : role == GameManager.StaffRole.Repairer
            ? (w.repairedToday + w.shelvedToday > 0 ? $"I mended <color=#D67840>{w.repairedToday}</color> books and put <color=#D67840>{w.shelvedToday}</color> back on the shelves." : "Nothing came in for repair today, so I caught up on the catalogue.")
            : (w.servedToday > 0 ? $"I looked after <color=#D67840>{w.servedToday}</color> scholars in Rare Books." : "No scholars came by, so I dusted the display cases. Twice.");
        string thanks = help.helpedToday > 1 ? $" Thanks for helping me out {help.helpedToday} times."
                      : help.helpedToday == 1 ? " Thanks for sorting out that problem." : "";
        string bye = role == GameManager.StaffRole.Cleaner ? " Night, boss!" : role == GameManager.StaffRole.Repairer ? " Goodnight!" : " Goodnight, my dear.";
        int cut = Cut(w.earnedToday);
        string money = w.earnedToday <= 0 ? ""
                     : w.earnedToday - cut > 0 ? $" My work brought in <color=#D67840>{w.earnedToday} coins</color>. I've kept my {CutPercent}% (<color=#D67840>{cut}</color>), so here's <color=#D67840>{w.earnedToday - cut} coins</color> for you."
                     : $" My work brought in <color=#D67840>{w.earnedToday} coin</color>, and that's my cut, I'm afraid.";
        // How they feel about the job (their mood decides how fast they work).
        var gm = GameManager.Instance;
        float mood = gm != null ? gm.staffMood[(int)role] : 3f;
        string feel = ignored ? " You didn't stop by once today. Just saying."
                    : help.paidToday ? " And thanks for paying for the proper fix. I'll be in bright and early tomorrow."
                    : mood >= 4f ? " I do love this job, you know."
                    : mood < 1.2f ? " I'm not sure how much more of this I can take, boss."
                    : mood < 2.5f ? " Bit of a long day, if I'm honest." : "";
        return work + money + thanks + feel + bye;
    }
}
