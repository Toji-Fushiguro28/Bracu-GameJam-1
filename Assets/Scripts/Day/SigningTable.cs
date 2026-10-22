using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The book signing table (on the BookSigning object). On a signing day the visiting author sits in its chair from
/// 2:00 to 3:30, and anyone who buys one of their books then walks over to have it signed: a short chat in speech
/// bubbles, then off home. Fans have their own short line (3 at most), running out into the hall away from the
/// front desk's line.
/// </summary>
public class SigningTable : MonoBehaviour
{
    public static SigningTable Instance { get; private set; }

    [Tooltip("The author's chair (its forward faces the table).")]
    public Transform chair;
    [Tooltip("The table the fans stand at.")]
    public Transform desk;
    [Tooltip("Most fans in line at once; anyone else just goes home.")]
    public int maxLine = 3;
    public float spacing = 0.9f;
    [Tooltip("The front fan spot: this far past the table's far edge, on the side away from the chair.")]
    public float frontGap = 0.45f;
    [Tooltip("Which way the fans' line runs from the front spot (world, flat). Away from the front desk's line.")]
    public Vector3 lineDirection = Vector3.forward;
    [Tooltip("Coins the author gives the library for each fan who gets a book signed.")]
    public int fanBonus = 5;
    [Tooltip("Where the author walks to before sitting down, from the chair (world).")]
    public Vector3 approachOffset = new Vector3(0f, 0f, 0.8f);

    readonly List<Customer> line = new List<Customer>();

    /// <summary>The author, once they've sat down at the table.</summary>
    public Customer Author { get; private set; }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    // ---------------- Where everyone goes ----------------

    Vector3 ChairForward => Vector3.ProjectOnPlane(chair.forward, Vector3.up).normalized;
    public Quaternion AuthorFacing => Quaternion.LookRotation(ChairForward);
    /// <summary>Where the author's root goes in the chair (like SeatPoint: up onto the seat, forward off the back).</summary>
    public Vector3 AuthorSeat => chair.position + Vector3.up * 0.1f + ChairForward * 0.17f;
    public Vector3 AuthorApproach => chair.position + approachOffset;

    /// <summary>Fan spot number i (0 = at the table, facing the author).</summary>
    public Vector3 SlotPosition(int i)
    {
        var r = desk.GetComponentInChildren<Renderer>();
        float halfDepth = r != null ? Vector3.Scale(r.bounds.extents, ChairForward).magnitude : 0.4f;
        Vector3 front = new Vector3(r != null ? r.bounds.center.x : desk.position.x, chair.position.y, r != null ? r.bounds.center.z : desk.position.z)
                        + ChairForward * (halfDepth + frontGap);
        return front + Vector3.ProjectOnPlane(lineDirection, Vector3.up).normalized * spacing * i;
    }
    public Quaternion FanFacing => Quaternion.LookRotation(-ChairForward);

    // ---------------- The author ----------------

    /// <summary>The author's linger: sit at the table until the signing ends. Always handled.</summary>
    public bool SeatAuthor(Customer c, float until)
    {
        Author = c;
        c.SitAtTable(this, until);
        return true;
    }

    public void AuthorLeft(Customer c)
    {
        if (Author == c) Author = null;
    }

    bool AuthorReady => Author != null && Author.SeatedAtTable;

    // ---------------- Fans ----------------

    /// <summary>Someone just bought one of the author's books: join the signing line if it's on and there's room.</summary>
    public bool Join(Customer c, BookData book)
    {
        if (!AuthorReady || !DayStory.SigningTitle(book) || line.Count >= maxLine || line.Contains(c)) return false;
        line.Add(c);
        return true;
    }

