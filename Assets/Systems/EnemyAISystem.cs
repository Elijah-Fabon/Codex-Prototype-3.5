using UnityEngine;

[RequireComponent(typeof(EnemyMotor))]
[RequireComponent(typeof(DamageSystem))]
public class EnemyAISystem : MonoBehaviour
{
    enum State { Idle, Chase, Attack, Stagger, Hide }
    enum HideMode { Pause, Wander, Follow }

    [Header("Target")]
    [SerializeField] Transform target;               // the player
    [SerializeField] float aimHeight = 1f;           // aim at the body, not the feet
    [SerializeField] bool ignoreDepthInSidescroller = true;

    [Header("Chase")]
    [SerializeField] float detectRange = 10f;
    [SerializeField] float stopDistance = 0.8f;
    [SerializeField] float speed = 3f;

    [Header("Attack")]
    [SerializeField] float attackRange = 1.2f;
    [SerializeField] float attackWindup = 0.3f;      // telegraph before the hit lands
    [SerializeField] float attackRecovery = 0.5f;    // pause afterwards (also the cooldown)
    [SerializeField] int attackDamage = 1;

    [Header("Knockback")]
    [SerializeField] float knockbackForce = 10f;
    [SerializeField] float staggerTime = 0.3f;

    [Header("Hiding (a door's demon lock reads IsHiding)")]
    [SerializeField] bool hiding;                    // starts hidden: invisible, wanders, never attacks, until revealed
    [SerializeField] bool revealWhenHit = true;      // taking damage ends the hiding
    [SerializeField] float wanderRadius = 3f;        // how far from where it started it wanders
    [SerializeField] float wanderSpeed = 1.5f;
    [SerializeField] Vector2 pauseTime = new Vector2(1f, 3f);   // random rest between moves
    [SerializeField] float followChance = 0.3f;      // chance, after each rest, to loosely trail the player instead of wandering
    [SerializeField] float followTime = 3f;          // how long a trail lasts
    [SerializeField] float followDistance = 4f;      // it keeps at least this far from the player while trailing

    EnemyMotor motor;
    DamageSystem health;

    State state = State.Idle;
    float stateTimer;
    bool attackStruck;

    HideMode hideMode = HideMode.Pause;
    float hideModeTimer;
    Vector3 homePosition;
    Vector3 wanderTarget;
    Renderer[] renderers;
    DungeonRoom room;

    bool sidescroller;
    Vector3 sideForward = Vector3.forward;

    // The door checks this: while true the demon is hiding its door
    public bool IsHiding => hiding;

