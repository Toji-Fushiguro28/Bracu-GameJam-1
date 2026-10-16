using UnityEngine;

/// <summary>
/// Day chores: readers leave teacups, browsers leave stray books at closing time,
/// and hired staff (Repairer, Cleaner) work in the background. Lives on the GameManager.
/// </summary>
public class Housekeeping : MonoBehaviour
{
    public static Housekeeping Instance { get; private set; }

    public GameObject cupModel, bookModel;
    public Material propMaterial;
    [Range(0f, 1f)] public float cupChance = 0.5f;
    public Vector2Int strayBooksAtClosing = new Vector2Int(1, 3);
    public float cleanerInterval = 10f;
    public float repairerInterval = 6f;

    float nextClean, nextRepair;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (GameManager.Instance != null) GameManager.Instance.PhaseChanged += OnPhase;
    }

    /// <summary>A reader got up: maybe they left their tea.</summary>
    public void ReaderLeft(Vector3 seatPosition)
    {
        if (Random.value < cupChance) MessSpot.Spawn(cupModel, propMaterial, TablePoint(seatPosition));
    }

    void OnPhase(GameManager.Phase phase)
    {
        if (phase != GameManager.Phase.Closing) return;
        // Browsers left a few books on the reading tables.
        var gm = GameManager.Instance;
        var seats = FindObjectsByType<SeatPoint>();
        int n = Random.Range(strayBooksAtClosing.x, strayBooksAtClosing.y + 1);
        for (int i = 0; i < n && seats.Length > 0; i++)
        {
            var seat = seats[Random.Range(0, seats.Length)];
            if (seat.rareBooksOnly) continue;
            var pool = gm.Books.FindAll(b => b.Sellable && b.Quantity > 0 && b.Format == BookFormat.Book);
            if (pool.Count == 0) break;
            var book = pool[Random.Range(0, pool.Count)];
            var best = book.BestCopy;
            if (best == null || !book.TakeCopy(best.Value)) continue;
            var cond = best.Value;
            MessSpot.Spawn(bookModel, propMaterial, TablePoint(seat.SeatPosition(0)), book, cond);
        }
        gm.NotifyBooksChanged();
    }

    /// <summary>Somewhere on the table next to a seat (or the floor if there's no table).</summary>
    static Vector3 TablePoint(Vector3 seat)
    {
        Vector3 p = seat + Random.insideUnitSphere * 0.6f;
        p.y = seat.y + 2f;
        return Physics.Raycast(p, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point : new Vector3(p.x, seat.y, p.z);
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
