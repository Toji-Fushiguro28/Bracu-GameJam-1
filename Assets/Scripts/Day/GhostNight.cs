using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Night ghosts: once the library has closed and every visitor and staff member has gone home, 1-3 see-through,
/// softly glowing readers fade in. Some drift
/// between the shelves, the others sit in an empty chair and read.
/// Other characters don't step around them, and the librarian walks straight through. They fade out
/// when the next day opens. Made at runtime (no scene edits); the look is Resources/M_Ghost (Game/Ghost).
/// </summary>
public class GhostNight : MonoBehaviour
{
    const int MaxGhosts = 5;
    const float WalkSpeed = 0.7f, FadeSeconds = 2f;

    class Ghost
    {
        public GameObject go;
        public NavMeshAgent agent;
        public CharacterAnimator anim;
        public Renderer[] renderers;
        public bool reader, seated;
        public SeatPoint seat;
        public int seatIndex;
        public float alpha, targetAlpha = 1f, nextMoveAt, phase;
        public Vector3 seatedAt, seatTarget;
        // The night letter (GhostLetter): Ada shelving the returns, or reading at her shelf.
        public Job job;
        public BookData held;
        public BookCondition heldCondition;
        public bool toBin;
    }

    enum Job { None, Shelver, ShelfReader }

    readonly List<Ghost> ghosts = new List<Ghost>();
    bool spawnedTonight;
    float nextCheck;
    Material material;
    MaterialPropertyBlock block;
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static GameManager GM => GameManager.Instance;

    // Not just the first scene: a new game goes menu → intro (InterviewScene) → MainGame again, and the
    // run-once hook alone would leave that MainGame without ghosts.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        Create();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

    static void Create()
    {
        if (FindAnyObjectByType<GameManager>() == null || FindAnyObjectByType<GhostNight>() != null) return;
        new GameObject("GhostNight").AddComponent<GhostNight>();
    }

    void Start()
    {
        material = Resources.Load<Material>("M_Ghost");
        block = new MaterialPropertyBlock();
        if (GM == null) return;
        GM.PhaseChanged += OnPhase;
    }

    void OnDestroy()
    {
        if (GM != null) GM.PhaseChanged -= OnPhase;
    }

    void OnPhase(GameManager.Phase phase)
    {
        if (phase != GameManager.Phase.Open) return;
        spawnedTonight = false;
        foreach (var g in ghosts)
        {
            g.targetAlpha = 0f; // morning: fade away
            if (g.held != null) { g.held.AddCopy(g.heldCondition); g.held = null; } // a book still in Ada's hands goes back
        }
    }

    /// <summary>Closed, and nobody left: no visitors in the building, and the staff have reported and gone home
    /// (off-duty staff are switched off).</summary>
    static bool EveryoneGone()
    {
        if (GM == null || GM.CurrentPhase != GameManager.Phase.Closed) return false;
        // Only active objects count here: off-duty staff are disabled when they leave, and FindAnyObjectByType
        // can still see those objects, which keeps the ghost from ever spawning.
        return FindObjectsByType<Customer>(FindObjectsInactive.Exclude).Length == 0
            && FindObjectsByType<StaffWorker>(FindObjectsInactive.Exclude).Length == 0;
    }

    // ---------------- Appearing ----------------

    void Spawn()
    {
        if (GhostLetter.GhostsGone) return; // you kept Ada's book: they never come back
        SpawnAda();
        var prefabs = CustomerSpawner.Instance != null ? CustomerSpawner.Instance.customerPrefabs : null;
        if (material == null || prefabs == null || prefabs.Length == 0) return;
        int n = Random.Range(1, MaxGhosts + 1);
        for (int i = 0; i < n; i++)
        {
            var prefab = prefabs[Random.Range(0, prefabs.Length)];
            if (prefab == null || !RandomSpot(out var at)) continue;
            var g = Make(prefab, at);
            if (g == null) continue;
            g.reader = i % 2 == 1; // every other one sits down to read
            g.phase = Random.value * 10f;
            g.nextMoveAt = Time.time + Random.Range(0.5f, 2f);
            ghosts.Add(g);
        }
    }

