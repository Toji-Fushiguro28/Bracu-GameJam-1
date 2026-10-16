using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

/// <summary>
/// A visiting customer. Three kinds:
///  Buyer    - asks for an item, pays for it and leaves with it.
///  Reader   - asks for an item, sits on a chair/sofa/armchair to read it, then brings it back,
///             pays a reading fee and leaves. The copy goes into the return bin.
///  Returner - only brings back a book they borrowed earlier.
/// Patience only runs while they stand at the counter. Added at runtime by CustomerSpawner.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Customer : ClickInteractable
{
    public enum Kind { Buyer, Reader, Returner, SignUp }
    enum State { Queueing, Waiting, ToSeat, Reading, CheckIn, Leaving }

    public Kind kind;
    [Tooltip("Dean / professors: read Rare Books in the Rare Books room.")]
    public bool vip;
    [Tooltip("Character prefab name: who this visitor is (see Members).")]
    public string profileKey;
    public Member Membership => Members.Find(profileKey);
    public float walkSpeed = 1.6f;
    [Tooltip("Share of patience lost for a wrong book.")]
    public float wrongBookPenalty = 0.25f;
    public Vector2 readSeconds = new Vector2(25f, 45f);

    NavMeshAgent agent;
    CharacterAnimator anim;
    State state;
    BookRequest request;
    float patience, maxPatience;
    float reactionUntil;
    string clue;
    bool impatientLoop;

    // The copy they're holding (reading or returning)
    BookData heldBook;
    BookCondition heldCondition;
    SeatPoint seat;
    int seatIndex;
    float readUntil;

    VisualElement overhead, bar, fill;
    Label speech;
    WorldPrompts.Follower follower;

    public BookRequest CurrentRequest => request;
    public bool IsWaiting => state == State.Waiting;
    public float PatienceLeft => maxPatience > 0f ? Mathf.Clamp01(patience / maxPatience) : 1f;

    protected override string DefaultVerb => "Ask";
    public override string Verb
    {
        get
        {
            if (state == State.CheckIn) return "Check in";
            if (state == State.Reading) return chatted ? "Reading..." : "Chat";
            if (kind == Kind.SignUp) return introduced ? "Card on the PC" : "Talk";
            var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
            return carry != null && !carry.IsEmpty ? "Give book" : "Ask";
        }
    }
    public override Vector3 PromptAnchor => transform.position + Vector3.up * 1.0f;
    public override bool CanInteract => state == State.Waiting || state == State.CheckIn || state == State.Reading;

    bool introduced;
    /// <summary>The sign-up visitor waiting at the counter who has told you their details.</summary>
    public static Customer WaitingForCard { get; private set; }
    string SignUpText => !introduced ? "I'd like a library card, please!"
        : Members.TryProfile(profileKey, out var p) ? $"I'm {p.name}, born {p.dob}.\n<i>{p.line}</i>" : "";

    string RequestText => kind == Kind.SignUp ? SignUpText : request == null ? "" : clue == null ? request.Line : $"{request.Line}\n<i>{clue}</i>";

    protected override void Awake()
    {
        base.Awake();
        range = 2.8f;
        agent = GetComponent<NavMeshAgent>();
        agent.speed = walkSpeed;
        agent.angularSpeed = 540f;
        agent.radius = 0.3f;
        agent.stoppingDistance = 0.05f;
        anim = GetComponent<CharacterAnimator>();
    }

    void Start()
    {
        BuildOverhead();
        if (kind == Kind.Returner)
        {
            // A copy they borrowed some days ago, in whatever state they kept it.
            var list = GameManager.Instance.Books;
            var lendable = list.FindAll(b => b.Format == BookFormat.Book && b.Sellable);
            if (lendable.Count > 0)
            {
                heldBook = lendable[Random.Range(0, lendable.Count)];
                heldCondition = RollWear(Random.value < 0.5f ? BookCondition.Good : BookCondition.Worn);
            }
            else kind = Kind.Buyer;
        }
        JoinQueue();
    }

    void OnDestroy()
    {
        if (CounterQueue.Instance != null) CounterQueue.Instance.Leave(this);
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.Unfollow(follower);
        if (seat != null) seat.Release(this);
    }

    bool closingHandled;

    /// <summary>Lights out: whoever hasn't been served heads home, readers wrap up.</summary>
    void HandleClosed()
    {
        closingHandled = true;
        if (state == State.Reading) { readUntil = Time.time; return; }
        if (state == State.Queueing || state == State.Waiting)
        {
            Say("Oh, closing time already? Tomorrow, then.", 2.5f);
            Leave();
        }
    }

    void Update()
    {
        if (!closingHandled && GameManager.Instance != null && GameManager.Instance.CurrentPhase == GameManager.Phase.Closed) HandleClosed();
        if (agent.enabled && anim) anim.SetSpeed(Mathf.Clamp01(agent.velocity.magnitude / walkSpeed) * 0.5f);

        switch (state)
        {
            case State.Queueing: UpdateQueue(); break;
            case State.Waiting:
            case State.CheckIn: UpdatePatience(); break;
            case State.ToSeat:
                if (Arrived()) SitDown();
                break;
            case State.Reading:
                // Don't get up in the middle of a chat.
                if (Time.time >= readUntil && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen)) StandUp();
                break;
            case State.Leaving:
                if (Arrived()) Destroy(gameObject);
                break;
        }

        if (reactionUntil > 0f && Time.time > reactionUntil)
        {
            reactionUntil = 0f;
            speech.text = state == State.Waiting ? RequestText : state == State.CheckIn ? CheckInLine : "";
            speech.EnableInClassList("hidden", string.IsNullOrEmpty(speech.text));
        }
    }

    // ---------------- Counter ----------------

    void JoinQueue()
    {
        CounterQueue.Instance.Join(this);
        state = State.Queueing;
    }

    void UpdateQueue()
    {
        var queue = CounterQueue.Instance;
        int index = queue.IndexOf(this);
        agent.SetDestination(queue.SlotPosition(index));
        if (index != 0 || !Arrived()) return;
        transform.rotation = queue.FacingDesk;

        if (heldBook != null) { BeginCheckIn(); return; }

        if (kind == Kind.SignUp)
        {
            StartPatience(70f);
            state = State.Waiting;
            GameAudio.Play(a => a.customerArrives);
            Say(RequestText);
            return;
        }
        request = BookRequest.Create(GameManager.Instance.Books, kind == Kind.Reader, vip);
        if (request == null) { Say("Oh... nothing I want today."); Leave(); return; }
        StartPatience(request.Patience);
        state = State.Waiting;
        GameAudio.Play(a => a.customerArrives);
        Say(RequestText);
    }

    void StartPatience(float seconds)
    {
        maxPatience = patience = seconds * (GameManager.Instance != null ? GameManager.Instance.PatienceMultiplier : 1f) * Members.PatienceFactor(Membership);
        impatientLoop = false;
        bar.RemoveFromClassList("hidden");
    }

    void UpdatePatience()
    {
        transform.rotation = Quaternion.RotateTowards(transform.rotation, CounterQueue.Instance.FacingDesk, 360f * Time.deltaTime);
        patience -= Time.deltaTime;
        float t = Mathf.Clamp01(patience / maxPatience);
        fill.style.width = Length.Percent(t * 100f);
        fill.EnableInClassList("mid", t < 0.6f && t >= 0.3f);
        fill.EnableInClassList("low", t < 0.3f);

        bool low = t < 0.3f;
        if (low != impatientLoop && anim)
        {
            impatientLoop = low;
            if (low) GameAudio.Play(a => a.impatient, 0.5f);
            anim.SetActivity(low ? CharacterAnimator.Activity.Impatient : CharacterAnimator.Activity.None);
        }
        if (patience > 0f) return;
        if (state == State.CheckIn) GiveUpCheckIn(); else LeaveAngry();
    }

    bool Arrived() => agent.enabled && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;

    public override void Interact(GameObject player)
    {
        if (state == State.CheckIn) { CheckIn(); return; }
        if (state == State.Reading) { Chat(); return; }
        if (state != State.Waiting) return;
        if (kind == Kind.SignUp)
        {
            introduced = true;
            WaitingForCard = this;
            Say(RequestText);
            return;
        }

        var carry = player.GetComponent<PlayerCarry>();
        if (carry == null || carry.IsEmpty) { GiveClue(); return; }

        var book = carry.Carried;
        bool reading = kind == Kind.Reader;
        if (!reading && !book.Sellable && request.Matches(book))
        {
            Say("Oh, that one's not for sale? Never mind then.", 2.5f);
            return;
        }
        if (!request.Matches(book))
        {
            Say("Hmm, that's not what I asked for.", 2.5f);
            GameAudio.Play(a => a.wrongBook);
            Penalise(wrongBookPenalty);
            return;
        }
        if (carry.CarriedCondition == BookCondition.Damaged)
        {
            Say("This copy is falling apart! Got a better one?", 2.5f);
            Penalise(0.15f);
            return;
        }

        var condition = carry.CarriedCondition;
        carry.Drop();
        if (reading) StartReading(book, condition);
        else Sell(book, condition);
    }

    // ---------------- Chatting with readers ----------------

    bool chatted;
    int chatBonus;

    /// <summary>A short talk with a seated reader. Pointing them to the right shelf earns a bigger tip.</summary>
    void Chat()
    {
        var ui = DialogueUI.Instance;
        if (chatted || heldBook == null) { Say(Pick("Shh... I'm at a good part!", "Mm-hm, still reading.", "Just a few more pages!"), 2f); return; }
        if (ui == null || ui.IsOpen) return;
        chatted = true;

        string me = Members.TryProfile(profileKey, out var p) ? p.name.Split(' ')[0] : "Reader";
        string shelf = ShelfInteractable.NameOf(heldBook.ShelfLocation);
        var shelves = new System.Collections.Generic.List<string> { shelf };
        foreach (var b in GameManager.Instance.Books)
        {
            string n = ShelfInteractable.NameOf(b.ShelfLocation);
            if (!shelves.Contains(n) && !string.IsNullOrEmpty(n)) shelves.Add(n);
        }
        // Right answer plus two others, shuffled.
        var options = new System.Collections.Generic.List<string> { shelf };
        shelves.RemoveAt(0);
        while (options.Count < 3 && shelves.Count > 0) { int i = Random.Range(0, shelves.Count); options.Add(shelves[i]); shelves.RemoveAt(i); }
        for (int i = options.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (options[i], options[j]) = (options[j], options[i]); }

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = me;
        string opinion = heldBook.Format == BookFormat.Book
            ? Pick($"\"{heldBook.Title}\" is wonderful. I can't put it down!", $"I love {heldBook.Author}'s writing.", $"This is the best {heldBook.Subject} book I've read in ages.")
            : Pick($"This {heldBook.FormatName.ToLower()} is fascinating.", $"So much in \"{heldBook.Title}\"!", "I could read these all afternoon.");
        d.lines.Add(new DialogueAsset.Line { id = "start", text = opinion });
        var q = new DialogueAsset.Line { id = "q", text = "When I'm done, where would I find more like this?" };
        foreach (var o in options) q.choices.Add(new DialogueAsset.Choice { text = o, next = o == shelf ? "right" : "wrong" });
        q.choices.Add(new DialogueAsset.Choice { text = "Enjoy your read!", next = "bye" });
        d.lines.Add(q);
        d.lines.Add(new DialogueAsset.Line { id = "right", text = $"The {shelf}, of course! Thank you, you know this place so well.", next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "wrong", text = "Hmm... are you sure? I don't think that's right.", next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "bye", text = "Thank you! Back to my book, then.", next = "end" });

        string outcome = null;
        System.Action<string> seen = id => { if (id == "right" || id == "wrong" || id == "bye") outcome = id; };
        ui.LineShown += seen;
        ui.Play(d, () =>
        {
            ui.LineShown -= seen;
            Destroy(d);
            if (outcome == "right") { chatBonus = Mathf.Max(3, Mathf.RoundToInt(heldBook != null ? heldBook.BaseValue * 0.1f : 3)); Members.Satisfy(Membership, 0.5f); GameAudio.Play(a => a.served, 0.6f); }
            else if (outcome == "wrong") { Members.Satisfy(Membership, -0.25f); GameAudio.Play(a => a.wrongBook, 0.6f); }
            else Members.Satisfy(Membership, 0.15f);
        });
    }

    static string Pick(params string[] lines) => lines[Random.Range(0, lines.Length)];

    // ---------------- Library card ----------------

    /// <summary>Called by the PC Members form. Returns what was wrong, or null if the card is right.</summary>
    public string ReceiveCard(string name, string dob, AccessLevel access)
    {
        if (kind != Kind.SignUp || state != State.Waiting || !Members.TryProfile(profileKey, out var p)) return "Nobody is waiting for a card.";
        string wrong = name != p.name ? "That's not my name..."
                     : dob != p.dob ? "That's not my birthday!"
                     : access != p.access ? (access == AccessLevel.Rare ? "Rare Books? I don't think I'm allowed in there!"
                                           : access == AccessLevel.Basic ? "I asked for more than a basic card." : "Hmm, I won't need the Archive.")
                     : null;
        if (wrong != null)
        {
            Say(wrong, 2.5f);
            GameAudio.Play(a => a.cardWrong);
            Penalise(0.2f);
            return wrong;
        }
        var gm = GameManager.Instance;
        var member = Members.Add(profileKey, p, access);
        int fee = 10, tip = Mathf.RoundToInt(6 * PatienceLeft);
        Members.Satisfy(member, PatienceLeft > 0.5f ? 0.5f : 0f, fee + tip);
        gm.AddCoins(fee + tip);
        gm.Today.income += fee + tip; gm.Today.tips += tip; gm.Today.served++; gm.Today.signups++;
        gm.AddReputation(1);
        if (WaitingForCard == this) WaitingForCard = null;
        GameAudio.Play(a => a.cardIssued);
        Say($"My very own card! Thank you!  +{fee + tip}", 2.5f);
        Leave();
        return null;
    }

    // ---------------- Buying ----------------

    void Sell(BookData book, BookCondition condition)
    {
        bool worn = condition == BookCondition.Worn;
        float speed = PatienceLeft;
        int price = Mathf.RoundToInt(book.BaseValue * (worn ? 0.8f : 1f));
        int tip = worn ? 0 : Mathf.RoundToInt(book.BaseValue * 0.4f * speed * request.TipBonus * Members.TipFactor(Membership));
        Members.Satisfy(Membership, speed > 0.5f ? 0.5f : 0.2f, price + tip);
        var gm = GameManager.Instance;
        gm.AddCoins(price + tip);
        gm.Today.served++; gm.Today.booksSold++; gm.Today.income += price + tip; gm.Today.tips += tip;
        gm.AddReputation(worn ? 0 : 1);
        gm.NotifyBooksChanged();
        GameAudio.Play(a => a.served);
        Say(tip > 0 ? $"Perfect, thank you!  +{price + tip}" : $"Thanks.  +{price}", 2.5f);
        Leave();
    }

    // ---------------- Reading ----------------

    void StartReading(BookData book, BookCondition condition)
    {
        heldBook = book;
        heldCondition = condition;
        bool rareRoom = BookRequest.IsRare(book);
        CounterQueue.Instance.Leave(this);
        bar.AddToClassList("hidden");
        StopImpatient(true);

        if (!SeatPoint.TryReserve(this, rareRoom, out seat, out seatIndex))
        {
            // Nowhere to sit: they hand it straight back.
            Say("Oh, every seat's taken. Maybe another day.", 2.5f);
            ReturnHeldCopy(false);
            Leave();
            return;
        }
        Say(rareRoom ? "Wonderful. I'll read it in the Rare Books room." : "Lovely, I'll find a seat.", 2.5f);
        state = State.ToSeat;
        agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
    }

    void SitDown()
    {
        state = State.Reading;
        agent.enabled = false;
        transform.SetPositionAndRotation(seat.SeatPosition(seatIndex), seat.SeatRotation);
        if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitRead); }
        readUntil = Time.time + Random.Range(readSeconds.x, readSeconds.y);
    }

    void StandUp()
    {
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        transform.position = NearestOnNavMesh(seat.ApproachPosition(seatIndex));
        if (Housekeeping.Instance != null) Housekeeping.Instance.ReaderLeft(seat.SeatPosition(seatIndex));
        bool rareRoom = seat.rareBooksOnly;
        seat.Release(this);
        seat = null;
        agent.enabled = true;
        // The Rare Books attendant checks them in on the spot.
        if (rareRoom && GameManager.Instance.Hired(GameManager.StaffRole.RareAttendant)) { maxPatience = patience = 1f; CheckIn(); return; }
        JoinQueue();
    }

    // ---------------- Returning (readers and returners) ----------------

    string CheckInLine => kind == Kind.Returner
        ? $"Returning \"{heldBook.Title}\", thanks!"
        : "All done! Here's the book back.";

    void BeginCheckIn()
    {
        state = State.CheckIn;
        GameAudio.Play(a => a.customerArrives);
        StartPatience(45f);
        Say(CheckInLine);
    }

    void CheckIn()
    {
        var gm = GameManager.Instance;
        int fee = 0, tip = 0;
        if (kind == Kind.Reader)
        {
            // Reading fee: cheap shelves pay a big share, precious books a small share of a big value.
            bool rare = BookRequest.IsRare(heldBook), archive = BookRequest.IsArchive(heldBook);
            float share = rare ? 0.1f : archive ? 0.15f : 0.4f;
            int minFee = rare ? 20 : archive ? 8 : 4;
            fee = Mathf.RoundToInt(Mathf.Max(minFee, heldBook.BaseValue * share) * gm.ReadingFeeMultiplier);
            tip = Mathf.RoundToInt(fee * 0.6f * PatienceLeft * Members.TipFactor(Membership)) + chatBonus;
            Members.Satisfy(Membership, PatienceLeft > 0.5f ? 0.5f : 0.2f, fee + tip);
            gm.Today.booksRead++;
            gm.AddReputation(1);
        }
        else
        {
            gm.Today.returns++;
            // Some returns are overdue: a small late fee.
            if (Random.value < 0.35f) fee = Random.Range(2, 6);
        }
        if (fee + tip > 0)
        {
            gm.AddCoins(fee + tip);
            gm.Today.income += fee + tip;
            gm.Today.tips += tip;
        }
        gm.Today.served++;
        ReturnHeldCopy(kind == Kind.Reader);
        GameAudio.Play(a => a.served);
        Say(fee > 0 ? $"Thank you!  +{fee + tip}" : "Thanks, see you!", 2.5f);
        Leave();
    }

    void GiveUpCheckIn()
    {
        // They leave the book on the counter anyway, but they're not happy about the wait.
        ReturnHeldCopy(false);
        Members.Satisfy(Membership, -0.5f);
        GameManager.Instance.AddReputation(-1);
        Say("I'll just leave it here then...", 2.5f);
        Leave();
    }

    /// <summary>Puts the held copy in the return bin, sometimes a little more worn.</summary>
    void ReturnHeldCopy(bool wasRead)
    {
        if (heldBook == null) return;
        var condition = wasRead ? RollWear(heldCondition) : heldCondition;
        if (ReturnBin.Instance != null) ReturnBin.Instance.Add(heldBook, condition);
        else heldBook.AddCopy(condition);
        GameManager.Instance.NotifyBooksChanged();
        heldBook = null;
    }

    static BookCondition RollWear(BookCondition c)
    {
        float r = Random.value;
        if (c == BookCondition.Good) return r < 0.08f ? BookCondition.Damaged : r < 0.25f ? BookCondition.Worn : c;
        if (c == BookCondition.Worn) return r < 0.2f ? BookCondition.Damaged : c;
        return c;
    }

    // ---------------- Reactions ----------------

    /// <summary>"Ask" with empty hands: an extra clue that stays under their request.</summary>
    void GiveClue()
    {
        var b = request.Target;
        clue ??= request.Hint switch
        {
            HintType.Author => $"I think it's a {b.Subject} book.",
            HintType.Subject => $"Something by {b.Author} would be lovely.",
            HintType.Tag => b.Format == BookFormat.Book ? $"Maybe something on {b.Subject}?" : $"Check the {ShelfInteractable.NameOf(b.ShelfLocation)}?",
            HintType.Format => $"They're on the {ShelfInteractable.NameOf(b.ShelfLocation)}, right?",
            HintType.FormatSubject => $"Something like \"{b.Title}\" would do.",
            _ => $"It's a {b.Subject} {(b.Format == BookFormat.Book ? "book" : b.FormatName.ToLower())}, by {b.Author}... I think.",
        };
        Say(RequestText);
    }

    void Penalise(float share)
    {
        patience -= maxPatience * share;
        if (anim) anim.SetActivity(CharacterAnimator.Activity.Impatient);
        Invoke(nameof(EndPenaltyAnim), 1.5f);
        if (patience <= 0f) LeaveAngry();
    }

    void EndPenaltyAnim() => StopImpatient(false);

    void StopImpatient(bool force)
    {
        if (force) impatientLoop = false;
        if (anim && !impatientLoop) anim.SetActivity(CharacterAnimator.Activity.None);
    }

    void LeaveAngry()
    {
        var gm = GameManager.Instance;
        gm.Today.angry++;
        Members.Satisfy(Membership, -1f);
        gm.AddReputation(-2);
        GameAudio.Play(a => a.angryLeave);
        Say("Hmph! Forget it.", 2.5f);
        Leave();
    }

    void Leave()
    {
        if (WaitingForCard == this) WaitingForCard = null;
        var m = Membership;
        if (m != null && state != State.Leaving) m.visits++;
        state = State.Leaving;
        StopImpatient(true);
        CounterQueue.Instance.Leave(this);
        bar.AddToClassList("hidden");
        if (!agent.enabled) agent.enabled = true;
        agent.SetDestination(CounterQueue.Instance.exit.position);
    }

    static Vector3 NearestOnNavMesh(Vector3 p) =>
        NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : p;

    /// <summary>Show a line overhead. seconds = 0 keeps it until replaced.</summary>
    void Say(string text, float seconds = 0f)
    {
        speech.text = text;
        speech.RemoveFromClassList("hidden");
        reactionUntil = seconds > 0f ? Time.time + seconds : 0f;
    }

    void BuildOverhead()
    {
        overhead = new VisualElement();
        overhead.AddToClassList("customer-tag");
        speech = new Label();
        speech.AddToClassList("customer-speech");
        speech.AddToClassList("hidden");
        bar = new VisualElement();
        bar.AddToClassList("patience-bar");
        bar.AddToClassList("hidden");
        fill = new VisualElement();
        fill.AddToClassList("patience-fill");
        bar.Add(fill);
        overhead.Add(speech);
        overhead.Add(bar);
        // Head height of the 0.75-scale characters (TopAnchor could pick the blob shadow).
        follower = WorldPrompts.Instance?.Follow(overhead, () => transform.position + Vector3.up * 1.75f);
    }
}
