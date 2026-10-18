using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

/// <summary>
/// A visiting customer. Kinds:
///  Browser  - picks a book off a shelf themselves and brings it to the counter: you just stamp it out.
///  Buyer    - asks for something; you find it (or order and reserve it if it's sold out).
///  Reader   - asks for something, reads it on a seat, then checks it back in and pays a reading fee.
///  Returner - brings back a book they borrowed.
///  SignUp   - wants a library card (filled in on the PC).
///  Pickup   - comes back for a book they reserved yesterday.
/// Talking to someone at the counter opens a conversation (hint, give a book, "it's sold out"...).
/// All timers use game time, so they stand still while a menu or chat is open.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Customer : ClickInteractable
{
    // Seeker: looks around for a book the library doesn't have, then asks you to order it.
    public enum Kind { Buyer, Reader, Returner, SignUp, Browser, Pickup, Seeker }
    enum State { Queueing, Waiting, ToSeat, Reading, CheckIn, Leaving, ToShelf, Browsing, Checkout, WaitingForBook }

    public Kind kind;
    [Tooltip("Dean / professors: read Rare Books in the Rare Books room.")]
    public bool vip;
    [Tooltip("Character prefab name: who this visitor is (see Members).")]
    public string profileKey;
    public Member Membership => Members.Find(profileKey);
    public float walkSpeed = 1.6f;
    [Tooltip("Share of patience lost for a wrong book.")]
    public float wrongBookPenalty = 0.2f;
    public Vector2 readSeconds = new Vector2(50f, 90f);
    [Tooltip("Everyone waits this much longer than the base request time: cozy, not frantic.")]
    public float patienceScale = 1.6f;
    [Tooltip("Browsers who sit and read their pick here (the rest take it home).")]
    [Range(0f, 1f)] public float readHereChance = 0.75f;

    NavMeshAgent agent;
    CharacterAnimator anim;
    State state;
    BookRequest request;
    float patience, maxPatience;
    float reactionLeft;
    string clue;
    bool impatientLoop;

    // The copy they're holding (browsing, reading or returning)
    BookData heldBook;
    BookCondition heldCondition;
    SeatPoint seat;
    int seatIndex;
    float timer; // read / browse time left
    ShelfInteractable browseShelf;
    GameManager.Reservation reservation;

    VisualElement overhead, mood, mouth;
    Label speech, nameplate;
    WorldPrompts.Follower follower;

    public BookRequest CurrentRequest => request;
    public bool IsWaiting => state == State.Waiting;
    public float PatienceLeft => maxPatience > 0f ? Mathf.Clamp01(patience / maxPatience) : 1f;
    static float Dt => GameManager.Dt;
    static GameManager GM => GameManager.Instance;

    protected override string DefaultVerb => "Talk";
    public override string Verb
    {
        get
        {
            switch (state)
            {
                case State.CheckIn: return "Check in";
                case State.Checkout: return "Stamp out";
                case State.Reading: return "Chat";
            }
            if (kind == Kind.SignUp) return introduced ? "Card on the PC" : "Talk";
            var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
            return carry != null && carry.Items.Count > 0 ? "Talk / give book" : "Talk"; // never gives away whether it's the right one
        }
    }
    public override Vector3 PromptAnchor => transform.position + Vector3.up * 1.0f;
    public override bool CanInteract => state == State.Waiting || state == State.CheckIn || state == State.Reading || state == State.Checkout || state == State.WaitingForBook;

    bool introduced;
    /// <summary>The sign-up visitor waiting at the counter who has told you their details.</summary>
    public static Customer WaitingForCard { get; private set; }
    string FirstName => Members.TryProfile(profileKey, out var p) ? p.name.Split(' ')[0] : "Visitor";
    string FullName => Members.TryProfile(profileKey, out var p) ? p.name : "Visitor";

    string SignUpText => !introduced
        ? (vip ? "Good day. I'll need a library card with Rare Books access." : "I'd like a library card, please!")
        : Members.TryProfile(profileKey, out var p) ? $"I'm {p.name}, born {p.dob}.\n<i>{p.line}</i>" : "";

    string RequestText
    {
        get
        {
            if (kind == Kind.SignUp) return SignUpText;
            if (kind == Kind.Pickup && reservation != null) return reservation.urgent
                ? $"I'm back for \"{reservation.title}\"! Did it arrive?"
                : $"Hello again! I'm here for \"{reservation.title}\".";
            if (request == null) return "";
            return clue == null ? request.Line : $"{request.Line}\n<color=#D67840>Hint: {clue}</color>";
        }
    }

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
            var lendable = GM.Books.FindAll(b => b.Format == BookFormat.Book && b.Sellable && BookRequest.Available(b));
            if (lendable.Count > 0)
            {
                heldBook = lendable[Random.Range(0, lendable.Count)];
                heldCondition = RollWear(Random.value < 0.5f ? BookCondition.Good : BookCondition.Worn);
            }
            else kind = Kind.Buyer;
        }
        if (kind == Kind.Pickup)
        {
            reservation = GM.ReservationFor(profileKey);
            if (reservation == null) kind = Kind.Buyer;
            else request = new BookRequest { Hint = HintType.Title, Target = GM.FindTitle(reservation.title), Line = "" };
            if (request != null && request.Target == null) { kind = Kind.Buyer; request = null; reservation = null; }
        }
        if (kind == Kind.Seeker && !SetUpSeek()) kind = Kind.Buyer; // nothing to look for: just ask
        if (kind == Kind.Seeker && GoSeek()) return;
        if (kind == Kind.Browser && GoBrowse()) return;
        if (vip && kind == Kind.Reader && AttendantServes()) return;
        if (kind == Kind.Browser) kind = Kind.Buyer; // nothing on the shelves: ask instead
        JoinQueue();
    }

    void OnDestroy()
    {
        if (CounterQueue.Instance != null) CounterQueue.Instance.Leave(this);
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.Unfollow(follower);
        if (seat != null) seat.Release(this);
        if (WaitingForCard == this) WaitingForCard = null;
    }

    /// <summary>Lights out (checked every frame until they've gone): readers stop and come to check their book in,
    /// browsers with a book get it stamped, everyone else heads home. Nobody is left sitting in the dark.</summary>
    void HandleClosed()
    {
        switch (state)
        {
            case State.Reading: if (timer > 0f) timer = 0f; return; // stands up and queues to check in
            case State.ToSeat:
                // Still on the way to a seat: skip the reading, just check the book in.
                if (seat != null) { seat.Release(this); seat = null; }
                if (heldBook != null) { Say("Oh, closing time! I'll just return this.", 2.5f); JoinQueue(); }
                else Leave();
                return;
            case State.WaitingForBook: GiveUpWaiting(); return;
            case State.ToShelf:
            case State.Browsing:
                Say("Oh, closing already!", 2.5f);
                Leave();
                return;
            case State.Queueing:
            case State.Waiting:
                if (heldBook != null) return; // a book to check in or stamp out: let them finish up
                Say("Oh, closing time already? Tomorrow, then.", 2.5f);
                RequestLog.Finish(this, "Sent home at closing");
                Leave();
                return;
        }
    }

    void Update()
    {
        if (GM != null && GM.CurrentPhase == GameManager.Phase.Closed) HandleClosed();
        if (agent.enabled && anim) anim.SetSpeed(Mathf.Clamp01(agent.velocity.magnitude / walkSpeed) * 0.5f);

        switch (state)
        {
            case State.Queueing: UpdateQueue(); break;
            case State.Waiting:
            case State.CheckIn:
            case State.Checkout: UpdatePatience(); break;
            case State.ToShelf:
                if (Arrived()) StartBrowsing();
                break;
            case State.Browsing:
                FaceTowards(browseShelf ? browseShelf.transform.position : transform.position + transform.forward);
                timer -= Dt;
                if (timer <= 0f) PickFromShelf();
                break;
            case State.ToSeat:
                if (Arrived()) SitDown();
                break;
            case State.Reading:
                timer -= Dt;
                // Don't get up in the middle of a chat.
                if (timer <= 0f && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen)) StandUp();
                break;
            case State.Leaving:
                if (Arrived()) Destroy(gameObject);
                break;
            case State.WaitingForBook: UpdateWaitingForBook(); break;
        }

        UpdateRequestIcon();
        if (reactionLeft > 0f)
        {
            reactionLeft -= Time.deltaTime;
            if (reactionLeft <= 0f) ShowIdleLine();
        }
    }

    /// <summary>Until you talk to them, a waiting visitor only shows a small "?" bubble.</summary>
    bool heard;
    string WaitingText => heard ? RequestText : AskMark;
    const string AskMark = "?";

    void ShowIdleLine()
    {
        speech.text = state == State.Waiting ? WaitingText
                    : state == State.CheckIn ? CheckInLine
                    : state == State.Checkout ? CheckoutLine : "";
        speech.EnableInClassList("hidden", string.IsNullOrEmpty(speech.text));
    }

    void FaceTowards(Vector3 p)
    {
        var d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
    }

    // ---------------- VIP with the Rare Books attendant ----------------

    /// <summary>With an attendant on duty, a VIP scholar skips the counter: Arthur hands them a rare book.</summary>
    bool AttendantServes()
    {
        if (!GM.RareOpen || StaffManager.Instance == null || !StaffManager.Instance.Working(GameManager.StaffRole.RareAttendant)) return false;
        var attendant = StaffManager.Instance.Worker(GameManager.StaffRole.RareAttendant);
        if (attendant == null || !attendant.CanWork) return false; // stuck on a problem, or off duty
        if (attendant.Slow && Random.value < 0.5f) return false;     // makeshift fix: too slow to catch everyone
        var rare = GM.Books.FindAll(b => BookRequest.IsRare(b) && BookRequest.InStock(b));
        if (rare.Count == 0) return false;
        var book = rare[Random.Range(0, rare.Count)];
        var cond = book.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
        if (!SeatPoint.TryReserve(this, true, out seat, out seatIndex)) return false;
        if (!book.TakeCopy(cond)) { seat.Release(this); seat = null; return false; }
        heldBook = book;
        heldCondition = cond;
        GM.NotifyBooksChanged();
        Say("Good day! The attendant has my book ready.", 3f);
        attendant.servedToday++;
        state = State.ToSeat;
        agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
        return true;
    }

    // ---------------- Browsing (self-service) ----------------

    bool GoBrowse()
    {
        var shelves = new List<ShelfInteractable>();
        foreach (var s in FindObjectsByType<ShelfInteractable>())
        {
            if (RoomGate.IsLockedAt(s.transform.position)) continue;
            foreach (var b in s.Books) if (b.Sellable && BookRequest.InStock(b)) { shelves.Add(s); break; }
        }
        if (shelves.Count == 0) return false;
        browseShelf = shelves[Random.Range(0, shelves.Count)];
        state = State.ToShelf;
        agent.SetDestination(NearestOnNavMesh(browseShelf.transform.position, 2.5f));
        return true;
    }

    void StartBrowsing()
    {
        state = State.Browsing;
        timer = Random.Range(4f, 7f);
        if (anim) anim.PlayReachShelf();
    }

    void PickFromShelf()
    {
        if (kind == Kind.Seeker) { NotHere(); return; }
        var pool = new List<BookData>();
        if (browseShelf != null) foreach (var b in browseShelf.Books) if (b.Sellable && BookRequest.InStock(b)) pool.Add(b);
        if (pool.Count == 0)
        {
            // Someone took the last one: try another shelf, or give up kindly.
            if (GoBrowse()) return;
            Say("Nothing for me today. Bye!", 2.5f);
            Leave();
            return;
        }
        var book = pool[Random.Range(0, pool.Count)];
        var cond = book.GoodCopies > 0 ? BookCondition.Good : BookCondition.Worn;
        if (!book.TakeCopy(cond)) { timer = 1f; return; }
        heldBook = book;
        heldCondition = cond;
        GM.NotifyBooksChanged();
        if (anim) anim.PlayReachShelf();

        // Most browsers sit down and read it here, then check it in at the counter (reading fee).
        // The rest take it home: they bring it to the counter to be stamped out.
        if (Random.value < readHereChance && SeatPoint.TryReserve(this, false, out seat, out seatIndex))
        {
            kind = Kind.Reader;
            Say(Pick("Ooh, I'll read this one here.", "Perfect, time for a sit down.", "This looks lovely. I'll find a seat."), 2.5f);
            state = State.ToSeat;
            agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
            return;
        }
        Say(Pick("Ooh, this one!", "Found it!", "This looks lovely."), 2f);
        JoinQueue();
    }

    string CheckoutLine => heldBook == null ? "" : $"I found \"{heldBook.Title}\" myself! Can you check it out?";

    void StampOut(GameObject player)
    {
        var playerAnim = player.GetComponent<CharacterAnimator>();
        if (playerAnim) playerAnim.PlayStamp();
        bool worn = heldCondition == BookCondition.Worn;
        int price = Mathf.RoundToInt(heldBook.BaseValue * (worn ? 0.8f : 1f));
        int tip = Mathf.RoundToInt(heldBook.BaseValue * 0.15f * PatienceLeft * Members.TipFactor(Membership));
        Members.Satisfy(Membership, PatienceLeft > 0.5f ? 0.4f : 0.15f, price + tip);
        GM.AddCoins(price + tip);
        GM.Today.served++; GM.Today.booksSold++; GM.Today.income += price + tip; GM.Today.tips += tip;
        GM.AddReputation(1);
        heldBook = null; // it leaves with them
        GameAudio.Play(a => a.served);
        Say(tip > 0 ? $"Thank you!  +{price + tip}" : $"Thanks!  +{price}", 2.5f);
        Leave();
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
        GameAudio.Play(a => a.customerArrives);

        if (kind == Kind.Browser && heldBook != null)
        {
            state = State.Checkout;
            StartPatience(60f);
            Say(CheckoutLine);
            return;
        }
        if (heldBook != null) { BeginCheckIn(); return; }

        if (kind == Kind.SignUp)
        {
            StartPatience(70f);
            state = State.Waiting;
            Say(WaitingText);
            return;
        }
        if (kind != Kind.Pickup && kind != Kind.Seeker)
            request = BookRequest.Create(GM.Books, kind == Kind.Reader, vip);
        if (request == null) { Say("Oh... nothing I want today."); Leave(); return; }
        StartPatience(kind == Kind.Pickup ? 70f : request.Patience);
        state = State.Waiting;
        if (GM.FreeFirstHint && kind != Kind.Pickup) clue = HintText(); // Cozy: the hint comes for free
        Say(WaitingText);
    }

    void StartPatience(float seconds)
    {
        maxPatience = patience = seconds * patienceScale * GM.PatienceMultiplier * Members.PatienceFactor(Membership);
        impatientLoop = false;
        mood.RemoveFromClassList("hidden");
        SetMood(1f);
    }

    void UpdatePatience()
    {
        transform.rotation = Quaternion.RotateTowards(transform.rotation, CounterQueue.Instance.FacingDesk, 360f * Time.deltaTime);
        patience -= Dt;
        float t = PatienceLeft;
        SetMood(t);

        bool low = t < 0.25f;
        if (low != impatientLoop && anim)
        {
            impatientLoop = low;
            if (low) GameAudio.Play(a => a.impatient, 0.4f);
            anim.SetActivity(low ? CharacterAnimator.Activity.Impatient : CharacterAnimator.Activity.None);
        }
        if (patience > 0f) return;
        if (GM.NoStormingOut) { patience = 0f; return; } // Cozy: they wait, just not happily
        if (state == State.CheckIn) GiveUpCheckIn();
        else if (state == State.Checkout) GiveUpCheckout();
        else LeaveAngry();
    }

    void SetMood(float t)
    {
        mood.EnableInClassList("happy", t >= 0.55f);
        mood.EnableInClassList("ok", t < 0.55f && t >= 0.25f);
        mood.EnableInClassList("sad", t < 0.25f);
    }

    bool Arrived() => agent.enabled && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;

    /// <summary>Index in the bag of a copy that answers this request (best condition, not damaged), or -1.</summary>
    int MatchIndex(PlayerCarry carry)
    {
        if (state != State.Waiting || request == null || carry == null) return -1;
        bool reading = kind == Kind.Reader;
        return carry.Find((b, c) => c != BookCondition.Damaged && request.Matches(b) && (reading || b.Sellable || kind == Kind.Pickup));
    }

    public override void Interact(GameObject player)
    {
        switch (state)
        {
            case State.CheckIn: CheckIn(); return;
            case State.Checkout: StampOut(player); return;
            case State.Reading: Chat(); return;
            case State.WaitingForBook: Say(WaitingForBookLine, 3f); return;
            case State.Waiting: break;
            default: return;
        }
        if (kind == Kind.SignUp)
        {
            introduced = true;
            RequestLog.Begin(this, RequestLog.Kind.Card, FullName, SignUpText); // what they tell you: name, birthday, wishes
            heard = true;
            WaitingForCard = this;
            Say(RequestText);
            return;
        }

        // Always talk it through, so the player can't tell a right book from a wrong one by the click.
        Converse(player.GetComponent<PlayerCarry>());
    }

    void Give(PlayerCarry carry, int index)
    {
        LogRequest(); // handed over before you even talked: still goes in the log
        var (book, condition) = carry.TakeAt(index);
        if (kind == Kind.Reader) StartReading(book, condition);
        else Sell(book, condition);
    }

    // ---------------- Conversation at the counter ----------------

    void Converse(PlayerCarry carry)
    {
        var ui = DialogueUI.Instance;
        if (ui == null || ui.IsOpen) return;
        heard = true;
        LogRequest();

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = vip ? $"{FullName} (VIP)" : FirstName;
        var start = new DialogueAsset.Line { id = "start", text = RequestText };
        d.lines.Add(start);

        // Books in your hands you could offer.
        if (carry != null)
            for (int i = carry.Count - 1; i >= 0; i--) // every book you carry
                start.choices.Add(new DialogueAsset.Choice { text = $"Here: \"{carry.Items[i].book.Title}\"", next = "give" + i });

        bool pickup = kind == Kind.Pickup;
        bool soldOut = !pickup && request.OutOfStock(GM.Books);
        bool seeker = kind == Kind.Seeker;
        if (!pickup && !seeker && clue == null && !GM.NoHints) start.choices.Add(new DialogueAsset.Choice { text = "Could you give me a hint? (smaller tip)", next = "hint" });
        if (pickup)
            start.choices.Add(new DialogueAsset.Choice { text = "Sorry, it isn't here yet.", next = "notyet" });
        else
        {
            // Not in the library (sold out, or a title we don't have): ask when they need it.
            if (kind == Kind.Buyer || seeker)
                start.choices.Add(new DialogueAsset.Choice { text = seeker ? "When do you need it?" : "It's sold out. When do you need it?", next = soldOut ? "need" : "reserve" });
            start.choices.Add(new DialogueAsset.Choice { text = "Sorry, we can't get that.", next = "nohave" });
        }
        start.choices.Add(new DialogueAsset.Choice { text = seeker ? "Let me check." : "Let me find it.", next = "end" });

        string hintText = HintText();
        d.lines.Add(new DialogueAsset.Line { id = "hint", text = $"<color=#D67840>{hintText}</color>", next = "end" });
        // Some need it today (express) and will wait here; others are happy to come back tomorrow.
        wantsToday = soldOut && Random.value < (seeker ? 0.5f : 0.4f) && GM.Hour + 2.5f < GM.closingHour;
        var need = new DialogueAsset.Line { id = "need",
            text = wantsToday ? $"As soon as possible! I can wait here in the library. Could it be here by <color=#D67840>{GameManager.FormatHour(GM.Hour + 2.5f)}</color>?"
                              : "No rush. I'll come back tomorrow for it." };
        need.choices.Add(new DialogueAsset.Choice { text = wantsToday ? "I'll have it here for you today." : "I'll order it for you.", next = "reserve" });
        need.choices.Add(new DialogueAsset.Choice { text = "Sorry, I can't get that one.", next = "nohave" });
        d.lines.Add(need);
        d.lines.Add(new DialogueAsset.Line { id = "reserve", next = "end",
            text = !soldOut ? "Sold out? I'm sure I saw it on the shelf..."
                 : wantsToday ? "Thank you! I'll find a seat and wait."
                 : "Wonderful! I'll pick it up tomorrow. Don't forget to order it!" });
        d.lines.Add(new DialogueAsset.Line { id = "nohave", next = "end",
            text = soldOut ? "That's a shame. Thanks for checking!" : "Really? I'm sure you do... never mind." });
        bool ordered = pickup && reservation != null && GM.OnOrder(request.Target) > 0;
        d.lines.Add(new DialogueAsset.Line { id = "notyet", next = "end",
            text = ordered ? "Still on its way? I'll pop back tomorrow, then." : "You never ordered it, did you? How disappointing." });
        for (int i = 0; i < (carry != null ? carry.Count : 0); i++) d.lines.Add(new DialogueAsset.Line { id = "give" + i, text = "Let me see...", next = "end" });

        string outcome = null;
        System.Action<string> seen = id => { if (id != "start") outcome = id; };
        ui.LineShown += seen;
        ui.Play(d, () =>
        {
            ui.LineShown -= seen;
            Destroy(d);
            if (state != State.Waiting) return;
            Say(RequestText); // pinned overhead now that you've heard it
            switch (outcome)
            {
                case "hint": clue = hintText; usedHint = true; RequestLog.SetHint(this, clue, true); Say(RequestText); break;
                case "reserve":
                    if (soldOut && wantsToday) ReserveAndWait();
                    else if (soldOut) ReserveAndLeave();
                    else { RequestLog.Note(this, "Said it was sold out (it wasn't)"); GameAudio.Play(a => a.wrongBook); Penalise(0.15f); }
                    break;
                case "nohave":
                    GM.reservations.RemoveAll(r => r.key == profileKey); // nothing left waiting for them
                    RequestLog.Finish(this, soldOut ? "Sold out, sent away" : "Told we didn't have it (we did)");
                    if (soldOut) { Say("Maybe next time!", 2.5f); Leave(); }
                    else { GM.AddReputation(-1); Members.Satisfy(Membership, -0.5f); Say("Hmph. I'll try elsewhere.", 2.5f); Leave(); }
                    break;
                case "notyet":
                    RequestLog.Finish(this, ordered ? "Not here yet, back tomorrow" : "Never ordered");
                    if (ordered) { Say(reservation.urgent ? "Not yet? Fine, tomorrow then." : "See you tomorrow!", 2.5f); reservation.day = GM.day + 1; reservation.urgent = false; reservation.queued = false; Leave(); }
                    else { GM.reservations.Remove(reservation); GM.Today.angry++; GM.AddReputation(-2); Members.Satisfy(Membership, -1f); GameAudio.Play(a => a.angryLeave); Say("I won't reserve here again.", 2.5f); Leave(); }
                    break;
                default:
                    if (outcome != null && outcome.StartsWith("give") && int.TryParse(outcome.Substring(4), out int idx)) TryGive(carry, idx);
                    break;
            }
        });
    }

    void TryGive(PlayerCarry carry, int index)
    {
        if (carry == null || index >= carry.Count) return;
        var (book, condition) = carry.Items[index];
        if (kind != Kind.Reader && kind != Kind.Pickup && !book.Sellable && request.Matches(book))
        {
            RequestLog.Note(this, $"Handed \"{book.Title}\": not for sale");
            Say("Oh, that one's not for sale? Never mind then.", 2.5f);
            return;
        }
        if (!request.Matches(book))
        {
            RequestLog.Rejected(this, book);
            Say("Hmm, that's not what I asked for.", 2.5f);
            GameAudio.Play(a => a.wrongBook);
            Penalise(wrongBookPenalty);
            return;
        }
        if (condition == BookCondition.Damaged)
        {
            RequestLog.Note(this, $"Handed a damaged copy of \"{book.Title}\"");
            Say("This copy is falling apart! Got a better one?", 2.5f);
            Penalise(0.1f);
            return;
        }
        Give(carry, index);
    }

    bool wantsToday;

    // ---------------- Looking for a book we don't have ----------------

    int looksLeft;

    /// <summary>Picks a book the library doesn't have yet (or a sold-out one) to look for.</summary>
    bool SetUpSeek()
    {
        var hunted = new HashSet<BookData>();
        foreach (var c in FindObjectsByType<Customer>()) if (c != this && c.request != null) hunted.Add(c.request.Target);
        bool Free(BookData b) => !hunted.Contains(b) && !GM.reservations.Exists(r => r.title == b.Title) && BookRequest.Available(b) && !BookRequest.IsRare(b);
        var pool = GM.NewTitles.FindAll(b => Free(b));
        if (pool.Count == 0) pool = GM.Books.FindAll(b => b.Sellable && Free(b) && !BookRequest.InStock(b) && GM.OnOrder(b) == 0);
        if (pool.Count == 0) return false;
        var t = pool[Random.Range(0, pool.Count)];
        request = new BookRequest { Hint = HintType.Title, Target = t,
            Line = $"I looked all over the library for \"{t.Title}\" by {t.Author}, but couldn't find it. Could you order it for me?" };
        looksLeft = Random.Range(1, 3);
        return true;
    }

    /// <summary>Wander to a shelf (the right one first) and look for it.</summary>
    bool GoSeek()
    {
        var shelves = new List<ShelfInteractable>();
        ShelfInteractable home = null;
        foreach (var s in FindObjectsByType<ShelfInteractable>())
        {
            if (RoomGate.IsLockedAt(s.transform.position)) continue;
            shelves.Add(s);
            if (s.Belongs(request.Target)) home = s;
        }
        if (shelves.Count == 0) return false;
        browseShelf = home != null && browseShelf != home ? home : shelves[Random.Range(0, shelves.Count)];
        state = State.ToShelf;
        agent.SetDestination(NearestOnNavMesh(browseShelf.transform.position, 2.5f));
        return true;
    }

    void NotHere()
    {
        looksLeft--;
        Say(Pick("Hmm, not here...", "Where is it?", "It should be around here somewhere..."), 2f);
        if (looksLeft > 0 && GoSeek()) return;
        JoinQueue(); // time to ask at the desk
    }

    string WaitingForBookLine => reservation != null && GM.OnOrder(request.Target) > 0
        ? $"Waiting for \"{request.Target.Title}\". Is it on its way?"
        : $"Have you ordered \"{request.Target.Title}\" yet? I'm waiting here!";

    /// <summary>"As soon as possible": they stay, sitting or browsing, until the express order arrives.</summary>
    void ReserveAndWait()
    {
        GM.Reserve(profileKey, FirstName, request.Target, true);
        reservation = GM.ReservationFor(profileKey);
        if (reservation != null) reservation.queued = true; // they're already here: no second visit today
        Members.Satisfy(Membership, 0.2f);
        GameAudio.Play(a => a.cardIssued, 0.7f);
        RequestLog.Note(this, $"Waiting in the library for \"{request.Target.Title}\" (express)");
        StopImpatient(true);
        CounterQueue.Instance.Leave(this);
        state = State.WaitingForBook;
        if (SeatPoint.TryReserve(this, false, out seat, out seatIndex))
            agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
        else agent.SetDestination(NearestOnNavMesh(transform.position + transform.right * 2f, 2.5f));
        Say("I'll wait over here!", 2.5f);
    }

    void UpdateWaitingForBook()
    {
        // Walk to the seat, then sit and wait.
        if (seat != null && agent.enabled && Arrived())
        {
            agent.enabled = false;
            transform.SetPositionAndRotation(seat.SitPosition(seatIndex), seat.SeatRotation);
            if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitIdle); }
        }
        if (reservation == null || !GM.reservations.Contains(reservation)) { GetUp(); Leave(); return; }
        var cart = DeliveryCart.Instance;
        bool arrived = reservation.ordered && GM.OnOrder(request.Target) == 0
                    && (BookRequest.InStock(request.Target) || (cart != null && cart.Has(request.Target)));
        if (arrived)
        {
            // It's here: off to the desk to collect it.
            GetUp();
            kind = Kind.Pickup;
            heard = false;
            Say("It's here? Wonderful!", 2.5f);
            JoinQueue();
            return;
        }
        if (GM.Hour >= reservation.dueHour + 0.5f) GiveUpWaiting();
    }

    void GetUp()
    {
        if (seat == null) return;
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        transform.position = NearestOnNavMesh(seat.ApproachPosition(seatIndex));
        seat.Release(this);
        seat = null;
        agent.enabled = true;
    }

    /// <summary>Too late (or closing): back tomorrow if it's on order, otherwise off, unhappy.</summary>
    void GiveUpWaiting()
    {
        GetUp();
        if (reservation != null && GM.OnOrder(request.Target) > 0)
        {
            reservation.day = GM.day + 1; reservation.urgent = false; reservation.queued = false;
            RequestLog.Finish(this, "Waited, express didn't come: back tomorrow");
            Say("It won't make it today? I'll come back tomorrow, then.", 3f);
        }
        else
        {
            if (reservation != null) GM.reservations.Remove(reservation);
            GM.Today.angry++; GM.AddReputation(-1); Members.Satisfy(Membership, -0.75f);
            GameAudio.Play(a => a.angryLeave);
            RequestLog.Finish(this, "Waited all that time: never ordered");
            Say("I waited all that time for nothing...", 3f);
        }
        Leave();
    }

    void ReserveAndLeave()
    {
        GM.Reserve(profileKey, FirstName, request.Target, wantsToday);
        GM.Today.served++;
        Members.Satisfy(Membership, 0.3f);
        GameAudio.Play(a => a.cardIssued, 0.7f);
        RequestLog.Finish(this, wantsToday ? $"Reserved, express by {GameManager.FormatHour(GM.Hour + 2.5f)}" : "Reserved for tomorrow");
        Say(wantsToday ? $"Lovely! I'll be back at {GameManager.FormatHour(GM.Hour + 2.5f)}."
                       : $"Reserved \"{request.Target.Title}\". See you tomorrow!", 3f);
        Leave();
    }

    /// <summary>Into the notebook the first time you hear what they want.</summary>
    void LogRequest()
    {
        if (request == null || RequestLog.Has(this)) return;
        bool pickup = kind == Kind.Pickup;
        RequestLog.Begin(this, pickup ? RequestLog.Kind.Pickup : RequestLog.Kind.Book, FirstName, pickup ? RequestText : request.Line, clue, usedHint);
    }

    /// <summary>Asking for a hint costs a third of the tip (Cozy's free hint doesn't count).</summary>
    bool usedHint;
    float HintTipFactor => usedHint ? 0.67f : 1f;

    /// <summary>A nudge in the right direction, never the answer.</summary>
    string HintText()
    {
        var b = request.Target;
        string noun = b.Format == BookFormat.Book ? "book" : b.FormatName.ToLower();
        string who = b.Author ?? "";
        return request.Hint switch
        {
            HintType.Author => $"I think it's a {b.Subject} {noun}.",
            HintType.Subject => who.Length > 0 ? $"Maybe something by an author whose name starts with \"{who[0]}\"?" : $"It's a {noun}.",
            HintType.Tag => $"I'm fairly sure it's a {b.Subject} {noun}.",
            HintType.Format or HintType.FormatSubject => $"It's one of the {b.FormatName.ToLower()}s. Maybe the catalogue knows where.",
            HintType.Title => $"It's by {who}. Is it in the catalogue?",
            _ => $"It's a {b.Subject} {noun}, if that helps.",
        };
    }

    // ---------------- Chatting with readers ----------------

    bool chatted;
    int chatBonus;

    /// <summary>A short talk with a seated reader. Pointing them to the right shelf earns a bigger tip.</summary>
    void Chat()
    {
        var ui = DialogueUI.Instance;
        if (heldBook == null) return;
        if (chatted && smallTalked)
        {
            // They've said their piece: back to the book.
            Say(Pick("Shh... I'm at a good part!", "Mm-hm, still reading.", "Just a few more pages!", "Can't talk, plot twist!", "Five more minutes..."), 2f);
            return;
        }
        if (chatted) { SmallTalk(); return; }
        if (ui == null || ui.IsOpen) return;
        chatted = true;

        string shelf = ShelfInteractable.NameOf(heldBook.ShelfLocation);
        var shelves = new List<string>();
        foreach (var b in GM.Books)
        {
            string n = ShelfInteractable.NameOf(b.ShelfLocation);
            if (n != shelf && !shelves.Contains(n) && !string.IsNullOrEmpty(n)) shelves.Add(n);
        }
        // Right answer plus two others, shuffled.
        var options = new List<string> { shelf };
        while (options.Count < 3 && shelves.Count > 0) { int i = Random.Range(0, shelves.Count); options.Add(shelves[i]); shelves.RemoveAt(i); }
        for (int i = options.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (options[i], options[j]) = (options[j], options[i]); }

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = vip ? $"{FullName} (VIP)" : FirstName;
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

    // ---------------- Small talk (every chat after the first is different) ----------------

    // {0} = book title, {1} = author, {2} = subject. Each topic: their line, two replies, their answers.
    static readonly string[][] Topics =
    {
        new[] { "Did you know \"{0}\" was nearly never published?", "Really? Tell me more!", "Lucky for us it was.", "So the story goes! I love a book with a past.", "Lucky indeed. I'd be lost without it." },
        new[] { "This chair is so comfy I might never leave.", "Stay as long as you like.", "We do close at six...", "You're too kind. Just one more chapter, then.", "Ha! I'll be out before the bell, promise." },
        new[] { "I always read {1} with a cup of tea.", "The tea's on the house today.", "Careful with the pages!", "Oh, you spoil me!", "Don't worry, I'm a very tidy reader." },
        new[] { "It's so quiet in here. I can finally think.", "That's what libraries are for.", "Enjoy it before the rush!", "Exactly. No phones, no noise. Bliss.", "Ha, I'll savour every minute." },
        new[] { "Is it true the library has a ghost?", "He's our Senior Librarian now.", "Only on Tuesdays.", "Ha! Then I'd better behave.", "Good thing it's not Tuesday... is it?" },
        new[] { "I'm learning so much about {2} today.", "Ask me if you need more.", "Knowledge is the best souvenir.", "I might! You know this place so well.", "I like that. I'll take some home." },
        new[] { "My exams are next week. Wish me luck!", "You'll do great!", "Did you study yesterday too?", "Thank you, that really helps.", "...Let's not talk about yesterday." },
        new[] { "I found a pressed flower in here. Someone left it years ago.", "Keep it safe inside the book.", "A little gift from the past.", "I will. It feels like it belongs here.", "Isn't it lovely? Books remember people." },
        new[] { "Do you ever get time to read yourself?", "Every quiet moment!", "Not today, it's busy!", "Lucky you. You must have read them all.", "Then I'll be quick and free up a seat!" },
        new[] { "The light from the windows is perfect for reading.", "Best seat in the house.", "Wait until the evening lamps.", "I'll come back to this exact spot.", "Ooh, I'll stay to see that." },
        new[] { "I read this once as a child. It's even better now.", "Some books grow up with you.", "What changed?", "They really do, don't they?", "Me, I suppose. The book stayed the same." },
        new[] { "Just checking: you won't lock me in at closing time?", "We'll ring the bell first.", "Only if you're very quiet.", "Phew! Good to know.", "Ha! I'll keep one eye on the clock." },

        // From the notice board: library notices
        new[] { "The notice says the guy wearing a hat was near the restricted section again. Who is he?", "Nobody knows. Nobody asks.", "Probably just a very keen reader.", "...I'm going to keep my eyes on my book.", "A keen reader in a hat. Sure. Sure." },
        new[] { "I swear this book just stared back at me.", "No it didn't.", "Turn the page slowly...", "Right. It didn't. Okay. Moving on.", "Don't say that! Now I'm scared to." },
        new[] { "Is it true the library ghost got promoted to Senior Librarian?", "Well deserved. Never misses a shift.", "Shh, they might hear you.", "Ha! More reliable than most staff, I bet.", "...Sorry, Senior Librarian." },
        new[] { "Someone keeps moving the chairs. Was it you?", "We know what you did.", "I plead the fifth.", "W-what? I only moved mine a little!", "Very suspicious. Very suspicious indeed." },
        new[] { "Does sleeping in the library count as studying?", "Probably not.", "Only if the book is on your face.", "Hmm. I'll call it 'resting my eyes', then.", "Perfect, that's my whole plan." },
        new[] { "Someone left 17 books on one table yesterday!", "We're getting them help.", "Was it you?", "Seventeen! That's a whole personality.", "Only twelve! The rest weren't mine." },
        new[] { "The group study table next door is SO loud.", "It's not a shouting contest.", "Want me to have a word?", "Thank you! Somebody had to say it.", "Yes please. Gently. They look scary." },
        new[] { "The notice board says my assignment is due tomorrow. How did it know?", "That's your sign.", "Libraries know everything.", "Ugh. Fine. Back to work.", "That's honestly terrifying." },
        new[] { "Someone keeps making eye contact with me across the shelves...", "The library is not a dating app.", "Maybe say hello?", "Right. Eyes on the book. Eyes on the book.", "In a library? I'd die of embarrassment!" },
        new[] { "Did you hear whispering from the basement just now?", "Please ignore it.", "That's just the old pipes.", "Ignoring it. Very loudly ignoring it.", "...Pipes don't whisper my name." },
        new[] { "The forecast says a 90% chance of academic suffering today.", "Bring an umbrella.", "The other 10% is tea.", "Ha! Too late, I'm already soaked.", "I'll take that 10%, thank you." },

        // From the notice board: BRAC University notices
        new[] { "Advising season has begun. I'm nervous.", "May the odds be ever in your favour.", "You'll find a good slot!", "Thanks... I'll need all the luck.", "From your mouth to the portal's ears." },
        new[] { "My friend said this trimester will be easy. Haven't seen them since.", "They've been reported missing.", "Check the Archive.", "Rest in peace, brave soul.", "Ha! They're probably still in there." },
        new[] { "I just told my advisor 'just one more course'.", "You're wanted dead or alive now.", "Bold move.", "Oh no. What have I done?", "Bold, or foolish. We'll find out." },
        new[] { "Is it okay to calculate my CGPA in here?", "Please don't, the calculators are scared.", "Only if you whisper the numbers.", "Fine, I'll spare the poor calculators.", "Three point... no. I can't say it." },
        new[] { "My group project has one active member. Me.", "Investigations are ongoing.", "You deserve a medal.", "Glad someone's on the case.", "I'll accept it as extra credit." },
        new[] { "Someone reserved this study space and vanished for three hours.", "The seat is yours now.", "Maybe they're in the Archive.", "Finders keepers!", "Lost among the old newspapers, probably." },
        new[] { "I'm writing my essay and... Turnitin has entered the chat.", "Cite your sources!", "Write it in your own words.", "Already did! Mostly. Probably.", "Easier said than done, but okay!" },
        new[] { "All the good course sections are full again.", "Have a wonderful day!", "There's always next trimester.", "Very funny. Very, very funny.", "That's what I said last trimester..." },
        new[] { "I'll start my studying tonight. For real this time.", "The most popular academic fiction.", "I believe in you.", "Okay, okay, I'll start now.", "Thank you. You're the only one." },
        new[] { "The exam was fair, they said.", "Your preparation was a creative choice.", "Fair is a strong word.", "...I prepared creatively, yes.", "Thank you! Finally, someone gets it." },
        new[] { "Did you hear about the toilet incident?", "Problem-solving skills, please.", "Let's not talk about it.", "A true engineering failure.", "Agreed. Back to my book." },
        new[] { "Is that Bagha the cat sneaking around again?", "Bagha has been banned. Again.", "Bagha is welcome here.", "Ha! Banned and still here. Legend.", "Good. Every library needs a cat." },
    };

    // Shared by every visitor: each reader brings up one topic, and topics don't repeat until all are used.
    static readonly List<int> topicsLeft = new List<int>();
    // Exams, assignments, advising, CGPA...: only students bring these up (not professors, VIPs or the Dean).
    static readonly HashSet<int> StudentTopics = new HashSet<int> { 6, 16, 18, 19, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
    bool smallTalked;

    void SmallTalk()
    {
        var ui = DialogueUI.Instance;
        if (ui == null || ui.IsOpen) return;
        smallTalked = true;
        bool student = profileKey != null && profileKey.Contains("Student");
        var options = topicsLeft.FindAll(i => student || !StudentTopics.Contains(i));
        if (options.Count == 0)
        {
            for (int i = 0; i < Topics.Length; i++) if (!topicsLeft.Contains(i)) topicsLeft.Add(i);
            options = topicsLeft.FindAll(i => student || !StudentTopics.Contains(i));
        }
        int pick = options[Random.Range(0, options.Count)];
        topicsLeft.Remove(pick);
        var t = Topics[pick];
        string F(string s) => string.Format(s, heldBook.Title, heldBook.Author, heldBook.Subject);

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = vip ? $"{FullName} (VIP)" : FirstName;
        var start = new DialogueAsset.Line { id = "start", text = F(t[0]) };
        start.choices.Add(new DialogueAsset.Choice { text = F(t[1]), next = "a" });
        start.choices.Add(new DialogueAsset.Choice { text = F(t[2]), next = "b" });
        d.lines.Add(start);
        d.lines.Add(new DialogueAsset.Line { id = "a", text = F(t[3]), next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "b", text = F(t[4]), next = "end" });
        ui.Play(d, () => { Destroy(d); Members.Satisfy(Membership, 0.05f); });
    }

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
            RequestLog.CardAttempt(this, $"Card: {name}, born {dob}, {access} access", wrong);
            Say(wrong, 2.5f);
            GameAudio.Play(a => a.cardWrong);
            Penalise(0.2f);
            return wrong;
        }
        RequestLog.CardAttempt(this, $"Card: {name}, born {dob}, {access} access", null);
        var member = Members.Add(profileKey, p, access);
        int fee = vip ? 80 : 10, tip = Mathf.RoundToInt((vip ? 20 : 6) * PatienceLeft); // VIP cards: their own room, attendant and all
        Members.Satisfy(member, PatienceLeft > 0.5f ? 0.5f : 0f, fee + tip);
        GM.AddCoins(fee + tip);
        GM.Today.income += fee + tip; GM.Today.tips += tip; GM.Today.served++; GM.Today.signups++;
        GM.AddReputation(1);
        if (WaitingForCard == this) WaitingForCard = null;
        GameAudio.Play(a => a.cardIssued);
        RefreshNameplate();
        // Most new members have a book in mind straight away.
        var wish = !vip && Random.value < 0.6f ? PickWish() : null;
        RequestLog.Finish(this, $"Card issued ({access} access)  +{fee + tip}");
        if (vip)
        {
            // A scholar with a fresh Rare card goes straight to reading.
            Say($"Splendid. Now, to the Rare Books room!  +{fee + tip}", 3f);
            CounterQueue.Instance.Leave(this);
            request = null; clue = null; heard = false; usedHint = false; introduced = false;
            kind = Kind.Reader;
            if (!AttendantServes()) JoinQueue();
            return null;
        }
        if (wish != null)
        {
            // They ask you at the desk (same as anyone looking for a book): you decide whether to order it.
            kind = Kind.Seeker;
            request = new BookRequest { Hint = HintType.Title, Target = wish,
                Line = $"Now that I have a card... the library doesn't have \"{wish.Title}\" by {wish.Author}. Could you order it for me?" };
            heard = true;
            StartPatience(60f);
            Say($"My very own card! Thank you!  +{fee + tip}  One more thing...", 3f);
            return null;
        }
        Say($"My very own card! Thank you!  +{fee + tip}", 2.5f);
        if (Random.value < 0.6f) StayAsMember();
        else Leave();
        return null;
    }

    /// <summary>A fresh member tries out their card: browse the shelves, or ask for something to read.</summary>
    void StayAsMember()
    {
        CounterQueue.Instance.Leave(this);
        request = null; clue = null; heard = false; usedHint = false; introduced = false;
        kind = Random.value < 0.5f ? Kind.Browser : Kind.Reader;
        if (kind == Kind.Browser && GoBrowse()) return;
        if (kind == Kind.Browser) kind = Kind.Buyer;
        JoinQueue();
    }

    /// <summary>A book a new member wants the library to get in: preferably one it hasn't got.</summary>
    BookData PickWish()
    {
        bool Free(BookData b) => !GM.reservations.Exists(r => r.title == b.Title) && !BookRequest.IsRare(b) && BookRequest.Available(b);
        var pool = GM.NewTitles.FindAll(b => Free(b));
        if (pool.Count == 0) pool = GM.Books.FindAll(b => b.Sellable && Free(b) && !BookRequest.InStock(b));
        if (pool.Count == 0) pool = GM.Books.FindAll(b => b.Sellable && Free(b));
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    // ---------------- Buying ----------------

    void Sell(BookData book, BookCondition condition)
    {
        bool worn = condition == BookCondition.Worn;
        float speed = PatienceLeft;
        int price = Mathf.RoundToInt(book.BaseValue * (worn ? 0.8f : 1f));
        // Reserved books: they were happy to wait, and grateful you remembered.
        float tipShare = kind == Kind.Pickup ? 0.5f : 0.4f * request.TipBonus;
        int tip = worn ? 0 : Mathf.RoundToInt(book.BaseValue * tipShare * Mathf.Max(speed, kind == Kind.Pickup ? 0.8f : 0f) * Members.TipFactor(Membership) * HintTipFactor);
        Members.Satisfy(Membership, speed > 0.5f || kind == Kind.Pickup ? 0.5f : 0.2f, price + tip);
        GM.AddCoins(price + tip);
        GM.Today.served++; GM.Today.booksSold++; GM.Today.income += price + tip; GM.Today.tips += tip;
        GM.AddReputation(worn ? 0 : 1);
        if (reservation != null) GM.reservations.Remove(reservation);
        GM.NotifyBooksChanged();
        var playerAnim = GameObject.FindWithTag("Player")?.GetComponent<CharacterAnimator>();
        if (playerAnim) playerAnim.PlayStamp();
        GameAudio.Play(a => a.served);
        RequestLog.Finish(this, (kind == Kind.Pickup ? "Collected" : $"Sold \"{book.Title}\"") + $"  +{price + tip}");
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
        mood.AddToClassList("hidden");
        StopImpatient(true);

        if (!SeatPoint.TryReserve(this, rareRoom, out seat, out seatIndex))
        {
            // Nowhere to sit: they hand it straight back.
            RequestLog.Finish(this, "No free seat");
            Say("Oh, every seat's taken. Maybe another day.", 2.5f);
            ReturnHeldCopy(false);
            Leave();
            return;
        }
        RequestLog.Finish(this, $"Reading \"{book.Title}\" here");
        Say(rareRoom ? "Wonderful. I'll read it in the Rare Books room." : "Lovely, I'll find a seat.", 2.5f);
        state = State.ToSeat;
        agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
    }

    void SitDown()
    {
        state = State.Reading;
        agent.enabled = false;
        transform.SetPositionAndRotation(seat.SitPosition(seatIndex), seat.SeatRotation);
        if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitRead); }
        timer = Random.Range(readSeconds.x, readSeconds.y);
        if (GM.CurrentPhase != GameManager.Phase.Open) timer = Mathf.Min(timer, 8f); // closing bell: a quick look only
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
        var attendant = rareRoom && StaffManager.Instance != null ? StaffManager.Instance.Worker(GameManager.StaffRole.RareAttendant) : null;
        if (attendant != null && attendant.gameObject.activeSelf && attendant.CanWork) { maxPatience = patience = 1f; CheckIn(attendant); return; }
        JoinQueue();
    }

    // ---------------- Returning (readers and returners) ----------------

    string CheckInLine => heldBook == null ? "" : kind == Kind.Returner
        ? $"Returning \"{heldBook.Title}\", thanks!"
        : "All done! Here's the book back.";

    void BeginCheckIn()
    {
        state = State.CheckIn;
        StartPatience(45f);
        Say(CheckInLine);
    }

    /// <param name="attendant">The Rare Books attendant, if they checked the scholar in: the fee goes in
    /// their purse and reaches you with their end-of-day report.</param>
    void CheckIn(StaffWorker attendant = null)
    {
        int fee = 0, tip = 0;
        if (kind == Kind.Reader)
        {
            // Reading fee: cheap shelves pay a big share, precious books a small share of a big value.
            bool rare = BookRequest.IsRare(heldBook), archive = BookRequest.IsArchive(heldBook);
            float share = rare ? 0.1f : archive ? 0.15f : 0.4f;
            int minFee = rare ? 20 : archive ? 8 : 4;
            fee = Mathf.RoundToInt(Mathf.Max(minFee, heldBook.BaseValue * share) * GM.ReadingFeeMultiplier);
            if (vip) fee *= 2; // VIP scholars pay for the Rare Books room's service
            tip = Mathf.RoundToInt(fee * 0.6f * PatienceLeft * Members.TipFactor(Membership) * HintTipFactor) + chatBonus;
            Members.Satisfy(Membership, PatienceLeft > 0.5f ? 0.5f : 0.2f, fee + tip);
            GM.Today.booksRead++;
            GM.AddReputation(1);
        }
        else
        {
            GM.Today.returns++;
            // Some returns are overdue: a small late fee.
            if (Random.value < 0.35f) fee = Random.Range(2, 6);
        }
        if (fee + tip > 0 && attendant != null) attendant.earnedToday += fee + tip;
        else if (fee + tip > 0)
        {
            GM.AddCoins(fee + tip);
            GM.Today.income += fee + tip;
            GM.Today.tips += tip;
        }
        GM.Today.served++;
        ReturnHeldCopy(kind == Kind.Reader);
        var stamper = GameObject.FindWithTag("Player")?.GetComponent<CharacterAnimator>();
        if (stamper) stamper.PlayStamp(); // check-ins get stamped too
        GameAudio.Play(a => a.served);
        Say(fee > 0 ? $"Thank you!  +{fee + tip}" : "Thanks, see you!", 2.5f);
        Leave();
    }

    void GiveUpCheckIn()
    {
        // They leave the book on the counter anyway, but they're not happy about the wait.
        ReturnHeldCopy(false);
        Members.Satisfy(Membership, -0.5f);
        GM.AddReputation(-1);
        Say("I'll just leave it here then...", 2.5f);
        Leave();
    }

    void GiveUpCheckout()
    {
        ReturnHeldCopy(false);
        Members.Satisfy(Membership, -0.5f);
        GM.AddReputation(-1);
        Say("I'll leave it here. Maybe next time.", 2.5f);
        Leave();
    }

    /// <summary>Puts the held copy in the return bin, sometimes a little more worn.</summary>
    void ReturnHeldCopy(bool wasRead)
    {
        if (heldBook == null) return;
        var condition = wasRead ? RollWear(heldCondition) : heldCondition;
        if (ReturnBin.Instance != null) ReturnBin.Instance.Add(heldBook, condition);
        else heldBook.AddCopy(condition);
        GM.NotifyBooksChanged();
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

    void Penalise(float share)
    {
        patience -= maxPatience * share * GM.ModeWrongPenalty;
        if (anim) anim.SetActivity(CharacterAnimator.Activity.Impatient);
        Invoke(nameof(EndPenaltyAnim), 1.5f);
        if (patience <= 0f) { if (GM.NoStormingOut) patience = 0f; else LeaveAngry(); }
    }

    void EndPenaltyAnim() => StopImpatient(false);

    void StopImpatient(bool force)
    {
        if (force) impatientLoop = false;
        if (anim && !impatientLoop) anim.SetActivity(CharacterAnimator.Activity.None);
    }

    void LeaveAngry()
    {
        GM.Today.angry++;
        Members.Satisfy(Membership, -1f);
        GM.AddReputation(-2);
        GameAudio.Play(a => a.angryLeave);
        Say("Hmph! Forget it.", 2.5f);
        RequestLog.Finish(this, "Left angry");
        Leave();
    }

    void Leave()
    {
        RequestLog.Finish(this, "Left"); // anything not already closed above
        if (WaitingForCard == this) WaitingForCard = null;
        // A browser leaving before paying puts the book back.
        if (heldBook != null && (state == State.Browsing || state == State.ToShelf || state == State.Queueing && kind == Kind.Browser))
        {
            heldBook.AddCopy(heldCondition);
            heldBook = null;
            GM.NotifyBooksChanged();
        }
        GetUp(); // in case they were sitting
        var m = Membership;
        if (m != null && state != State.Leaving) m.visits++;
        state = State.Leaving;
        StopImpatient(true);
        CounterQueue.Instance.Leave(this);
        mood.AddToClassList("hidden");
        if (!agent.enabled) agent.enabled = true;
        agent.SetDestination(CounterQueue.Instance.exit.position);
    }

    static Vector3 NearestOnNavMesh(Vector3 p, float radius = 1.5f) =>
        NavMesh.SamplePosition(p, out var hit, radius, NavMesh.AllAreas) ? hit.position : p;

    /// <summary>Show a line overhead. seconds = 0 keeps it until replaced.</summary>
    void Say(string text, float seconds = 0f)
    {
        speech.EnableInClassList("ask", text == AskMark);
        speech.text = text;
        speech.RemoveFromClassList("hidden");
        reactionLeft = seconds;
    }

    void RefreshNameplate()
    {
        var m = Membership;
        nameplate.text = vip ? $"VIP  {FullName}" : "";
        nameplate.EnableInClassList("vip", vip);
        nameplate.EnableInClassList("hidden", string.IsNullOrEmpty(nameplate.text));
    }

    VisualElement reqIcon;
    string reqIconClass;

    /// <summary>Icon class for what they want: card, buy, read, return, collect, new book or a rare book.</summary>
    string RequestIconClass
    {
        get
        {
            if (state == State.CheckIn) return "req-return";
            if (state == State.Checkout) return "req-buy";
            switch (kind)
            {
                case Kind.SignUp: return "req-card";
                case Kind.Pickup: return "req-collect";
                case Kind.Seeker: return "req-new";
                case Kind.Returner: return "req-return";
                case Kind.Reader: return vip ? "req-rare" : "req-read";
                default: return "req-buy";
            }
        }
    }

    /// <summary>Shows the icon only beside their request prompt (at the desk), not beside passing remarks.</summary>
    void UpdateRequestIcon()
    {
        if (reqIcon == null) return;
        bool prompt = (state == State.Waiting || state == State.CheckIn || state == State.Checkout) && !speech.ClassListContains("hidden") && reactionLeft <= 0f;
        string cls = prompt ? RequestIconClass : null;
        if (cls == reqIconClass) return;
        if (reqIconClass != null) reqIcon.RemoveFromClassList(reqIconClass);
        reqIconClass = cls;
        if (cls != null) reqIcon.AddToClassList(cls);
        reqIcon.EnableInClassList("on", cls != null);
        reqIcon.MarkDirtyRepaint();
    }

    void BuildOverhead()
    {
        overhead = new VisualElement();
        overhead.AddToClassList("customer-tag");
        nameplate = new Label();
        nameplate.AddToClassList("customer-name");
        speech = new Label();
        speech.AddToClassList("customer-speech");
        speech.AddToClassList("hidden");
        if (vip) speech.AddToClassList("vip");

        // Mood face instead of a countdown bar: happy, okay, sad.
        mood = new VisualElement();
        mood.AddToClassList("mood");
        mood.AddToClassList("hidden");
        var eyeL = new VisualElement(); eyeL.AddToClassList("mood-eye"); eyeL.AddToClassList("left");
        var eyeR = new VisualElement(); eyeR.AddToClassList("mood-eye"); eyeR.AddToClassList("right");
        mouth = new VisualElement(); mouth.AddToClassList("mood-mouth");
        mood.Add(eyeL); mood.Add(eyeR); mood.Add(mouth);

        // What kind of request this is, as an icon to the left of their bubble (same icons as the computer).
        var sayRow = new VisualElement();
        sayRow.AddToClassList("customer-say-row");
        reqIcon = new VisualElement();
        reqIcon.AddToClassList("req-icon");
        RequestIcon.Attach(reqIcon);
        sayRow.Add(reqIcon);
        sayRow.Add(speech);
        overhead.Add(nameplate);
        overhead.Add(sayRow);
        overhead.Add(mood);
        RefreshNameplate();
        // Head height of the 0.75-scale characters (TopAnchor could pick the blob shadow).
        follower = WorldPrompts.Instance?.Follow(overhead, () => transform.position + Vector3.up * 1.75f);
    }
}
