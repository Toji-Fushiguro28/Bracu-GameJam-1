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
    enum State { ToChair, AtChair, ToBin, ToDesk, Repairing, Wander, Idle, ToMess, Cleaning }

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
        timer -= GameManager.Dt;
        switch (state)
        {
            case State.ToChair: if (Arrived()) SitDown(); break;
            case State.AtChair: if (timer <= 0f) { timer = 1.5f; LookForWork(); } break;
            case State.ToBin: if (Arrived()) TakeFromBin(); break;
            case State.ToDesk: if (Arrived()) StartRepair(); break;
            case State.Repairing: UpdateRepair(); break;
            case State.Wander: if (Arrived() || timer <= 0f) { state = State.Idle; timer = Random.Range(2f, 5f); } if (MessWaiting()) GoClean(); break;
            case State.Idle: if (MessWaiting()) GoClean(); else if (timer <= 0f) Wander(); break;
            case State.ToMess: UpdateToMess(); break;
            case State.Cleaning:
                if (timer > 0f) break;
                if (target != null && target.IsDirty) { target.Clean(false); GameAudio.Play(a => a.tidy, 0.4f); }
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
        transform.SetPositionAndRotation(chair.SeatPosition(0), chair.SeatRotation);
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
        if (role != GameManager.StaffRole.Repairer) return;
        var bin = ReturnBin.Instance;
        var desk = RepairDesk.Instance;
        if (bin == null || desk == null || desk.Working.Count > 0) return; // the table is busy
        if (desk.Waiting.Count > 0 && GameManager.Instance.Coins >= desk.Cost(desk.Waiting[0]))
        {
            // Something was dropped off at the desk: go and repair that.
            job = desk.Waiting[0];
            Walk(desk.transform.position, State.ToDesk);
            return;
        }
        if (bin.HasNeedingRepair) Walk(bin.transform.position, State.ToBin);
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
        if (desk == null || job == null || !desk.Working.Contains(job)) { job = null; GoToChair(); }
    }

    void FaceTowards(Vector3 p)
    {
        var d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
    }

    // ---------------- Cleaner: roam and tidy ----------------

    bool MessWaiting()
    {
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
            timer = 1.6f;
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
