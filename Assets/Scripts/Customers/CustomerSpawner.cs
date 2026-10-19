using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sends customers in through the entrance while the library is open.
/// The day has a rhythm: a morning rush, a quiet lunch, a busy afternoon and a slow evening.
/// People who reserved a book come back for it at their own time of day, spread out from morning to late afternoon.
/// Also brings in DayStory's story visitors and the book thief.
/// </summary>
public class CustomerSpawner : MonoBehaviour
{
    [Tooltip("Character prefabs customers are picked from.")]
    public GameObject[] customerPrefabs;
    public float firstDelay = 4f;
    public Vector2 interval = new Vector2(15f, 26f);
    [Tooltip("Most customers in the library at once.")]
    public int maxCustomers = 4;

    [Header("Visitor mix (the rest are buyers)")]
    [Range(0f, 1f)] public float browserChance = 0.40f;
    [Range(0f, 1f)] public float readerChance = 0.30f;
    [Range(0f, 1f)] public float returnerChance = 0.08f;
    [Tooltip("Chance a visitor without a library card comes to sign up.")]
    [Range(0f, 1f)] public float signUpChance = 0.15f;
    [Tooltip("Visitors who look for a book we don't have, then ask you to order it.")]
    [Range(0f, 1f)] public float seekerChance = 0.12f;
    [Tooltip("Chance (from day 3) a professor visits as a VIP scholar.")]
    [Range(0f, 1f)] public float vipChance = 0.08f;

    [Header("Day rhythm: x = hour, y = how busy (1 = normal)")]
    public AnimationCurve busyness = new AnimationCurve(
        new Keyframe(9f, 1.3f), new Keyframe(11f, 1.15f), new Keyframe(12f, 0.45f),
        new Keyframe(13.5f, 0.45f), new Keyframe(14.5f, 1.1f), new Keyframe(16.5f, 1f), new Keyframe(17.5f, 0.6f));

    public static CustomerSpawner Instance { get; private set; }

