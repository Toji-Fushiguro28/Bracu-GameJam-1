using System;
using System.Collections.Generic;
using UnityEngine;

public enum AccessLevel { Basic, Archive, Rare }

/// <summary>A library card. Satisfaction 0..5 hearts (in halves): raises tips and patience. 4 = Friend (brings
/// friends who want a card), 5 = Patron (donates books), 1 or less = Unhappy (may hand the card back).</summary>
[Serializable]
public class Member
{
    public string key, name, dob;
    public AccessLevel access;
    public float satisfaction = 3f;
    public int visits, spent;
    /// <summary>Let down (left angry, lied to, or never got their order): they stay away until after this day.</summary>
    public int awayUntil;
    /// <summary>Their last few real complaints, oldest first (the Members page; their words if they hand the card back).</summary>
    public List<Complaint> complaints = new List<Complaint>();
    /// <summary>The day they last brought the library books (patrons, at most every 3 days).</summary>
    public int lastDonationDay;
    /// <summary>Hearts lost on the visit they're on now (capped, so one bad visit can't sink a happy member).</summary>
    [NonSerialized] public float visitLoss;
    public int Hearts => Mathf.Clamp(Mathf.RoundToInt(satisfaction), 0, 5);
    public string Title => Members.TitleOf(satisfaction);
}

/// <summary>Something that went wrong for a member, in their words.</summary>
[Serializable]
public class Complaint
{
    public int day;
    public string text;
}

/// <summary>A card handed back: that visitor can't sign up again for a couple of days.</summary>
[Serializable]
public class ReturnedCard
{
    public string key;
    public int day;
}

/// <summary>Who each visitor is (what they tell you when they sign up) and the card list.</summary>
public static class Members
{
    public struct Profile
    {
        public string name, dob, line;
        public AccessLevel access;
    }

