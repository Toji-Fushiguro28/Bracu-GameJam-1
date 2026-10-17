using UnityEngine;

/// <summary>
/// Thin driver for AC_Character. Same controller for the player and every NPC.
/// Speed is normalized: 0 = idle, 0.5 = walk, 1 = run.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimator : MonoBehaviour
{
    public enum Activity { None = 0, SitIdle, SitRead, Type, Talk, Wave, Impatient }

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int CarryingId = Animator.StringToHash("Carrying");
    static readonly int ActivityId = Animator.StringToHash("Activity");
    static readonly int StampId = Animator.StringToHash("Stamp");
    static readonly int ReachShelfId = Animator.StringToHash("ReachShelf");

    Animator anim;

    void Awake() => anim = GetComponent<Animator>();

    public void SetSpeed(float normalized) => anim.SetFloat(SpeedId, normalized, 0.08f, Time.deltaTime);
    public void SetCarrying(bool carrying) => anim.SetBool(CarryingId, carrying);
    public void SetActivity(Activity activity) => anim.SetInteger(ActivityId, (int)activity);
    public void PlayStamp()
    {
        anim.ResetTrigger(StampId);
        anim.CrossFadeInFixedTime("Stamp", 0.08f, 0, 0f);
    }
    public void PlayReachShelf() => anim.SetTrigger(ReachShelfId);
}