    void Awake()
    {
        motor = GetComponent<EnemyMotor>();
        health = GetComponent<DamageSystem>();
        homePosition = transform.position;
        renderers = GetComponentsInChildren<Renderer>(true);
        room = GetComponentInParent<DungeonRoom>();

        if (target == null)
            target = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (hiding) EnterHide();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    void OnEnable()
    {
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        OnPerspectiveChanged(PerspectiveShiftSystem.CurrentSidescroller,
                             PerspectiveShiftSystem.CurrentViewForward);
        health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;
        health.Damaged -= OnDamaged;
    }

    void OnPerspectiveChanged(bool sidescrollerMode, Vector3 viewForward)
    {
        sidescroller = sidescrollerMode;
        sideForward = viewForward.normalized;
    }

    void SetState(State next)
    {
        state = next;
        stateTimer = 0f;
        attackStruck = false;
    }

    // Hide again, or reveal (the door shows up when it is revealed). Revealing goes straight to chasing.
    public void SetHiding(bool value)
    {
        if (value == hiding) return;
        hiding = value;

        if (value)
        {
            hiding = true;
            EnterHide();
        }
        else
        {
            Reveal();
            if (state == State.Hide) SetState(State.Chase);
        }
    }

    void EnterHide()
    {
        SetState(State.Hide);
        EnterPause();
    }

    // The room switches renderers on and off whenever it refreshes, so a hiding demon
    // re-hides itself every frame (after the room has had its say)
    void LateUpdate()
    {
        if (!hiding) return;

        foreach (Renderer r in renderers)
            if (r != null) r.forceRenderingOff = true;
    }

    // Stops hiding and shows the demon again
    void Reveal()
    {
        if (!hiding) return;
        hiding = false;

        // The room knows whether the demon's own room is currently visible
        if (room != null) room.RefreshVisibility();
        else
            foreach (Renderer r in renderers)
                if (r != null) r.forceRenderingOff = false;
    }

    void OnDamaged(int amount)
    {
        // A hit gives the demon away
        if (revealWhenHit) Reveal();

        // Push away from the target. Taking a hit interrupts whatever we were doing.
        Vector3 away = -GetToTarget();
        if (away.sqrMagnitude < 0.001f) away = -transform.forward;

        motor.AddKnockback(away.normalized * knockbackForce);
        SetState(State.Stagger);
    }

    // Update is called once per frame
    void Update()
    {
        if (target == null) return;

        // Frozen during a rotation or mode switch (state timers pause with it)
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning || DungeonMapSystem.IsEditing) return;

        Vector3 toTarget = GetToTarget();
        float dist = toTarget.magnitude;
        stateTimer += Time.deltaTime;

        switch (state)
        {
            case State.Idle:
                motor.Stop();
                if (dist <= detectRange) SetState(State.Chase);
                break;

            case State.Chase:
                if (dist > detectRange * 1.5f) { SetState(State.Idle); break; }
                if (dist <= attackRange) { SetState(State.Attack); break; }

                if (dist > stopDistance) motor.SetVelocity(toTarget / dist * speed);
                else motor.Stop();
                motor.Face(toTarget);
                break;

            case State.Attack:
                motor.Stop();
                motor.Face(toTarget);

                if (!attackStruck && stateTimer >= attackWindup)
                {
                    attackStruck = true;
                    if (dist <= attackRange && target.TryGetComponent(out IDamageable dmg))
                        dmg.TakeDamage(attackDamage);
                }

                if (stateTimer >= attackWindup + attackRecovery) SetState(State.Chase);
                break;

            case State.Stagger:
                motor.Stop(); // only knockback moves us
                if (stateTimer >= staggerTime && !motor.IsKnockedBack) SetState(State.Chase);
                break;

            case State.Hide:
                UpdateHide(toTarget, dist);
                break;
        }
    }

    // ---------------------------------------------------------------- hiding

    // Rests, then either wanders near home or loosely trails the player. Never attacks.
    void UpdateHide(Vector3 toTarget, float dist)
    {
        hideModeTimer -= Time.deltaTime;

        switch (hideMode)
        {
            case HideMode.Pause:
                motor.Stop();
                if (hideModeTimer <= 0f) PickHideAction(dist);
                break;

            case HideMode.Wander:
                {
                    Vector3 to = wanderTarget - transform.position;
                    to.y = 0f;
                    if (sidescroller) to = Vector3.ProjectOnPlane(to, sideForward);

                    if (to.magnitude < 0.2f || hideModeTimer <= 0f) { EnterPause(); break; }

                    motor.SetVelocity(to.normalized * wanderSpeed);
                    motor.Face(to);
                    break;
                }

            case HideMode.Follow:
                // Lost interest, or the trail is over
                if (dist > detectRange * 1.5f || hideModeTimer <= 0f) { EnterPause(); break; }

                if (dist > followDistance)
                {
                    motor.SetVelocity(toTarget / dist * wanderSpeed);
                    motor.Face(toTarget);
                }
                else motor.Stop();
                break;
        }
    }

    void EnterPause()
    {
        hideMode = HideMode.Pause;
        hideModeTimer = Random.Range(pauseTime.x, pauseTime.y);
    }

    void PickHideAction(float distToTarget)
    {
        if (distToTarget <= detectRange && Random.value < followChance)
        {
            hideMode = HideMode.Follow;
            hideModeTimer = followTime;
            return;
        }

        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = homePosition + new Vector3(offset.x, 0f, offset.y);
        hideMode = HideMode.Wander;
        hideModeTimer = 4f; // gives up if it can't get there
    }

    // Vector from the enemy to the target's body, with depth removed in sidescroller
    Vector3 GetToTarget()
    {
        Vector3 aimPoint = target.position + Vector3.up * aimHeight;
        Vector3 toTarget = aimPoint - transform.position;

        if (sidescroller && ignoreDepthInSidescroller)
            toTarget = Vector3.ProjectOnPlane(toTarget, sideForward);

        return toTarget;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!hiding) return;

        Vector3 home = Application.isPlaying ? homePosition : transform.position;
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.6f);
        Gizmos.DrawWireSphere(home, wanderRadius);
    }
#endif
}
