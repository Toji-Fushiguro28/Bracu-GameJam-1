using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The repair table. Damaged/worn copies are dropped off here, repaired one at a time in the
/// background, and put back on their shelf: Perfect Restoration = Good copy (full value),
/// Normal Repair = Worn copy (reduced value).
/// </summary>
public class RepairDesk : ClickInteractable
{
    public static RepairDesk Instance { get; private set; }

    [Header("Balance (per tier: Normal, Epic, Legendary)")]
    public float[] perfectChance = { 0.70f, 0.45f, 0.25f };
    public float[] repairSeconds = { 5f, 10f, 20f };
    [Tooltip("Repair cost as a share of the book's value.")]
    public float costShare = 0.1f;
    [Tooltip("Upgrades can't push Perfect chance above this.")]
    public float maxPerfectChance = 0.9f;
    [Tooltip("Most a perfect minigame adds to the Perfect chance.")]
    public float minigameBonus = 0.2f;

    [Header("Upgrades (raised later from the PC)")]
    public float speedMultiplier = 1f;
    public float chanceBonus = 0f;

    public AudioClip doneSound;

    public class Job
    {
        public BookData book;
        public BookCondition condition;
        public float bonus;        // from the minigame
        public float duration, elapsed;
        public bool started;
        public bool byHand;        // started one at a time on the repair screen (not Repair all, not the archivist)
        public float Progress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;
    }

    /// <summary>Copies waiting for a repair to be started.</summary>
    public readonly List<Job> Waiting = new List<Job>();
    /// <summary>Paid repairs, worked through one at a time.</summary>
    public readonly List<Job> Working = new List<Job>();

    /// <summary>Repaired copies waiting on the table to be shelved (by you or the archivist).</summary>
    public readonly List<(BookData book, BookCondition condition)> Done = new List<(BookData, BookCondition)>();

    public event Action Changed;
    public event Action<Job, bool> Finished; // job, perfect

    protected override string DefaultVerb => "Repair desk";
    public override string Verb
    {
        get
        {
            var carry = GameObject.FindWithTag("Player")?.GetComponent<PlayerCarry>();
            return carry != null && carry.Find((b, c) => c != BookCondition.Good) >= 0 ? "Drop off" : "Repair desk";
        }
    }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void Interact(GameObject player)
    {
        var carry = player.GetComponent<PlayerCarry>();
        if (carry != null && !carry.IsEmpty)
        {
            // Every worn or damaged copy in your hands goes on the table.
            int i, dropped = 0;
            while ((i = carry.Find((b, c) => c != BookCondition.Good)) >= 0)
            {
                var (book, cond) = carry.TakeAt(i);
                DropOff(book, cond);
                dropped++;
            }
            if (dropped == 0 && Done.Count == 0) { Bubble("Those copies are fine - they don't need repairs."); return; }
        }
        // Finished books stay on the table until you press Take (or Take all) on the repair screen: clicking the desk
        // just to check on a repair mustn't fill your hands.
        if (RepairUI.Instance != null) RepairUI.Instance.Open(this);
    }