    // Keyed by the character prefab name.
    static readonly Dictionary<string, Profile> Profiles = new Dictionary<string, Profile>
    {
        { "PF_NPC_Professor_Edmund", P("Edmund Thorne", "04 Feb 1958", AccessLevel.Rare, "I teach history at the university - I'll need the Rare Books room.") },
        { "PF_NPC_Professor_Hazel", P("Hazel Whitcombe", "19 Sep 1963", AccessLevel.Rare, "Professor of philosophy. My research needs the rare manuscripts.") },
        { "PF_NPC_Scholar_Omar", P("Omar Haddad", "27 Jun 1985", AccessLevel.Archive, "I'm a researcher, so I'll be in the Archive a lot.") },
        { "PF_NPC_Student_Amara", P("Amara Okafor", "11 Mar 2003", AccessLevel.Basic, "Just the main hall for me, thanks!") },
        { "PF_NPC_Student_Clara", P("Clara Bennett", "30 Jul 2002", AccessLevel.Basic, "Only the normal shelves, nothing fancy.") },
        { "PF_NPC_Student_Kai", P("Kai Tanaka", "08 Dec 2004", AccessLevel.Basic, "A regular card is fine.") },
        { "PF_NPC_Student_Leo", P("Leo Marsh", "15 May 2003", AccessLevel.Archive, "My thesis needs old newspapers from the Archive.") },
        { "PF_NPC_Student_Maya", P("Maya Rahman", "22 Jan 2002", AccessLevel.Basic, "I just want to read by the fire. Main hall is perfect.") },
        { "PF_NPC_Student_Nadia", P("Nadia Karim", "03 Oct 2001", AccessLevel.Archive, "I'll need the Archive for my journals.") },
        { "PF_NPC_Student_Ravi", P("Ravi Sen", "17 Apr 2004", AccessLevel.Basic, "Basic card, please - I'm new here.") },
        { "PF_NPC_Visitor_Arthur", P("Arthur Pembrey", "09 Nov 1971", AccessLevel.Basic, "Just visiting town. The main hall will do nicely.") },
        { "PF_NPC_Dean_Whitmore", P("Edgar Whitmore", "12 Apr 1955", AccessLevel.Archive, "I'm the Dean, but rules are rules. The Archive is my favourite room.") },
        { "PF_NPC_Writer_Finn", P("Finn Calloway", "25 Aug 1990", AccessLevel.Archive, "I'm writing a novel - the Archive has the old papers I need.") },
        // Recoloured regulars (Prefabs/Characters/Variants).
        { "PF_NPC_Visitor_Bernard", P("Bernard Hale", "14 Jun 1952", AccessLevel.Basic, "Retired postman. I read everything twice, just to be sure.") },
        { "PF_NPC_Visitor_Gus", P("Gus Ferreira", "02 Mar 1968", AccessLevel.Basic, "I fix boats. I read about them more than I fix them.") },
        { "PF_NPC_Professor_Victor", P("Victor Lindqvist", "21 Oct 1961", AccessLevel.Rare, "Classics department. The old manuscripts are why I'm here.") },
        { "PF_NPC_Professor_Ingrid", P("Ingrid Sato", "07 Jan 1966", AccessLevel.Rare, "I study medieval maps. I'll need the Rare Books room.") },
        { "PF_NPC_Scholar_Tariq", P("Tariq Mansour", "18 Aug 1983", AccessLevel.Archive, "Local historian. The Archive is my second home.") },
        { "PF_NPC_Student_Zara", P("Radiya Khatoon", "26 Apr 2003", AccessLevel.Basic, "Just the main hall, please. I like the comfy chairs.") },
        { "PF_NPC_Student_Ruby", P("Ruby Collins", "09 Dec 2002", AccessLevel.Basic, "A normal card is perfect, thank you!") },
        { "PF_NPC_Student_Hiro", P("Hiro Nakamura", "13 Feb 2004", AccessLevel.Basic, "Basic is fine. I mostly read comics anyway.") },
        { "PF_NPC_Student_Theo", P("Theo Grant", "30 Sep 2003", AccessLevel.Archive, "My history project needs the Archive's old letters.") },
        { "PF_NPC_Student_Priya", P("Priya Das", "05 Jul 2002", AccessLevel.Basic, "Main hall only. I'm here for the poetry.") },
        { "PF_NPC_Student_Leila", P("Leila Aziz", "22 Nov 2001", AccessLevel.Archive, "I'm studying old newspapers, so the Archive, please.") },
        { "PF_NPC_Student_Arjun", P("Arjun Mehta", "16 Mar 2004", AccessLevel.Basic, "Just a regular card. First time in a real library!") },
        { "PF_NPC_Visitor_Walter", P("Walter Wheat", "28 Feb 1949", AccessLevel.Basic, "I've lived here sixty years and never had a card. Time I did.") },
        { "PF_NPC_Writer_Ezra", P("Ezra Quinn", "11 May 1988", AccessLevel.Archive, "I write mysteries. The Archive is full of good ones.") },
        { "PF_NPC_Student_Sofia", P("Sofia Russo", "19 Aug 2003", AccessLevel.Basic, "Main hall is lovely, thank you.") },
    };

    static Profile P(string name, string dob, AccessLevel access, string line) =>
        new Profile { name = name, dob = dob, access = access, line = line };

    public static bool TryProfile(string key, out Profile profile) => Profiles.TryGetValue(key ?? "", out profile);

    static List<Member> List => GameManager.Instance != null ? GameManager.Instance.members : new List<Member>();
    public static IReadOnlyList<Member> All => List;

    public static Member Find(string key) => string.IsNullOrEmpty(key) ? null : List.Find(m => m.key == key);

    /// <summary>A new card: 3 hearts, or 2.5 if the card took a few tries.</summary>
    public static Member Add(string key, Profile p, AccessLevel access, bool mistakes = false)
    {
        var m = new Member { key = key, name = p.name, dob = p.dob, access = access, satisfaction = mistakes ? 2.5f : 3f };
        List.Add(m);
        GameManager.Instance?.returnedCards.RemoveAll(r => r.key == key); // a fresh start
        return m;
    }

