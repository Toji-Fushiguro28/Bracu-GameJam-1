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

    [Header("Places")]
    public SeatPoint archiveChair;
    public SeatPoint rareChair;
    [Tooltip("Where staff appear when they start (by the front doors).")]
    public Vector3 arrivePoint = new Vector3(20f, 0f, 1.6f);

    public AudioClip typingClip;

    readonly StaffWorker[] workers = new StaffWorker[3];
    float checkTimer;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (cleaner != null) cleaner.SetActive(false);
        Sync();
    }

    /// <summary>True if this character prefab is busy working here today (so they don't visit as a customer).</summary>
    public bool IsStaffCharacter(string prefabName)
    {
        for (int i = 0; i < workers.Length; i++)
            if (workers[i] != null && Source(i) != null && Source(i).name == prefabName) return true;
        return false;
    }

    GameObject Source(int role) =>
        role == (int)GameManager.StaffRole.Repairer ? archivistPrefab :
        role == (int)GameManager.StaffRole.RareAttendant ? attendantPrefab : cleaner;

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
            if (want && workers[i] == null) workers[i] = Spawn(role);
            else if (!want && workers[i] != null) Dismiss(i);
        }
    }

    public bool Working(GameManager.StaffRole role) => workers[(int)role] != null;

    StaffWorker Spawn(GameManager.StaffRole role)
    {
        Vector3 pos = NavMesh.SamplePosition(arrivePoint, out var hit, 2f, NavMesh.AllAreas) ? hit.position : arrivePoint;
        GameObject go;
        if (role == GameManager.StaffRole.Cleaner)
        {
            if (cleaner == null) return null;
            go = cleaner;
            // He's a talkable scene NPC: switch that off, he's working now.
            foreach (var npc in go.GetComponents<NPCInteractable>()) Destroy(npc);
            go.transform.position = pos;
        }
        else
        {
            var prefab = role == GameManager.StaffRole.Repairer ? archivistPrefab : attendantPrefab;
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
        go.SetActive(true);
        return w;
    }

    void Dismiss(int i)
    {
        var w = workers[i];
        workers[i] = null;
        if (w == null) return;
        if (w.gameObject == cleaner) { Destroy(w); cleaner.SetActive(false); }
        else Destroy(w.gameObject);
    }
}
