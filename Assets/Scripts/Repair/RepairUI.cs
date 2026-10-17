using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Repair desk screen (open book). Left page: queue with Repair / Repair All.
/// Right page: either the work in progress, or the 3-step repair minigame:
///   1. Glue the spine  - hold, release in the green zone
///   2. Press the pages - click when the marker is centred
///   3. Fit the cover   - click when the sliding cover lines up
/// The average score adds to the Perfect Restoration chance.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class RepairUI : MonoBehaviour
{
    public static RepairUI Instance { get; private set; }

    public AudioClip stepGood, stepBad;

    VisualElement root, list, workList, statusPage, gamePage, zone, marker, fillBar;
    Label totalLabel, stepTitle, stepHint, stepFeedback, resultLabel;
    Button repairAll, action, skip;
    InputAction cancelAction;
    AudioSource sfx;

    RepairDesk desk;
    RepairDesk.Job gameJob;
    int step;               // 0 glue, 1 press, 2 cover
    float score, value, dir = 1f;
    bool holding, stepDone;
    float nextStepAt;

    static readonly string[] Titles = { "1. Glue the spine", "2. Press the pages", "3. Fit the new cover" };
    static readonly string[] Hints =
    {
        "Hold the button to spread glue. Let go in the green zone.",
        "Click when the press is right in the middle.",
        "Click when the new cover lines up with the book.",
    };

    public bool IsOpen => desk != null;

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        cancelAction = InputSystem.actions?.FindAction("UI/Cancel");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerLock.Unlock(this);
    }

    void OnEnable()
    {
        cancelAction?.Enable();
        var doc = GetComponent<UIDocument>().rootVisualElement;
        root = doc.Q("repair");
        list = doc.Q("repair-list");
        workList = doc.Q("repair-work");
        statusPage = doc.Q("repair-status");
        gamePage = doc.Q("repair-game");
        zone = doc.Q("rg-zone");
        marker = doc.Q("rg-marker");
        fillBar = doc.Q("rg-fill");
        totalLabel = doc.Q<Label>("repair-total");
        resultLabel = doc.Q<Label>("repair-result");
        stepTitle = doc.Q<Label>("rg-title");
        stepHint = doc.Q<Label>("rg-hint");
        stepFeedback = doc.Q<Label>("rg-feedback");
        repairAll = doc.Q<Button>("repair-all");
        action = doc.Q<Button>("rg-action");
        skip = doc.Q<Button>("rg-skip");

        doc.Q<Button>("repair-close")?.RegisterCallback<ClickEvent>(_ => Close());
        repairAll?.RegisterCallback<ClickEvent>(_ => { desk?.RepairAll(); Refresh(); });
        skip?.RegisterCallback<ClickEvent>(_ => FinishGame(0f));
        // Hold-to-glue needs press and release, so listen before the button eats the events.
        action?.RegisterCallback<PointerDownEvent>(_ => OnActionDown(), TrickleDown.TrickleDown);
        action?.RegisterCallback<PointerUpEvent>(_ => OnActionUp(), TrickleDown.TrickleDown);
        root?.AddToClassList("hidden");
    }

    public void Open(RepairDesk target)
    {
        if (target == null || root == null) return;
        if (desk != target)
        {
            if (desk != null) { desk.Changed -= Refresh; desk.Finished -= OnFinished; }
            desk = target;
            desk.Changed += Refresh;
            desk.Finished += OnFinished;
        }
        PlayerLock.Lock(this);
        if (root.ClassListContains("hidden")) GameAudio.Play(a => a.panelOpen, 0.6f);
        root.RemoveFromClassList("hidden");
        resultLabel.text = "";
        ShowStatus();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        gameJob = null;
        GameAudio.Play(a => a.panelClose, 0.6f);
        root.AddToClassList("hidden");
        desk.Changed -= Refresh;
        desk.Finished -= OnFinished;
        desk = null;
        PlayerLock.Unlock(this);
    }

    void OnFinished(RepairDesk.Job job, bool perfect)
    {
        resultLabel.text = perfect ? $"Perfect Restoration!\n\"{job.book.Title}\" is good as new."
                                   : $"Normal Repair.\n\"{job.book.Title}\" is usable, but worn.";
        resultLabel.EnableInClassList("perfect", perfect);
    }

    // ---------------- Queue & status ----------------

    void Refresh()
    {
        if (!IsOpen) return;
        list.Clear();
        if (desk.Waiting.Count == 0)
            list.Add(new Label("Nothing waiting.\nBring damaged or worn copies here.") { name = "row-sub" });
        foreach (var j in desk.Waiting)
        {
            var job = j;
            var row = new VisualElement();
            row.AddToClassList("shelf-row");
            var text = new VisualElement();
            text.AddToClassList("shelf-row-text");
            text.Add(new Label(job.book.Title) { name = "row-title" });
            text.Add(new Label($"{job.book.Tier}  ·  {job.condition}  ·  worth {job.book.BaseValue}  ·  cost {desk.Cost(job)}  ·  {Mathf.RoundToInt(desk.Chance(job) * 100)}% perfect") { name = "row-sub" });
            row.Add(text);
            var b = new Button(() => StartGame(job)) { text = "Repair" };
            b.AddToClassList("kit-button");
            b.SetEnabled(gameJob == null && GameManager.Instance != null && GameManager.Instance.Coins >= desk.Cost(job));
            row.Add(b);
            list.Add(row);
        }

        int total = desk.RepairAllCost();
        totalLabel.text = desk.Waiting.Count > 0 ? $"Repair all: {total} coins" : "";
        repairAll.SetEnabled(gameJob == null && desk.Waiting.Count > 0 && GameManager.Instance != null && GameManager.Instance.Coins >= total);

        workList.Clear();
        if (desk.Working.Count == 0) workList.Add(new Label("The table is free.") { name = "row-sub" });
        for (int i = 0; i < desk.Working.Count; i++)
        {
            var job = desk.Working[i];
            var item = new VisualElement();
            item.AddToClassList("work-item");
            item.Add(new Label(i == 0 ? job.book.Title : $"{job.book.Title} (next)") { name = "row-title" });
            var bar = new VisualElement();
            bar.AddToClassList("patience-bar");
            bar.AddToClassList("work-bar");
            var f = new VisualElement { name = "work-fill" };
            f.AddToClassList("patience-fill");
            f.style.width = Length.Percent(job.Progress * 100f);
            bar.Add(f);
            item.Add(bar);
            workList.Add(item);
        }
    }

    void ShowStatus()
    {
        statusPage.RemoveFromClassList("hidden");
        gamePage.AddToClassList("hidden");
    }

    void Update()
    {
        if (!IsOpen) return;
        if (cancelAction != null && cancelAction.WasPressedThisFrame()) { Close(); return; }

        // Live progress bars
        int i = 0;
        foreach (var item in workList.Query(className: "work-item").ToList())
        {
            if (i < desk.Working.Count) item.Q("work-fill").style.width = Length.Percent(desk.Working[i].Progress * 100f);
            i++;
        }

        if (gameJob != null) UpdateGame();
    }

    // ---------------- Minigame ----------------

    void StartGame(RepairDesk.Job job)
    {
        gameJob = job;
        step = 0;
        score = 0f;
        statusPage.AddToClassList("hidden");
        gamePage.RemoveFromClassList("hidden");
        BeginStep();
        Refresh();
    }

    void BeginStep()
    {
        stepTitle.text = Titles[step];
        stepHint.text = Hints[step];
        stepFeedback.text = "";
        stepDone = false;
        holding = false;
        value = step == 0 ? 0f : Random.value;
        dir = 1f;
        action.text = step == 0 ? "Hold to glue" : step == 1 ? "Press!" : "Place cover";
        fillBar.EnableInClassList("hidden", step != 0);
        marker.EnableInClassList("hidden", step == 0);
        // Green zone: glue 60-85%, press centre 40-60%, cover centre 44-56% (harder)
        float from = step == 0 ? 0.60f : step == 1 ? 0.40f : 0.44f;
        float to = step == 0 ? 0.85f : step == 1 ? 0.60f : 0.56f;
        zone.style.left = Length.Percent(from * 100f);
        zone.style.width = Length.Percent((to - from) * 100f);
    }

    void UpdateGame()
    {
        if (stepDone)
        {
            if (Time.time >= nextStepAt)
            {
                if (++step >= 3) FinishGame(score / 3f);
                else BeginStep();
            }
            return;
        }

        if (step == 0)
        {
            if (holding) value += 0.55f * Time.deltaTime;
            if (value >= 1f) { value = 1f; ScoreStep(0f, "Too much glue!"); }
            fillBar.style.width = Length.Percent(value * 100f);
        }
        else
        {
            float speed = step == 1 ? 1.1f : 1.6f;
            value += dir * speed * Time.deltaTime;
            if (value > 1f) { value = 1f; dir = -1f; }
            if (value < 0f) { value = 0f; dir = 1f; }
            marker.style.left = Length.Percent(value * 100f);
        }
    }

    void OnActionDown()
    {
        if (gameJob == null || stepDone) return;
        if (step == 0) { holding = true; return; }
        // Timing steps: how close to the middle?
        float s = Mathf.Clamp01(1f - Mathf.Abs(value - 0.5f) * (step == 1 ? 2.5f : 3.2f));
        ScoreStep(s, null);
    }

    void OnActionUp()
    {
        if (gameJob == null || stepDone || step != 0 || !holding) return;
        holding = false;
        float s = Mathf.Clamp01(1f - Mathf.Abs(value - 0.725f) / 0.35f);
        ScoreStep(s, null);
    }

    void ScoreStep(float s, string message)
    {
        stepDone = true;
        holding = false;
        score += s;
        stepFeedback.text = message ?? (s > 0.85f ? "Perfect!" : s > 0.55f ? "Good" : s > 0.25f ? "Okay..." : "Oops!");
        var clip = s > 0.55f ? stepGood : stepBad;
        if (clip != null) sfx.PlayOneShot(clip, 0.7f);
        nextStepAt = Time.time + 0.8f;
    }

    void FinishGame(float finalScore)
    {
        var job = gameJob;
        gameJob = null;
        ShowStatus();
        if (job != null && desk.StartRepair(job, finalScore))
            resultLabel.text = finalScore > 0f ? $"Nice work! +{Mathf.RoundToInt(finalScore * desk.minigameBonus * 100)}% Perfect chance." : "Repair started.";
        else if (job != null)
            resultLabel.text = "Not enough coins for that repair.";
        resultLabel.RemoveFromClassList("perfect");
        Refresh();
    }
}
