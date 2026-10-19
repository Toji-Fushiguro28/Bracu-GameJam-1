using UnityEngine;

/// <summary>
/// The notice board: one "Today's Notice" per day, going through every notice before repeating.
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
    public float bubbleSeconds = 5f;
    [Tooltip("Notices pinned each day; clicking cycles through them.")]
    public int perDay = 6;

    protected override string DefaultVerb => "Read notice";

    int[] order;
    int reads, readsDay = -1;

    /// <summary>Each click reads the next notice of a fixed shuffle; each day starts further along the list.</summary>
    string Next()
    {
        if (notices == null || notices.Length == 0) return "";
        if (order == null || order.Length != notices.Length)
        {
            order = new int[notices.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            var rng = new System.Random(489); // same order every playthrough
            for (int i = order.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        }
        int day = GameManager.Instance != null ? GameManager.Instance.day : 1;
        if (day != readsDay) { readsDay = day; reads = 0; }
        // In the first week, today's twist has its own notice, pinned first.
        string pinned = DayStory.Instance != null ? DayStory.Instance.Notice : null;
        int k = pinned != null ? reads++ % (perDay + 1) : reads++ % perDay;
        if (pinned != null) { if (k == 0) return pinned; k--; }
        // Each day pins its own set of notices; clicks cycle through that day's set.
        int index = ((day - 1) * perDay + k) % order.Length;
        return notices[order[index]];
    }

    public override void Interact(GameObject player)
    {
        WorldPrompts.Instance?.ShowBubble(this, "Today's Notice: " + Next(), bubbleSeconds);
    }
}