    // ---------------- Titles ----------------

    public const float FriendHearts = 4f, PatronHearts = 5f, UnhappyHearts = 1f;
    public static string TitleOf(float hearts) => hearts >= PatronHearts ? "Patron" : hearts >= FriendHearts ? "Friend" : hearts <= UnhappyHearts ? "Unhappy" : "Member";
    public static string PerkOf(string title) => title == "Patron" ? "donates books to the library"
        : title == "Friend" ? "brings friends who want a card"
        : title == "Unhappy" ? "may hand their card back" : "tips and waits a little more when happy";

    /// <summary>No hearts, complaints, gifts or card returns during the tutorial or for the Dean. (VIP scholars do count;
    /// the thief never gains or loses any: no code path touches their hearts.)</summary>
    public static bool Counts(Member m) => m != null && !Tutorial.HoldsClock && !m.key.Contains("Dean");

    /// <summary>A member walks in: what they lose on this visit starts from nothing.</summary>
    public static void StartVisit(Member m) { if (m != null) m.visitLoss = 0f; }

    // ---------------- Complaints ----------------

    public enum Gripe { Ignored, WrongBook, WrongShelf, WalkedOut, NeverOrdered, CantGetIt, WaitedForBook, WaitedAtDesk }
    static readonly string[][] GripeLines =
    {
        new[] { "I waved at you for ages and nobody came over.", "I wanted a word, and you walked right past me.", "I waited by the shelves for help that never came." },
        new[] { "You gave me the wrong book. Do you even know your own shelves?", "I asked for one book and got another.", "You handed me the wrong book." },
        new[] { "You sent me to the wrong shelf.", "I looked where you told me. It wasn't there." },
        new[] { "I stood at the desk forever and gave up.", "Nobody served me, so I walked out.", "The queue never moved for me." },
        new[] { "You promised to order my book. You never did.", "My reserved book was never ordered." },
        new[] { "You said you couldn't get a book that was right there on the shelf.", "You never have what I ask for." },
        new[] { "I sat there all day waiting for a book that never came.", "I waited hours for my order for nothing." },
        new[] { "I waited at the desk with my book and nobody came.", "I couldn't even get my book stamped." },
    };

    /// <summary>Remembers what went wrong (their last three), in their own words.</summary>
    public static void Complain(Member m, Gripe gripe)
    {
        if (!Counts(m) || GameManager.Instance == null) return;
        if (m.complaints == null) m.complaints = new List<Complaint>();
        var lines = GripeLines[(int)gripe];
        m.complaints.Add(new Complaint { day = GameManager.Instance.day, text = lines[UnityEngine.Random.Range(0, lines.Length)] });
        while (m.complaints.Count > 3) m.complaints.RemoveAt(0);
    }

    // ---------------- Handing the card back ----------------

    /// <summary>Unhappy, and with real reasons: on their next visit they hand the card back (old saves with no
    /// complaints on record never do).</summary>
    public static bool WantsToLeave(Member m) => Counts(m) && m.satisfaction <= UnhappyHearts && m.complaints != null && m.complaints.Count > 0;

    /// <summary>What they say: their two latest complaints.</summary>
    public static string LeavingWords(Member m)
    {
        var said = new List<string>();
        if (m.complaints != null)
            for (int i = m.complaints.Count - 1; i >= 0 && said.Count < 2; i--)
                if (!said.Contains(m.complaints[i].text)) said.Add(m.complaints[i].text);
        return said.Count == 0 ? "It's just not for me anymore." : string.Join(" And ", said);
    }

