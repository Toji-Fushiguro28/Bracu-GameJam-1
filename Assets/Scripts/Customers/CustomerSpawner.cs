using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sends customers in through the entrance while the library is open.
/// </summary>
public class CustomerSpawner : MonoBehaviour
{
    [Tooltip("Character prefabs customers are picked from.")]
    public GameObject[] customerPrefabs;
    public float firstDelay = 4f;
    public Vector2 interval = new Vector2(15f, 26f);
    [Tooltip("Most customers in the library at once.")]
    public int maxCustomers = 4;
    [Range(0f, 1f)] public float readerChance = 0.55f;
    [Range(0f, 1f)] public float returnerChance = 0.12f;
    [Tooltip("Chance a visitor without a library card comes to sign up.")]
    [Range(0f, 1f)] public float signUpChance = 0.2f;

    float nextSpawn;
    int lastPrefab = -1;

    void Start() => nextSpawn = Time.time + firstDelay;

    void Update()
    {
        var gm = GameManager.Instance;
        var queue = CounterQueue.Instance;
        if (gm == null || queue == null || gm.CurrentPhase != GameManager.Phase.Open) return;
        if (Time.time < nextSpawn) return;
        // Reputation brings more visitors, more often.
        float busy = (1f + Mathf.Min(gm.Reputation, 60) / 40f) * gm.VisitorMultiplier;
        nextSpawn = Time.time + Random.Range(interval.x, interval.y) / busy;
        int cap = maxCustomers + Mathf.Min(3, gm.Reputation / 15);
        if (FindObjectsByType<Customer>().Length >= cap) return;
        Spawn(queue);
    }

    void Spawn(CounterQueue queue)
    {
        if (customerPrefabs == null || customerPrefabs.Length == 0) return;
        int i = Random.Range(0, customerPrefabs.Length);
        if (customerPrefabs.Length > 1 && i == lastPrefab) i = (i + 1) % customerPrefabs.Length;
        lastPrefab = i;

        Vector3 pos = queue.entrance.position;
        if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas)) pos = hit.position;

        // Agent and click collider are added before the Customer wakes up.
        var go = Instantiate(customerPrefabs[i], pos, queue.entrance.rotation);
        go.name = customerPrefabs[i].name + " (Customer)";
        go.SetActive(false);
        go.AddComponent<NavMeshAgent>();
        var cap = go.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 0.95f, 0);
        cap.height = 1.9f;
        cap.radius = 0.3f;
        var customer = go.AddComponent<Customer>();
        string n = customerPrefabs[i].name;
        customer.vip = n.Contains("Professor") || n.Contains("Dean");
        float r = Random.value;
        customer.kind = customer.vip ? Customer.Kind.Reader
            : r < readerChance ? Customer.Kind.Reader
            : r < readerChance + returnerChance ? Customer.Kind.Returner
            : Customer.Kind.Buyer;
        // Visitors without a card sometimes come to sign up.
        customer.profileKey = n;
        if (Members.Find(n) == null && Members.TryProfile(n, out _) && Random.value < signUpChance)
            customer.kind = Customer.Kind.SignUp;
        go.SetActive(true);
    }
}
