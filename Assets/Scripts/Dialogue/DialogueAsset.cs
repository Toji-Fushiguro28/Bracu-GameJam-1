using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A conversation written in the Inspector. Lines play top to bottom.
/// A line can jump with "next" (an id, or "end"), or offer choices that each jump to an id.
/// </summary>
[CreateAssetMenu(menuName = "Library/Dialogue", fileName = "DLG_New")]
public class DialogueAsset : ScriptableObject
{
    public const string End = "end";

    public string speaker = "???";
    [Tooltip("Pitch of this character's voice blips.")]
    [Range(0.5f, 2f)] public float voicePitch = 1f;
    public List<Line> lines = new List<Line>();

    [Serializable]
    public class Line
    {
        [Tooltip("Optional. Lets choices or 'next' jump here.")]
        public string id;
        [Tooltip("Empty = the dialogue's speaker.")]
        public string speaker;
        [TextArea(2, 4)] public string text;
        [Tooltip("Empty = go to the next line. 'end' = finish. Otherwise the id to jump to.")]
        public string next;
        public List<Choice> choices = new List<Choice>();
    }

    [Serializable]
    public class Choice
    {
        public string text;
        [Tooltip("Id of the line to jump to, or 'end'.")]
        public string next;
    }

    /// <summary>Index of the line after <paramref name="from"/> following a jump target; -1 ends.</summary>
    public int Resolve(int from, string target)
    {
        if (string.IsNullOrEmpty(target)) return from + 1 < lines.Count ? from + 1 : -1;
        if (target == End) return -1;
        int i = lines.FindIndex(l => l.id == target);
        if (i < 0) Debug.LogWarning($"[Dialogue] '{name}' has no line with id '{target}'.", this);
        return i;
    }
}
