using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the running game: coins, the live book catalogue and the in-game clock.
/// One per gameplay scene. Runs early so the catalogue exists before any UI asks for it.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum Phase { Open, Closing, Closed }

    [Header("Economy")]
    public int startingCoins = 50;

    [Header("Day")]
    public int day = 1;
    public float openHour = 9f;
    [Tooltip("Bell rings: no new customers, everyone finishes up.")]
    public float closingHour = 17.5f;
    public float closeHour = 18f;
    [Tooltip("Real minutes a full day (open to close) takes.")]
    public float realMinutesPerDay = 6f;
    public AudioClip closingBell;

    /// <summary>Running totals for the day report.</summary>
    [Serializable]
    public class DayStats
    {
        public int served, angry, booksSold, booksRead, returns, income, tips;
    }
    public DayStats Today { get; private set; } = new DayStats();
    public int Reputation { get; private set; }
    public void AddReputation(int amount) => Reputation = Mathf.Max(0, Reputation + amount);

    public int Coins { get; private set; }
    public float Hour { get; private set; }
    public Phase CurrentPhase { get; private set; }
    public List<BookData> Books { get; private set; } = new List<BookData>();

    public event Action<int, int> CoinsChanged;   // new total, delta
    public event Action<Phase> PhaseChanged;
    public event Action BooksChanged;

    void Awake()
    {
        Instance = this;
        var db = Resources.Load<BookDatabase>(BookDatabase.ResourceName);
        if (db != null) Books = db.CreateRuntimeCopy();
        else Debug.LogWarning("[Game] No BookDatabase in Resources — library is empty.", this);
        Coins = startingCoins;
        Hour = openHour;
        CurrentPhase = Phase.Open;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (CurrentPhase == Phase.Closed) return;
        float hoursPerSecond = (closeHour - openHour) / Mathf.Max(0.1f, realMinutesPerDay * 60f);
        Hour = Mathf.Min(closeHour, Hour + hoursPerSecond * Time.deltaTime);

        if (CurrentPhase == Phase.Open && Hour >= closingHour) SetPhase(Phase.Closing);
        else if (CurrentPhase == Phase.Closing && Hour >= closeHour) SetPhase(Phase.Closed);
    }

    void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        if (phase == Phase.Closing && closingBell != null)
            AudioSource.PlayClipAtPoint(closingBell, Camera.main ? Camera.main.transform.position : Vector3.zero, 0.8f);
        PhaseChanged?.Invoke(phase);
    }

    /// <summary>"09:30" style clock text.</summary>
    public string ClockText
    {
        get
        {
            int h = Mathf.FloorToInt(Hour), m = Mathf.FloorToInt((Hour - h) * 60f / 10f) * 10;
            return $"{h:00}:{m:00}";
        }
    }

    public void AddCoins(int amount)
    {
        if (amount == 0) return;
        Coins += amount;
        CoinsChanged?.Invoke(Coins, amount);
    }

    public bool TrySpend(int amount)
    {
        if (amount > Coins) return false;
        AddCoins(-amount);
        return true;
    }

    /// <summary>Call after changing any book's stock or condition so UIs refresh.</summary>
    public void NotifyBooksChanged() => BooksChanged?.Invoke();
}