    /// <summary>The card comes back: off the list, their reservations cancelled, -2 reputation, a notice and a Log line.</summary>
    public static void ReturnCard(Member m)
    {
        var gm = GameManager.Instance;
        if (m == null || gm == null || !List.Contains(m)) return;
        string reasons = LeavingWords(m);
        List.Remove(m);
        gm.reservations.RemoveAll(r => r.key == m.key);
        gm.returnedCards.RemoveAll(r => r.key == m.key);
        gm.returnedCards.Add(new ReturnedCard { key = m.key, day = gm.day });
        gm.AddReputation(-2);
        gm.Today.memberNews.Add($"Returned: {m.name}");
        UnityEngine.Object.FindAnyObjectByType<HUD>()?.ShowBanner($"{m.name} handed their library card back.", 7f);
        RequestLog.News($"{m.name} returned their card: \"{reasons}\"");
    }

    /// <summary>Not if they handed their card back in the last two days.</summary>
    public static bool CanSignUp(string key)
    {
        var gm = GameManager.Instance;
        return gm == null || !gm.returnedCards.Exists(r => r.key == key && gm.day <= r.day + 2);
    }

    // ---------------- Patrons' gifts and Friends' friends ----------------

    public static bool CanDonate(Member m) => Counts(m) && m.satisfaction >= PatronHearts
        && (m.lastDonationDay == 0 || GameManager.Instance.day - m.lastDonationDay >= 3); // 0 = never donated yet

    static int giftDay = -1;
    static readonly HashSet<BookData> giftedToday = new HashSet<BookData>();

