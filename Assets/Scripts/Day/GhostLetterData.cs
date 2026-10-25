using UnityEngine;
using UnityEngine.UIElements;

/// <summary>The night letter's assets (Resources/GhostLetter): who comes for the book, the ghost who helps after,
/// the book on the empty shelf, and the letter's styles.</summary>
[CreateAssetMenu(menuName = "Whitmore/Ghost Letter")]
public class GhostLetterData : ScriptableObject
{
    [Tooltip("Elias Harrow, the stranger who comes for Ada's book (PF_NPC_Mysterious_Visitor).")]
    public GameObject stranger;
    [Tooltip("Ada Whitmore's ghost: the one who shelves the returns at night once she has her book back.")]
    public GameObject adaBody;
    [Tooltip("The book that stands alone on the Whitmore shelf (SM_Book_Folio).")]
    public GameObject folio;
    [Tooltip("Styles for the letter page, the lock and the typewriter (GameUI, PC for the wheels, GhostLetter).")]
    public StyleSheet[] styles;
}
