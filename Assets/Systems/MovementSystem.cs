using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class MovementSystem : MonoBehaviour
{
    [SerializeField] InputTrackerSystem input;
    [SerializeField] Transform cameraTransform;
    [SerializeField] float speed = 6f;
    [SerializeField] float runSpeed = 10f;
    [SerializeField] float jumpHeight = 1.6f;
    [SerializeField] float gravity = -25f;
    [SerializeField] float turnSpeed = 15f;
    [SerializeField] float dashSpeed = 18f;
    [SerializeField] float dashDuration = 0.15f;
    [SerializeField] float dashCooldown = 0.5f;
    [SerializeField] float coyoteTime = 0.12f;
    [SerializeField] float jumpBufferTime = 0.12f;
    [SerializeField] float lowJumpMultiplier = 2.5f; // extra gravity when jump is released early
    [SerializeField] float fallMultiplier = 1.6f;
    [SerializeField] float apexThreshold = 1.5f;   // how close to zero vertical speed counts as the peak
    [SerializeField] float apexGravityScale = 0.5f; // lower = floatier
    [SerializeField] float whipZoomDuration = 0.2f;
    [SerializeField] float depthEaseRate = 10f; // how quickly landing on a platform eases the player to its depth; higher = snappier

    Vector3 planeOrigin;
    Vector3 sideForward = Vector3.forward; // direction the camera looks = locked depth axis
    Vector3 sideRight = Vector3.right;     // screen-right = sidescroller movement axis

    float dashTimer;
    float dashCooldownTimer;
    Vector3 dashDirection;

    bool grounded;
    float coyoteTimer;
    float jumpBufferTimer;

    // Pillar launch: straight up first, steering only counts after a set height
    bool launched;
    float launchStartY;
    float launchSteerHeight;

    // Whip grapple: zoom to a hook, or hang from the ceiling
    enum Grapple { None, Zoom, Hang }
    Grapple grapple = Grapple.None;
    Vector3 zoomStart;
    Vector3 zoomTarget;
    float zoomDuration;
    float zoomTimer;
    bool zoomControllerWasEnabled;
    float hangCeilingY;
    float hangClimbSpeed;
    float hangHeadGap;

    // The perspective shift system flips this
    public bool SidescrollerMode { get; set; }
    public bool Grounded => grounded;
    public bool IsGrappling => grapple != Grapple.None;
    public Vector3 GrappleAnchor { get; private set; } // where the rope is attached

    CharacterController cc;
    float verticalVelocity;

    // Landing on a platform eases the player to its depth (sidescroller)
    PlatformSystem standingOn;       // the platform under the player this frame, if any
    Collider lastGroundCollider;
    PlatformSystem lastGroundPlatform;

    void Awake() => cc = GetComponent<CharacterController>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // Frozen during a rotation or mode switch
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning || DungeonMapSystem.IsEditing) return;

        // The whip has taken over: it moves the player, nothing else does
        if (grapple != Grapple.None)
        {
            UpdateGrapple();
            if (grapple != Grapple.Zoom) LockToSidescrollerPlane();
            return;
        }

        Vector3 dir = GetMoveDirection();

        // A pillar launch goes straight up until they've risen far enough. A direction held
        // meanwhile isn't lost: it takes effect the moment they get there.
        if (launched && transform.position.y - launchStartY < launchSteerHeight) dir = Vector3.zero;

        HandleDash(dir);
        HandleGravityAndJump();

        Vector3 horizontal = GetHorizontalVelocity(dir);
        standingOn = null;
        ApplyMovement(horizontal);
        EaseDepthOnPlatform();

        HandleRotation(dir);
        LockToSidescrollerPlane();
        
    }

    Vector3 GetMoveDirection()
    {
        Vector2 move = input.Move;
        Vector3 dir;

        if (SidescrollerMode)
        {
            // Left/right only, along the world X axis (use your plane's axis)
            dir = sideRight * move.x;
        }
        else
        {
            // Camera-relative, flattened onto the ground plane
            Vector3 fwd = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            dir = fwd * move.y + right * move.x;
        }

        return Vector3.ClampMagnitude(dir, 1f);
    }

    // Dash (top-down only)
    void HandleDash(Vector3 dir)
    {
        dashCooldownTimer -= Time.deltaTime;

        if (SidescrollerMode || launched || !input.JumpDashPressed) return;
        if (dashTimer > 0f || dashCooldownTimer > 0f) return;

        // Dash toward input, or straight ahead if no input is held
        //dashDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
        // No direction held: no dash (and the cooldown isn't spent)
        if (dir.sqrMagnitude < 0.001f) return;

        dashDirection = dir.normalized;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        transform.rotation = Quaternion.LookRotation(dashDirection);

        Debug.Log($"Dash. Direction: {dashDirection}, Speed: {dashSpeed}, Duration: {dashDuration}s");
    }

    // Gravity and jump (jump is sidescroller only)
    void HandleGravityAndJump()
    {
        if (launched && verticalVelocity <= 0f) launched = false; // the launch is over at the top of the rise
        if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;

        // Timers
        coyoteTimer = grounded ? coyoteTime : coyoteTimer - Time.deltaTime;
        jumpBufferTimer = (SidescrollerMode && input.JumpDashPressed)
            ? jumpBufferTime
            : jumpBufferTimer - Time.deltaTime;

        // Jump if a press is buffered and we're on the ground (or just left it)
        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f; // prevents a double jump from the coyote window
        }

        // Variable height: fall faster while rising if the button was released + heavier fall
        float g = gravity;
        if (SidescrollerMode && !launched && verticalVelocity > 0f && !input.JumpDashHeld)
            g *= lowJumpMultiplier;
        else if (verticalVelocity < 0f)
            g *= fallMultiplier;             // falling: come down faster than you went up

        // Near the peak, ease gravity for a brief hang
        if (!grounded && Mathf.Abs(verticalVelocity) < apexThreshold)
            g *= apexGravityScale;

        verticalVelocity += g * Time.deltaTime;
        verticalVelocity = Mathf.Max(verticalVelocity, -40f);
    }

    // Normal movement, run while held, or the dash override (also ticks the dash timer)
    Vector3 GetHorizontalVelocity(Vector3 dir)
    {
        if (dashTimer > 0f)
        {
            dashTimer -= Time.deltaTime;
            return dashDirection * dashSpeed;
        }

        bool running = SidescrollerMode || input.JumpDashHeld;
        return dir * (running ? runSpeed : speed);
    }

    void ApplyMovement(Vector3 horizontal)
    {
        Vector3 velocity = horizontal + Vector3.up * verticalVelocity;
        cc.Move(velocity * Time.deltaTime);

        // Cache right after the main Move; isGrounded only reflects the last Move call
        grounded = cc.isGrounded;

        // Bonk your head: stop rising
        if ((cc.collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
    }

    // Face movement direction (not while dashing)
    void HandleRotation(Vector3 dir)
    {
        if (dashTimer > 0f || dir.sqrMagnitude <= 0.001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    // Keep the player on the sidescroller plane
    void LockToSidescrollerPlane()
    {
        if (!SidescrollerMode) return;

        float depth = Vector3.Dot(transform.position - planeOrigin, sideForward);
        if (Mathf.Abs(depth) > 0.0001f) cc.Move(-sideForward * depth);
    }

    public void SetSidescrollerPlane(Vector3 viewForward)
    {
        sideForward = viewForward.normalized;
        sideRight = Vector3.Cross(Vector3.up, sideForward);
        planeOrigin = transform.position;
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        cc.enabled = true;

        verticalVelocity = 0f;
        launched = false;
        grapple = Grapple.None;
        dashTimer = 0f;
        if (SidescrollerMode) planeOrigin = transform.position; // keep the plane lock from pulling you back
    }

    // The controller reports what it's pressing against: remember a platform under the player's feet
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y < 0.5f) return; // a floor, not a wall or ceiling

        if (hit.collider != lastGroundCollider)
        {
            lastGroundCollider = hit.collider;
            lastGroundPlatform = hit.collider.GetComponent<PlatformSystem>();
        }

        standingOn = lastGroundPlatform;
    }

    // Landing on a platform eases the player to the platform's depth, just far enough to be fully
    // on it, so leaving sidescroller (or turning the view) doesn't leave them hanging off it
    void EaseDepthOnPlatform()
    {
        if (!SidescrollerMode || !grounded || standingOn == null) return;

        standingOn.GetRealRange(sideForward, out float min, out float max);

        float inset = cc.radius;
        float current = Vector3.Dot(transform.position, sideForward);
        float target = (min + inset <= max - inset)
            ? Mathf.Clamp(current, min + inset, max - inset)
            : (min + max) * 0.5f; // a platform thinner than the player: its middle

        if (Mathf.Abs(target - current) < 0.002f) return;

        // Slide the locked plane toward that depth; LockToSidescrollerPlane carries the player along
        float planeDepth = Vector3.Dot(planeOrigin, sideForward);
        float next = Mathf.Lerp(planeDepth, target, 1f - Mathf.Exp(-depthEaseRate * Time.deltaTime));
        planeOrigin += sideForward * (next - planeDepth);
    }

    // ---------------------------------------------------------------- pillar jump

    // Pillar: shoots the player up to a height, in either mode. The jump's slower gravity near
    // the peak adds a little height, so the launch speed is worked out backwards from the target.
    public void LaunchToHeight(float height, float steerHeight)
    {
        float g = Mathf.Abs(gravity);
        float s = Mathf.Max(apexGravityScale, 0.01f);
        float a = apexThreshold;

        float v0 = Mathf.Sqrt(Mathf.Max(0f, 2f * g * height - a * a * (1f / s - 1f)));
        if (v0 < a) v0 = Mathf.Sqrt(2f * g * s * height); // a very short launch, all inside the peak zone

        verticalVelocity = v0;
        launched = true;
        launchStartY = transform.position.y;
        launchSteerHeight = steerHeight;
        dashTimer = 0f;
    }

    // ---------------------------------------------------------------- whip grapple

    // Whip on a hook: zoom to it, in either mode.
    public void ZoomTo(Vector3 anchor, Vector3 arrival, float speed)
    {
        // Capture the player's actual position when the whip is activated.
        zoomStart = transform.position;
        zoomTarget = arrival;

        // Face the hook immediately, changing only the Y rotation.
        Vector3 direction = anchor - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            Vector3 euler = transform.eulerAngles;
            euler.y = Quaternion.LookRotation(direction).eulerAngles.y;
            transform.eulerAngles = euler;
        }

        GrappleAnchor = anchor;

        zoomDuration = Mathf.Max(whipZoomDuration, 0.01f);
        zoomTimer = 0f;

        zoomControllerWasEnabled = cc.enabled;
        cc.enabled = false;

        grapple = Grapple.Zoom;
        launched = false;
        dashTimer = 0f;
    }

    // Whip at the ceiling (sidescroller): hang there. Up and down only, jump to let go.
    public void HangFromCeiling(float ceilingY, float climbSpeed, float headGap)
    {
        hangCeilingY = ceilingY;
        hangClimbSpeed = climbSpeed;
        hangHeadGap = headGap;
        GrappleAnchor = new Vector3(transform.position.x, ceilingY, transform.position.z);

        grapple = Grapple.Hang;
        launched = false;
        dashTimer = 0f;
    }

    public void EndGrapple()
    {
        if (grapple == Grapple.Zoom)
            cc.enabled = zoomControllerWasEnabled;

        grapple = Grapple.None;
    }

    Vector3 OnPlane(Vector3 point)
        => point - sideForward * Vector3.Dot(point - planeOrigin, sideForward);

    void UpdateGrapple()
    {
        float dt = Time.deltaTime;

        verticalVelocity = 0f; // no gravity while the whip has hold of you

        if (grapple == Grapple.Zoom)
        {
            zoomTimer += dt;

            float t = Mathf.Clamp01(zoomTimer / zoomDuration);

            // Ease out so the player decelerates into the destination.
            float easedT = 1f - Mathf.Pow(1f - t, 3f);

            // Position only. Rotation was already handled when the zoom began.
            transform.position = Vector3.Lerp(zoomStart, zoomTarget, easedT);

            if (t >= 1f)
            {
                transform.position = zoomTarget;

                cc.enabled = zoomControllerWasEnabled;

                if (cc.enabled)
                {
                    Physics.SyncTransforms();
                    grounded = cc.isGrounded;
                }

                if (SidescrollerMode) planeOrigin = transform.position; // the plane moves to the hook's depth

                grapple = Grapple.None;
            }

            return;
        }

        // Hanging is a sidescroller thing: the fez switches it off
        if (!SidescrollerMode)
        {
            grapple = Grapple.None;
            return;
        }

        // Up and down only, never past the ceiling
        float climb = input.Move.y * hangClimbSpeed * dt;
        float headRoom = hangCeilingY - hangHeadGap - cc.bounds.max.y;
        climb = Mathf.Min(climb, Mathf.Max(headRoom, 0f));

        cc.Move(Vector3.up * climb);
        grounded = cc.isGrounded;
        GrappleAnchor = new Vector3(transform.position.x, hangCeilingY, transform.position.z);

        // Jump lets go with a normal jump, from wherever you are
        if (input.JumpDashPressed)
        {
            grapple = Grapple.None;
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            return;
        }

        // Climbed down to the floor: let go
        if (climb < 0f && grounded) grapple = Grapple.None;
    }
}
