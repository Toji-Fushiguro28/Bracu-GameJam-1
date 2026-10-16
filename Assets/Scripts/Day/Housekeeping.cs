using UnityEngine;

/// <summary>
/// Day chores: readers leave their teacup on the table, browsers leave open books out at
/// closing time (using the cups and books already placed in the scene), and hired staff
/// (Repairer, Cleaner) work in the background. Lives on the GameManager.
/// </summary>
public class Housekeeping : MonoBehaviour
{
    public static Housekeeping Instance { get; private set; }

    [Range(0f, 1f)] public float cupChance = 0.6f;
    [Tooltip("How far from the seat a placed cup can be to count as theirs.")]
    public float cupRange = 4f;
    public Vector2Int openBooksAtClosing = new Vector2Int(1, 3);
    public float cleanerInterval = 10f;
    public float repairerInterval = 6f;

    float nextClean, nextRepair;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (GameManager.Instance != null) GameManager.Instance.PhaseChanged += OnPhase;
    }

    /// <summary>A reader got up: maybe they left their tea on the table.</summary>
    public void ReaderLeft(Vector3 seatPosition)
    {
        if (Random.value > cupChance) return;
        var cup = MessSpot.NearestTidy(MessSpot.Kind.Cup, seatPosition, cupRange);
        if (cup != null) cup.MakeMess();
    }

    void OnPhase(GameManager.Phase phase)
    {
        if (phase != GameManager.Phase.Closing) return;
        // Browsers left a few books open on the tables: real copies off the shelves.
        var gm = GameManager.Instance;
        int n = Random.Range(openBooksAtClosing.x, openBooksAtClosing.y + 1);
        for (int i = 0; i < n; i++)
        {
            var spots = MessSpot.Placed.FindAll(m => m.kind == MessSpot.Kind.Book && !m.IsDirty);
            if (spots.Count == 0) break;
            var spot = spots[Random.Range(0, spots.Count)];
            var pool = gm.Books.FindAll(b => b.Sellable && b.Format == BookFormat.Book && b.BestCopy != null);
            if (pool.Count == 0) { spot.MakeMess(); continue; }
            var book = pool[Random.Range(0, pool.Count)];
            var cond = book.BestCopy.Value;
            if (book.TakeCopy(cond)) spot.MakeMess(book, cond);
        }
        gm.NotifyBooksChanged();
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        if (gm.Hired(GameManager.StaffRole.Cleaner) && Time.time >= nextClean && MessSpot.All.Count > 0)
        {
            nextClean = Time.time + cleanerInterval;
            MessSpot.All[0].Clean(false);
        }

        var desk = RepairDesk.Instance;
        if (gm.Hired(GameManager.StaffRole.Repairer) && desk != null && Time.time >= nextRepair)
        {
            nextRepair = Time.time + repairerInterval;
            var bin = ReturnBin.Instance;
            if (bin != null && bin.TakeNeedingRepair(out var book, out var cond)) desk.DropOff(book, cond);
            if (desk.Working.Count == 0 && desk.Waiting.Count > 0) desk.StartRepair(desk.Waiting[0], 0.5f);
        }
    }
}