    /// <summary>A visitor model with everything but its looks and walk taken off, in the ghost material.</summary>
    Ghost Make(GameObject prefab, Vector3 at)
    {
        // Built under an inactive parent so none of the visitor's own scripts wake up.
        var holder = new GameObject("GhostHolder");
        holder.SetActive(false);
        var go = Instantiate(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), holder.transform);
        go.name = "Ghost";
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(mb is CharacterAnimator)) DestroyImmediate(mb);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var o in go.GetComponentsInChildren<NavMeshObstacle>(true)) DestroyImmediate(o);
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);

        // Ghosts cast no shadow: the visitor's blob-shadow quad goes.
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.name.Contains("BlobShadow") || (r.sharedMaterial != null && r.sharedMaterial.name.Contains("BlobShadow"))) DestroyImmediate(r.gameObject);
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent == null) agent = go.AddComponent<NavMeshAgent>(); // (not ??: Unity objects don't null-coalesce)
        agent.speed = WalkSpeed;
        agent.angularSpeed = 240f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.avoidancePriority = 99; // nobody steps aside for a ghost

        go.transform.SetParent(null, true);
        Destroy(holder);
        go.SetActive(true);
        var g = new Ghost { go = go, agent = agent, anim = go.GetComponent<CharacterAnimator>(), renderers = renderers };
        SetAlpha(g, 0f);
        return g;
    }

    /// <summary>A walkable point on the floor in front of a shelf, in a room that's open (the NavMesh also
    /// covers some shelf tops: those don't count).</summary>
    static bool RandomSpot(out Vector3 at)
    {
        var shelves = FindObjectsByType<ShelfInteractable>();
        var player = GameObject.FindWithTag("Player");
        float floor = player != null ? player.transform.position.y : 0f; // the librarian is always on the floor
        for (int tries = 0; tries < 16 && shelves.Length > 0; tries++)
        {
            var s = shelves[Random.Range(0, shelves.Length)];
            if (RoomGate.IsLockedAt(s.transform.position)) continue;
            var p = s.transform.position + s.transform.forward * 1.2f + new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
            p.y = floor;
            if (NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - floor) < 0.4f) { at = hit.position; return true; }
        }
        at = default;
        return false;
    }

    // ---------------- Drifting, reading, fading ----------------

    void Update()
    {
        // Once a second at night: the ghosts come out when the last person has left.
        if (!spawnedTonight && Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + 1f;
            if (EveryoneGone() && !GhostLetter.StrangerDue) { spawnedTonight = true; Spawn(); } // Elias comes first
        }
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            var g = ghosts[i];
            if (g.go == null) { ghosts.RemoveAt(i); continue; }
            g.alpha = Mathf.MoveTowards(g.alpha, g.targetAlpha, Time.deltaTime / FadeSeconds);
            // A slow shimmer on top of the fade.
            SetAlpha(g, g.alpha * (0.85f + 0.15f * Mathf.Sin(Time.time * 1.7f + g.phase)));
            if (g.targetAlpha <= 0f && g.alpha <= 0f) { Destroy(g.go); ghosts.RemoveAt(i); continue; }

            float bob = 0.06f + 0.05f * Mathf.Sin(Time.time * 1.3f + g.phase); // floating, a hand's width up
            if (g.seated)
            {
                g.go.transform.position = g.seatedAt + Vector3.up * bob;
                continue;
            }
            if (!g.agent.enabled) continue;
            g.agent.baseOffset = bob;
            if (g.anim != null) g.anim.SetSpeed(Mathf.Clamp01(g.agent.velocity.magnitude / 1.5f) * 0.5f);
            bool stopped = !g.agent.pathPending && g.agent.remainingDistance <= 0.15f;
            if (g.job == Job.Shelver) { Shelve(g, stopped); continue; }
            if (g.job == Job.ShelfReader) { ReadAtShelf(g, stopped); continue; }
            if (g.reader && g.seat != null && stopped)
            {
                // Only sit once actually at the chair; a chair it can't reach: forget it and drift on.
                var flat = g.seatTarget - g.go.transform.position; flat.y = 0f;
                if (flat.magnitude < 0.6f) { SitDown(g); continue; }
                g.seat = null;
                g.nextMoveAt = Time.time + Random.Range(2f, 5f);
            }
            if (stopped && Time.time >= g.nextMoveAt) NextMove(g);
        }
    }

    void NextMove(Ghost g)
    {
        if (g.reader && g.seat == null && FreeSeat(out var seat, out int index))
        {
            // Aim for the walkable spot nearest the front of the chair (as visitors do).
            var front = seat.ApproachPosition(index);
            g.seatTarget = NavMesh.SamplePosition(front, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : front;
            g.seat = seat; g.seatIndex = index;
            g.agent.SetDestination(g.seatTarget);
            return;
        }
        if (RandomSpot(out var at)) g.agent.SetDestination(at);
        g.nextMoveAt = Time.time + Random.Range(4f, 9f); // a pause at each shelf, as if browsing
    }

    /// <summary>An empty chair in an open room (visitors have gone home by now).</summary>
    bool FreeSeat(out SeatPoint seat, out int index)
    {
        var free = new List<(SeatPoint, int)>();
        var player = GameObject.FindWithTag("Player");
        float floor = player != null ? player.transform.position.y : 0f;
        foreach (var s in SeatPoint.All)
        {
            if (s.staffOnly || RoomGate.IsLockedAt(s.transform.position)) continue;
            if (Mathf.Abs(s.transform.position.y - floor) > 0.5f) continue; // on the floor (not furniture stored up high)
            for (int i = 0; i < s.seats.Length; i++)
                if (s.IsFree(i) && !ghosts.Exists(o => o.seat == s && o.seatIndex == i)) free.Add((s, i));
        }
        if (free.Count == 0) { seat = null; index = -1; return false; }
        (seat, index) = free[Random.Range(0, free.Count)];
        return true;
    }

    void SitDown(Ghost g)
    {
        g.agent.enabled = false;
        g.seated = true;
        g.seatedAt = g.seat.SitPosition(g.seatIndex);
        g.go.transform.SetPositionAndRotation(g.seatedAt, g.seat.SeatRotation);
        if (g.anim != null) { g.anim.SetSpeed(0f); g.anim.SetActivity(CharacterAnimator.Activity.SitRead); }
    }

    // ---------------- Ada Whitmore (the night letter) ----------------

    /// <summary>Ada joins the readers: shelving the returns once she has her book back, or reading at her own shelf
    /// the night before Elias comes for it.</summary>
    void SpawnAda()
    {
        var body = GhostLetter.AdaBody;
        var job = GhostLetter.AdaHelps ? Job.Shelver : GhostLetter.ReaderAtShelf ? Job.ShelfReader : Job.None;
        if (body == null || material == null || job == Job.None || !RandomSpot(out var at)) return;
        var g = Make(body, at);
        if (g == null) return;
        g.go.name = "Ghost (Ada)";
        g.job = job;
        g.phase = Random.value * 10f;
        g.nextMoveAt = Time.time + Random.Range(1f, 3f);
        ghosts.Add(g);
    }

    static bool FloorNear(Vector3 p, out Vector3 at)
    {
        bool ok = NavMesh.SamplePosition(p, out var hit, 1.6f, NavMesh.AllAreas);
        at = ok ? hit.position : p;
        return ok;
    }

    /// <summary>One book at a time, slowly: to the return bin, take a good copy, carry it to its shelf, put it away,
    /// rest a while. Nothing to shelve: she drifts between the shelves like the others.</summary>
    void Shelve(Ghost g, bool stopped)
    {
        if (!stopped || Time.time < g.nextMoveAt) return;
        var bin = ReturnBin.Instance;
        if (g.held == null)
        {
            if (g.toBin)
            {
                g.toBin = false;
                if (bin != null && bin.TakeGood(out var book, out var condition))
                {
                    var shelf = ShelfFor(book);
                    if (shelf == null || !FloorNear(shelf.transform.position + shelf.transform.forward * 1f, out var to)) { book.AddCopy(condition); GM?.NotifyBooksChanged(); }
                    else
                    {
                        g.held = book; g.heldCondition = condition;
                        if (g.anim != null) g.anim.SetCarrying(true);
                        Carrying(g, true);
                        g.agent.SetDestination(to);
                        return;
                    }
                }
            }
            else if (bin != null && bin.HasGood && FloorNear(bin.transform.position, out var binAt))
            {
                g.toBin = true;
                g.agent.SetDestination(binAt);
                return;
            }
            if (RandomSpot(out var spot)) g.agent.SetDestination(spot);
            g.nextMoveAt = Time.time + Random.Range(6f, 10f);
            return;
        }
        // At the shelf: put it away, then a rest before the next one.
        g.held.AddCopy(g.heldCondition);
        g.held = null;
        GM?.NotifyBooksChanged();
        if (g.anim != null) { g.anim.SetCarrying(false); g.anim.PlayReachShelf(); }
        Carrying(g, false);
        g.nextMoveAt = Time.time + Random.Range(5f, 9f);
    }

    /// <summary>With a book in her arms Ada drifts slower, and her steps play slower too (the carry walk at full
    /// speed looked like a scurry).</summary>
    static void Carrying(Ghost g, bool on)
    {
        g.agent.speed = on ? WalkSpeed * 0.6f : WalkSpeed;
        var animator = g.go.GetComponent<Animator>();
        if (animator != null) animator.speed = on ? 0.6f : 1f;
    }

    static ShelfInteractable ShelfFor(BookData b) =>
        System.Array.Find(FindObjectsByType<ShelfInteractable>(), s => s.Belongs(b) && !RoomGate.IsLockedAt(s.transform.position));

    /// <summary>Stands at the Whitmore shelf with a book in her hands, all night.</summary>
    void ReadAtShelf(Ghost g, bool stopped)
    {
        var shelf = GhostLetter.WhitmoreShelf;
        if (shelf == null || !stopped || Time.time < g.nextMoveAt) return;
        if (!g.toBin) // (reused: "on my way to the shelf")
        {
            if (FloorNear(shelf.position + shelf.forward * 0.9f, out var to)) g.agent.SetDestination(to);
            g.toBin = true;
            return;
        }
        var face = shelf.position - g.go.transform.position; face.y = 0f;
        if (face.sqrMagnitude > 0.01f) g.go.transform.rotation = Quaternion.LookRotation(face);
        if (g.anim != null) g.anim.SetCarrying(true);
        g.nextMoveAt = float.MaxValue;
    }

    void SetAlpha(Ghost g, float a)
    {
        foreach (var r in g.renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(AlphaId, a);
            r.SetPropertyBlock(block);
        }
    }
}
