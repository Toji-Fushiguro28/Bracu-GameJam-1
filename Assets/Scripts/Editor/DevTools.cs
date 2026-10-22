using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Dev Tools (in Play mode): buttons to jump the clock, start a day's twist, trigger its moments,
/// bring in a visitor, give coins or reputation, and play every event sound. Editor only: never in a build.
/// Nothing here saves; the game only saves when the next day starts.
/// </summary>
public class DevTools : EditorWindow
{
    [MenuItem("Tools/Dev Tools")]
    static void Open() => GetWindow<DevTools>("Dev Tools");

    Vector2 scroll;

    void OnInspectorUpdate() => Repaint(); // keep the clock line live

    void OnGUI()
    {
        var gm = GameManager.Instance;
        if (!EditorApplication.isPlaying || gm == null)
        {
            EditorGUILayout.HelpBox("Enter Play mode (and start the day) to use these.", MessageType.Info);
            return;
        }
        var story = DayStory.Instance;
        scroll = EditorGUILayout.BeginScrollView(scroll);

        string twists = story != null && story.Today.Count > 0 ? string.Join(" + ", story.Today) : "none";
        EditorGUILayout.HelpBox($"Day {gm.day}  ·  {GameManager.FormatHour(gm.Hour)}  ·  {gm.CurrentPhase}  ·  Twists: {twists}\n" +
                                "Coins and reputation stick if you play on into the next day (that's when it saves).", MessageType.None);

        Header("Clock");
        Row(("10:00", () => gm.DevSetHour(10f)), ("11:00", () => gm.DevSetHour(11f)), ("12:00", () => gm.DevSetHour(12f)), ("1:00", () => gm.DevSetHour(13f)));
        Row(("2:00", () => gm.DevSetHour(14f)), ("3:30", () => gm.DevSetHour(15.5f)), ("+30 min", () => gm.DevSetHour(gm.Hour + 0.5f)), ("Closing", () => gm.DevSetHour(gm.closingHour)));

        if (story != null)
        {
            Header("Start a twist today (its moments follow the clock)");
            var all = (DayStory.Twist[])Enum.GetValues(typeof(DayStory.Twist));
            for (int i = 0; i < all.Length; i += 3)
                Row(all.Skip(i).Take(3).Select(t => ((string)t.ToString(), (Action)(() => story.DevStartTwist(t)))).ToArray());

            Header("Right now");
            Row(("Leak now", story.DevLeakNow), ("Thief now", story.DevThiefNow), ("Visitor now", () => CustomerSpawner.Instance?.DevSpawnNow()));
            Row(("Power off", () => story.DevPower(false)), ("Power on", () => story.DevPower(true)));
        }

        var jobs = SpecialJobs.Instance;
        if (jobs != null)
        {
            Header("VIP (needs Rare Books open: Day 3 on)");
            if (!gm.RareOpen) EditorGUILayout.HelpBox("Rare Books isn't open on this day, so no VIP will come.", MessageType.Warning);
            Row(("VIP now (book on shelf)", () => jobs.DevVipNow(false)), ("VIP now (rare dealer)", () => jobs.DevVipNow(true)));
            Row(("VIP comes back now", jobs.DevVipBackNow));
            Header("Second-week jobs (Days 6-9)");
            Row(("Collector now", () => jobs.DevJobNow(0)), ("Reading list now", () => jobs.DevJobNow(1)));
            Row(("Rare loan now", () => jobs.DevJobNow(2)), ("Restoration now", () => jobs.DevJobNow(3)));
            Row(("Job deadline now", jobs.DevJobDueNow));
        }

        Header("Money and reputation");
        Row(("+100 coins", () => gm.AddCoins(100)), ("+5 reputation", () => gm.AddReputation(5)), ("-5 reputation", () => gm.AddReputation(-5)));

        Header("Event sounds");
        var sfx = typeof(GameAudio).GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(GameAudio.Sfx)).ToArray();
        for (int i = 0; i < sfx.Length; i += 3)
            Row(sfx.Skip(i).Take(3).Select(f => ((string)ObjectNames.NicifyVariableName(f.Name), (Action)(() => PlaySfx(f)))).ToArray());
        Row(("Stop loops", () => { GameAudio.Loop(a => a.rainLoop, false); GameAudio.Loop(a => a.candleLoop, false); }));

        EditorGUILayout.EndScrollView();
    }

    /// <summary>Loops (rain, candles) start looping; the rest play once.</summary>
    static void PlaySfx(FieldInfo f)
    {
        if (f.Name.EndsWith("Loop")) GameAudio.Loop(a => (GameAudio.Sfx)f.GetValue(a), true);
        else GameAudio.Play(a => (GameAudio.Sfx)f.GetValue(a));
    }

    static void Header(string text)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
    }

    static void Row(params (string label, Action run)[] buttons)
    {
        EditorGUILayout.BeginHorizontal();
        foreach (var (label, run) in buttons)
            if (GUILayout.Button(label)) run();
        EditorGUILayout.EndHorizontal();
    }
}