    /// <summary>1-2 books a patron brings: ordinary titles whose shelf is open, sold-out and low ones first,
    /// never past 3 copies, and each title at most once a day.</summary>
    public static List<BookData> PickDonation()
    {
        var gm = GameManager.Instance;
        var picked = new List<BookData>();
        if (gm == null) return picked;
        if (giftDay != gm.day) { giftDay = gm.day; giftedToday.Clear(); }
        var shelves = UnityEngine.Object.FindObjectsByType<ShelfInteractable>();
        var pool = gm.Books.FindAll(b => b.Sellable && b.Format == BookFormat.Book && !BookRequest.IsRare(b) && b.Quantity < 3 && !giftedToday.Contains(b)
            && Array.Exists(shelves, s => s.Belongs(b) && !RoomGate.IsLockedAt(s.transform.position)));
        pool.Sort((a, b) => a.Quantity.CompareTo(b.Quantity)); // the emptiest first
        int want = UnityEngine.Random.Range(1, 3);
        while (picked.Count < want && pool.Count > 0)
        {
            int i = UnityEngine.Random.Range(0, Mathf.Min(4, pool.Count));
            picked.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return picked;
    }

    /// <summary>The books go straight onto their shelves.</summary>
    public static void Donate(Member m, List<BookData> books)
    {
        var gm = GameManager.Instance;
        if (m == null || gm == null || books == null || books.Count == 0) return;
        if (giftDay != gm.day) { giftDay = gm.day; giftedToday.Clear(); }
        foreach (var b in books) { b.AddCopy(BookCondition.Good); giftedToday.Add(b); }
        m.lastDonationDay = gm.day;
        gm.NotifyBooksChanged();
        gm.Today.memberNews.Add($"Gift: {books.Count} {(books.Count == 1 ? "book" : "books")} from {m.name}");
        UnityEngine.Object.FindAnyObjectByType<HUD>()?.ShowBanner($"{m.name} donated {books.Count} {(books.Count == 1 ? "book" : "books")} to the library. They're on the shelves.", 6f);
    }

    static int friendDay = -1, friendsToday;

    /// <summary>A Friend sometimes brings someone who wants a card (at most two a day).</summary>
    public static bool BringsFriend(Member m)
    {
        var gm = GameManager.Instance;
        if (!Counts(m) || gm == null || m.satisfaction < FriendHearts || gm.CurrentPhase != GameManager.Phase.Open) return false;
        if (friendDay != gm.day) { friendDay = gm.day; friendsToday = 0; }
        if (friendsToday >= 2 || UnityEngine.Random.value > 0.25f) return false;
        friendsToday++;
        return true;
    }

    /// <summary>Tip multiplier: strangers tip a little, happy members a lot.</summary>
    public static float TipFactor(Member m) =>
        (m == null ? 0.6f : 0.6f + 0.16f * m.satisfaction) * (GameManager.Instance != null ? GameManager.Instance.TipMultiplier : 1f);
    public static float PatienceFactor(Member m) => m == null ? 1f : 1f + 0.04f * m.satisfaction;

    /// <summary>A regular you let down skips tomorrow.</summary>
    public static void LetDown(Member m)
    {
        var gm = GameManager.Instance;
        if (m != null && gm != null) m.awayUntil = gm.day + 1;
    }

    public static bool Away(Member m) => m != null && GameManager.Instance != null && m.awayUntil >= GameManager.Instance.day;

    /// <summary>Hearts up or down (spent coins are added too). A single visit can cost at most 1.5 hearts. When a
    /// half heart is crossed a heart pops over their head; a new title (Friend, Patron, Unhappy) gets a notice.</summary>
    public static void Satisfy(Member m, float amount, int spent = 0)
    {
        if (m == null) return;
        m.spent += spent;
        if (!Counts(m)) { m.satisfaction = Mathf.Clamp(m.satisfaction + amount, 0f, 5f); return; }
        if (amount > 0f) amount *= GainScale; // hearts are earned over a few days, not one visit
        if (amount < 0f) { amount = -Mathf.Min(-amount, Mathf.Max(0f, MaxVisitLoss - m.visitLoss)); m.visitLoss -= amount; }
        float before = m.satisfaction;
        m.satisfaction = Mathf.Clamp(before + amount, 0f, 5f);
        float change = m.satisfaction - before;
        if (Mathf.Approximately(change, 0f)) return;
        var gm = GameManager.Instance;
        if (gm != null) { if (change > 0f) gm.Today.heartsUp += change; else gm.Today.heartsDown -= change; }

        // A heart pops whenever a half-heart line is crossed (a lost half always crosses one).
        if (Mathf.FloorToInt(before * 2f) != Mathf.FloorToInt(m.satisfaction * 2f)) Pop(m, change > 0f);

        string was = TitleOf(before), now = TitleOf(m.satisfaction);
        if (was == now) return;
        var hud = UnityEngine.Object.FindAnyObjectByType<HUD>();
        if (now == "Friend" && was != "Patron") { hud?.ShowBanner($"{m.name} is now a Friend of the library: they'll bring friends.", 6f); gm?.Today.memberNews.Add($"New Friend: {m.name}"); }
        else if (now == "Patron") { hud?.ShowBanner($"{m.name} is now a Patron of the library: they'll donate books.", 6f); gm?.Today.memberNews.Add($"New Patron: {m.name}"); }
        else if (now == "Unhappy") hud?.ShowBanner($"{m.name} is unhappy with the library. Treat them well, or they'll hand their card back.", 7f);
    }

    const float MaxVisitLoss = 1.5f;
    /// <summary>Every heart gained is scaled by this (losses aren't): Friend around Day 2-3, Patron around Day 3-4
    /// for a well-treated regular.</summary>
    const float GainScale = 0.7f;

    /// <summary>A little heart over the member's head: full and rising, or faded and falling.</summary>
    static void Pop(Member m, bool up)
    {
        foreach (var c in UnityEngine.Object.FindObjectsByType<Customer>())
            if (c.profileKey == m.key)
            {
                HUD.SkillPop(c.transform.position + Vector3.up * 1.45f, up ? "+\u00BD" : "-\u00BD", up ? "heart-up" : "heart-down");
                GameAudio.Play(a => up ? a.heartUp : a.heartDown, 0.6f);
                return;
            }
    }
}
