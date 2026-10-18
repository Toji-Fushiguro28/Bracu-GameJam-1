using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Puts hired staff into the library as real characters, and takes them out when let go.
/// Lives on the GameManager. The cleaner (Sam) is a scene character kept hidden until hired.
/// </summary>
public class StaffManager : MonoBehaviour
{
    public static StaffManager Instance { get; private set; }

    [Header("Characters")]
    public GameObject archivistPrefab;
    public GameObject attendantPrefab;
    [Tooltip("Scene character used as the cleaner (hidden until hired).")]
    public GameObject cleaner;
    [Tooltip("Other people you can hire in interviews (picked by prefab name).")]
    public GameObject[] staffCandidates;
    readonly GameObject[] hiredPrefab = new GameObject[3];

    [Header("Places")]
    public SeatPoint archiveChair;
    public SeatPoint rareChair;
    [Tooltip("Where staff appear when they start (by the front doors).")]
    public Vector3 arrivePoint = new Vector3(20f, 0f, 1.6f);

    public AudioClip typingClip;
    [Tooltip("How far inside the front doors (from Arrive Point) staff wait to report at closing.")]
    public float reportDepth = 0.5f;

    readonly StaffWorker[] workers = new StaffWorker[3];
    float checkTimer;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (cleaner != null) cleaner.SetActive(false);
        Sync();
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.PhaseChanged += p => { if (p == GameManager.Phase.Closing) SendHome(); };
            gm.DayStarted += BringIn;
        }
    }

    static readonly string[][] Goodbyes =
    {
        new[] { "Saving the catalogue... and done. Goodnight!", "Same time tomorrow. Don't let anyone touch my glue.", "That's the bell. The books can look after themselves now." },
        new[] { "That's the bell. Me and my mop are clocking out.", "Floors are done. Mostly. Night, boss!", "Home time. My knees have been waiting all day." },
        new[] { "Time for my tea. Goodnight, all.", "I'll say goodnight to the old books on my way out.", "Another lovely day. Off I toddle." },
    };

    /// <summary>The closing bell: staff line up by the main gate and wait to tell you about their day.</summary>
    void SendHome()
    {
        for (int i = 0; i < workers.Length; i++)
        {
            var w = workers[i];
            if (w == null || !w.gameObject.activeSelf) continue;
            // Side by side, a few steps inside the main gate: far enough back that the gate rope (which
            // you click to lock up) doesn't cover them on screen, so both stay clickable.
            var spot = arrivePoint + new Vector3((i - 1) * 1.4f, 0f, reportDepth);
            w.WaitToReport(NavMesh.SamplePosition(spot, out var hit, 2f, NavMesh.AllAreas) ? hit.position : spot);
            var help = w.GetComponent<StaffHelp>();
            if (help != null) help.WaitToReport();
        }
    }

    /// <summary>After their report: a goodbye, then out of the front door.</summary>
    public void Dismiss(StaffWorker w)
    {
        var door = CounterQueue.Instance != null ? CounterQueue.Instance.exit.position : arrivePoint;
        var talk = w.GetComponent<StaffTalk>();
        var lines = Goodbyes[(int)w.role];
        if (talk != null) WorldPrompts.Instance?.ShowBubble(talk, lines[Random.Range(0, lines.Length)], 3.5f);
        w.GoHome(door);
    }

    public StaffWorker Worker(GameManager.StaffRole r) => workers[(int)r];

    /// <summary>Someone still waiting by the gate to give their report (null if everyone has).</summary>
    public StaffWorker PendingReport()
    {
        foreach (var w in workers) if (w != null && w.gameObject.activeSelf && w.WaitingToReport) return w;
        return null;
    }

    /// <summary>Nine o'clock: staff walk in through the front door (visitors start at ten past).</summary>
    void BringIn()
    {
        Vector3 pos = NavMesh.SamplePosition(arrivePoint, out var hit, 2f, NavMesh.AllAreas) ? hit.position : arrivePoint;
        foreach (var w in workers)
        {
            if (w == null) continue;
            var agent = w.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            w.transform.position = pos;
            w.gameObject.SetActive(true);
            if (agent != null) agent.enabled = true;
            w.StartShift();
            w.GetComponent<StaffHelp>()?.NewDay();
        }
    }

    /// <summary>True if this character prefab is busy working here today (so they don't visit as a customer).</summary>
    public bool IsStaffCharacter(string prefabName)
    {
        for (int i = 0; i < workers.Length; i++)
            if (workers[i] != null && Source(i) != null && Source(i).name == prefabName) return true;
        return false;
    }

    GameObject Source(int role) =>
        hiredPrefab[role] != null ? hiredPrefab[role] : Usual(role);

    GameObject Usual(int role) =>
        role == (int)GameManager.StaffRole.Repairer ? archivistPrefab :
        role == (int)GameManager.StaffRole.RareAttendant ? attendantPrefab : cleaner;

    /// <summary>The interview pick for this role, if it isn't the usual character.</summary>
    GameObject Chosen(GameManager.StaffRole role)
    {
        string want = GameManager.StaffPrefab[(int)role];
        var usual = Usual((int)role);
        if (string.IsNullOrEmpty(want) || usual != null && usual.name.Contains(want) || staffCandidates == null) return null;
        foreach (var p in staffCandidates) if (p != null && p.name.Contains(want)) return p;
        return null;
    }

    void Update()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = 0.5f;
        Sync();
    }

    void Sync()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        for (int i = 0; i < workers.Length; i++)
        {
            var role = (GameManager.StaffRole)i;
            bool want = gm.Hired(role) && gm.StaffUnlocked(role);
            // New hires start work while the library is open; after the bell they come in tomorrow.
            if (want && workers[i] == null && gm.CurrentPhase == GameManager.Phase.Open) workers[i] = Spawn(role);
            else if (!want && workers[i] != null) Dismiss(i);
            // Upgrades make them quicker on their feet too.
            if (workers[i] != null && workers[i].TryGetComponent(out NavMeshAgent a))
                a.speed = workers[i].walkSpeed * GameManager.StaffSpeed[i] * gm.StaffWorkFactor(role);
        }
    }

    public bool Working(GameManager.StaffRole role) => workers[(int)role] != null;

    StaffWorker Spawn(GameManager.StaffRole role)
    {
        Vector3 pos = NavMesh.SamplePosition(arrivePoint, out var hit, 2f, NavMesh.AllAreas) ? hit.position : arrivePoint;
        GameObject go;
        hiredPrefab[(int)role] = null;
        hiredPrefab[(int)role] = Chosen(role);
        if (role == GameManager.StaffRole.Cleaner && hiredPrefab[(int)role] == null)
        {
            if (cleaner == null) return null;
            go = cleaner;
            // He's a talkable scene NPC: switch that off, he's working now.
            foreach (var npc in go.GetComponents<NPCInteractable>()) Destroy(npc);
            go.transform.position = pos;
        }
        else
        {
            var prefab = hiredPrefab[(int)role] != null ? hiredPrefab[(int)role] : role == GameManager.StaffRole.Repairer ? archivistPrefab : attendantPrefab;
            if (prefab == null) return null;
            go = Instantiate(prefab, pos, Quaternion.identity);
            go.name = prefab.name + " (Staff)";
            go.SetActive(false);
        }
        if (go.GetComponent<NavMeshAgent>() == null) go.AddComponent<NavMeshAgent>();
        var w = go.GetComponent<StaffWorker>();
        if (w == null) w = go.AddComponent<StaffWorker>();
        w.role = role;
        w.chair = role == GameManager.StaffRole.Repairer ? archiveChair : role == GameManager.StaffRole.RareAttendant ? rareChair : null;
        w.typingClip = typingClip;
        // Something to click on, and a chat when you do.
        if (go.GetComponent<Collider>() == null)
        {
            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.95f, 0);
            cap.height = 1.9f;
            cap.radius = 0.3f;
        }
        var talk = go.GetComponent<StaffTalk>();
        if (talk == null) talk = go.AddComponent<StaffTalk>();
        talk.role = role;
        var help = go.GetComponent<StaffHelp>();
        if (help == null) help = go.AddComponent<StaffHelp>();
        help.role = role;
        go.GetComponent<NavMeshAgent>().speed = w.walkSpeed * GameManager.StaffSpeed[(int)role];
        go.SetActive(true);
        return w;
    }

    void Dismiss(int i)
    {
        var w = workers[i];
        workers[i] = null;
        if (w == null) return;
        if (w.gameObject == cleaner) { Destroy(w); Destroy(cleaner.GetComponent<StaffTalk>()); cleaner.SetActive(false); }
        else Destroy(w.gameObject);
    }
}
