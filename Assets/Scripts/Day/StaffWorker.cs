using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A hired staff member walking around the library (added at runtime by StaffManager).
///  Archivist  - types at their Archive desk; fetches one worn/damaged copy from the returns at a
///               time, takes it to the repair desk and repairs it, then goes back to typing.
///  Cleaner    - roams the hall, walking to each cup or open book and tidying it away.
///  Attendant  - sits reading in the Rare Books room; looks after VIP scholars (see Customer).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class StaffWorker : MonoBehaviour
{
    enum State { ToChair, AtChair, ToBin, ToDesk, Repairing, Wander, Idle, ToMess, Cleaning, ToCollect, ToShelf, ToBinShelve, GoingHome, OffDuty, ToReport, Reporting, ToFetch }

    public GameManager.StaffRole role;
    public SeatPoint chair;
    public AudioClip typingClip;
    public float walkSpeed = 1.5f;

    NavMeshAgent agent;
    CharacterAnimator anim;
    AudioSource typing;
    State state;
    float timer;
    BookData carried;
    BookCondition carriedCondition;
    RepairDesk.Job job;
    MessSpot target;
    ShelfInteractable shelf;

    /// <summary>Today's work, for the end-of-day report.</summary>
    public int repairedToday, shelvedToday, cleanedToday, servedToday;
    /// <summary>Coins their work brought in today, handed over with the report.</summary>
    public int earnedToday;
    [Tooltip("Coins the library earns for each book the archivist repairs / puts back on a shelf.")]
    public int perRepair = 5, perShelved = 2;
    /// <summary>Stuck on a problem (see StaffHelp): no work until the librarian helps.</summary>
    public bool OnHold { get; private set; }
    /// <summary>Closing time: standing by to tell the librarian about the day.</summary>
    public bool WaitingToReport => state == State.Reporting || state == State.ToReport;

    /// <summary>Stops (or restarts) them where they are.</summary>
    /// <summary>Stuck on a problem: they put down what they're doing and can't do their job until it's sorted.
    /// The cleaner roams without tidying; the archivist and the attendant sit at their desk without working.</summary>
    public void SetHold(bool hold)
    {
        OnHold = hold;
        if (!hold) return;
        if (carried != null) { ReturnBin.Instance?.Add(carried, carriedCondition); carried = null; if (anim) anim.SetCarrying(false); }
        job = null; target = null;
        if (state == State.Reporting || state == State.ToReport || state == State.GoingHome) return;
        if (role == GameManager.StaffRole.Cleaner) Wander(); else GoToChair();
    }

    /// <summary>Patched up for free: works, but slower for the rest of the day.</summary>
    public bool Slow;
    float SlowFactor => Slow ? 0.6f : 1f;
    public bool CanWork => !OnHold && state != State.Reporting && state != State.ToReport && state != State.GoingHome;

    /// <summary>Caught on the way to the gate: stop here and turn to face the librarian for the report.</summary>
    public void StopToTalk(Vector3 lookAt)
    {
        if (agent.enabled) { agent.isStopped = true; agent.ResetPath(); }
        if (state == State.ToReport) state = State.Reporting;
        if (anim) anim.SetSpeed(0f);
        if (agent.enabled) FaceTowards(lookAt);
    }

    /// <summary>The closing bell: put down what they're doing, walk to the main gate and wait to report.</summary>
    public void WaitToReport(Vector3 gateSpot)
    {
        if (carried != null) { ReturnBin.Instance?.Add(carried, carriedCondition); carried = null; if (anim) anim.SetCarrying(false); }
        job = null; target = null;
        OnHold = false;
        if (agent.enabled) agent.isStopped = false;
        Walk(gateSpot, State.ToReport);
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = walkSpeed;
        agent.angularSpeed = 540f;
        agent.radius = 0.3f;
        agent.stoppingDistance = 0.1f;
        anim = GetComponent<CharacterAnimator>();
    }

    void Start()
    {
        typing = gameObject.AddComponent<AudioSource>();
        typing.clip = typingClip;
        typing.loop = true;
        typing.playOnAwake = false;
        typing.spatialBlend = 1f;
        typing.minDistance = 2f;
        typing.maxDistance = 14f;
        typing.volume = 0.35f;
        if (role == GameManager.StaffRole.Cleaner) Wander(); else GoToChair();
    }

    void Update()
    {
        if (agent.enabled && anim) anim.SetSpeed(Mathf.Clamp01(agent.velocity.magnitude / walkSpeed) * 0.5f);
        if (state == State.ToReport && Arrived()) { agent.ResetPath(); state = State.Reporting; }
        if (state == State.Reporting || state == State.ToReport) return;
        timer -= GameManager.Dt;
        switch (state)
        {
            case State.ToChair: if (Arrived()) SitDown(); break;
            case State.AtChair: if (timer <= 0f) { timer = 1.5f / SlowFactor; LookForWork(); } break;
            case State.ToFetch: if (Arrived()) TakeFromShelf(); break;
            case State.ToBin: if (Arrived()) TakeFromBin(); break;
            case State.ToDesk: if (Arrived()) StartRepair(); break;
            case State.Repairing: UpdateRepair(); break;
            case State.ToCollect: if (Arrived()) CollectFinished(); break;
            case State.ToShelf: if (Arrived()) ShelveCarried(); break;
            case State.ToBinShelve: if (Arrived()) TakeReturnToShelve(); break;
            case State.GoingHome: if (Arrived()) { state = State.OffDuty; gameObject.SetActive(false); } break;
            case State.Wander: if (Arrived() || timer <= 0f) { state = State.Idle; timer = Random.Range(2f, 5f); } if (MessWaiting()) GoClean(); break;
            case State.Idle: if (MessWaiting()) GoClean(); else if (CleanerShelves()) { } else if (timer <= 0f) Wander(); break;
            case State.ToMess: UpdateToMess(); break;
            case State.Cleaning:
                if (timer > 0f) break;
                if (target != null && target.IsDirty) { earnedToday += target.CleanByStaff(); cleanedToday++; GameAudio.Play(a => a.tidy, 0.4f); }
                target = null;
                if (MessWaiting()) GoClean(); else Wander();
                break;
        }
    }

    bool Arrived() => agent.enabled && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f;

    static Vector3 OnMesh(Vector3 p, float radius = 1.5f) =>
        NavMesh.SamplePosition(p, out var hit, radius, NavMesh.AllAreas) ? hit.position : p;

    void Walk(Vector3 to, State next)
    {
        Stand();
        if (agent.enabled) agent.isStopped = false;
        state = next;
        agent.SetDestination(OnMesh(to));
    }

    void Stand()
    {
        if (typing != null && typing.isPlaying) typing.Stop();
        if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
        if (!agent.enabled)
        {
            if (chair != null) transform.position = OnMesh(chair.ApproachPosition(0));
            agent.enabled = true;
        }
    }

    // ---------------- Sitting at the desk / chair ----------------

    void GoToChair()
    {
        if (chair == null) { state = State.Idle; return; }
        Walk(chair.ApproachPosition(0), State.ToChair);
    }

    void SitDown()
    {
        state = State.AtChair;
        timer = 1f;
        agent.enabled = false;
        transform.SetPositionAndRotation(chair.SitPosition(0), chair.SeatRotation);
        if (anim) anim.SetSpeed(0f);
        if (role == GameManager.StaffRole.Repairer)
        {
            // Writing up the catalogue, with the clack of the typewriter.
            if (anim) anim.SetActivity(CharacterAnimator.Activity.Type);
            if (typing.clip != null) typing.Play();
        }
        else if (anim) anim.SetActivity(CharacterAnimator.Activity.SitRead);
    }

    // ---------------- Archivist: one repair at a time ----------------

    void LookForWork()
    {
        if (role != GameManager.StaffRole.Repairer || !CanWork) return;
        var bin = ReturnBin.Instance;
        var desk = RepairDesk.Instance;
        if (bin == null || desk == null) return;
        // Finished repairs first: they go back on their shelves.
        if (desk.Done.Count > 0) { Walk(desk.transform.position, State.ToCollect); return; }
        if (desk.Working.Count > 0) return; // the table is busy
        if (desk.Waiting.Count > 0)
        {
            if (GameManager.Instance.Coins >= desk.Cost(desk.Waiting[0]))
            {
                // Something was dropped off at the desk: go and repair that.
                job = desk.Waiting[0];
                Walk(desk.transform.position, State.ToDesk);
            }
            else TellBroke(desk.Cost(desk.Waiting[0]));
            return; // don't pile up more until that one's paid for
        }
        if (bin.HasNeedingRepair) { Walk(bin.transform.position, State.ToBin); return; }
        // Worn or damaged copies still on the shelves: fetch one to mend.
        foreach (var s in FindObjectsByType<ShelfInteractable>())
        {
            if (RoomGate.IsLockedAt(s.transform.position)) continue;
            foreach (var b in s.Books)
                if (b.DamagedCopies > 0 || b.WornCopies > 0) { shelf = s; Walk(s.transform.position, State.ToFetch); return; }
        }
        // Nothing to fix: put good returns back where they belong.
        if (bin.HasGood) Walk(bin.transform.position, State.ToBinShelve);
    }

    void TakeFromShelf()
    {
        carried = null;
        if (shelf != null)
            foreach (var b in shelf.Books)
            {
                var cond = b.DamagedCopies > 0 ? BookCondition.Damaged : BookCondition.Worn;
                if ((b.DamagedCopies > 0 || b.WornCopies > 0) && b.TakeCopy(cond)) { carried = b; carriedCondition = cond; break; }
            }
        if (carried == null) { GoToChair(); return; }
        GameManager.Instance?.NotifyBooksChanged();
        if (anim) { anim.PlayReachShelf(); anim.SetCarrying(true); }
        Walk(RepairDesk.Instance.transform.position, State.ToDesk);
    }

    float brokeToldAt = -999f;

    /// <summary>Can't pay for a repair: say so (now and then), so the player knows why nothing's being mended.</summary>
    void TellBroke(int cost)
    {
        if (Time.time - brokeToldAt < 45f) return;
        brokeToldAt = Time.time;
        var talk = GetComponent<StaffTalk>();
        if (talk != null) WorldPrompts.Instance?.ShowBubble(talk, $"I need {cost} coins in the till to mend the next book.", 4f);
        FindAnyObjectByType<HUD>()?.ShowBanner($"{GameManager.StaffPerson[(int)role]} can't afford the next repair ({cost} coins).", 4f);
    }

    void TakeFromBin()
    {
        var bin = ReturnBin.Instance;
        if (bin == null || !bin.TakeNeedingRepair(out carried, out carriedCondition)) { GoToChair(); return; }
        if (anim) { anim.PlayReachShelf(); anim.SetCarrying(true); }
        Walk(RepairDesk.Instance.transform.position, State.ToDesk);
    }

    void StartRepair()
    {
        var desk = RepairDesk.Instance;
        if (anim) anim.SetCarrying(false);
        if (carried != null)
        {
            desk.DropOff(carried, carriedCondition);
            job = desk.Waiting[desk.Waiting.Count - 1];
            carried = null;
        }
        if (job != null && GameManager.Instance != null && GameManager.Instance.Coins < desk.Cost(job)) TellBroke(desk.Cost(job));
        if (job == null || !desk.StartRepair(job, 0.5f))
        {
            // Can't afford it: leave it on the desk for the librarian and go back to work.
            job = null;
            GoToChair();
            return;
        }
        state = State.Repairing;
        FaceTowards(desk.transform.position);
        if (anim) anim.SetActivity(CharacterAnimator.Activity.Type);
    }

    void UpdateRepair()
    {
        var desk = RepairDesk.Instance;
        if (desk == null || job == null || !desk.Working.Contains(job)) { job = null; CollectFinished(); }
    }

    void TakeReturnToShelve()
    {
        var bin = ReturnBin.Instance;
        if (bin == null || !bin.TakeGood(out carried, out carriedCondition)) { GoToChair(); return; }
        if (anim) { anim.PlayReachShelf(); anim.SetCarrying(true); }
        GoToShelfFor(carried);
    }

    void GoToShelfFor(BookData book)
    {
        shelf = null;
        foreach (var s in FindObjectsByType<ShelfInteractable>())
            if (s.Belongs(book) && !RoomGate.IsLockedAt(s.transform.position)) { shelf = s; break; }
        if (shelf == null) { ShelveCarried(); return; }
        Walk(shelf.transform.position, State.ToShelf);
    }

    // ---------------- Clocking in and out ----------------

    /// <summary>At the closing bell: say goodbye and walk out the front door.</summary>
    public void GoHome(Vector3 door)
    {
        if (carried != null) { ReturnBin.Instance?.Add(carried, carriedCondition); carried = null; if (anim) anim.SetCarrying(false); }
        job = null; target = null;
        Walk(door, State.GoingHome);
    }

    /// <summary>A new morning: in through the door at nine, then off to work.</summary>
    public void StartShift()
    {
        repairedToday = shelvedToday = cleanedToday = servedToday = earnedToday = 0;
        Slow = false;
        OnHold = false;
        if (agent.enabled) agent.isStopped = false;
        if (role == GameManager.StaffRole.Cleaner) Wander(); else GoToChair();
    }

    /// <summary>Picks the repaired copy up off the table and takes it to its shelf.</summary>
    void CollectFinished()
    {
        var desk = RepairDesk.Instance;
        if (desk == null || !desk.TakeDone(out carried, out carriedCondition)) { GoToChair(); return; }
        repairedToday++;
        earnedToday += perRepair + Mathf.RoundToInt(carried.BaseValue * 0.4f); // a mended book is worth a fair bit
        if (anim) { anim.PlayReachShelf(); anim.SetCarrying(true); }
        GoToShelfFor(carried);
    }

    void ShelveCarried()
    {
        if (anim) { anim.SetCarrying(false); anim.PlayReachShelf(); }
        if (carried != null)
        {
            carried.AddCopy(carriedCondition);
            carried = null;
            shelvedToday++;
            earnedToday += perShelved;
            GameManager.Instance?.NotifyBooksChanged();
        }
        GoToChair();
    }

    void FaceTowards(Vector3 p)
    {
        var d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
    }

    // ---------------- Cleaner: roam and tidy ----------------

    /// <summary>Cleaner with nothing to tidy: puts good returned copies back on their shelves.</summary>
    bool CleanerShelves()
    {
        if (role != GameManager.StaffRole.Cleaner || !CanWork || ReturnBin.Instance == null || !ReturnBin.Instance.HasGood) return false;
        Walk(ReturnBin.Instance.transform.position, State.ToBinShelve);
        return true;
    }

    bool MessWaiting()
    {
        if (!CanWork) return false;
        foreach (var m in MessSpot.All) if (!RoomGate.IsLockedAt(m.transform.position)) return true;
        return false;
    }

    void GoClean()
    {
        target = null;
        float best = float.MaxValue;
        foreach (var m in MessSpot.All)
        {
            if (RoomGate.IsLockedAt(m.transform.position)) continue;
            float d = (m.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; target = m; }
        }
        if (target == null) { Wander(); return; }
        Walk(target.transform.position, State.ToMess);
        timer = 20f; // give up if it can't get there
    }

    void UpdateToMess()
    {
        if (target == null || !target.IsDirty) { if (MessWaiting()) GoClean(); else Wander(); return; }
        var flat = target.transform.position - transform.position; flat.y = 0f;
        if (flat.magnitude < 1.3f || Arrived() || timer <= 0f)
        {
            agent.ResetPath();
            FaceTowards(target.transform.position);
            if (anim) anim.PlayReachShelf();
            state = State.Cleaning;
            timer = 1.6f / ((GameManager.Instance != null ? GameManager.Instance.StaffWorkFactor(role) : 1f) * SlowFactor);
        }
    }

    void Wander()
    {
        // A random spot in the open part of the library.
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3(Random.Range(11f, 29f), 0f, Random.Range(2f, 17f));
            if (RoomGate.IsLockedAt(p) || !NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas)) continue;
            Walk(hit.position, State.Wander);
            timer = 15f;
            return;
        }
        state = State.Idle;
        timer = 3f;
    }

    void OnDisable()
    {
        // Let go of anything half done so no copy is lost.
        if (carried != null) { ReturnBin.Instance?.Add(carried, carriedCondition); carried = null; }
    }
}
