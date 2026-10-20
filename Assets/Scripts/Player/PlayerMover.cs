using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// DRAFT: camera-relative top-down movement for the librarian.
/// Reads Move/Sprint from the project-wide input actions and drives CharacterAnimator.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMover : MonoBehaviour
{
    public float walkSpeed = 2.2f;
    public float runSpeed = 4f;
    public float turnSpeed = 720f;
    public float acceleration = 12f;
    public float gravity = -20f;

    [Header("Stamina (running)")]
    [Tooltip("Seconds of running on a full meter (the Stamina upgrade adds more).")]
    public float staminaSeconds = 3f;
    [Tooltip("Meter refilled per second once you stop running.")]
    public float staminaRecovery = 0.5f;
    [Tooltip("Wait after running before the meter starts to refill.")]
    public float recoveryDelay = 0.6f;

    CharacterController controller;
    CharacterAnimator anim;
    Transform cam;
    InputAction moveAction, sprintAction;
    Vector3 velocity;
    float verticalSpeed;
    float stamina = 1f, recoverIn;
    bool exhausted;

    /// <summary>0..1 meter. Running drains it; empty means a cooldown until it's full again.</summary>
    public float Stamina01 => enabled || disabledAt < 0f ? stamina : Rested(Time.time - disabledAt).stamina; // still filling while you talk
    public bool Exhausted => exhausted;
    public bool Running { get; private set; }

    float MaxSeconds => staminaSeconds + (GameManager.Instance != null ? 1.5f * GameManager.Instance.Upgrade(GameManager.UpgradeKind.Stamina) : 0f);
    float Recovery => staminaRecovery * (1f + (GameManager.Instance != null ? 0.25f * GameManager.Instance.Upgrade(GameManager.UpgradeKind.Stamina) : 0f));

    /// <summary>Current horizontal speed in m/s (used by footsteps).</summary>
    public float Speed => new Vector3(velocity.x, 0, velocity.z).magnitude;
    public bool IsGrounded => controller.isGrounded;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        anim = GetComponent<CharacterAnimator>();
        cam = Camera.main ? Camera.main.transform : null;
        moveAction = InputSystem.actions?.FindAction("Move");
        sprintAction = InputSystem.actions?.FindAction("Sprint");
    }

    void OnEnable()
    {
        moveAction?.Enable(); sprintAction?.Enable();
        // Back from a conversation (or any screen): the breath you got back while standing there.
        if (disabledAt >= 0f) { (stamina, recoverIn, exhausted) = Rested(Time.time - disabledAt); disabledAt = -1f; }
    }

    // PlayerLock switches this script off while you talk or use a screen; stamina keeps recovering meanwhile.
    float disabledAt = -1f;

    /// <summary>After running out of breath, you can run again once the meter is back to half.</summary>
    const float RunAgainAt = 0.5f;
    void OnDisable() => disabledAt = Time.time;

    /// <summary>Stamina after standing still for this long (the recovery delay first, then refilling).</summary>
    (float stamina, float recoverIn, bool exhausted) Rested(float seconds)
    {
        float s = stamina, wait = recoverIn;
        float refill = seconds - Mathf.Max(0f, wait);
        wait -= seconds;
        if (refill > 0f) s = Mathf.Min(1f, s + Recovery * refill);
        return (s, wait, exhausted && s < RunAgainAt);
    }

    void UpdateStamina(bool sprinting)
    {
        if (sprinting)
        {
            stamina -= Time.deltaTime / Mathf.Max(0.5f, MaxSeconds);
            recoverIn = recoveryDelay;
            if (stamina <= 0f) { stamina = 0f; exhausted = true; } // out of breath: walk until it's half back
            return;
        }
        recoverIn -= Time.deltaTime;
        if (recoverIn > 0f) return;
        stamina = Mathf.Min(1f, stamina + Recovery * Time.deltaTime);
        if (exhausted && stamina >= RunAgainAt) exhausted = false;
    }

    void Update()
    {
        Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        bool sprint = sprintAction != null && sprintAction.IsPressed() && !exhausted && input.sqrMagnitude > 0.01f;
        UpdateStamina(sprint);
        Running = sprint;

        // Camera-relative on the ground plane, so "up" moves away from the camera.
        Vector3 fwd = cam ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 right = cam ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
        Vector3 wish = Vector3.ClampMagnitude(fwd * input.y + right * input.x, 1f);

        float targetSpeed = sprint ? runSpeed : walkSpeed;
        Vector3 targetVel = wish * targetSpeed;
        velocity = Vector3.MoveTowards(velocity, targetVel, acceleration * Time.deltaTime);

        if (wish.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(wish, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        verticalSpeed = controller.isGrounded ? -1f : verticalSpeed + gravity * Time.deltaTime;
        controller.Move((velocity + Vector3.up * verticalSpeed) * Time.deltaTime);

        // 0 = idle, 0.5 = walk, 1 = run (see CharacterAnimator)
        float normalized = Speed <= walkSpeed ? Speed / walkSpeed * 0.5f : 0.5f + (Speed - walkSpeed) / (runSpeed - walkSpeed) * 0.5f;
        if (anim) anim.SetSpeed(normalized);
    }
}
