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
    // ToSpot: walking to somewhere in the library to wait for you (a shelf, a rack, a chair, the notice board).
    // AtTable: the visiting author, walking to and sitting at the signing table. ToSigning: a fan in the signing line.
    enum State { Queueing, Waiting, ToSeat, Reading, CheckIn, Leaving, ToShelf, Browsing, Checkout, WaitingForBook, ToSpot, AtTable, ToSigning, Peek }

    public Kind kind;
    [Tooltip("Dean / professors: read Rare Books in the Rare Books room.")]
    public bool vip;
    [Tooltip("Character prefab name: who this visitor is (see Members).")]
    public string profileKey;
    /// <summary>Set before they wake: a story visit (DayStory). They queue, wait without losing patience, and talk when clicked.</summary>
    [System.NonSerialized] public DayStory.Visit story;
    /// <summary>Set before they wake: browses, pockets a book and heads for the door. Click them before they get out.</summary>
    [System.NonSerialized] public bool thief;
    /// <summary>Shown instead of their card name (the inspector, a visiting author, the thief), with a name tag.</summary>
    [System.NonSerialized] public string displayName;
    /// <summary>Set before they wake: what they'll ask for at the desk (the tutorial's first visitor).</summary>
    [System.NonSerialized] public BookRequest presetRequest;
    public Member Membership => Members.Find(profileKey);
    public float walkSpeed = 1.6f;
    [Tooltip("Share of patience lost for a wrong book.")]
    public float wrongBookPenalty = 0.2f;
    public Vector2 readSeconds = new Vector2(50f, 90f);
    [Tooltip("Everyone waits this much longer than the base request time: cozy, not frantic.")]
    public float patienceScale = 1.6f;
    [Tooltip("Browsers who sit and read their pick here (the rest take it home).")]
    [Range(0f, 1f)] public float readHereChance = 0.75f;

    // VIP scholars still pay the most, but not so much that money stops mattering once Rare Books opens.
    // (A rare read now pays about 43-77 with tip, a normal read about 13. It used to be 73-198.)
    const int VipCardFee = 50, VipCardTip = 10;     // normal cards: 10 + 6
    const float VipFeeMultiplier = 1.5f;            // on top of the reading fee
    const float RareShare = 0.06f; const int RareMinFee = 15;
    const int MaxChatBonus = 10;                    // good-recommendation bonus, was 10% of the book's value (up to 50 on rare books)

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

    VisualElement overhead, mood, mouth, patienceBar, patienceFill;
    Label speech, nameplate;
    WorldPrompts.Follower follower;

    public BookRequest CurrentRequest => request;
    public bool IsWaiting => state == State.Waiting;
    public bool IsLeaving => state == State.Leaving;
    public float PatienceLeft => maxPatience > 0f ? Mathf.Clamp01(patience / maxPatience) : 1f;
    static float Dt => GameManager.Dt;
    static GameManager GM => GameManager.Instance;

    protected override string DefaultVerb => "Talk";
    public override string Verb
    {
        get
        {
            if (sneaking) return "Stop them!";
            if (story != null) return "Talk";
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
    public override bool CanInteract => sneaking || state == State.Waiting || state == State.CheckIn || state == State.Reading || state == State.Checkout || state == State.WaitingForBook;

    bool introduced;
    /// <summary>A member's errand at the desk instead of a book: handing the card back, or (patrons) donating books.</summary>
    enum Errand { None, CardBack, Donate }
    Errand errand;
    List<BookData> gift;
    /// <summary>Wrong card details tried before the right ones: a card made with mistakes starts at 2.5 hearts.</summary>
    int cardMistakes;
    /// <summary>When they started waiting with a "?" (game hours); a member left 1.5 hours gives up.</summary>
    float askSince = -1f;
    const float IgnoreHours = 1.5f;
    /// <summary>Set for someone a Friend of the library brought along to get a card.</summary>
    public string broughtBy;
    static readonly string[] DonationWishes =
    {
        "I hope they help you serve your readers well.",
        "May they help you serve every reader who comes in.",
        "I hope your readers enjoy them as much as I did.",
    };
    /// <summary>The sign-up visitor waiting at the counter who has told you their details.</summary>
    public static Customer WaitingForCard { get; private set; }
    string FirstName => !string.IsNullOrEmpty(displayName) ? displayName : Members.TryProfile(profileKey, out var p) ? p.name.Split(' ')[0] : "Visitor";
    string FullName => !string.IsNullOrEmpty(displayName) ? displayName : Members.TryProfile(profileKey, out var p) ? p.name : "Visitor";

    string SignUpText => !introduced
        ? (vip ? "Good day. I'll need a library card with Rare Books access."
           : broughtBy != null ? $"{broughtBy} said I should get a card here!" : "I'd like a library card, please!")
        : Members.TryProfile(profileKey, out var p) ? $"I'm <color=#D67840>{p.name}</color>, born <color=#D67840>{p.dob}</color>.\n<i>{p.line}</i>" : "";

    string RequestText
    {
        get
        {
            if (errand == Errand.CardBack) return "I need a word about my library card.";
            if (errand == Errand.Donate) return "I've brought something for the library!";
            if (kind == Kind.SignUp) return SignUpText;
            if (kind == Kind.Pickup && reservation != null) return reservation.urgent
                ? $"I'm back for <color=#D67840>\"{reservation.title}\"</color>! Did it arrive?"
                : $"Hello again! I'm here for <color=#D67840>\"{reservation.title}\"</color>.";
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
        Members.StartVisit(Membership);
        if (story != null)
        {
            if (story.direct) { StartDirect(); return; } // no desk: straight to what they came for
            if (story.atDesk || !GoRoam()) JoinQueue();
            return;
        }
        if (thief)
        {
            if (GoBrowse()) return;
            thief = false; displayName = null; // nothing on the shelves to take: just an ordinary visitor
            RefreshNameplate();
        }
        // Members: an unhappy one comes to hand the card back, a patron sometimes brings books, a Friend a friend.
        var member = Membership;
        if (Members.Counts(member) && !vip && !thief && kind != Kind.Pickup && kind != Kind.SignUp)
        {
            if (Members.WantsToLeave(member)) errand = Errand.CardBack;
            else if (Members.CanDonate(member) && Random.value < 0.5f && (gift = Members.PickDonation()).Count > 0) errand = Errand.Donate;
            if (errand != Errand.None) { kind = Kind.Buyer; JoinQueue(); return; }
            if (Members.BringsFriend(member)) StartCoroutine(BringFriend(FirstName));
        }
        // The book signing: about a third of ordinary visitors bring their own copy of the author's book and go
        // straight to the signing table (if the author's sitting and the line has room); otherwise in as normal.
        if (!vip && !Tutorial.HoldsClock && (kind == Kind.Buyer || kind == Kind.Browser || kind == Kind.Reader)
            && Random.value < 0.33f && GoToSigning(DayStory.SigningBook())) return;
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
        if (kind == Kind.Seeker && !Tutorial.HoldsClock && GoSeek()) return; // the tutorial's comes straight to the desk
        if (kind == Kind.Browser && GoBrowse()) return;
        if (vip && kind == Kind.Reader && AttendantServes()) return;
        if (kind == Kind.Browser) kind = Kind.Buyer; // nothing on the shelves: ask instead
        JoinQueue();
    }

    void OnDestroy()
    {
        if (CounterQueue.Instance != null) CounterQueue.Instance.Leave(this);
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.Unfollow(follower);
        DayStory.Unpoint(ref askPointer);
        if (seat != null) seat.Release(this);
        if (WaitingForCard == this) WaitingForCard = null;
        if (table != null) { table.Remove(this); table.AuthorLeft(this); }
    }

    /// <summary>Lights out (checked every frame until they've gone): readers stop and come to check their book in,
    /// browsers with a book get it stamped, everyone else heads home. Nobody is left sitting in the dark.</summary>
    void HandleClosed()
    {
        // Story visitors (still waiting, or looking around) just head home, settling up first if their scene says so
        // (the author still pays for the table).
        if ((story != null || lingering) && state != State.Leaving)
        {
            var unheard = story?.unheard;
            story = null;
            lingering = false;
            unheard?.Invoke(this);
            Leave();
            return;
        }
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
            case State.ToSpot:
            case State.ToSigning:
            case State.Peek:
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
        // Talking freezes the clock, and everyone stands still with it (the agent would otherwise walk on).
        bool frozen = GM != null && GM.Paused;
        bool stomping = stompUntil > 0f && Time.time < stompUntil;
        if (stompUntil > 0f && !stomping) { stompUntil = 0f; if (anim) anim.SetActivity(CharacterAnimator.Activity.None); }
        bool hold = frozen || stomping;
        if (agent.enabled && agent.isOnNavMesh && agent.isStopped != hold) agent.isStopped = hold;
        int priority = state == State.Queueing ? 80 : 50; // in the line: step aside for people walking by
        if (agent.avoidancePriority != priority) agent.avoidancePriority = priority;
        if (storming && overhead != null) overhead.style.rotate = new Rotate(new Angle(stomping ? Mathf.Sin(Time.time * 45f) * 6f : 0f));
        if (GM != null && GM.CurrentPhase == GameManager.Phase.Closed) HandleClosed();
        else if (DayStory.PowerOut && !wentForPower && kind == Kind.SignUp && story == null && (state == State.Queueing || state == State.Waiting)
                 && Members.Find(profileKey) == null && !Tutorial.HoldsClock)
            ComeBackAfterPower();
        Watchdog();
        if (agent.enabled && anim) anim.SetSpeed(Mathf.Clamp01(agent.velocity.magnitude / walkSpeed) * 0.5f);

        switch (state)
        {
            case State.Queueing: UpdateQueue(); break;
            case State.Waiting:
                // A story visitor waits as long as it takes (they're here to talk, not on a clock).
                if (story != null) { if (!roaming) transform.rotation = Quaternion.RotateTowards(transform.rotation, CounterQueue.Instance.FacingDesk, 360f * Time.deltaTime); }
                else UpdatePatience();
                if (roaming) UpdateRoaming();
                break;
            case State.ToSpot:
                if (Arrived()) ArriveAtSpot();
                break;
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
            case State.Peek:
                UpdatePeek();
                break;
            case State.Reading:
                timer -= Dt;
                // Don't get up in the middle of a chat.
                if (timer <= 0f && (DialogueUI.Instance == null || !DialogueUI.Instance.IsOpen)) StandUp();
                break;
            case State.Leaving:
                if (!Arrived()) break;
                if (sneaking) { sneaking = false; DayStory.Instance?.ThiefGotAway(this, heldBook); heldBook = null; }
                Destroy(gameObject);
                break;
            case State.WaitingForBook: UpdateWaitingForBook(); break;
            case State.AtTable: UpdateAtTable(); break;
            case State.ToSigning: UpdateSigning(); break;
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
    string WaitingText => story != null ? (roaming && !heard ? AskMark : story.teaser) : heard || errand != Errand.None ? RequestText : "";
    const string AskMark = "?";

    void ShowIdleLine()
    {
        SetSpeech(state == State.Waiting ? WaitingText
                : state == State.CheckIn ? CheckInLine
                : state == State.Checkout ? CheckoutLine : "");
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
        HUD.StaffNote($"{GameManager.StaffPerson[(int)GameManager.StaffRole.RareAttendant]} brought {FullName} a rare book");
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
        agent.SetDestination(NearestOnNavMesh(ShelfSpot(browseShelf), 2.5f));
        return true;
    }

    void StartBrowsing()
    {
        state = State.Browsing;
        timer = lingering ? Random.Range(10f, 18f) : Random.Range(4f, 7f); // story visitors take their time
        if (anim) anim.PlayReachShelf();
    }

    void PickFromShelf()
    {
        if (lingering) { if (GM.Hour >= lingerUntil || !GoBrowse()) EndLinger(); return; } // just looking (the inspector, the author)
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
        if (thief) { Pocket(); return; }

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

    string CheckoutLine => heldBook == null ? "" : $"I found <color=#D67840>\"{heldBook.Title}\"</color> myself! Can you check it out?";

    void StampOut(GameObject player)
    {
        var playerAnim = player.GetComponent<CharacterAnimator>();
        if (playerAnim) playerAnim.PlayStamp();
        GameAudio.DeskStamp();
        bool worn = heldCondition == BookCondition.Worn;
        int price = Mathf.RoundToInt(heldBook.SaleValue * (worn ? 0.8f : 1f));
        int tip = Mathf.RoundToInt(heldBook.BaseValue * 0.15f * PatienceLeft * Members.TipFactor(Membership));
        Members.Satisfy(Membership, PatienceLeft > 0.5f ? 0.4f : 0.15f, price + tip);
        GM.AddCoins(price + tip);
        GM.Today.served++; GM.Today.booksSold++; GM.Today.Earn(GameManager.DayStats.Source.Sales, price + tip); GM.Today.tips += tip;
        RequestLog.Paid(this, price + tip, FirstName, $"Stamped out <color=#D67840>\"{heldBook.Title}\"</color>");
        GM.AddReputation(1);
        DayStory.Instance?.Sold(heldBook);
        var bought = heldBook;
        heldBook = null; // it leaves with them
        GameAudio.Play(a => a.served);
        Say(tip > 0 ? $"Thank you!  +{price + tip}" : $"Thanks!  +{price}", 2.5f);
        if (!GoToSigning(bought)) Leave();
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
        // Further back in the line: face the person ahead (the line bends along the wall).
        if (index > 0 && Arrived())
        {
            // The one ahead is still far off (walking in from the door or a shelf): this one is here, so goes first.
            var ahead = queue.At(index - 1);
            if (ahead != null && (ahead.transform.position - queue.SlotPosition(index - 1)).sqrMagnitude > 3f * 3f) { queue.MoveUp(this); return; }
            FaceTowards(queue.SlotPosition(index - 1));
            return;
        }
        if (index != 0 || !Arrived()) return;
        transform.rotation = queue.FacingDesk;
        GameAudio.Play(a => a.customerArrives);

        if (story != null)
        {
            state = State.Waiting;
            Say(WaitingText);
            return;
        }
        // First times at the desk: a one-line tip (once per game).
        if (kind == Kind.Seeker) Tutorial.Show("seeker", "Not in the library? Reserve it, then order it on the computer.");
        if (kind == Kind.Pickup) Tutorial.Show("pickup", "Back for a reserved book: take it off the book cart and hand it over.");
        if (vip) Tutorial.Show("vip", "VIPs need a Rare Books card (computer, Members). Then they pay well.");

        if (kind == Kind.Browser && heldBook != null)
        {
            Tutorial.Show("checkout", "They found a book themselves. Click them to stamp it out.");
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
        BeginRequest();
    }

    /// <summary>They know what they want: at the desk, or wherever they're waiting in the library.</summary>
    void BeginRequest()
    {
        if (errand != Errand.None) { StartPatience(45f); state = State.Waiting; Say(WaitingText); return; } // here to talk, not for a book
        if (kind != Kind.Pickup && kind != Kind.Seeker)
            request = presetRequest ?? BookRequest.Create(GM.Books, kind == Kind.Reader, vip);
        if (request == null) { Say("Oh... nothing I want today."); Leave(); return; }
        // Waiting away from the desk: a bit more patience, as you have to spot them and walk over.
        StartPatience(kind == Kind.Pickup ? 70f : request.Patience * (roaming ? RoamPatience : 1f));
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
        if (!roaming) transform.rotation = Quaternion.RotateTowards(transform.rotation, CounterQueue.Instance.FacingDesk, 360f * Time.deltaTime);
        if (!Tutorial.HoldsClock) patience -= Dt; // the tutorial's visitors wait as long as you need
        float t = PatienceLeft;
        SetMood(t);

        bool low = t < 0.25f;
        if (low != impatientLoop && anim && !Seated)
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
        if (patienceBar != null)
        {
            // Drains in chunky tenths (a pixel bar, not a smooth one); the last step only goes at zero.
            float steps = Mathf.Ceil(Mathf.Clamp01(t) * 10f) / 10f;
            patienceFill.style.width = Length.Percent(steps * 100f);
            patienceBar.EnableInClassList("ok", t < 0.55f && t >= 0.25f);
            patienceBar.EnableInClassList("sad", t < 0.25f);
        }
        // About to give up: the face throbs so you notice from across the room.
        float s = t < 0.25f ? 1f + 0.2f * Mathf.Abs(Mathf.Sin(Time.time * 7f)) : 1f;
        mood.style.scale = new Scale(new Vector3(s, s, 1f));
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
        if (sneaking) { Caught(); return; }
        if (story != null && state == State.Waiting) { PlayStory(); return; }
        switch (state)
        {
            case State.CheckIn: CheckIn(); return;
            case State.Checkout: StampOut(player); return;
            case State.Reading: Chat(); return;
            case State.WaitingForBook: Say(WaitingForBookLine, 3f); return;
            case State.Waiting: break;
            default: return;
        }
        if (errand != Errand.None) { RunErrand(); return; }
        if (request == null && kind != Kind.SignUp && kind != Kind.Pickup) return; // about to ask (their second request): a moment
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
        // Into the notebook only once they've actually said what they want (after any small talk).

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = vip ? $"{FullName} (VIP)" : FirstName;
        d.vip = vip;
        var start = new DialogueAsset.Line { id = "start", text = RequestText };
        // The first time you talk, some mention their day before getting to the point.
        if (!openerSaid && kind != Kind.Pickup)
        {
            openerSaid = true;
            string opener = DayStory.Opener(profileKey);
            if (opener != null) d.lines.Add(new DialogueAsset.Line { id = "chat", text = opener, next = "start" });
        }
        d.lines.Add(start);

        // Books in your hands you could offer.
        if (carry != null)
            for (int i = carry.Count - 1; i >= 0; i--) // every book you carry
                start.choices.Add(new DialogueAsset.Choice { text = $"Here: <color=#D67840>\"{carry.Items[i].book.Title}\"</color>", next = "give" + i });

        bool pickup = kind == Kind.Pickup;
        bool soldOut = !pickup && request.OutOfStock(GM.Books);
        if (soldOut) Tutorial.Show("soldout", "Sold out? Reserve it, then order more on the computer.");
        bool seeker = kind == Kind.Seeker;
        // After any book in your hands: go and find it, ask for a hint, then the "we haven't got it" answers.
        bool tutorial = Tutorial.HoldsClock; // the tutorial: only the way forward
        start.choices.Add(new DialogueAsset.Choice { text = seeker ? "Let me check." : "Let me find it.", next = "end" });
        if (!tutorial && !pickup && !seeker && clue == null && !GM.NoHints) start.choices.Add(new DialogueAsset.Choice { text = "Could you give me a hint? (smaller tip)", next = "hint" });
        if (pickup)
        {
            if (!tutorial) start.choices.Add(new DialogueAsset.Choice { text = "Sorry, it isn't here yet.", next = "notyet" });
        }
        else
        {
            // Not in the library (sold out, or a title we don't have): ask when they need it.
            if (kind == Kind.Buyer && !tutorial || seeker)
                start.choices.Add(new DialogueAsset.Choice { text = seeker ? "When do you need it?" : "It's sold out. When do you need it?", next = soldOut ? "need" : "reserve" });
            if (!tutorial) start.choices.Add(new DialogueAsset.Choice { text = "Sorry, we can't get that.", next = "nohave" });
        }

        string hintText = HintText();
        d.lines.Add(new DialogueAsset.Line { id = "hint", text = $"<color=#D67840>{hintText}</color>", next = "end" });
        // Some need it today (express) and will wait here; others are happy to come back tomorrow.
        wantsToday = soldOut && (Tutorial.HoldsClock || Random.value < (seeker ? 0.5f : 0.4f)) && GM.Hour + 2.5f < GM.closingHour;
        var need = new DialogueAsset.Line { id = "need",
            text = wantsToday && Tutorial.HoldsClock ? "<color=#D67840>Today</color>, please! I'll wait right here in the library."
                 : wantsToday ? $"As soon as possible! I can wait here in the library. Could it be here by <color=#D67840>{GameManager.FormatHour(GM.Hour + 2.5f)}</color>?"
                              : "No rush. I'll come back tomorrow for it." };
        need.choices.Add(new DialogueAsset.Choice { text = wantsToday ? "I'll have it here for you today." : "I'll order it for you.", next = "reserve" });
        if (!Tutorial.HoldsClock) need.choices.Add(new DialogueAsset.Choice { text = "Sorry, I can't get that one.", next = "nohave" });
        d.lines.Add(need);
        d.lines.Add(new DialogueAsset.Line { id = "reserve", next = "end",
            text = !soldOut ? "Sold out? I'm sure I saw it on the shelf..."
                 : wantsToday ? "Thank you! I'll find a seat and wait."
                 : "Wonderful! I'll pick it up tomorrow. Don't forget to order it!" });
        d.lines.Add(new DialogueAsset.Line { id = "nohave", next = "end",
            text = soldOut ? "That's a shame. Thanks for checking!" : "Really? I'm sure you do... never mind." });
        // Ordered (still on its way, or already sitting on the delivery cart): not the player's fault it isn't handed over yet.
        bool ordered = pickup && reservation != null && (reservation.ordered || GM.OnOrder(request.Target) > 0);
        d.lines.Add(new DialogueAsset.Line { id = "notyet", next = "end",
            text = ordered ? "Still on its way? I'll pop back tomorrow, then." : "You never ordered it, did you? How disappointing." });
        for (int i = 0; i < (carry != null ? carry.Count : 0); i++) d.lines.Add(new DialogueAsset.Line { id = "give" + i, text = "Let me see...", next = "end" });

        string outcome = null;
        System.Action<string> seen = id =>
        {
            if (id == "start") { heard = true; LogRequest(); }
            else outcome = id;
        };
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
                    else { GM.AddReputation(-2); GM.Today.letDown++; Members.Satisfy(Membership, -0.5f); Members.Complain(Membership, Members.Gripe.CantGetIt); Members.LetDown(Membership); Say("Hmph. I'll try elsewhere.", 2.5f); Leave(); StormOff(); }
                    break;
                case "notyet":
                    RequestLog.Finish(this, ordered ? "Not here yet, back tomorrow" : "Never ordered");
                    if (ordered) { Say(reservation.urgent ? "Not yet? Fine, tomorrow then." : "See you tomorrow!", 2.5f); reservation.day = GM.day + 1; reservation.urgent = false; reservation.queued = false; Leave(); }
                    else { GM.reservations.Remove(reservation); GM.Today.angry++; GM.Today.letDown++; GM.AddReputation(-3); Members.Satisfy(Membership, -1f); Members.Complain(Membership, Members.Gripe.NeverOrdered); Members.LetDown(Membership); GameAudio.Play(a => a.angryLeave); Say("I won't reserve here again.", 2.5f); Leave(); StormOff(); }
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
            RequestLog.Note(this, $"Handed <color=#D67840>\"{book.Title}\"</color>: not for sale");
            Say("Oh, that one's not for sale? Never mind then.", 2.5f);
            return;
        }
        if (!request.Matches(book))
        {
            RequestLog.Rejected(this, book);
            Say(request.WrongLine(book), 3f); // says why, so you know what to look for
            GameAudio.Play(a => a.wrongBook);
            if (!Tutorial.HoldsClock)
            {
                GM.AddReputation(-1); GM.Today.wrongBooks++; // word gets around (not in the tutorial)
                Members.Satisfy(Membership, -0.25f);
                Members.Complain(Membership, Members.Gripe.WrongBook);
            }
            Tutorial.Show("wrongbook", "Not what they asked for. Check your Notes, then the computer.");
            Penalise(wrongBookPenalty);
            return;
        }
        if (condition == BookCondition.Damaged)
        {
            RequestLog.Note(this, $"Handed a damaged copy of <color=#D67840>\"{book.Title}\"</color>");
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
            Line = $"I looked all over the library for <color=#D67840>\"{t.Title}\"</color> by {t.Author}, but couldn't find it. Could you order it for me?" };
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
        agent.SetDestination(NearestOnNavMesh(ShelfSpot(browseShelf), 2.5f));
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
        ? $"Waiting for <color=#D67840>\"{request.Target.Title}\"</color>. Is it on its way?"
        : $"Have you ordered <color=#D67840>\"{request.Target.Title}\"</color> yet? I'm waiting here!";

    /// <summary>"As soon as possible": they stay, sitting or browsing, until the express order arrives.</summary>
    void ReserveAndWait()
    {
        GM.Reserve(profileKey, FirstName, request.Target, true);
        reservation = GM.ReservationFor(profileKey);
        if (reservation != null) reservation.queued = true; // they're already here: no second visit today
        Members.Satisfy(Membership, 0.2f);
        GameAudio.Play(a => a.cardIssued, 0.7f);
        RequestLog.Note(this, $"Waiting in the library for <color=#D67840>\"{request.Target.Title}\"</color> (express)");
        Tutorial.Show("express", "They're waiting here: order it Express (2 hours).");
        StopImpatient(true);
        CounterQueue.Instance.Leave(this);
        state = State.WaitingForBook;
        StopRoaming();
        GetUp(); // out of the chair they waited in (they'll take one to wait for the book)
        bool rare = vip && GM.RareOpen; // scholars wait in Rare Books too
        if (SeatPoint.TryReserve(this, rare, out seat, out seatIndex) || (rare && SeatPoint.TryReserve(this, false, out seat, out seatIndex)))
            agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
        else agent.SetDestination(NearestOnNavMesh(transform.position + transform.right * 2f, 2.5f));
        Say("I'll wait over here!", 2.5f);
    }

    static PlayerCarry playerCarry;

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
        // A copy anywhere they can have it: on a shelf, on the book cart, or already in your hands (taken off the
        // cart before they asked, or before they noticed). Whether it was "ordered" doesn't matter any more.
        var cart = DeliveryCart.Instance;
        if (playerCarry == null) playerCarry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
        bool arrived = BookRequest.InStock(request.Target) || (cart != null && cart.Has(request.Target))
                    || (playerCarry != null && playerCarry.Find((b, c) => b == request.Target && c != BookCondition.Damaged) >= 0);
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
            GM.Today.angry++; GM.Today.letDown++; GM.AddReputation(-2); Members.Satisfy(Membership, -0.75f); Members.Complain(Membership, Members.Gripe.WaitedForBook); Members.LetDown(Membership);
            GameAudio.Play(a => a.angryLeave);
            RequestLog.Finish(this, "Waited all that time: never ordered");
            Say("I waited all that time for nothing...", 3f);
            Leave();
            StormOff();
            return;
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
                       : $"Reserved <color=#D67840>\"{request.Target.Title}\"</color>. See you tomorrow!", 3f);
        Leave();
    }

    /// <summary>Into the notebook the first time you hear what they want.</summary>
    void LogRequest()
    {
        if (request == null) return; // already noted: Begin only puts a cleared note back
        bool pickup = kind == Kind.Pickup;
        RequestLog.Begin(this, pickup ? RequestLog.Kind.Pickup : RequestLog.Kind.Book, FirstName, pickup ? RequestText : request.Line, clue, usedHint);
    }

    /// <summary>Asking for a hint costs a third of the tip (Cozy's free hint doesn't count).</summary>
    bool usedHint;
    float HintTipFactor => usedHint ? 0.67f : 1f;

    /// <summary>A nudge in the right direction, never the answer.</summary>
    string HintText()
    {
        // Hints always point the right way: an Archive record says Archive, a magazine says magazine.
        var b = request.Target;
        string who = BookRequest.HasAuthor(b) ? b.Author : "";
        return request.Hint switch
        {
            HintType.Subject => who.Length > 0 ? $"Maybe something by an author whose name starts with \"{who[0]}\"?" : $"It's {BookRequest.Kind(b)}.",
            HintType.Format or HintType.FormatSubject => $"They keep those on the {ShelfInteractable.NameOf(b.ShelfLocation)} shelf, I think.",
            HintType.Title => who.Length > 0 ? $"It's by {who}. Is it in the catalogue?" : $"It's {BookRequest.Kind(b)}. Is it in the catalogue?",
            // Any item with the tag will do; the hint names the tag so a merely "Science" book isn't mistaken for it.
            HintType.Tag => $"It's tagged \"{request.Value}\" on the computer. {BookRequest.KindHint(b)}",
            _ => BookRequest.KindHint(b), // author, tag and description requests
        };
    }

    // ---------------- Chatting with readers ----------------

    bool chatted;

    /// <summary>The first talk with a seated reader: they gush about what they're reading (and let slip what they
    /// love), and two times in three ask you to recommend their next read from three titles. A title that suits them
    /// earns a tip; any other gets a reaction, never a penalty. Books, journals, magazines and newspapers alike.</summary>
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

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = vip ? $"{FullName} (VIP)" : FirstName;
        d.vip = vip;
        d.lines.Add(new DialogueAsset.Line { id = "start", text = Gush(heldBook) });

        // Their next read: one title that suits them and two that don't (none from locked rooms).
        var options = Random.value < 0.67f ? NextReadOptions(heldBook, out _) : null;
        if (options == null)
        {
            d.lines.Add(new DialogueAsset.Line { id = "bye", text = Pick("Anyway, I'll let you get on. Back to it!", "Right. Where was I...", "Don't let me keep you. This chapter won't read itself!"), next = DialogueAsset.End });
        }
        else
        {
            var q = new DialogueAsset.Line { id = "q", text = Pick("What should I read after this?", "Any idea what I should read next?", "You must know these shelves. What would you read next, in my shoes?") };
            for (int i = 0; i < options.Count; i++)
                q.choices.Add(new DialogueAsset.Choice { text = $"\"{options[i].book.Title}\"", next = "r" + i });
            q.choices.Add(new DialogueAsset.Choice { text = "Enjoy your read!", next = "bye" });
            d.lines.Add(q);
            for (int i = 0; i < options.Count; i++)
            {
                string t = $"<color=#D67840>\"{options[i].book.Title}\"</color>";
                d.lines.Add(new DialogueAsset.Line
                {
                    id = "r" + i, next = DialogueAsset.End,
                    text = options[i].match
                        ? Pick($"{t}? Oh, that's exactly my kind of thing. Thank you!", $"Ooh, {t}. You've got me sussed.", $"{t}... yes. Yes! Going on my list right now.", $"You're good at this. {t} it is.")
                        : Pick($"{t}? Huh. Not really my thing... but I'll have a look.", $"{t}... interesting choice. Maybe one day.", $"Hm, {t}? I'll trust you. Probably.", $"{t}? Ha! Well, I've always meant to branch out."),
                });
            }
            d.lines.Add(new DialogueAsset.Line { id = "bye", text = "Thank you! Back to my book, then.", next = DialogueAsset.End });
        }

        string outcome = null;
        System.Action<string> seen = id => { if (id == "bye" || (id.Length == 2 && id[0] == 'r')) outcome = id; };
        ui.LineShown += seen;
        ui.Play(d, () =>
        {
            ui.LineShown -= seen;
            Destroy(d);
            int pick = outcome != null && outcome[0] == 'r' ? outcome[1] - '0' : -1;
            if (options != null && pick >= 0 && pick < options.Count && options[pick].match)
            {
                // A recommendation that suits them pays, right now and where you can see it.
                int bonus = Mathf.Clamp(Mathf.RoundToInt(heldBook != null ? heldBook.BaseValue * 0.1f : 3), 3, MaxChatBonus);
                GM.AddCoins(bonus);
                RequestLog.Paid(this, bonus, FirstName, "Good recommendation: chat bonus");
                GM.Today.Earn(vip ? GameManager.DayStats.Source.Vip : GameManager.DayStats.Source.Reading, bonus);
                GM.Today.tips += bonus; // a tip, as before
                GM.AddReputation(1); // a reader who's been well advised tells their friends
                Members.Satisfy(Membership, 0.5f);
                GameAudio.Play(a => a.cardIssued, 0.7f);
                HUD.SkillPop(transform.position + Vector3.up * 2.2f, $"Good pick! +{bonus} coins, +1 reputation");
            }
            else if (pick >= 0) Members.Satisfy(Membership, 0.05f); // not their thing, but they liked being asked
            else Members.Satisfy(Membership, 0.15f);
        });
    }

    // ---------------- What a reader loves (for the gush and the next-read pick) ----------------

    // Each topic as something a reader would say they love: "I'm a sucker for the night sky."
    static readonly Dictionary<string, string> TopicLoves = new Dictionary<string, string>
    {
        { "Astronomy", "the night sky" }, { "Engineering", "how things get built" }, { "Biology", "living things" },
        { "Physics", "how the world works" }, { "Trade", "merchants and markets" }, { "Geography", "far-off places" },
        { "Empires", "kings and empires" }, { "Local history", "this town's past" }, { "Culture", "how other people live" },
        { "Reflection", "a good quiet think" }, { "Wellbeing", "a bit of calm" }, { "Big questions", "the big why-are-we-here questions" },
        { "Poetry", "a good poem" }, { "Ethics", "right and wrong" }, { "Arithmetic", "a good sum" }, { "Geometry", "shapes and angles" },
        { "Paradoxes", "a proper brain-twister" }, { "Probability", "chance and odds" },
        { "Home & leisure", "a cosy, lazy afternoon" }, { "Science & nature", "nature" }, { "Puzzles", "a good puzzle" },
        { "Essays", "a long think-piece" }, { "Local news", "knowing how the town ticks" }, { "Regional news", "what's going on round the region" },
        { "Weekend", "a lazy weekend read" }, { "Archaeology", "digging up the past" }, { "Mathematics", "numbers" },
        { "Chemistry", "what things are made of" }, { "Philosophy", "the big questions" },
        { "Town records", "old town records" }, { "Old maths", "old-fashioned sums" }, { "Letters & logs", "other people's old letters" },
        { "Almanacs", "old almanacs and weather lore" }, { "Faith", "matters of faith" }, { "Maps", "old maps" },
        { "Star charts", "old star charts" }, { "Maps & charts", "old maps" }, { "Herbals", "old plant lore" },
        { "Early prints", "very old printing" }, { "Manuscripts", "old handwriting" },
    };

    // Topics that suit each other (both ways): close neighbours you could guess from a title, and links from
    // newspapers and magazines to books. Tags aren't used: they're too general ("Reference", "Classic").
    static readonly Dictionary<string, string[]> RelatedTopics = new Dictionary<string, string[]>
    {
        { "Physics", new[] { "Engineering", "Astronomy" } }, { "Biology", new[] { "Herbals" } },
        { "Culture", new[] { "Empires", "Local history", "Geography" } }, { "Trade", new[] { "Geography" } },
        { "Poetry", new[] { "Reflection", "Wellbeing" } }, { "Ethics", new[] { "Big questions", "Reflection" } },
        { "Wellbeing", new[] { "Reflection" } }, { "Paradoxes", new[] { "Probability", "Geometry" } },
        { "Probability", new[] { "Arithmetic" } }, { "Arithmetic", new[] { "Geometry" } },
        { "Faith", new[] { "Big questions", "Reflection" } }, { "Maps", new[] { "Geography", "Maps & charts" } },
        { "Local news", new[] { "Local history", "Town records" } }, { "Regional news", new[] { "Geography", "Trade" } },
        { "Weekend", new[] { "Wellbeing", "Home & leisure" } }, { "Home & leisure", new[] { "Wellbeing" } },
        { "Science & nature", new[] { "Biology", "Astronomy" } }, { "Puzzles", new[] { "Paradoxes", "Probability", "Geometry" } },
        { "Essays", new[] { "Reflection", "Big questions", "Ethics" } }, { "Archaeology", new[] { "Local history", "Empires", "Culture" } },
        { "Mathematics", new[] { "Arithmetic", "Geometry", "Probability", "Paradoxes", "Old maths" } },
        { "Chemistry", new[] { "Physics", "Biology" } }, { "Philosophy", new[] { "Big questions", "Reflection", "Ethics" } },
        { "Old maths", new[] { "Arithmetic" } }, { "Star charts", new[] { "Astronomy" } }, { "Herbals", new[] { "Biology" } },
        { "Town records", new[] { "Local history" } }, { "Letters & logs", new[] { "Local history", "Culture" } }, { "Almanacs", new[] { "Astronomy" } },
    };

    static string Loves(BookData b) => TopicLoves.TryGetValue(b.SubjectName ?? "", out var s) ? s : (b.SubjectName ?? "a good read").ToLowerInvariant();

    /// <summary>Their opening line: how much they're enjoying it, then what they love, without naming the shelf.</summary>
    static string Gush(BookData b)
    {
        string t = $"<color=#D67840>\"{b.Title}\"</color>";
        string opinion = b.Format == BookFormat.Book
            ? Pick($"{t} is wonderful. I can't put it down!", BookRequest.HasAuthor(b) ? $"I love {b.Author}'s writing." : "Whoever wrote this knew their stuff.", "I've read this page three times. On purpose.")
            : b.Format == BookFormat.Newspaper
                ? Pick("I always read the paper cover to cover. Even the adverts.", "There's more in today's paper than you'd think.", "Don't tell anyone, but I read the paper for the gossip.")
                : Pick($"This {b.FormatName.ToLower()} is a proper treat.", $"So much in {t}!", "I could read these all afternoon.");
        string love = Loves(b);
        return opinion + " " + Pick($"Give me {love} and I'm happy.", $"I'm a sucker for {love}.", $"I just love {love}.");
    }

    /// <summary>Would this reader enjoy that one? The same topic, or a closely related one.</summary>
    static bool Suits(BookData reading, BookData other)
    {
        string a = reading.SubjectName ?? "", b = other.SubjectName ?? "";
        if (a == b) return true;
        if (RelatedTopics.TryGetValue(a, out var ra) && System.Array.IndexOf(ra, b) >= 0) return true;
        return RelatedTopics.TryGetValue(b, out var rb) && System.Array.IndexOf(rb, a) >= 0;
    }

    /// <summary>Three titles to recommend: one that suits them and two that don't, shuffled. Null if the library
    /// can't make a fair three (then they just chat). Books, journals, magazines and papers from open rooms only.</summary>
    List<(BookData book, bool match)> NextReadOptions(BookData reading, out int matchIndex)
    {
        matchIndex = -1;
        var pool = new List<BookData>();
        var titles = new HashSet<string> { reading.Title };
        foreach (var b in GM.Books)
        {
            if (b == null || b.Format == BookFormat.Newspaper || !BookRequest.Available(b) || (BookRequest.IsRare(b) && !vip)) continue;
            if (titles.Add(b.Title)) pool.Add(b);
        }
        var good = pool.FindAll(b => Suits(reading, b));
        var bad = pool.FindAll(b => !Suits(reading, b));
        if (good.Count == 0 || bad.Count < 2) return null;
        var options = new List<(BookData book, bool match)> { (good[Random.Range(0, good.Count)], true) };
        // Two misses from different topics, so the choice isn't two of a kind.
        var first = bad[Random.Range(0, bad.Count)];
        options.Add((first, false));
        var second = bad.FindAll(b => b.SubjectName != first.SubjectName);
        if (second.Count == 0) second = bad.FindAll(b => b != first);
        if (second.Count == 0) return null;
        options.Add((second[Random.Range(0, second.Count)], false));
        for (int i = options.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (options[i], options[j]) = (options[j], options[i]); }
        matchIndex = options.FindIndex(o => o.match);
        return options;
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
        string F(string s) => string.Format(s, heldBook.Title, BookRequest.HasAuthor(heldBook) ? heldBook.Author : "old books like this", heldBook.SubjectName);

        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = vip ? $"{FullName} (VIP)" : FirstName;
        d.vip = vip;
        var start = new DialogueAsset.Line { id = "start", text = F(t[0]) };
        start.choices.Add(new DialogueAsset.Choice { text = F(t[1]), next = "a" });
        start.choices.Add(new DialogueAsset.Choice { text = F(t[2]), next = "b" });
        d.lines.Add(start);
        d.lines.Add(new DialogueAsset.Line { id = "a", text = F(t[3]), next = "end" });
        d.lines.Add(new DialogueAsset.Line { id = "b", text = F(t[4]), next = "end" });
        ui.Play(d, () => { Destroy(d); Members.Satisfy(Membership, 0.05f); });
    }

    // ---------------- Library card ----------------

    /// <summary>Called by the PC Members form. Returns what was wrong, or null if the card is right.
    /// chime false: the form plays the "issued" chime itself, after its stamp lands.</summary>
    public string ReceiveCard(string name, string dob, AccessLevel access, bool chime = true)
    {
        if (kind != Kind.SignUp || state != State.Waiting || !Members.TryProfile(profileKey, out var p)) return "Nobody is waiting for a card.";
        string wrong = name != p.name ? "That's not my name..."
                     : dob != p.dob ? "That's not my birthday!"
                     : access != p.access ? (access == AccessLevel.Rare ? "Rare Books? I don't think I'm allowed in there!"
                                           : access == AccessLevel.Basic ? "I asked for more than a basic card." : "Hmm, I won't need the Archive.")
                     : null;
        if (wrong != null)
        {
            cardMistakes++; // the card starts at 2.5 hearts instead of 3
            Say(wrong, 2.5f);
            GameAudio.Play(a => a.cardWrong);
            Penalise(0.2f);
            return wrong;
        }
        RequestLog.CardAttempt(this, $"Card: {name}, born {dob}, {access} access", null);
        var member = Members.Add(profileKey, p, access, cardMistakes > 0); // 3 hearts, or 2.5 after mistakes
        int fee = vip ? VipCardFee : 10, tip = Mathf.RoundToInt((vip ? VipCardTip : 6) * PatienceLeft); // VIP cards: their own room, attendant and all
        Members.Satisfy(member, 0f, fee + tip);
        GM.AddCoins(fee + tip);
        RequestLog.Paid(this, fee + tip, FirstName, "Library card");
        GM.Today.Earn(vip ? GameManager.DayStats.Source.Vip : GameManager.DayStats.Source.Cards, fee + tip); GM.Today.tips += tip; GM.Today.served++; GM.Today.signups++;
        GM.AddReputation(1);
        if (WaitingForCard == this) WaitingForCard = null;
        if (chime) GameAudio.Play(a => a.cardIssued);
        RefreshNameplate();
        // Most new members have a book in mind straight away.
        var wish = !vip && !Tutorial.HoldsClock && Random.value < 0.6f ? PickWish() : null;
        RequestLog.Finish(this, $"Card issued ({access} access)");
        if (vip)
        {
            // A scholar with a fresh Rare card goes straight to reading.
            Say($"Splendid. Now, to the Rare Books room!  +{fee + tip}", 3f);
            request = null; clue = null; heard = false; usedHint = false; introduced = false;
            kind = Kind.Reader;
            CounterQueue.Instance.Leave(this);
            if (!AttendantServes()) { CounterQueue.Instance.JoinFront(this); AskAgainAtDesk(); } // ask for their book right here (still first in line)
            return null;
        }
        if (wish != null)
        {
            // They ask you at the desk (same as anyone looking for a book): you decide whether to order it.
            kind = Kind.Seeker;
            request = new BookRequest { Hint = HintType.Title, Target = wish,
                Line = $"Now that I have a card... the library doesn't have <color=#D67840>\"{wish.Title}\"</color> by {wish.Author}. Could you order it for me?" };
            heard = true;
            StartPatience(60f);
            Say($"My very own card! Thank you!  +{fee + tip}  One more thing...", 3f);
            return null;
        }
        Say($"My very own card! Thank you!  +{fee + tip}", 2.5f);
        if (!Tutorial.HoldsClock && Random.value < 0.6f) StayAsMember();
        else Leave();
        return null;
    }

    /// <summary>A fresh member tries out their card: browse the shelves, or ask for something to read.</summary>
    void StayAsMember()
    {
        request = null; clue = null; heard = false; usedHint = false; introduced = false;
        kind = Random.value < 0.5f ? Kind.Browser : Kind.Reader;
        if (kind == Kind.Browser) { CounterQueue.Instance.Leave(this); if (GoBrowse()) return; CounterQueue.Instance.JoinFront(this); kind = Kind.Buyer; }
        AskAgainAtDesk();
    }

    /// <summary>A second request straight after the first: they stay at the desk and ask (another ring of the bell)
    /// once their thank-you has been seen, instead of going round the line again.</summary>
    void AskAgainAtDesk()
    {
        state = State.Waiting;
        StartCoroutine(AskAgainSoon());
    }

    System.Collections.IEnumerator AskAgainSoon()
    {
        yield return new WaitForSeconds(2.5f);
        if (state != State.Waiting || request != null || this == null) yield break;
        if (CounterQueue.Instance.IndexOf(this) < 0) CounterQueue.Instance.JoinFront(this); // still at the desk: keep first place
        GameAudio.Play(a => a.customerArrives);
        BeginRequest();
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
        int price = Mathf.RoundToInt(book.SaleValue * (worn ? 0.8f : 1f));
        // Reserved books: they were happy to wait, and grateful you remembered.
        float tipShare = kind == Kind.Pickup ? 0.5f : 0.4f * request.TipBonus;
        int tip = worn ? 0 : Mathf.RoundToInt(book.BaseValue * tipShare * Mathf.Max(speed, kind == Kind.Pickup ? 0.8f : 0f) * Members.TipFactor(Membership) * HintTipFactor);
        Members.Satisfy(Membership, speed > 0.5f || kind == Kind.Pickup ? 0.5f : 0.2f, price + tip);
        GM.AddCoins(price + tip);
        GM.Today.served++; GM.Today.booksSold++; GM.Today.Earn(GameManager.DayStats.Source.Sales, price + tip); GM.Today.tips += tip;
        RequestLog.Paid(this, price + tip, FirstName, $"Sold <color=#D67840>\"{book.Title}\"</color>");
        GM.AddReputation(worn ? 0 : 1);
        DayStory.Instance?.Sold(book);
        if (reservation != null && reservation.bonus > 0)
        {
            // A special order (SpecialJobs): the extra they promised for getting it in.
            GM.AddCoins(reservation.bonus);
            RequestLog.Paid(this, reservation.bonus, FirstName, "Special order bonus");
            GM.Today.Earn(GameManager.DayStats.Source.Other, reservation.bonus);
            GM.AddReputation(1);
            HUD.SkillPop(transform.position + Vector3.up * 2.2f, $"Special order! +{reservation.bonus} coins, +1 reputation");
        }
        if (reservation != null) GM.reservations.Remove(reservation);
        GM.NotifyBooksChanged();
        var playerAnim = GameObject.FindWithTag("Player")?.GetComponent<CharacterAnimator>();
        if (playerAnim) playerAnim.PlayStamp();
        GameAudio.Play(a => a.served);
        RequestLog.Finish(this, kind == Kind.Pickup ? "Collected" : $"Sold <color=#D67840>\"{book.Title}\"</color>");
        Say(tip > 0 ? $"Perfect, thank you!  +{price + tip}" : $"Thanks.  +{price}", 2.5f);
        if (!GoToSigning(book)) Leave();
    }

    // ---------------- Reading ----------------

    void StartReading(BookData book, BookCondition condition)
    {
        StopRoaming();
        GetUp(); // they may have been waiting in a chair: pick a proper reading seat
        heldBook = book;
        heldCondition = condition;
        GameAudio.Play(a => a.pageTurn);
        // Scholars read in the Rare Books room whatever the book (a main hall chair only if every rare seat is taken).
        bool rareRoom = BookRequest.IsRare(book) || (vip && GM.RareOpen);
        CounterQueue.Instance.Leave(this);
        mood.AddToClassList("hidden");
        StopImpatient(true);

        if (!SeatPoint.TryReserve(this, rareRoom, out seat, out seatIndex)
            && !(rareRoom && !BookRequest.IsRare(book) && SeatPoint.TryReserve(this, false, out seat, out seatIndex)))
        {
            // Nowhere to sit: they hand it straight back.
            RequestLog.Finish(this, "No free seat");
            Say("Oh, every seat's taken. Maybe another day.", 2.5f);
            ReturnHeldCopy(false);
            Leave();
            return;
        }
        RequestLog.Finish(this, $"Reading <color=#D67840>\"{book.Title}\"</color> here");
        Tutorial.Show("reader", "Readers love a chat. Suggest a book they'll like for a bonus.");
        Say(seat.rareBooksOnly ? "Wonderful. I'll read it in the Rare Books room." : "Lovely, I'll find a seat.", 2.5f);
        state = State.ToSeat;
        agent.SetDestination(NearestOnNavMesh(seat.ApproachPosition(seatIndex)));
    }

    void SitDown()
    {
        state = State.Reading;
        agent.enabled = false;
        transform.SetPositionAndRotation(seat.SitPosition(seatIndex), seat.SeatRotation);
        if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitRead); }
        ShowReadingBook(true);
        timer = Random.Range(readSeconds.x, readSeconds.y);
        if (GM.CurrentPhase != GameManager.Phase.Open) timer = Mathf.Min(timer, 8f); // closing bell: a quick look only
    }

    // ---------------- The open book in a seated reader's hands ----------------

    GameObject readingBook;
    Transform leftHand, rightHand;

    void ShowReadingBook(bool on)
    {
        if (on && readingBook == null)
        {
            var prefab = CustomerSpawner.Instance != null ? CustomerSpawner.Instance.openBookPrefab : null;
            var animator = GetComponentInChildren<Animator>();
            if (prefab == null || animator == null || !animator.isHuman) return;
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            readingBook = Instantiate(prefab, transform);
            foreach (var c in readingBook.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        if (readingBook != null) readingBook.SetActive(on);
    }

    void LateUpdate()
    {
        if (readingBook == null || !readingBook.activeSelf || leftHand == null || rightHand == null) return;
        if (state != State.Reading) { readingBook.SetActive(false); return; } // up and about (closing, a chat that ended it...)
        var sp = CustomerSpawner.Instance;
        var hold = (leftHand.position + rightHand.position) * 0.5f;
        // Lying flat facing the reader's way, then tipped up towards their face.
        var rot = Quaternion.AngleAxis(-(sp != null ? sp.openBookTilt : 40f), transform.right) * transform.rotation * Quaternion.Euler(270f, 0f, 0f);
        readingBook.transform.SetPositionAndRotation(hold + transform.rotation * (sp != null ? sp.openBookOffset : Vector3.zero), rot);
    }

    void StandUp()
    {
        ShowReadingBook(false);
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
        ? $"Returning <color=#D67840>\"{heldBook.Title}\"</color>, thanks!"
        : "All done! Here's the book back.";

    void BeginCheckIn()
    {
        if (kind == Kind.Returner) Tutorial.Show("returns", "Click them to check the book in. Worn copies go to the return bin.");
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
            float share = rare ? RareShare : archive ? 0.15f : 0.4f;
            int minFee = rare ? RareMinFee : archive ? 8 : 4;
            // VIP scholars pay extra for the Rare Books room's service.
            fee = Mathf.RoundToInt(Mathf.Max(minFee, heldBook.BaseValue * share) * GM.ReadingFeeMultiplier * (vip ? VipFeeMultiplier : 1f));
            tip = Mathf.RoundToInt(fee * 0.6f * PatienceLeft * Members.TipFactor(Membership) * HintTipFactor); // the right-shelf bonus is paid on the spot in Chat()
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
            RequestLog.Paid(this, fee + tip, FirstName, kind == Kind.Reader ? $"Read <color=#D67840>\"{heldBook.Title}\"</color>: reading fee" : $"Returned <color=#D67840>\"{heldBook.Title}\"</color> late: late fee");
            GM.Today.Earn(vip || BookRequest.IsRare(heldBook) ? GameManager.DayStats.Source.Vip : GameManager.DayStats.Source.Reading, fee + tip);
            GM.Today.tips += tip;
        }
        GM.Today.served++;
        ReturnHeldCopy(kind == Kind.Reader);
        var stamper = GameObject.FindWithTag("Player")?.GetComponent<CharacterAnimator>();
        if (stamper) stamper.PlayStamp(); // check-ins get stamped too
        GameAudio.DeskStamp();
        GameAudio.Play(a => a.served);
        Say(fee > 0 ? $"Thank you!  +{fee + tip}" : "Thanks, see you!", 2.5f);
        Leave();
    }

    void GiveUpCheckIn()
    {
        // They leave the book on the counter anyway, but they're not happy about the wait.
        ReturnHeldCopy(false);
        Members.Satisfy(Membership, -0.5f);
        Members.Complain(Membership, Members.Gripe.WaitedAtDesk);
        GM.AddReputation(-1);
        Say("I'll just leave it here then...", 2.5f);
        Leave();
    }

    void GiveUpCheckout()
    {
        ReturnHeldCopy(false);
        Members.Satisfy(Membership, -0.5f);
        Members.Complain(Membership, Members.Gripe.WaitedAtDesk);
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

    // ---------------- Waiting around the library (questions about books) ----------------

    /// <summary>A story visitor who only wants a chat: waiting for you somewhere in the library rather than at the desk.</summary>
    bool roaming;
    Vector3 roamLook;
    WorldPrompts.Follower askPointer;
    const float RoamPatience = 1.35f;

    bool Seated => seat != null && !agent.enabled;

    /// <summary>Somewhere to wait: browsing a shelf, at the newspaper or magazine rack, in a chair, or reading the
    /// notice board. Scholars wait in the Rare Books room once it's open. False if there's nowhere (then the desk).</summary>
    bool GoRoam()
    {
        var shelves = new List<ShelfInteractable>();
        var racks = new List<ShelfInteractable>();
        foreach (var s in FindObjectsByType<ShelfInteractable>())
        {
            if (RoomGate.IsLockedAt(s.transform.position)) continue;
            bool rareShelf = s.shelfId != null && s.shelfId.StartsWith("RARE");
            if (vip != rareShelf && !(vip && !GM.RareOpen)) continue; // scholars by the rare shelves, others not
            if (s.shelfId == "NEWS" || s.shelfId == "MAG") racks.Add(s); else shelves.Add(s);
        }
        float r = Random.value;
        // A chair (about a third of the time).
        if (r < 0.3f && SeatPoint.TryReserve(this, vip && GM.RareOpen, out seat, out seatIndex))
            return WalkToSpot(seat.ApproachPosition(seatIndex), seat.SitPosition(seatIndex));
        // The notice board.
        var board = FindAnyObjectByType<NoticeBoard>();
        if (r < 0.42f && board != null && !vip && !RoomGate.IsLockedAt(board.transform.position))
            return WalkToSpot(board.transform.position + board.transform.forward * 1.3f, board.transform.position);
        // The newspaper and magazine racks.
        if (r < 0.6f && racks.Count > 0 && !vip)
        {
            var rack = racks[Random.Range(0, racks.Count)];
            return WalkToSpot(ShelfSpot(rack), rack.transform.position);
        }
        // A bookshelf.
        var pool = shelves.Count > 0 ? shelves : racks;
        if (pool.Count == 0) return false;
        var shelf = pool[Random.Range(0, pool.Count)];
        return WalkToSpot(ShelfSpot(shelf), shelf.transform.position);
    }

    bool WalkToSpot(Vector3 spot, Vector3 look)
    {
        roamLook = look;
        state = State.ToSpot;
        agent.SetDestination(NearestOnNavMesh(spot, 2.5f));
        return true;
    }

    /// <summary>There: sit down if it's a chair, then wait for you with a "?".</summary>
    void ArriveAtSpot()
    {
        if (seat != null)
        {
            agent.enabled = false;
            transform.SetPositionAndRotation(seat.SitPosition(seatIndex), seat.SeatRotation);
            if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitIdle); }
        }
        else FaceTowards(roamLook);
        WaitHere(roamLook);
    }

    /// <summary>Wait right here for you: a "?" over their head, and an arrow at the screen edge when they're off it.</summary>
    void WaitHere(Vector3 look)
    {
        roamLook = look;
        roaming = true;
        CounterQueue.Instance.Leave(this);
        Tutorial.Show("ask", "A \"?\" means they'd like a word. Click them.");
        if (story != null) { state = State.Waiting; Say(WaitingText); }
        else BeginRequest();
        if (state == State.Waiting && !heard)
        {
            askSince = GM.Hour; // a member left waiting too long gives up (UpdateRoaming)
            var t = transform;
            askPointer = DayStory.Point("?", () => t != null ? t.position + Vector3.up * 2.3f : Vector3.zero, true); // only at the screen edge
        }
    }

    /// <summary>While they wait: look at you when you come close, otherwise at what they're browsing; now and then
    /// reach for a book. The "?" arrow goes once you've heard them.</summary>
    void UpdateRoaming()
    {
        if (heard && askPointer != null) DayStory.Unpoint(ref askPointer);
        // A member nobody came to for 1.5 game hours (only while open; the clock stops on screens and in talks).
        if (!heard && askSince >= 0f && !vip && !thief && Members.Counts(Membership)
            && GM.CurrentPhase == GameManager.Phase.Open && GM.Hour - askSince >= IgnoreHours) { GiveUpAsking(); return; }
        if (Seated) return;
        var player = GameObject.FindWithTag("Player");
        bool close = player != null && (player.transform.position - transform.position).sqrMagnitude < 3.5f * 3.5f;
        FaceTowards(close ? player.transform.position : roamLook);
        if (!close && anim && Random.value < Time.deltaTime * 0.08f) anim.PlayReachShelf(); // idly checking the shelves
    }

    /// <summary>Left waiting with a "?" too long: the "?" goes, half a heart goes with it, and they head off.</summary>
    void GiveUpAsking()
    {
        askSince = -1f;
        var m = Membership;
        Members.Satisfy(m, -0.5f);
        Members.Complain(m, Members.Gripe.Ignored);
        RequestLog.Finish(this, "Gave up waiting for you");
        StopRoaming();
        Say("Never mind, I'll manage.", 2.5f);
        Leave();
    }

    // ---------------- A member's errand: the card back, or a patron's books ----------------

    void RunErrand()
    {
        var ui = DialogueUI.Instance;
        if (ui == null || ui.IsOpen) return;
        var m = Membership;
        var d = ScriptableObject.CreateInstance<DialogueAsset>();
        d.speaker = FirstName;
        if (errand == Errand.CardBack)
        {
            d.lines.Add(new DialogueAsset.Line { id = "start", text = $"I'm handing my library card back. {Members.LeavingWords(m)}", next = "bye" });
            d.lines.Add(new DialogueAsset.Line { id = "bye", text = "Goodbye.", next = "end" });
        }
        else
        {
            string titles = string.Join(" and ", gift.ConvertAll(b => $"<color=#D67840>\"{b.Title}\"</color>"));
            d.lines.Add(new DialogueAsset.Line { id = "start", text = $"I brought {(gift.Count == 1 ? "this" : "these")} for the library: {titles}. {Pick(DonationWishes)}", next = "end" });
        }
        var was = errand;
        heard = true;
        ui.Play(d, () => { Destroy(d); FinishErrand(was, m); }); // the errand (and its icon) stays until the talk ends
    }

    void FinishErrand(Errand was, Member m)
    {
        errand = Errand.None; // done, once
        StopImpatient(true);
        if (was == Errand.CardBack)
        {
            Members.ReturnCard(m);
            RefreshNameplate();
            GameAudio.Play(a => a.angryLeave, 0.5f);
        }
        else
        {
            Members.Donate(m, gift);
            GM.AddReputation(1); // the HUD plays the reputation chime
            HUD.SkillPop(transform.position + Vector3.up * 1.45f, "+1 reputation");
            Say("Happy reading!", 2f);
        }
        Leave();
    }

    /// <summary>Waited too long with their errand: the card is left on the counter anyway; the books go home again.</summary>
    void ErrandGaveUp()
    {
        var m = Membership;
        if (errand == Errand.CardBack) { Say("I'll just leave my card here.", 2.5f); Members.ReturnCard(m); RefreshNameplate(); }
        else Say("Never mind. Another time.", 2.5f);
        errand = Errand.None;
        Leave();
    }

    /// <summary>A Friend of the library brings someone along who wants a card (a moment behind them).</summary>
    System.Collections.IEnumerator BringFriend(string friendName)
    {
        yield return new WaitForSeconds(2f);
        if (GM != null && GM.CurrentPhase == GameManager.Phase.Open && !Tutorial.HoldsClock && CustomerSpawner.Instance != null)
            CustomerSpawner.Instance.SpawnFriend(friendName);
    }

    void StopRoaming()
    {
        roaming = false;
        DayStory.Unpoint(ref askPointer);
    }

    // ---------------- Story visits and the book thief (DayStory) ----------------

    bool chatReward;           // walked over to a story visitor's "?": +1 reputation when the chat ends
    bool openerSaid;           // said their opening small talk already
    bool sneaking;           // the thief, heading out with a book under their coat
    bool lingering;          // a story visitor looking round the shelves before coming back to the desk
    float lingerUntil;

    /// <summary>Their scene from DayStory; afterwards they ask for a book, come back later, or head off.</summary>
    void PlayStory()
    {
        var ui = DialogueUI.Instance;
        if (ui == null || ui.IsOpen) return;
        var visit = story;
        chatReward = roaming && !heard; // went over to hear them out: +1 reputation once the chat ends
        heard = true; // the "?" goes
        SetSpeech(""); // ...right away, not when their next line comes
        DayStory.Unpoint(ref askPointer);
        var d = visit.talk != null ? visit.talk(this) : null;
        if (d == null) { FinishStory(visit, null); return; }
        if (string.IsNullOrEmpty(d.speaker)) d.speaker = vip ? $"{FullName} (VIP)" : FullName;
        d.vip = vip;
        string last = null;
        System.Action<string> seen = id => last = id;
        ui.LineShown += seen;
        ui.Play(d, () =>
        {
            ui.LineShown -= seen;
            Destroy(d);
            FinishStory(visit, last);
        });
    }

    void FinishStory(DayStory.Visit visit, string last)
    {
        if (story != visit || state != State.Waiting) return;
        story = null;
        if (chatReward)
        {
            chatReward = false;
            GM.AddReputation(1); // the HUD plays the reputation chime
            HUD.SkillPop(transform.position + Vector3.up * 1.45f, "+1 reputation"); // starts just above their head, floats up and fades
        }
        visit.after?.Invoke(this, last);
        if (request != null || kind == Kind.SignUp || state != State.Waiting) return; // now asking for something like anyone else
        if (visit.next != null)
        {
            // Off to look round the shelves (or wherever the visit sends them); back to the desk later for their next scene.
            story = visit.next;
            lingerUntil = visit.lingerUntil;
            lingering = true;
            CounterQueue.Instance.Leave(this);
            speech.AddToClassList("hidden");
            if (GM.Hour < lingerUntil && visit.linger != null && visit.linger(this)) return;
            if (GM.Hour >= lingerUntil || !GoBrowse()) EndLinger();
            return;
        }
        Leave();
    }

    void EndLinger()
    {
        lingering = false;
        JoinQueue();
    }

    /// <summary>A visit that skips the desk (the author at two, the inspector at one): its after runs as they walk in,
    /// then they go straight to their linger (the signing table, looking round) and come to the desk for next.</summary>
    void StartDirect()
    {
        var visit = story;
        visit.after?.Invoke(this, null);
        story = visit.next;
        lingerUntil = visit.lingerUntil;
        lingering = true;
        if (speech != null) speech.AddToClassList("hidden");
        if (GM.Hour < lingerUntil && visit.linger != null && visit.linger(this)) return;
        if (GM.Hour >= lingerUntil || !GoBrowse()) EndLinger();
    }

    // ---------------- A quick look somewhere, then home (the author checking the signing table) ----------------

    Vector3 peekLook;

    /// <summary>Walk over, stand looking at it for a few seconds, then leave.</summary>
    public void PeekThenLeave(Vector3 spot, Vector3 look, float seconds)
    {
        CounterQueue.Instance?.Leave(this);
        if (!agent.enabled) agent.enabled = true;
        peekLook = look;
        timer = seconds;
        state = State.Peek;
        agent.SetDestination(NearestOnNavMesh(spot, 2.5f));
    }

    void UpdatePeek()
    {
        if (!Arrived()) return;
        FaceTowards(peekLook);
        timer -= Dt;
        if (timer <= 0f) Leave();
    }

    // ---------------- The book signing (SigningTable) ----------------

    SigningTable table;
    float tableUntil;
    bool atTable;       // the author: sitting down (not still walking over)
    int signStep;       // a fan at the front of the signing line: 0 asks, 1 the author answers, 2 thanks and goes
    float signTimer;

    /// <summary>The author sits down here.</summary>
    public bool SeatedAtTable => state == State.AtTable && atTable;

    /// <summary>The visiting author: walk to the signing table's chair and sit there until the hour, then back to the desk.</summary>
    public void SitAtTable(SigningTable t, float until)
    {
        table = t;
        tableUntil = until;
        atTable = false;
        state = State.AtTable;
        if (!agent.enabled) agent.enabled = true;
        agent.SetDestination(NearestOnNavMesh(t.AuthorApproach, 2.5f));
    }

    void UpdateAtTable()
    {
        if (table == null) { EndLinger(); return; }
        if (!atTable)
        {
            if (GM.Hour >= tableUntil) { LeaveTable(); EndLinger(); return; } // never made it in time
            if (!Arrived()) return;
            atTable = true;
            agent.enabled = false;
            transform.SetPositionAndRotation(table.AuthorSeat, table.AuthorFacing);
            if (anim) { anim.SetSpeed(0f); anim.SetActivity(CharacterAnimator.Activity.SitIdle); }
            return;
        }
        transform.rotation = table.AuthorFacing;
        if (GM.Hour >= tableUntil) { LeaveTable(); EndLinger(); }
    }

    /// <summary>Up out of the signing chair (time's up, or closing).</summary>
    void LeaveTable()
    {
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        if (atTable && table != null) transform.position = NearestOnNavMesh(table.AuthorApproach, 2.5f);
        atTable = false;
        if (!agent.enabled) agent.enabled = true;
        if (table != null) table.AuthorLeft(this);
    }

    /// <summary>A fan who just bought one of the author's books during the signing: off to the table to get it signed
    /// (if the author's there and the line isn't full; otherwise they just go home).</summary>
    bool GoToSigning(BookData book)
    {
        var t = SigningTable.Instance;
        if (book == null || t == null || vip || state == State.Leaving || !t.Join(this, book)) return false; // VIP scholars don't queue for autographs
        table = t;
        signStep = 0;
        signTimer = 0f;
        StopImpatient(true);
        mood.AddToClassList("hidden");
        CounterQueue.Instance.Leave(this);
        state = State.ToSigning;
        return true;
    }

    void UpdateSigning()
    {
        int i = table != null ? table.IndexOf(this) : -1;
        if (i < 0) { Leave(); return; }
        // The author's gone (the signing's over): nothing to wait for.
        if (table.Author == null || !table.Author.SeatedAtTable)
        {
            table.Remove(this);
            Say("Oh, they've packed up. Never mind.", 2.5f);
            Leave();
            return;
        }
        var spot = table.SlotPosition(i);
        if ((agent.destination - spot).sqrMagnitude > 0.04f) agent.SetDestination(NearestOnNavMesh(spot, 1.5f));
        if (!Arrived()) return;
        if (i > 0) { FaceTowards(table.SlotPosition(i - 1)); return; }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, table.FanFacing, 360f * Time.deltaTime);
        signTimer -= Dt;
        if (signTimer > 0f) return;
        switch (signStep++)
        {
            case 0:
                var (fan, author, thanks) = table.NextChat();
                pendingReply = author;
                pendingThanks = thanks;
                Say(fan, 4f);
                signTimer = 4f;
                break;
            case 1:
                table.Author.Say(pendingReply, 4f);
                signTimer = 4f;
                break;
            default:
                Say(pendingThanks, 2.5f);
                table.Signed(this); // the author's bonus for each fan
                table.Remove(this);
                Leave();
                break;
        }
    }

    string pendingReply, pendingThanks;

    /// <summary>A story visitor who, having said their piece, asks for a book like anyone else.</summary>
    public void ContinueWithRequest(BookRequest r, bool reading)
    {
        if (r == null || r.Target == null) return;
        story = null;
        kind = reading ? Kind.Reader : Kind.Buyer;
        if (roaming)
        {
            // Chatted where they were sitting or browsing: they ask for the book at the desk.
            presetRequest = r;
            ComeToDesk();
            return;
        }
        request = r;
        heard = true;
        StartPatience(request.Patience);
        state = State.Waiting;
        LogRequest();
        Say(RequestText);
    }

    /// <summary>A story or job visitor who sits down with this copy now (a VIP with the rare book you got them, or
    /// another one from the shelf). They read, then check it back in at the counter like any reader.</summary>
    public void ReadNow(BookData book, BookCondition condition)
    {
        if (book == null) return;
        story = null;
        kind = Kind.Reader;
        request = new BookRequest { Hint = HintType.Title, Target = book, Format = book.Format, Line = "" };
        heard = true;
        StartReading(book, condition);
    }

    /// <summary>A roaming story visitor who now wants something from the desk: up from their chair and into the line.
    /// At the front they ask for it like anyone else.</summary>
    void ComeToDesk()
    {
        StopRoaming();
        GetUp();
        heard = false;
        Say("I'll see you at the desk!", 2.5f);
        JoinQueue();
    }

    /// <summary>A story visitor who wants a library card (filled in on the PC as usual).</summary>
    bool wentForPower;

    /// <summary>The power's out, so the computer can't make their card: no fault of yours, so no penalty. They go
    /// home and come back after two to try again (DayStory.CardVisitorAfterPower).</summary>
    void ComeBackAfterPower()
    {
        wentForPower = true;
        if (WaitingForCard == this) WaitingForCard = null;
        RequestLog.Finish(this, "Power cut: coming back after two");
        Say("The computer's down? I'll pop back after two.", 3f);
        DayStory.Instance?.CardVisitorAfterPower(profileKey, vip);
        Leave();
    }

    public void ContinueAsSignUp()
    {
        story = null;
        kind = Kind.SignUp;
        introduced = false;
        if (roaming) { ComeToDesk(); return; } // cards are filled in at the desk
        heard = false;
        StartPatience(70f);
        state = State.Waiting;
        Say(WaitingText);
    }

    /// <summary>The thief: book under the coat, slowly towards the door (so you have a chance).</summary>
    void Pocket()
    {
        sneaking = true;
        state = State.Leaving;
        agent.speed = walkSpeed * 0.6f;
        agent.SetDestination(CounterQueue.Instance.exit.position);
        Say("*slips a book into their bag*");
        DayStory.Instance?.ThiefPocketed(this);
    }

    /// <summary>Clicked before they reached the door: the book goes back, and they leave empty-handed.</summary>
    void Caught()
    {
        sneaking = false;
        agent.speed = walkSpeed;
        var book = heldBook;
        if (book != null) { book.AddCopy(heldCondition); GM.NotifyBooksChanged(); }
        heldBook = null;
        Say("...", 2f);
        DayStory.Instance?.ThiefCaught(this, book);
    }

    // ---------------- Reactions ----------------

    void Penalise(float share)
    {
        if (Tutorial.HoldsClock) return; // the tutorial: mistakes are free
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
        if (errand != Errand.None) { ErrandGaveUp(); return; }
        GM.Today.angry++;
        Members.Satisfy(Membership, -1f);
        Members.Complain(Membership, Members.Gripe.WalkedOut);
        GM.AddReputation(-3);
        GameAudio.Play(a => a.angryLeave);
        // Regulars remember: they won't come in tomorrow.
        Members.LetDown(Membership);
        Say(Membership != null ? "Hmph! Don't expect me tomorrow." : "Hmph! Forget it.", 2.5f);
        RequestLog.Finish(this, "Left angry");
        Leave();
        StormOff();
    }

    float stompUntil;
    bool storming;

    /// <summary>Leaving angry, and you can tell: they stamp on the spot for a moment with a red, shaking face
    /// and a red "Hmph!", then march out faster than they came in. Call right after Leave().</summary>
    void StormOff()
    {
        storming = true;
        stompUntil = Time.time + 0.9f;
        if (anim) anim.SetActivity(CharacterAnimator.Activity.Impatient);
        agent.speed = walkSpeed * 1.8f;
        speech.AddToClassList("angry");
        mood.RemoveFromClassList("hidden");
        mood.AddToClassList("angry");
        SetMood(0f);
        if (patienceBar != null) patienceBar.AddToClassList("hidden");
        GameAudio.Play(a => a.impatient, 0.5f);
    }

    void Leave()
    {
        if (state == State.AtTable) LeaveTable(); // the author, out of the signing chair
        if (table != null) table.Remove(this);    // out of the signing line
        StopRoaming();
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
        if (speech.ClassListContains("ask")) SetSpeech(""); // nobody leaves with a "?" over their head
        if (!agent.enabled) agent.enabled = true;
        agent.SetDestination(CounterQueue.Instance.exit.position);
    }

    /// <summary>A spot by the shelf, a little different for everyone, so two visitors don't fight over one point.</summary>
    static Vector3 ShelfSpot(ShelfInteractable shelf)
    {
        var r = Random.insideUnitCircle * 0.9f;
        return shelf.transform.position + new Vector3(r.x, 0f, r.y);
    }

    // ---------------- Stuck watchdog ----------------

    Vector3 lastPos;
    float stuckFor;
    const float StuckAfter = 3f, HopAfter = 6f;

    /// <summary>Frees a visitor who's walking somewhere but hasn't moved for a while (usually two people wanting
    /// the same spot and pushing against each other). Close to where they're going: that counts as there.
    /// Still stuck: they hop to it. In the queue they hop to their place in line.</summary>
    void Watchdog()
    {
        bool walking = agent.enabled && agent.isOnNavMesh && !agent.pathPending && agent.hasPath
                    && agent.remainingDistance > agent.stoppingDistance + 0.1f;
        if (!walking || (transform.position - lastPos).sqrMagnitude > 0.04f)
        {
            lastPos = transform.position;
            stuckFor = 0f;
            return;
        }
        stuckFor += Dt;
        if (stuckFor < StuckAfter) return;
        Vector3 dest = agent.destination;
        bool close = agent.remainingDistance < 1.5f;
        if (state == State.Queueing || state == State.ToSigning)
        {
            // Their place in line is set every frame, so hop there rather than stopping short.
            if (close || stuckFor >= HopAfter) { agent.Warp(dest); stuckFor = 0f; }
            return;
        }
        if (close) agent.SetDestination(transform.position); // near enough: they arrive where they stand
        else if (stuckFor >= HopAfter) { agent.Warp(NearestOnNavMesh(dest)); agent.SetDestination(dest); }
        else return;
        lastPos = transform.position;
        stuckFor = 0f;
    }

    static Vector3 NearestOnNavMesh(Vector3 p, float radius = 1.5f) =>
        NavMesh.SamplePosition(p, out var hit, radius, NavMesh.AllAreas) ? hit.position : p;

    /// <summary>Show a line overhead. seconds = 0 keeps it until replaced.</summary>
    public void Say(string text, float seconds = 0f)
    {
        SetSpeech(text); // an empty line hides the bubble
        reactionLeft = seconds;
    }

    /// <summary>Their bubble. A "?" shows as the pixel question mark (an icon, no text).</summary>
    void SetSpeech(string text)
    {
        bool ask = text == AskMark;
        speech.EnableInClassList("ask", ask);
        speech.text = ask ? "" : text;
        speech.EnableInClassList("hidden", !ask && string.IsNullOrEmpty(text));
    }

    void RefreshNameplate()
    {
        var m = Membership;
        // A name over their head only once they have a library card (story visitors, the VIP badge and named visits too).
        nameplate.text = m == null ? "" : vip ? $"VIP  {FullName}" : !string.IsNullOrEmpty(displayName) || story != null ? FullName : "";
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
            if (errand == Errand.CardBack) return "req-card"; // about their card
            if (errand == Errand.Donate) return "req-new";    // bringing books
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
        bool prompt = story == null && (state == State.Waiting || state == State.CheckIn || state == State.Checkout) && !speech.ClassListContains("hidden") && reactionLeft <= 0f;
        string cls = prompt ? RequestIconClass : null;
        if (cls == reqIconClass) return;
        if (reqIconClass != null) reqIcon.RemoveFromClassList(reqIconClass);
        reqIconClass = cls;
        if (cls != null) reqIcon.AddToClassList(cls);
        reqIcon.EnableInClassList("on", cls != null);
        reqIcon.MarkDirtyRepaint();
    }

    /// <summary>Their name, bubble and mood stack over their head (in the world-prompt layer).</summary>
    public VisualElement Overhead => overhead;

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
        // How long they'll wait, as a bar beside the face that drains and turns amber, then red.
        patienceBar = new VisualElement();
        patienceBar.AddToClassList("mood-bar");
        patienceFill = new VisualElement();
        patienceFill.AddToClassList("mood-bar-fill");
        patienceBar.Add(patienceFill);
        mood.Add(patienceBar);

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
        follower = WorldPrompts.Instance?.Follow(overhead, () => transform.position + Vector3.up * 1.4f);
    }
}
