using UnityEngine;

/// <summary>
/// Click an NPC to talk. Both characters turn to face each other and the NPC plays Talk
/// for the length of the conversation.
/// </summary>
public class NPCInteractable : ClickInteractable
{
    public DialogueAsset dialogue;
    public float turnSpeed = 540f;

    CharacterAnimator anim;
    Quaternion? faceTarget;
    Vector3 restPosition;
    Quaternion restRotation;

    protected override string DefaultVerb => "Talk";

    protected override void Awake()
    {
        base.Awake();
        anim = GetComponent<CharacterAnimator>();
        restPosition = transform.position;
        restRotation = transform.rotation;
    }

    public override void Interact(GameObject player)
    {
        if (dialogue == null || DialogueUI.Instance == null || DialogueUI.Instance.IsOpen) return;

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            faceTarget = Quaternion.LookRotation(toPlayer);
            player.transform.rotation = Quaternion.LookRotation(-toPlayer);
        }

        if (anim) anim.SetActivity(CharacterAnimator.Activity.Talk);
        DialogueUI.Instance.Play(dialogue, () =>
        {
            if (anim) anim.SetActivity(CharacterAnimator.Activity.None);
            // Back to where they were standing, facing the way they were placed.
            transform.position = restPosition;
            faceTarget = restRotation;
        });
    }

    void Update()
    {
        if (faceTarget == null) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, faceTarget.Value, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, faceTarget.Value) < 0.5f) faceTarget = null;
    }
}
