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
    public enum Kind { Buyer, Reader, Returner }
    enum State { Queueing, Waiting, ToSeat, Reading, CheckIn, Leaving }

    public Kind kind;
    [Tooltip("Dean / professors: read Rare Books in the Rare Books room.")]
    public bool vip;
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
            var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
            return carry != null && !carry.IsEmpty ? "Give book" : "Ask";
        }
    }
    public override Vector3 PromptAnchor => transform.position + Vector3.up * 1.0f;
    public override bool CanInteract => state == State.Waiting || state == State.CheckIn;

    string RequestText => request == null ? "" : clue == null ? request.Line : $"{request.Line}\n<i>{clue}</i>";

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

    void Update()
    {
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
                if (Time.time >= readUntil) StandUp();
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

        request = BookRequest.Create(GameManager.Instance.Books, kind == Kind.Reader, vip);
        if (request == null) { Say("Oh... nothing I want today."); Leave(); return; }
        StartPatience(request.Patience);
        state = State.Waiting;
        Say(RequestText);
    }

    void StartPatience(float seconds)
    {
        maxPatience = patience = seconds;
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
            anim.SetActivity(low ? CharacterAnimator.Activity.Impatient : CharacterAnimator.Activity.None);
        }
        if (patience > 0f) return;
        if (state == State.CheckIn) GiveUpCheckIn(); else LeaveAngry();
    }

    bool Arrived() => agent.enabled && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;

    public override void Interact(GameObject player)
    {
        if (state == State.CheckIn) { CheckIn(); return; }
        if (state != State.Waiting) return;

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

    // ---------------- Buying ----------------

    void Sell(BookData book, BookCondition condition)
    {
        bool worn = condition == BookCondition.Worn;
        float speed = PatienceLeft;
        int price = Mathf.RoundToInt(book.BaseValue * (worn ? 0.8f : 1f));
        int tip = worn ? 0 : Mathf.RoundToInt(book.BaseValue * 0.4f * speed * request.TipBonus);
        var gm = GameManager.Instance;
        gm.AddCoins(price + tip);
        gm.Today.served++; gm.Today.booksSold++; gm.Today.income += price + tip; gm.Today.tips += tip;
        gm.AddReputation(worn ? 0 : 1);
        gm.NotifyBooksChanged();
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
        seat.Release(this);
        seat = null;
        agent.enabled = true;
        JoinQueue();
    }

    // ---------------- Returning (readers and returners) ----------------

    string CheckInLine => kind == Kind.Returner
        ? $"Returning \"{heldBook.Title}\", thanks!"
        : "All done! Here's the book back.";

    void BeginCheckIn()
    {
        state = State.CheckIn;
        StartPatience(45f);
        Say(CheckInLine);
    }

    void CheckIn()
    {
        var gm = GameManager.Instance;
        int fee = 0, tip = 0;
        if (kind == Kind.Reader)
        {
            bool special = BookRequest.IsRare(heldBook) || BookRequest.IsArchive(heldBook);
            fee = Mathf.Max(3, Mathf.RoundToInt(heldBook.BaseValue * (special ? 0.4f : 0.25f)));
            tip = Mathf.RoundToInt(fee * 0.5f * PatienceLeft);
            gm.Today.booksRead++;
            gm.AddReputation(1);
        }
        else
        {
            gm.Today.returns++;
        }
        if (fee + tip > 0)
        {
            gm.AddCoins(fee + tip);
            gm.Today.income += fee + tip;
            gm.Today.tips += tip;
        }
        gm.Today.served++;
        ReturnHeldCopy(kind == Kind.Reader);
        Say(fee > 0 ? $"Thank you!  +{fee + tip}" : "Thanks, see you!", 2.5f);
        Leave();
    }

    void GiveUpCheckIn()
    {
        // They leave the book on the counter anyway, but they're not happy about the wait.
        ReturnHeldCopy(false);
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
        gm.AddReputation(-2);
        Say("Hmph! Forget it.", 2.5f);
        Leave();
    }

    void Leave()
    {
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