    float spawnTimer;
    int lastPrefab = -1;
    readonly Queue<GameManager.Reservation> pickups = new Queue<GameManager.Reservation>();
    /// <summary>Today's pickups that haven't come yet, with the hour each one turns up.</summary>
    readonly List<(GameManager.Reservation r, float hour)> pickupTimes = new List<(GameManager.Reservation, float)>();

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        spawnTimer = firstDelay;
        if (GameManager.Instance != null) GameManager.Instance.DayStarted += QueuePickups;
        QueuePickups();
    }

    bool vipToday, seekerToday, deanToday;

    /// <summary>Is this character already in the library? (No one is in two places at once.)</summary>
    static bool Present(string prefabName)
    {
        foreach (var c in FindObjectsByType<Customer>()) if (c.profileKey == prefabName) return true;
        return false;
    }

    void QueuePickups()
    {
        vipToday = false;
        seekerToday = false;
        deanToday = false;
        pickups.Clear();
        pickupTimes.Clear();
        spawnTimer = firstDelay;
        var gm = GameManager.Instance;
        if (gm == null) return;
        // Everyone due today gets their own time: the day (9:30 to 4:15) is cut into one slot each, in a random
        // order, with a random moment inside each slot. So they come one by one, never all at opening.
        var due = gm.reservations.FindAll(r => r.day <= gm.day);
        for (int i = due.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (due[i], due[j]) = (due[j], due[i]); }
        float from = gm.openHour + 0.5f, to = gm.closingHour - 1.25f;
        float slot = (to - from) / Mathf.Max(1, due.Count);
        for (int i = 0; i < due.Count; i++) pickupTimes.Add((due[i], from + slot * (i + Random.Range(0.15f, 0.85f))));
    }

    void Update()
    {
        var gm = GameManager.Instance;
        var queue = CounterQueue.Instance;
        if (gm == null || queue == null || gm.CurrentPhase != GameManager.Phase.Open) return;
        if (gm.Hour < gm.openHour + 1f / 6f) return; // staff arrive at nine, visitors from ten past
        // "I need it today": they come back at the time they said.
        foreach (var r in gm.reservations)
            if (r.urgent && r.day == gm.day && !r.queued && gm.Hour >= r.dueHour) { r.queued = true; pickups.Enqueue(r); spawnTimer = 0f; }
        // A pickup's time has come: they're next through the door.
        for (int i = pickupTimes.Count - 1; i >= 0; i--)
        {
            if (gm.Hour < pickupTimes[i].hour) continue;
            pickups.Enqueue(pickupTimes[i].r);
            pickupTimes.RemoveAt(i);
            spawnTimer = Mathf.Min(spawnTimer, 1f);
        }
        spawnTimer -= GameManager.Dt;
        if (spawnTimer > 0f) return;

        // Reputation and flyers bring more visitors; lunch is quiet.
        float busy = (1f + Mathf.Min(gm.Reputation, 60) / 40f) * gm.VisitorMultiplier * Mathf.Max(0.2f, busyness.Evaluate(gm.Hour)) * DayStory.VisitorBoost;
        spawnTimer = Random.Range(interval.x, interval.y) / busy;
        int cap = maxCustomers + gm.ModeExtraVisitors + Mathf.Min(3, gm.Reputation / 15);
        if (FindObjectsByType<Customer>().Length >= cap) { spawnTimer = 3f; return; }
        Spawn(queue, gm);
    }

    void Spawn(CounterQueue queue, GameManager gm)
    {
        if (customerPrefabs == null || customerPrefabs.Length == 0) return;

        GameObject prefab = null;
        GameManager.Reservation pickup = null;
        bool vip = false;
        int tries = pickups.Count;
        while (pickups.Count > 0 && prefab == null && tries-- > 0)
        {
            pickup = pickups.Dequeue();
            if (!gm.reservations.Contains(pickup)) { pickup = null; continue; }
            // Already here (e.g. still waiting in the library): try them again later.
            if (Present(pickup.key)) { pickups.Enqueue(pickup); pickup = null; continue; }
            prefab = System.Array.Find(customerPrefabs, p => p != null && p.name == pickup.key);
        }
        if (prefab == null)
        {
            pickup = null;
            // Once Rare Books is open, a VIP scholar comes every day (by late morning at the latest).
            vip = gm.RareOpen && (Random.value < vipChance || (!vipToday && gm.Hour >= 10.5f));
            prefab = Pick(vip);
            if (prefab == null) return;
        }

        var customer = Bring(prefab);
        customer.vip = vip;
        var member = Members.Find(prefab.name);

        if (pickup != null) customer.kind = Customer.Kind.Pickup;
        else if (vip)
            // Scholars need a Rare Books card before they can read there.
            customer.kind = member != null && member.access == AccessLevel.Rare ? Customer.Kind.Reader : Customer.Kind.SignUp;
        else if (member == null && Members.TryProfile(prefab.name, out var profile) && profile.access != AccessLevel.Rare && Random.value < signUpChance)
            customer.kind = Customer.Kind.SignUp;
        else
        {
            float r = Random.value;
            float readers = readerChance + DayStory.ExtraReaders; // exam rush: more of them sit down to study
            // At least one "can you order it?" visitor a day, from Day 1, keeps the shop in use.
            bool seek = Random.value < seekerChance || (!seekerToday && gm.Hour >= 10.25f);
            if (seek) { customer.kind = Customer.Kind.Seeker; seekerToday = true; }
            else customer.kind = r < browserChance ? Customer.Kind.Browser
                : r < browserChance + readers ? Customer.Kind.Reader
                : r < browserChance + readers + returnerChance ? Customer.Kind.Returner
                : Customer.Kind.Buyer;
        }
        if (vip) vipToday = true;
        customer.gameObject.SetActive(true);
    }

    /// <summary>A character at the entrance, still asleep: the caller sets them up, then switches them on.
    /// Agent and click collider are added before the Customer wakes up.</summary>
    Customer Bring(GameObject prefab)
    {
        var queue = CounterQueue.Instance;
        Vector3 pos = queue.entrance.position;
        if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas)) pos = hit.position;
        var go = Instantiate(prefab, pos, queue.entrance.rotation);
        go.name = prefab.name + " (Customer)";
        go.SetActive(false);
        go.AddComponent<NavMeshAgent>();
        var cap = go.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 0.95f, 0);
        cap.height = 1.9f;
        cap.radius = 0.3f;
        var customer = go.AddComponent<Customer>();
        customer.profileKey = prefab.name;
        if (prefab.name.Contains("Dean")) deanToday = true;
        return customer;
    }

    /// <summary>Free to come in: not working here today and not already in the library.</summary>
    static bool Free(GameObject p) =>
        p != null && !(StaffManager.Instance != null && StaffManager.Instance.IsStaffCharacter(p.name)) && !Present(p.name);

    /// <summary>A story visitor (DayStory): the first of their characters who's free, or (when anyone will do) whoever is.</summary>
    public Customer SpawnStory(DayStory.Visit v, bool anyone)
    {
        if (CounterQueue.Instance == null || customerPrefabs == null) return null;
        GameObject prefab = null;
        if (v.prefabs != null)
            foreach (var key in v.prefabs)
            {
                var p = System.Array.Find(customerPrefabs, x => x != null && x.name == key);
                if (Free(p)) { prefab = p; break; }
            }
        if (prefab == null && anyone) prefab = Pick(false, p => !p.name.Contains("Dean"));
        if (prefab == null) return null;
        var c = Bring(prefab);
        c.story = v;
        c.displayName = v.name;
        c.kind = Customer.Kind.Buyer;
        c.gameObject.SetActive(true);
        return c;
    }

    /// <summary>The book thief: a stranger (someone without a card, if anyone's free) under a different name.</summary>
    public Customer SpawnThief(string name)
    {
        if (CounterQueue.Instance == null || customerPrefabs == null) return null;
        var prefab = Pick(false, p => !p.name.Contains("Dean") && Members.Find(p.name) == null)
                  ?? Pick(false, p => !p.name.Contains("Dean"));
        if (prefab == null) return null;
        var c = Bring(prefab);
        c.thief = true;
        c.displayName = name;
        c.kind = Customer.Kind.Browser;
        c.gameObject.SetActive(true);
        return c;
    }

    /// <summary>Professors only come as VIPs; everyone else (the Dean too) is a regular visitor.</summary>
    GameObject Pick(bool vip, System.Func<GameObject, bool> only = null)
    {
        var pool = new List<int>();
        for (int i = 0; i < customerPrefabs.Length; i++)
        {
            var p = customerPrefabs[i];
            if (p == null || (only != null && !only(p))) continue;
            if (StaffManager.Instance != null && StaffManager.Instance.IsStaffCharacter(p.name)) continue; // working here today
            if (Present(p.name)) continue;                          // already in the library
            if (Members.Away(Members.Find(p.name))) continue;       // let down yesterday: staying away today
            if (p.name.Contains("Dean") && deanToday) continue;    // the Dean pops in once a day at most
            bool professor = p.name.Contains("Professor");
            if (professor == vip) pool.Add(i);
        }
        if (pool.Count == 0) return null;
        // Exam rush: it's mostly students coming through the door.
        if (!vip && DayStory.StudentsWanted && Random.value < 0.75f)
        {
            var students = pool.FindAll(i => customerPrefabs[i].name.Contains("Student"));
            if (students.Count > 0) pool = students;
        }
        int pick = pool[Random.Range(0, pool.Count)];
        if (pool.Count > 1 && pick == lastPrefab) pick = pool[(pool.IndexOf(pick) + 1) % pool.Count];
        lastPrefab = pick;
        return customerPrefabs[pick];
    }
}
