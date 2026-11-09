using UnityEngine;

[DefaultExecutionOrder(10)] // run after the AI so it uses this frame's decision
[RequireComponent(typeof(CharacterController))]
public class EnemyMotor : MonoBehaviour
{
    [SerializeField] float turnSpeed = 10f;
    [SerializeField] float knockbackDrag = 8f; // higher = knockback dies out faster

    CharacterController cc;
    Vector3 desiredVelocity;
    Vector3 knockbackVelocity;

    public bool IsKnockedBack => knockbackVelocity.sqrMagnitude > 0.25f;

    void Awake() => cc = GetComponent<CharacterController>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    public void SetVelocity(Vector3 velocity) => desiredVelocity = velocity;
    public void Stop() => desiredVelocity = Vector3.zero;

    // Replaces any knockback already in progress
    public void AddKnockback(Vector3 impulse) => knockbackVelocity = impulse;

    // Turn left/right only, so the enemy doesn't tilt up and down while flying
    public void Face(Vector3 direction)
    {
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude <= 0.001f) return;

        Quaternion look = Quaternion.LookRotation(flat);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
    }

    // Update is called once per frame
    void Update()
    {
        // Frozen during a rotation or mode switch (knockback decay pauses too)
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning || DungeonMapSystem.IsEditing) return;

        knockbackVelocity *= Mathf.Exp(-knockbackDrag * Time.deltaTime);
        cc.Move((desiredVelocity + knockbackVelocity) * Time.deltaTime);
    }
}