    /// <summary>Takes one finished copy off the table (staff or the repair screen).</summary>
    public bool TakeDone(out BookData book, out BookCondition condition)
    {
        if (Done.Count == 0) { book = null; condition = BookCondition.Good; return false; }
        (book, condition) = Done[0];
        Done.RemoveAt(0);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts a copy on the table to be repaired; returns its job (SpecialJobs follows its own).</summary>
    public Job DropOff(BookData book, BookCondition condition)
    {
        var job = new Job { book = book, condition = condition };
        Waiting.Add(job);
        Changed?.Invoke();
        return job;
    }

    /// <summary>Takes a job off the table wherever it is (a visitor's own book going home): true if it was there.</summary>
    public bool Withdraw(Job job)
    {
        bool gone = Waiting.Remove(job) || Working.Remove(job);
        if (gone) Changed?.Invoke();
        return gone;
    }

    /// <summary>Takes one finished copy of this book in this state off the table (the owner collects it).</summary>
    public bool TakeDone(BookData book, BookCondition condition)
    {
        if (!Done.Remove((book, condition))) return false;
        Changed?.Invoke();
        return true;
    }

    int Tier(BookData b) => Mathf.Clamp((int)b.Tier, 0, 2);
    public int Cost(Job j) => Mathf.Max(2, Mathf.RoundToInt(j.book.BaseValue * costShare * (j.condition == BookCondition.Worn ? 0.5f : 1f)));
    public float Chance(Job j) => Mathf.Min(maxPerfectChance, perfectChance[Tier(j.book)] + chanceBonus + j.bonus);
    /// <summary>Value of the copy after a Normal repair, as a share of the original.</summary>
    public float NormalValueShare(BookData b) => b.Tier == BookTier.Normal ? 0.8f : b.Tier == BookTier.Epic ? 0.7f : 0.6f;

    /// <summary>Pays and starts a repair. bonus = 0..1 minigame score. byHand: the librarian's own repair on the
    /// repair screen; only those earn reputation when they come out perfect.</summary>
    public bool StartRepair(Job job, float minigameScore, bool byHand = false)
    {
        int cost = Cost(job);
        if (!Waiting.Contains(job) || GameManager.Instance == null || !GameManager.Instance.TrySpend(cost, "Repairs")) return false;
        RequestLog.Money("Repair desk", $"Repaired \"{job.book.Title}\"", "Repair", -cost);
        Waiting.Remove(job);
        job.bonus = minigameBonus * Mathf.Clamp01(minigameScore);
        job.duration = repairSeconds[Tier(job.book)] / Mathf.Max(0.1f, speedMultiplier);
        job.started = true;
        job.byHand = byHand;
        Working.Add(job);
        Changed?.Invoke();
        return true;
    }

    public int RepairAllCost()
    {
        int total = 0;
        foreach (var j in Waiting) total += Cost(j);
        return total;
    }

    public void RepairAll()
    {
        if (GameManager.Instance == null || GameManager.Instance.Coins < RepairAllCost()) return;
        foreach (var j in Waiting.ToArray()) StartRepair(j, 0f);
    }

    void Update()
    {
        if (Working.Count == 0) return;
        var job = Working[0]; // one book on the table at a time
        // The clock is paused while menus are open, but you should see the repair you're watching move.
        job.elapsed += RepairUI.Instance != null && RepairUI.Instance.IsOpen ? Time.deltaTime : GameManager.Dt;
        if (job.elapsed < job.duration) return;

        Working.RemoveAt(0);
        bool perfect = UnityEngine.Random.value < Chance(job);
        // Always at least one step better: Worn -> Good; Damaged -> Good (perfect) or Worn (normal).
        var fixedTo = perfect || job.condition == BookCondition.Worn ? BookCondition.Good : BookCondition.Worn;
        Done.Add((job.book, fixedTo));
        var gm = GameManager.Instance;
        if (gm != null)
        {
            if (perfect) gm.Today.repairsPerfect++; else gm.Today.repairsNormal++;
            gm.NotifyBooksChanged();
            if (perfect)
            {
                // A perfect job gets noticed: a small donation from the Friends of the Whitmore. The word only gets
                // round (+1 reputation) for your own careful work, not Repair all or the archivist's.
                gm.AddCoins(PerfectBonus);
                gm.Today.Earn(GameManager.DayStats.Source.Other, PerfectBonus);
                RequestLog.Money("Friends of the Whitmore", $"Perfect repair of \"{job.book.Title}\"", "Donation", PerfectBonus);
                if (job.byHand) gm.AddReputation(1);
                HUD.SkillPop(transform.position + Vector3.up * 1.6f, job.byHand ? $"Perfect repair! +{PerfectBonus} coins, +1 reputation" : $"Perfect repair! +{PerfectBonus} coins");
            }
        }
        if (doneSound != null) AudioSource.PlayClipAtPoint(doneSound, transform.position, 0.8f);
        Bubble(fixedTo == BookCondition.Good ? $"\"{job.book.Title}\" is good as new! It's ready on the table." : $"\"{job.book.Title}\" is patched up (worn). It's ready on the table.");
        Finished?.Invoke(job, perfect);
        Changed?.Invoke();
    }

    /// <summary>Coins for a perfect repair (as good as new from damaged, or a lucky roll).</summary>
    const int PerfectBonus = 5;

    void Bubble(string text)
    {
        if (WorldPrompts.Instance != null) WorldPrompts.Instance.ShowBubble(this, text, 3f);
    }
}
