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
    public Vector2 interval = new Vector2(18f, 32f);
    [Tooltip("Most customers in the library at once.")]
    public int maxCustomers = 3;
    [Range(0f, 1f)] public float readerChance = 0.35f;
    [Range(0f, 1f)] public float returnerChance = 0.15f;

    float nextSpawn;
    int lastPrefab = -1;

    void Start() => nextSpawn = Time.time + firstDelay;

    void Update()
    {
        var gm = GameManager.Instance;
        var queue = CounterQueue.Instance;
        if (gm == null || queue == null || gm.CurrentPhase != GameManager.Phase.Open) return;
        if (Time.time < nextSpawn) return;
        nextSpawn = Time.time + Random.Range(interval.x, interval.y);
        if (FindObjectsByType<Customer>(FindObjectsSortMode.None).Length >= maxCustomers) return;
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
        go.SetActive(true);
    }
}