    /// <summary>A fan got their book signed: the author thanks the library with a little bonus.</summary>
    public void Signed(Customer fan)
    {
        var gm = GameManager.Instance;
        if (gm == null || fanBonus <= 0) return;
        gm.AddCoins(fanBonus);
        gm.Today.Earn(GameManager.DayStats.Source.Other, fanBonus);
        RequestLog.Money("Book signing", "A fan got their book signed", "Signing bonus", fanBonus);
        HUD.SkillPop(fan.transform.position + Vector3.up * 2.2f, $"Signed! +{fanBonus} coins");
    }

    public int IndexOf(Customer c) => line.IndexOf(c);
    public void Remove(Customer c) => line.Remove(c);

    // What a fan says at the table, the author's answer, and how the fan signs off. Each one fits its own chat.
    static readonly (string fan, string author, string thanks)[] Chats =
    {
        ("Could you sign it to Margaret? She's my mum. She'll pretend she isn't thrilled.",
         "Margaret it is. Tell her the tea's on her next time.", "She'll frame it. Then deny it."),
        ("I've read your last one three times. The ending still annoys me. In a good way.",
         "Annoying in a good way is exactly what I was going for.", "Then you did it. Thank you."),
        ("Just your name is fine. I'm not brave enough for anything longer.",
         "Name, and a small flourish. There. Painless.", "That was painless. Thanks!"),
        ("My book group argued about chapter six for an hour. Who was right?",
         "Whoever said it was the brother. It's always the brother.", "I KNEW it. Wait till Thursday."),
        ("Could you write \"for Tom, who still owes me a fiver\"? It's a gift. For Tom.",
         "\"Who still owes me a fiver.\" Done. I hope he pays up.", "He won't. But now it's in writing."),
        ("I tried writing a book once. I got to page four.",
         "Page four is further than most. Go back and do page five.", "Page five. Right. Thank you."),
        ("Is it true you wrote the whole thing in a caravan?",
         "Half of it. The other half in a launderette. Better light.", "A launderette! Brilliant."),
        ("I bought two. One to read and one to keep nice.",
         "A careful reader. I'll sign the nice one. Don't let it near tea.", "No tea. Promise. Thank you!"),
        ("I'm not a big reader, honestly. But my dad loves you.",
         "Then I'll sign it to your dad. And you might borrow it after.", "Maybe I will. Thanks."),
        ("Do you ever get tired of signing your name?",
         "Only when it's a cheque. This is the nice kind.", "Ha! Thanks very much."),
        ("Sorry, my hands are shaking. I've never met an author.",
         "We're very ordinary. I had toast for lunch. There you go.", "Toast. Okay. Thank you so much."),
        ("Will there be another one? Please say there's another one.",
         "There's another one. It's mostly in my head, but it's there.", "I'll wait. Not patiently."),
        ("Could you sign it on the first page? The title page always feels too posh.",
         "First page it is. I agree, the title page has airs.", "Perfect. Thank you!"),
        ("I came on the bus from Brockley just for this.",
         "Brockley! Then this one gets my best handwriting.", "Worth the bus. Thanks!"),
    };

    readonly List<int> chatsLeft = new List<int>();

    /// <summary>The next fan's chat: no repeats until every one has been used.</summary>
    public (string fan, string author, string thanks) NextChat()
    {
        if (chatsLeft.Count == 0)
            for (int i = 0; i < Chats.Length; i++) chatsLeft.Insert(Random.Range(0, chatsLeft.Count + 1), i);
        int pick = chatsLeft[chatsLeft.Count - 1];
        chatsLeft.RemoveAt(chatsLeft.Count - 1);
        return Chats[pick];
    }

    void OnDrawGizmosSelected()
    {
        if (chair == null || desk == null) return;
        Gizmos.color = new Color(1f, 0.8f, 0.3f);
        for (int i = 0; i < maxLine; i++) Gizmos.DrawWireSphere(SlotPosition(i) + Vector3.up * 0.1f, 0.25f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(AuthorApproach + Vector3.up * 0.1f, 0.2f);
        Gizmos.DrawRay(AuthorSeat, ChairForward * 0.5f);
    }
}
