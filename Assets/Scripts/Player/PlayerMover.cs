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

    CharacterController controller;
    CharacterAnimator anim;
    Transform cam;
    InputAction moveAction, sprintAction;
    Vector3 velocity;
    float verticalSpeed;

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

    void OnEnable() { moveAction?.Enable(); sprintAction?.Enable(); }

    void Update()
    {
        Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        bool sprint = sprintAction != null && sprintAction.IsPressed();

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
