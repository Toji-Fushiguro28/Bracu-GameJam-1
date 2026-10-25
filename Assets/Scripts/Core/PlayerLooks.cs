using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The librarian's body, picked on the intro's "Who are you?" page (Resources/PlayerLooks). Every body shares the
/// librarian's skeleton, so picking one only swaps the mesh on the librarian's SkinnedMeshRenderer: animations, hands,
/// carrying and collisions stay as they are. Saved in PlayerPrefs ("PlayerLook") next to the name; old saves get the
/// first body. Applied to the Player in every scene, and to the intro's librarian by StaffInterview.
/// </summary>
[CreateAssetMenu(menuName = "Whitmore/Player Looks")]
public class PlayerLooks : ScriptableObject
{
    [Serializable]
    public class Look
    {
        public string name;
        [Tooltip("A character prefab on the shared rig; only its skinned mesh is used.")]
        public GameObject body;
    }

    public List<Look> looks = new List<Look>();
    [Tooltip("A plain rigged character (no player scripts) the preview page films; it wears the chosen body.")]
    public GameObject previewRig;

    const string Key = "PlayerLook";
    static PlayerLooks data;

    public static PlayerLooks Data => data != null ? data : (data = Resources.Load<PlayerLooks>("PlayerLooks"));
    public static int Count => Data != null ? Data.looks.Count : 0;
    public static int Current => Count == 0 ? 0 : Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, Count - 1);

    public static void Save(int index)
    {
        PlayerPrefs.SetInt(Key, index);
        PlayerPrefs.Save();
    }

    static SkinnedMeshRenderer BodyOf(int index)
    {
        if (Count == 0) return null;
        var body = Data.looks[Mathf.Clamp(index, 0, Count - 1)].body;
        return body != null ? body.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
    }

    /// <summary>Puts body <paramref name="index"/> on this character (the player, the intro's librarian or the preview).</summary>
    public static void Apply(GameObject character, int index)
    {
        if (character == null) return;
        var src = BodyOf(index);
        var dst = character.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (src == null || dst == null || src.sharedMesh == null) return;
        if (src.sharedMesh.bindposes.Length != dst.bones.Length) return; // not on the shared skeleton: leave it alone
        dst.sharedMesh = src.sharedMesh;
        dst.localBounds = src.localBounds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Apply(GameObject.FindWithTag("Player"), Current);
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode) => Apply(GameObject.FindWithTag("Player"), Current);
}
