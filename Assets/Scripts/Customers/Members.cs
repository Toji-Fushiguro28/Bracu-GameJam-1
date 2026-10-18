using System;
using System.Collections.Generic;
using UnityEngine;

public enum AccessLevel { Basic, Archive, Rare }

/// <summary>A library card. Satisfaction 0..5 hearts: raises tips and patience.</summary>
[Serializable]
public class Member
{
    public string key, name, dob;
    public AccessLevel access;
    public float satisfaction = 3f;
    public int visits, spent;
    public int Hearts => Mathf.Clamp(Mathf.RoundToInt(satisfaction), 0, 5);
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
        { "PF_NPC_Student_Zara", P("Zara Mensah", "26 Apr 2003", AccessLevel.Basic, "Just the main hall, please. I like the comfy chairs.") },
        { "PF_NPC_Student_Ruby", P("Ruby Collins", "09 Dec 2002", AccessLevel.Basic, "A normal card is perfect, thank you!") },
        { "PF_NPC_Student_Hiro", P("Hiro Nakamura", "13 Feb 2004", AccessLevel.Basic, "Basic is fine. I mostly read comics anyway.") },
        { "PF_NPC_Student_Theo", P("Theo Grant", "30 Sep 2003", AccessLevel.Archive, "My history project needs the Archive's old letters.") },
        { "PF_NPC_Student_Priya", P("Priya Das", "05 Jul 2002", AccessLevel.Basic, "Main hall only. I'm here for the poetry.") },
        { "PF_NPC_Student_Leila", P("Leila Aziz", "22 Nov 2001", AccessLevel.Archive, "I'm studying old newspapers, so the Archive, please.") },
        { "PF_NPC_Student_Arjun", P("Arjun Mehta", "16 Mar 2004", AccessLevel.Basic, "Just a regular card. First time in a real library!") },
        { "PF_NPC_Visitor_Walter", P("Walter Briggs", "28 Feb 1949", AccessLevel.Basic, "I've lived here sixty years and never had a card. Time I did.") },
        { "PF_NPC_Writer_Ezra", P("Ezra Quinn", "11 May 1988", AccessLevel.Archive, "I write mysteries. The Archive is full of good ones.") },
        { "PF_NPC_Student_Sofia", P("Sofia Russo", "19 Aug 2003", AccessLevel.Basic, "Main hall is lovely, thank you.") },
    };

    static Profile P(string name, string dob, AccessLevel access, string line) =>
        new Profile { name = name, dob = dob, access = access, line = line };

    public static bool TryProfile(string key, out Profile profile) => Profiles.TryGetValue(key ?? "", out profile);

    static List<Member> List => GameManager.Instance != null ? GameManager.Instance.members : new List<Member>();
    public static IReadOnlyList<Member> All => List;

    public static Member Find(string key) => string.IsNullOrEmpty(key) ? null : List.Find(m => m.key == key);

    public static Member Add(string key, Profile p, AccessLevel access)
    {
        var m = new Member { key = key, name = p.name, dob = p.dob, access = access };
        List.Add(m);
        return m;
    }

    /// <summary>Tip multiplier: strangers tip a little, happy members a lot.</summary>
    public static float TipFactor(Member m) =>
        (m == null ? 0.6f : 0.6f + 0.16f * m.satisfaction) * (GameManager.Instance != null ? GameManager.Instance.TipMultiplier : 1f);
    public static float PatienceFactor(Member m) => m == null ? 1f : 1f + 0.04f * m.satisfaction;

    public static void Satisfy(Member m, float amount, int spent = 0)
    {
        if (m == null) return;
        m.satisfaction = Mathf.Clamp(m.satisfaction + amount, 0f, 5f);
        m.spent += spent;
    }
}
