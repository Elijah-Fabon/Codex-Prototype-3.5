using System.Collections.Generic;
using UnityEngine;

public enum Firepower { None, Whip, Firecrackers, Firewall, Pillar }

// Lives on the player. A D-pad press picks what the rosary does when it is used, and it stays picked
// until another direction is pressed: up = whip, right = firecrackers, left = firewall, down =
// pillar. Pressing the direction that is already picked goes back to neutral (the rosary does its
// ordinary things again).
// button: up = whip, right = firecrackers, left = firewall, down = pillar.
//
// Firewall: press the rosary with it selected and keep holding. While held, the next nodes and wall
// are previewed (they follow the player). On release they come in.
// The first use puts out two nodes at knight's-move spots ahead of the player (one square to each
// side, two ahead) with a wall between them, so the wall is centered in front. After that each use
// puts out one node in front and a wall to the nearest existing node.
//
// Pillar: aimed the same way. Hold the rosary and a ghost pillar follows the player. On release it
// goes down and stays. If a raisable platform is within reach of the aimed spot the pillar snaps to
// it and lifts it instead. Hold Target when letting go and the pillar goes under the player and
// launches them (the pillar jump), so there is time to switch while aiming.
//
// Whip: a targeted hook is grappled right away, in either mode. In the sidescroller with no hook
// targeted it is aimed the same way as the others: hold the rosary and move, a ghost rope shows
// where it would catch the ceiling, and on release it grapples.
//
// While aiming, pressing another direction switches to that power.
public class FirepowerSystem : MonoBehaviour
{
    [Header("Firewall layout")]
    [SerializeField] float nodeUnit = 1.2f;                       // one square of the knight layout
    [SerializeField] Vector2 knightOffset = new Vector2(1f, 2f);  // (sideways, forward) in squares; the first two nodes sit at -/+ sideways
    [SerializeField] float nextNodeDistance = 2.4f;               // how far in front a single node goes
    [SerializeField] int maxNodesPerRoom = 8;                     // past this the oldest node (and its walls) go first
    [SerializeField] LayerMask groundMask = ~0;                   // set to your ground layers so nodes don't land on hurtboxes
    [SerializeField] float maxNodeRadius = 6f;   // a new node must be within this distance of the node it connects to
    [SerializeField] float groundSearchDepth = 15f;               // how far below the player's head to look for ground (drops, jumping, raised platforms)

    [Header("Firewall look")]
    [SerializeField] float wallHeight = 2f;
    [SerializeField] float wallThickness = 0.3f;
    [SerializeField] float riseTime = 0.35f;
    [SerializeField] float nodeSize = 0.4f;
    [SerializeField] Material nodeMaterial;     // optional; the default primitive material is used if empty
    [SerializeField] Material wallMaterial;     // optional
    [SerializeField] Material previewMaterial;  // optional; use a transparent material for a ghost look
    [SerializeField] Color nodeColor = new Color(1f, 0.45f, 0.1f, 1f);
    [SerializeField] Color wallColor = new Color(1f, 0.3f, 0.05f, 1f);
    [SerializeField] Color previewColor = new Color(1f, 0.6f, 0.2f, 0.4f);
    [SerializeField] Color blockedColor = new Color(1f, 0.1f, 0.1f, 0.4f); // preview nodes while the firewall can't be placed

    [Header("Firewall barrier (sidescroller)")]
    [SerializeField] float barrierHalfWidth = 3f;
    [SerializeField] float barrierHalfDepth = 2f;

    [Header("Firewall damage (both modes)")]
    [SerializeField] int barrierDamage = 1;
    [SerializeField] float barrierHitCooldown = 0.6f;

    [Header("Pillar")]
    [SerializeField] float pillarDistance = 2f;       // how far in front of the player the pillar goes
    [SerializeField] float pillarSnapRange = 2.5f;    // a raisable platform this close to that spot is picked instead
    [SerializeField] int maxPillarsPerRoom = 3;       // past this the oldest placed pillar goes first
    [SerializeField] float pillarJumpHeight = 5f;     // launch height when used under the player (Target held)
    [SerializeField] float pillarSteerHeight = 1.5f;  // how far the launch rises straight up before steering counts
    [SerializeField] float pillarWidth = 0.8f;
    [SerializeField] float pillarVisualHeight = 1.5f; // the column shown under the player, or where no platform is in reach
    [SerializeField] float pillarRiseTime = 0.2f;
    [SerializeField] float pillarVisualTime = 1f;     // how long that column stays
    [SerializeField] Material pillarMaterial;
    [SerializeField] Color pillarColor = new Color(0.55f, 0.5f, 0.45f, 1f);

    [Header("Lifetimes (walls and pillars)")]
    [SerializeField] float wallLifetime = 8f;       // seconds a firewall wall stays fully up
    [SerializeField] float wallLowerTime = 0.35f;   // how long it takes to sink back down
    [SerializeField] float pillarLifetime = 8f;     // seconds a placed pillar stays fully up
    [SerializeField] float pillarLowerTime = 0.25f;

    [Header("Preview loop")]
    [SerializeField] float previewHoldTime = 0.6f;  // the ghost stays risen this long
    [SerializeField] float previewLowerTime = 0.3f; // then sinks down
    [SerializeField] float previewGapTime = 0.25f;  // and waits this long before rising again
    [SerializeField] float previewRiseTime = 1f;

    [Header("Whip")]
    [SerializeField] float whipZoomSpeed = 28f;    // how fast the player is pulled to a hook
    [SerializeField] float whipCeilingRange = 8f;  // how far up the sidescroller whip reaches
    [SerializeField] float whipClimbSpeed = 5f;    // up/down speed while hanging
    [SerializeField] float whipHeadGap = 0.1f;     // how far below the ceiling the head stops
    [SerializeField] float whipRopeWidth = 0.06f;
    [SerializeField] Material whipMaterial;        // optional; a plain unlit material is used if empty
    [SerializeField] Color whipColor = new Color(0.6f, 0.4f, 0.2f, 1f);

    [Header("Firecrackers")]
    [SerializeField] Projectile firecrackerPrefab;
    [SerializeField] float firecrackerSpeed = 10f;
    [SerializeField] float firecrackerAreaMinRadius = 0.5f; // the landing area is never smaller than this
    [SerializeField] float firecrackerFuse = 2.5f;
    [SerializeField] float firecrackerFuseRandomness = 0.35f;
    [SerializeField] float firecrackerDirectionRandomness = 4f;

    [SerializeField] InputTrackerSystem input;

    CharacterController controller;
    MovementSystem movement;
    TargetingSystem targeting;
    LineRenderer whipRope;
    LineRenderer whipPreview;
    float whipPreviewTime;
    Transform muzzle; // the same muzzle the EquipSystem and TargetingSystem use
    GameObject firecrackerArea; // the flat disc that shows where the shots will land

    // What the held D-pad direction currently selects (handy for a HUD later)
    public Firepower Selected { get; private set; }
    public bool IsUsingFirepower => aiming;

    // Something is being aimed (firewall or pillar): the rosary is held and the preview is showing
    enum AimKind { Firewall, Pillar, Whip, Firecracker }
    bool aiming;
    AimKind aimKind;
    EquipSlot aimSlot;

    // ---------------------------------------------------------------- firewall data

    class FirewallNode
    {
        public Vector3 position;
        public GameObject visual;
        public DungeonRoom room;
    }

    class PlacedWall
    {
        public FirewallWall wall;
        public FirewallNode from;
        public FirewallNode to;
    }

    // What the next firewall use would do
    struct FirewallPlan
    {
        public Vector3 nodeA, nodeB;   // nodeB only when twoNodes
        public bool twoNodes;
        public FirewallNode joinTo;    // the existing node a single new node connects to
        public Vector3 wallFrom, wallTo;
        public bool hasWall;
        public bool tooFar;    // the new node would be out of range of the node it connects to
        public bool inTheWay;  // the player is standing where the wall would go
        public bool Blocked => tooFar || inTheWay;
    }

    // ---------------------------------------------------------------- pillar raise data

    // A pillar that was put down and stays
    class PlacedPillar
    {
        public FirewallWall wall;
        public DungeonRoom room;
    }

    // What letting go of the pillar would do right now
    struct PillarPlan
    {
        public Vector3 point;           // on the ground (under the player for the jump)
        public bool underPlayer;        // Target held: the pillar jump
        public PlatformSystem platform; // a raisable platform it snapped to, if any
    }

    readonly List<PlacedPillar> placedPillars = new List<PlacedPillar>();
    FirewallWall pillarPreview;

    enum RaiseState { Rising, Held, Falling }

    // A raisable platform that a pillar has lifted and hasn't finished lowering yet
    class ActiveRaise
    {
        public PlatformSystem platform;
        public RaiseState state = RaiseState.Rising;
        public float rise;       // how far above its resting spot
        public float holdTimer;
        public float fallSpeed;
        public Transform column; // the pillar drawn under the platform while it's up
    }

    readonly List<ActiveRaise> raises = new List<ActiveRaise>();

    readonly List<FirewallNode> nodes = new List<FirewallNode>();
    readonly List<PlacedWall> placedWalls = new List<PlacedWall>();

    GameObject previewNodeA, previewNodeB;
    FirewallWall previewWall;
    bool previewShown;
    bool previewTintBlocked;
    FirewallWall[] barrierPreview;
    bool lastSidescroller;

    // ---------------------------------------------------------------- setup

    void Reset() => input = FindFirstObjectByType<InputTrackerSystem>();

    void Awake()
    {
        if (input == null) input = FindFirstObjectByType<InputTrackerSystem>();
        controller = GetComponent<CharacterController>();
        movement = GetComponent<MovementSystem>();
        targeting = GetComponent<TargetingSystem>();
        EquipSystem equip = GetComponent<EquipSystem>();
        muzzle = equip != null ? equip.Muzzle : null;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        UpdateRaises();
        UpdateWhipRope();
        PruneExpired();

        if (!aiming)
        {
            HidePreview();
            return;
        }

        // Something took over (a transition, a menu, the dungeon map): drop the firewall
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning ||
            EquipMenu.IsOpen || DungeonMapSystem.IsEditing)
        {
            StopAiming(false);
            return;
        }

        if (input.IsEquipHeld(aimSlot))
        {
            ShowPreview();
        }
        else StopAiming(true); // released: it comes in
    }

    // Walls and pillars that timed out destroy themselves; drop the leftover entries
    void PruneExpired()
    {
        placedWalls.RemoveAll(w => w.wall == null);

        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            FirewallNode node = nodes[i];

            bool stillConnected = placedWalls.Exists(w =>
                w.from == node || w.to == node);

            if (stillConnected) continue;

            if (node.visual != null)
                Destroy(node.visual);

            nodes.RemoveAt(i);
        }

        placedPillars.RemoveAll(p => p.wall == null);
    }

    void OnEnable()
    {
        input.DPadPressed += OnDPadPressed;
        lastSidescroller = PerspectiveShiftSystem.CurrentSidescroller;
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
    }

    void OnDisable()
    {
        input.DPadPressed -= OnDPadPressed;
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;

        aiming = false;
        HidePreview();
    }

    // A D-pad press picks a firepower, and it stays picked until another one is picked. Pressing the
    // one that is already picked goes back to neutral.
    void OnDPadPressed(Vector2Int dir)
    {
        Firepower picked = FromDirection(dir);
        if (picked == Firepower.None) return;

        // The D-pad belongs to transitions, menus, the dungeon map and a targeted grid's cursor
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning ||
            EquipMenu.IsOpen || DungeonMapSystem.IsEditing ||
            (targeting != null && targeting.IsTargetingGrid))
            return;

        Selected = picked == Selected ? Firepower.None : picked;
        Debug.Log(Selected == Firepower.None ? "Firepower: neutral" : $"Firepower: {Selected}");

        // Pressed while the rosary is held: the aim follows the new selection
        if (aiming) ChangeAim();
    }

    // Switching between top-down and sidescroller takes every wall down. Pillars stay.
    void OnPerspectiveChanged(bool sidescroller, Vector3 view)
    {
        if (sidescroller == lastSidescroller) return; // a view rotation, not a mode switch
        lastSidescroller = sidescroller;

        foreach (PlacedWall w in placedWalls)
            if (w.wall != null) w.wall.Lower(wallLowerTime);
    }

    void OnDestroy()
    {
        foreach (ActiveRaise r in raises)
            if (r.column != null) Destroy(r.column.gameObject);

        if (previewNodeA != null) Destroy(previewNodeA);
        if (previewNodeB != null) Destroy(previewNodeB);
        if (previewWall != null) Destroy(previewWall.gameObject);
        if (whipRope != null) Destroy(whipRope.gameObject);
        if (whipPreview != null) Destroy(whipPreview.gameObject);
        if (barrierPreview != null)
            foreach (FirewallWall w in barrierPreview)
                if (w != null) Destroy(w.gameObject);
        if (firecrackerArea != null) Destroy(firecrackerArea);
    }

    // ---------------------------------------------------------------- using firepowers

    public static Firepower FromDirection(Vector2Int dir)
    {
        if (dir.y > 0) return Firepower.Whip;
        if (dir.y < 0) return Firepower.Pillar;
        if (dir.x > 0) return Firepower.Firecrackers;
        if (dir.x < 0) return Firepower.Firewall;
        return Firepower.None;
    }

    // Uses whichever firepower the held direction picks. Returns false if no direction is held.
    public bool TryUseSelected(EquipSystem owner)
    {
        if (Selected == Firepower.None)
            return false;

        // Toward the target if there is one
        bool aimed = targeting != null && targeting.AimMuzzle();
        Vector3 origin = owner.Muzzle != null ? owner.Muzzle.position : owner.User.position;
        Vector3 direction = aimed ? owner.Muzzle.forward : owner.User.forward;

        switch (Selected)
        {
            case Firepower.Whip:
                // A targeted hook fires right away in either mode. Otherwise, in the sidescroller,
                // aim the ceiling grapple and fire it on release.
                if (!HookTargeted && CanAim(Firepower.Whip)) BeginAim(owner, AimKind.Whip);
                else Whip(origin, direction);
                break;
            case Firepower.Firecrackers: BeginAim(owner, AimKind.Firecracker); break;
            case Firepower.Firewall: BeginAim(owner, AimKind.Firewall); break;
            case Firepower.Pillar: BeginAim(owner, AimKind.Pillar); break;
        }

        return true;
    }

    // Finds the ground under a point. The player may be in the air, on a raised platform, or the
    // ground ahead may be a step higher or a drop lower, so the ray starts at head height (always
    // inside the room, under any ceiling) and reaches a long way down.
    Vector3 SnapToGround(Vector3 point)
    {
        float feetY = controller != null ? controller.bounds.min.y : transform.position.y - 1f;
        float headY = controller != null ? controller.bounds.max.y : feetY + 2f;
        Vector3 start = new Vector3(point.x, headY, point.z);

        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, groundSearchDepth, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point;

        return new Vector3(point.x, feetY, point.z);
    }

    // ---------------------------------------------------------------- aiming (firewall and pillar)

    // The rosary was pressed with the firewall or pillar selected: aim it for as long as the button is held
    void BeginAim(EquipSystem owner, AimKind kind)
    {
        if (aiming) return;

        for (int i = 0; i < 3; i++)
        {
            if (owner.GetSlot(i) is RosaryItem && input.IsEquipHeld((EquipSlot)i))
            {
                aimSlot = (EquipSlot)i;
                aimKind = kind;
                aiming = true;
                return;
            }
        }

        // The button was already let go (a very quick tap): just put it out
        aimKind = kind;
        PlaceAimed();
    }

    bool HookTargeted => targeting != null && targeting.HookTarget != null;

    // Firewall and pillar can always be aimed. The whip only in the sidescroller (and not while
    // already hanging). Firecrackers fire right away, so there is nothing to aim.
    bool CanAim(Firepower power)
    {
        switch (power)
        {
            case Firepower.Firecrackers:
            case Firepower.Firewall:
            case Firepower.Pillar:
                return true;

            case Firepower.Whip:
                return movement != null && movement.SidescrollerMode && !movement.IsGrappling;

            default:
                return false;
        }
    }

    static AimKind AimKindFor(Firepower power)
    {
        switch (power)
        {
            case Firepower.Firecrackers: return AimKind.Firecracker;
            case Firepower.Pillar: return AimKind.Pillar;
            case Firepower.Whip: return AimKind.Whip;
            default: return AimKind.Firewall;
        }
    }

    // The selection changed while the rosary is held, so the aim follows it
    void ChangeAim()
    {
        // Back to neutral: nothing to aim, and nothing is placed when the rosary is let go
        if (Selected == Firepower.None)
        {
            StopAiming(false);
            return;
        }

        // A targeted hook fires right away, so the whip never waits on the button being let go
        if (Selected == Firepower.Whip && HookTargeted && movement != null && !movement.IsGrappling)
        {
            StopAiming(false);
            Whip(transform.position, transform.forward);
            return;
        }

        // Can't be aimed right now (the whip outside the sidescroller): drop the aim
        if (!CanAim(Selected))
        {
            StopAiming(false);
            return;
        }

        aimKind = AimKindFor(Selected);
        HidePreview(); // the new preview starts its loop from the beginning
    }

    void StopAiming(bool place)
    {
        aiming = false;
        HidePreview();

        if (place) PlaceAimed();
    }

    void PlaceAimed()
    {
        switch (aimKind)
        {
            case AimKind.Firecracker: Firecrackers(); break;
            case AimKind.Firewall: Firewall(); break;
            case AimKind.Pillar: PlacePillar(); break;
            case AimKind.Whip: Whip(transform.position, transform.forward); break;
        }
    }

    // ---------------------------------------------------------------- whip

    // Targeting a hook: zoom to it (either mode). Not targeting one: in sidescroller the whip goes
    // straight up and hooks the ceiling, and you hang there (up and down only, jump to let go).
    void Whip(Vector3 origin, Vector3 direction)
    {
        if (movement == null || movement.IsGrappling) return;

        HookPoint hook = targeting != null ? targeting.HookTarget : null;
        if (hook != null)
        {
            Collider hookCollider = hook.GetComponent<Collider>();

            Vector3 playerCenter = controller != null ? controller.bounds.center : transform.position;

            // Aim toward the upper portion of the hook.
            Bounds hookBounds = hookCollider.bounds;

            Vector3 upperPoint = new Vector3(
                hookBounds.center.x,
                hookBounds.max.y - hookBounds.size.y * 0.2f,
                hookBounds.center.z
            );

            // Find the closest point on the hook collider in 3D.
            Vector3 surface = hookCollider.ClosestPoint(upperPoint);

            // Approach horizontally, not from above or below.
            Vector3 awayFromHook = new Vector3(
                playerCenter.x - surface.x,
                0f,
                playerCenter.z - surface.z
            ).normalized;

            if (awayFromHook.sqrMagnitude < 0.001f)
            {
                awayFromHook = -hook.transform.forward;
                awayFromHook.y = 0f;
                awayFromHook.Normalize();
            }

            // In sidescroller the player ends up at the hook's depth, on the left or right of it
            if (movement.SidescrollerMode)
            {
                Vector3 view = PerspectiveShiftSystem.CurrentViewForward.normalized;
                Vector3 screenRight = Vector3.Cross(Vector3.up, view);

                awayFromHook -= view * Vector3.Dot(awayFromHook, view);

                // Exactly in line with the hook's depth: pick the side the player is on
                if (awayFromHook.sqrMagnitude < 0.001f)
                    awayFromHook = screenRight * (Vector3.Dot(playerCenter - surface, screenRight) >= 0f ? 1f : -1f);

                awayFromHook.Normalize();
            }

            float clearance = controller != null ? controller.radius + controller.skinWidth + 0.1f : 0.5f;

            // Keep the hook's actual height!
            Vector3 desiredCenter = surface + awayFromHook * clearance;

            Vector3 pivotOffset = controller != null ? controller.bounds.center - transform.position : Vector3.zero;

            Vector3 arrival = desiredCenter - pivotOffset;

            movement.ZoomTo(hook.AnchorPoint, arrival, whipZoomSpeed);

            Debug.Log($"Whip: zooming to {hook.name}");
            return;
        }

        if (!movement.SidescrollerMode)
        {
            Debug.Log("Whip: no hook targeted");
            return;
        }

        float headY = controller != null ? controller.bounds.max.y : transform.position.y + 1f;
        Vector3 start = new Vector3(transform.position.x, headY, transform.position.z);

        if (Physics.Raycast(start, Vector3.up, out RaycastHit hit, whipCeilingRange, groundMask, QueryTriggerInteraction.Ignore))
        {
            movement.HangFromCeiling(hit.point.y, whipClimbSpeed, whipHeadGap);
            Debug.Log("Whip: hooked the ceiling");
        }
        else
        {
            Debug.Log("Whip: no ceiling in reach");
        }
    }

    // A rope from the player to the hook or ceiling while the whip has hold of them
    void UpdateWhipRope()
    {
        bool show = movement != null && movement.IsGrappling;

        if (!show)
        {
            if (whipRope != null) whipRope.enabled = false;
            return;
        }

        if (whipRope == null) CreateWhipRope();

        Vector3 hand = controller != null ? controller.bounds.center : transform.position;
        whipRope.enabled = true;
        whipRope.SetPosition(0, hand);
        whipRope.SetPosition(1, movement.GrappleAnchor);
    }

    void CreateWhipRope()
    {
        GameObject go = new GameObject("Whip Rope");
        whipRope = go.AddComponent<LineRenderer>();
        whipRope.positionCount = 2;
        whipRope.useWorldSpace = true;
        whipRope.widthMultiplier = whipRopeWidth;
        whipRope.numCapVertices = 2;
        whipRope.material = whipMaterial != null ? whipMaterial : new Material(Shader.Find("Sprites/Default"));
        whipRope.startColor = whipColor;
        whipRope.endColor = whipColor;
    }

    // A ghost rope from the player to where the whip would catch (the ceiling
    // above). It reaches out, holds, and starts over for as long as the whip is being aimed.
    void ShowWhipPreview()
    {
        Vector3 hand = controller != null ? controller.bounds.center : transform.position;

        float headY = controller != null ? controller.bounds.max.y : transform.position.y + 1f;
        Vector3 start = new Vector3(transform.position.x, headY, transform.position.z);

        if (HookTargeted ||
            !Physics.Raycast(start, Vector3.up, out RaycastHit hit, whipCeilingRange, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Nothing to show
            if (whipPreview != null) whipPreview.enabled = false;
            whipPreviewTime = 0f;
            return;
        }

        Vector3 end = hit.point;

        if (whipPreview == null) CreateWhipPreview();

        float riseTime = Mathf.Max(previewRiseTime, 0.01f);
        whipPreviewTime += Time.deltaTime;
        if (whipPreviewTime >= riseTime + previewHoldTime) whipPreviewTime = 0f;

        float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(whipPreviewTime / riseTime));

        whipPreview.enabled = true;
        whipPreview.SetPosition(0, hand);
        whipPreview.SetPosition(1, Vector3.Lerp(hand, end, reach));
    }

    void CreateWhipPreview()
    {
        GameObject go = new GameObject("Whip Preview");
        whipPreview = go.AddComponent<LineRenderer>();
        whipPreview.positionCount = 2;
        whipPreview.useWorldSpace = true;
        whipPreview.widthMultiplier = whipRopeWidth;
        whipPreview.numCapVertices = 2;
        whipPreview.material = previewMaterial != null ? previewMaterial : new Material(Shader.Find("Sprites/Default"));
        whipPreview.startColor = previewColor;
        whipPreview.endColor = previewColor;
    }

    // ---------------------------------------------------------------- firecrackers

    void Firecrackers()
    {
        if (firecrackerPrefab == null)
        {
            Debug.LogWarning("Firecrackers: no firecracker prefab assigned on the FirepowerSystem");
            return;
        }

        if (targeting != null)
            targeting.AimMuzzle();

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Transform aim = muzzle != null ? muzzle : transform;

        Vector3[] directions = GetFirecrackerDirections(aim);

        Projectile[] shots = new Projectile[directions.Length];

        for (int i = 0; i < directions.Length; i++)
        {
            float randomX = Random.Range(-firecrackerDirectionRandomness, firecrackerDirectionRandomness);

            float randomY = Random.Range(-firecrackerDirectionRandomness, firecrackerDirectionRandomness);

            Quaternion randomRotation = Quaternion.LookRotation(directions[i]) * Quaternion.Euler(randomX, randomY, 0f);

            Vector3 randomizedDirection = randomRotation * Vector3.forward;

            float randomizedFuse = firecrackerFuse +
                Random.Range(-firecrackerFuseRandomness, firecrackerFuseRandomness);

            Projectile p = Instantiate(firecrackerPrefab, origin, randomRotation);

            // Launch first: it sets the fuse to the prefab's own, which SetFuse then overrides
            p.Launch(randomizedDirection * firecrackerSpeed);
            p.SetFuse(randomizedFuse);

            shots[i] = p;
        }

        // The shots start on top of each other, so they must not set each other off
        for (int i = 0; i < shots.Length; i++)
        {
            Collider a = shots[i].GetComponent<Collider>();
            if (a == null || !a.enabled) continue;

            for (int j = i + 1; j < shots.Length; j++)
            {
                Collider b = shots[j].GetComponent<Collider>();
                if (b != null && b.enabled) Physics.IgnoreCollision(a, b);
            }
        }

        // The shots never collide with the player either: walking into a landed one shouldn't set it
        // off, and the player's hurtbox sits on its own layer, so the Ground Mask doesn't cover it
        Collider[] playerColliders = GetComponentsInChildren<Collider>();

        foreach (Projectile shot in shots)
        {
            Collider shotCollider = shot.GetComponent<Collider>();
            if (shotCollider == null || !shotCollider.enabled) continue;

            foreach (Collider mine in playerColliders)
                if (mine.enabled) Physics.IgnoreCollision(shotCollider, mine);
        }
    }

    Vector3[] GetFirecrackerDirections(Transform aim)
    {
        float spread = 8f;

        Vector2[] offsets =
        {
        Vector2.zero,
        new Vector2(-spread, 0f),
        new Vector2(spread, 0f),
        new Vector2(0f, spread),
        new Vector2(0f, -spread)
    };

        Vector3[] directions = new Vector3[offsets.Length];

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2 offset = offsets[i];

            directions[i] = (
                aim.forward +
                aim.right * Mathf.Tan(offset.x * Mathf.Deg2Rad) +
                aim.up * Mathf.Tan(offset.y * Mathf.Deg2Rad)
            ).normalized;
        }

        return directions;
    }

    // ---------------------------------------------------------------- firewall

    void Firewall()
    {
        // Sidescroller: a barrier around the player instead of nodes and a wall
        if (movement != null && movement.SidescrollerMode)
        {
            PlaceBarrier();
            return;
        }

        DungeonRoom room = DungeonRoom.Current;
        FirewallPlan plan = MakePlan();

        if (plan.Blocked)
        {
            Debug.Log(plan.tooFar ? "Firewall: too far from the nearest node" : "Firewall: the player is in the way");
            return;
        }

        FirewallNode a = AddNode(plan.nodeA, room);
        FirewallNode b = plan.twoNodes ? AddNode(plan.nodeB, room) : null;

        if (plan.hasWall)
        {
            FirewallWall wall = FirewallWall.Create(wallHeight, wallThickness, riseTime, wallMaterial, wallColor);
            wall.SetEnds(plan.wallFrom, plan.wallTo);
            wall.EnableDamage(barrierDamage, barrierHitCooldown);
            if (room != null) wall.transform.SetParent(room.transform, true);
            wall.Rise();
            wall.SetLifetime(wallLifetime, wallLowerTime);

            placedWalls.Add(new PlacedWall
            {
                wall = wall,
                from = plan.twoNodes ? a : plan.joinTo,
                to = plan.twoNodes ? b : a
            });
        }

        TrimNodes(room);

        // Things made at runtime should follow the room's visibility
        if (room != null) room.RefreshVisibility();

        Debug.Log(plan.twoNodes
            ? "Firewall: first wall between two nodes"
            : "Firewall: new node, wall to the nearest node");
    }

    // Sidescroller firewall: four walls around the player, each a quad facing inward. The camera only
    // draws the side of a quad that faces it, so the near wall and the side walls vanish and it
    // reads as one wall going up behind the player. Enemies that touch any of them are hurt (and
    // knocked back by their own AI). Hurtboxes are stretched in the sidescroller, so these aren't.
    void PlaceBarrier()
    {
        DungeonRoom room = DungeonRoom.Current;
        Vector3 center = SnapToGround(transform.position);
        (Vector3 from, Vector3 to)[] sides = BarrierSides(center);

        // A node at each corner. Side i runs from corner i to corner i + 1.
        FirewallNode[] corners = new FirewallNode[sides.Length];
        for (int i = 0; i < sides.Length; i++)
            corners[i] = AddNode(sides[i].from, room);

        for (int i = 0; i < sides.Length; i++)
        {
            FirewallWall wall = FirewallWall.Create(wallHeight, wallThickness, riseTime,
                                                    wallMaterial, wallColor, FirewallShape.Quad);
            wall.SetEnds(sides[i].from, sides[i].to);
            wall.FaceToward(center);
            wall.EnableDamage(barrierDamage, barrierHitCooldown);

            if (room != null) wall.transform.SetParent(room.transform, true);
            wall.Rise();
            wall.SetLifetime(wallLifetime, wallLowerTime);

            // The corner nodes go when their walls do (PruneExpired), including on a mode switch
            placedWalls.Add(new PlacedWall
            {
                wall = wall,
                from = corners[i],
                to = corners[(i + 1) % corners.Length]
            });
        }

        if (room != null) room.RefreshVisibility();

        Debug.Log("Firewall: barrier around the player");
    }

    // The four sides of the barrier around a point: back, right, front, left (relative to the view)
    (Vector3 from, Vector3 to)[] BarrierSides(Vector3 center)
    {
        Vector3 view = Vector3.ProjectOnPlane(PerspectiveShiftSystem.CurrentViewForward, Vector3.up);
        view = view.sqrMagnitude > 0.0001f ? view.normalized : Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, view) * barrierHalfWidth;
        Vector3 depth = view * barrierHalfDepth;

        // Corners rest on the ground, like the top-down nodes
        Vector3 backLeft = SnapToGround(center - right + depth);
        Vector3 backRight = SnapToGround(center + right + depth);
        Vector3 frontRight = SnapToGround(center + right - depth);
        Vector3 frontLeft = SnapToGround(center - right - depth);

        return new[]
        {
            (backLeft, backRight),
            (backRight, frontRight),
            (frontRight, frontLeft),
            (frontLeft, backLeft)
        };
    }

    // Works out where the next nodes and wall would go
    FirewallPlan MakePlan()
    {
        DungeonRoom room = DungeonRoom.Current;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        FirewallPlan plan = new FirewallPlan();

        // No nodes in this room yet: two nodes at the knight's-move spots, wall centered between them
        if (!HasNodes(room))
        {
            Vector3 ahead = transform.position + forward * (knightOffset.y * nodeUnit);
            Vector3 side = right * (knightOffset.x * nodeUnit);

            plan.twoNodes = true;
            plan.nodeA = SnapToGround(ahead - side);
            plan.nodeB = SnapToGround(ahead + side);
            plan.wallFrom = plan.nodeA;
            plan.wallTo = plan.nodeB;
            plan.hasWall = true;
            plan.inTheWay = WallHitsPlayer(plan.wallFrom, plan.wallTo);
            return plan;
        }

        // Otherwise one node in front, joined to whichever existing node is closest to it
        plan.nodeA = SnapToGround(transform.position + forward * nextNodeDistance);
        plan.joinTo = NearestNode(plan.nodeA, room);
        plan.wallFrom = plan.joinTo.position;
        plan.wallTo = plan.nodeA;
        plan.hasWall = FlatDistance(plan.wallFrom, plan.wallTo) > 0.1f;
        plan.tooFar = FlatDistance(plan.wallFrom, plan.wallTo) > maxNodeRadius;
        plan.inTheWay = plan.hasWall && WallHitsPlayer(plan.wallFrom, plan.wallTo);
        return plan;
    }

    // True if the player is standing where a wall between these two points would go
    bool WallHitsPlayer(Vector3 from, Vector3 to)
    {
        Bounds body = controller != null
            ? controller.bounds
            : new Bounds(transform.position, new Vector3(1f, 2f, 1f));

        // A player above or below the wall isn't in its way
        float baseY = (from.y + to.y) * 0.5f;
        if (body.min.y >= baseY + wallHeight || body.max.y <= baseY) return false;

        Vector2 a = new Vector2(from.x, from.z);
        Vector2 b = new Vector2(to.x, to.z);
        Vector2 p = new Vector2(body.center.x, body.center.z);

        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        float t = lengthSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq) : 0f;
        Vector2 closest = a + ab * t;

        float playerRadius = controller != null ? controller.radius : 0.5f;
        return Vector2.Distance(p, closest) < wallThickness * 0.5f + playerRadius;
    }

    FirewallNode AddNode(Vector3 position, DungeonRoom room)
    {
        GameObject visual = MakeNodeVisual("Firewall Node", nodeMaterial, nodeColor);
        visual.transform.position = position;
        if (room != null) visual.transform.SetParent(room.transform, true);

        FirewallNode node = new FirewallNode { position = position, visual = visual, room = room };
        nodes.Add(node);
        return node;
    }

    bool HasNodes(DungeonRoom room)
    {
        foreach (FirewallNode n in nodes)
            if (n.room == room) return true;

        return false;
    }

    int CountNodes(DungeonRoom room)
    {
        int count = 0;
        foreach (FirewallNode n in nodes)
            if (n.room == room) count++;

        return count;
    }

    FirewallNode NearestNode(Vector3 point, DungeonRoom room)
    {
        FirewallNode best = null;
        float bestDist = float.MaxValue;

        foreach (FirewallNode n in nodes)
        {
            if (n.room != room) continue;

            float d = FlatDistance(n.position, point);
            if (d < bestDist) { bestDist = d; best = n; }
        }

        return best;
    }

    // Too many nodes in a room: the oldest one goes, along with any wall attached to it
    void TrimNodes(DungeonRoom room)
    {
        while (CountNodes(room) > maxNodesPerRoom)
        {
            FirewallNode oldest = nodes.Find(n => n.room == room);
            if (oldest == null) return;

            RemoveNode(oldest);
        }
    }

    void RemoveNode(FirewallNode node)
    {
        for (int i = placedWalls.Count - 1; i >= 0; i--)
        {
            if (placedWalls[i].from != node && placedWalls[i].to != node) continue;

            if (placedWalls[i].wall != null) Destroy(placedWalls[i].wall.gameObject);
            placedWalls.RemoveAt(i);
        }

        if (node.visual != null) Destroy(node.visual);
        nodes.Remove(node);
    }

    // Removes every node and wall
    public void ClearFirewalls()
    {
        foreach (PlacedWall w in placedWalls)
            if (w.wall != null) Destroy(w.wall.gameObject);

        foreach (FirewallNode n in nodes)
            if (n.visual != null) Destroy(n.visual);

        placedWalls.Clear();
        nodes.Clear();
    }

    // ---------------------------------------------------------------- pillar

    // What letting go would do right now. Target held = the jump under the player. Otherwise the
    // spot in front of the player, snapped to the nearest raisable platform if one is in reach.
    PillarPlan MakePillarPlan()
    {
        PillarPlan plan = new PillarPlan();

        if (input.TargetHeld)
        {
            float feetY = controller != null ? controller.bounds.min.y : transform.position.y - 1f;
            plan.underPlayer = true;
            plan.point = new Vector3(transform.position.x, feetY, transform.position.z);
            return plan;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 spot = transform.position + forward * pillarDistance;

        plan.platform = NearestRaisable(spot);
        plan.point = SnapToGround(spot);
        return plan;
    }

    // The pillar was let go: jump, lift the platform it snapped to, or stand where it was aimed
    void PlacePillar()
    {
        PillarPlan plan = MakePillarPlan();

        if (plan.underPlayer)
        {
            PillarUnderPlayer();
            return;
        }

        if (plan.platform != null)
        {
            Raise(plan.platform);
            Debug.Log($"Pillar: raising {plan.platform.name}");
            return;
        }

        PlaceFreePillar(plan.point);
        Debug.Log("Pillar: placed");
    }

    // A pillar that stays where it was put, whether or not there is a platform around
    void PlaceFreePillar(Vector3 groundPoint)
    {
        DungeonRoom room = DungeonRoom.Current;

        FirewallWall column = CreatePillarWall(groundPoint, pillarVisualHeight, pillarMaterial, pillarColor);
        column.Rise();
        column.SetLifetime(pillarLifetime, pillarLowerTime);

        if (room != null)
        {
            column.transform.SetParent(room.transform, true);
            room.RefreshVisibility();
        }

        placedPillars.Add(new PlacedPillar { wall = column, room = room });
        TrimPillars(room);
    }

    // Too many pillars in a room: the oldest one goes
    void TrimPillars(DungeonRoom room)
    {
        while (placedPillars.FindAll(p => p.room == room).Count > maxPillarsPerRoom)
        {
            PlacedPillar oldest = placedPillars.Find(p => p.room == room);
            if (oldest == null) return;

            if (oldest.wall != null) Destroy(oldest.wall.gameObject);
            placedPillars.Remove(oldest);
        }
    }

    // Removes every placed pillar
    public void ClearPillars()
    {
        foreach (PlacedPillar p in placedPillars)
            if (p.wall != null) Destroy(p.wall.gameObject);

        placedPillars.Clear();
    }

    FirewallWall CreatePillarWall(Vector3 groundPoint, float height, Material material, Color color)
    {
        FirewallWall wall = FirewallWall.Create(height, pillarWidth, pillarRiseTime, material, color, FirewallShape.Cylinder);
        wall.SetEnds(groundPoint - Vector3.forward * (pillarWidth * 0.5f),
                     groundPoint + Vector3.forward * (pillarWidth * 0.5f));
        return wall;
    }

    void PillarUnderPlayer()
    {
        if (movement == null || !movement.Grounded)
        {
            Debug.Log("Pillar: needs the player on the ground");
            return;
        }

        float feetY = controller != null ? controller.bounds.min.y : transform.position.y - 1f;
        ShowPillar(new Vector3(transform.position.x, feetY, transform.position.z));

        movement.LaunchToHeight(pillarJumpHeight, pillarSteerHeight);
        Debug.Log($"Pillar: launch to {pillarJumpHeight}");
    }

    // The closest switched-on raisable platform to a spot, within the snap range
    PlatformSystem NearestRaisable(Vector3 point)
    {
        PlatformSystem best = null;
        float bestDist = pillarSnapRange;

        foreach (PlatformSystem p in PlatformSystem.Raisables)
        {
            if (!p.Usable) continue;

            float d = p.FlatDistanceTo(point);
            if (d <= bestDist) { bestDist = d; best = p; }
        }

        return best;
    }

    // A pillar is lifting this platform
    void Raise(PlatformSystem platform)
    {
        ActiveRaise raise = raises.Find(r => r.platform == platform);

        if (raise == null)
        {
            raises.Add(new ActiveRaise { platform = platform });
            return;
        }

        switch (raise.state)
        {
            case RaiseState.Falling:
                raise.state = RaiseState.Rising;
                raise.fallSpeed = 0f;
                break;

            case RaiseState.Held:
                raise.holdTimer = platform.HoldTime; // used again while it's up: it stays up longer
                break;
        }
    }

    void UpdateRaises()
    {
        // Frozen during a rotation, mode switch or door transition, like everything else
        if (raises.Count == 0 || PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning) return;

        for (int i = raises.Count - 1; i >= 0; i--)
        {
            ActiveRaise r = raises[i];

            if (r.platform == null)
            {
                if (r.column != null) Destroy(r.column.gameObject);
                raises.RemoveAt(i);
                continue;
            }

            if (StepRaise(r)) continue;

            // Back at rest
            if (r.column != null) Destroy(r.column.gameObject);
            raises.RemoveAt(i);
        }
    }

    // Moves one raise forward a frame. Returns false once the platform is back at rest.
    bool StepRaise(ActiveRaise r)
    {
        PlatformSystem p = r.platform;
        float dt = Time.deltaTime;
        bool rising = r.state == RaiseState.Rising;

        switch (r.state)
        {
            case RaiseState.Rising:
                r.rise = Mathf.MoveTowards(r.rise, p.RaiseHeight, p.RaiseSpeed * dt);
                if (r.rise >= p.RaiseHeight)
                {
                    r.state = RaiseState.Held;
                    r.holdTimer = p.HoldTime;
                }
                break;

            case RaiseState.Held:
                r.holdTimer -= dt;
                if (r.holdTimer <= 0f)
                {
                    r.state = RaiseState.Falling;
                    r.fallSpeed = 0f;
                }
                break;

            case RaiseState.Falling:
                r.fallSpeed += p.FallGravity * dt;
                r.rise += r.fallSpeed * dt;
                if (r.rise <= 0f) r.rise = 0f;
                break;
        }

        p.SetRaiseOffset(r.rise, rising); // only a rising platform carries the player
        UpdateRaiseColumn(r);

        return !(r.state == RaiseState.Falling && r.rise <= 0f);
    }

    // A column from the platform's resting height up to wherever it is now
    void UpdateRaiseColumn(ActiveRaise r)
    {
        bool show = r.rise > 0.01f && r.platform.IsActive;

        if (r.column == null)
        {
            if (!show) return;
            r.column = CreateRaiseColumn(r.platform);
        }

        r.column.gameObject.SetActive(show);
        if (!show) return;

        Vector3 under = r.platform.UndersidePoint;

        r.column.position = new Vector3(under.x, under.y - r.rise * 0.5f, under.z);
        r.column.rotation = Quaternion.identity;

        // Desired dimensions in world space.
        Vector3 worldScale = new Vector3(
            r.platform.PillarWidth,
            r.rise * 0.5f,
            r.platform.PillarWidth
        );

        // Convert to local scale to compensate for the parent's scale.
        Transform parent = r.column.parent;

        if (parent != null)
        {
            Vector3 parentScale = parent.lossyScale;

            r.column.localScale = new Vector3(
                worldScale.x / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
                worldScale.y / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
                worldScale.z / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f)
            );
        }
        else
        {
            r.column.localScale = worldScale;
        }
    }

    Transform CreateRaiseColumn(PlatformSystem platform)
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = platform.name + " Pillar";
        Destroy(cylinder.GetComponent<Collider>());

        Renderer renderer = cylinder.GetComponent<Renderer>();
        if (platform.PillarMaterial != null)
            renderer.sharedMaterial = platform.PillarMaterial;

        FirewallWall.Tint(renderer, platform.PillarColor);

        if (platform.transform.parent != null)
            cylinder.transform.SetParent(platform.transform.parent, true);

        DungeonRoom room = platform.GetComponentInParent<DungeonRoom>();
        if (room != null)
            room.RefreshVisibility();

        return cylinder.transform;
    }

    // A plain column that rises out of the ground and goes away after a moment
    void ShowPillar(Vector3 groundPoint)
    {
        FirewallWall column = FirewallWall.Create(pillarVisualHeight, pillarWidth, pillarRiseTime,
                                                  pillarMaterial, pillarColor, FirewallShape.Cylinder);
        column.SetEnds(groundPoint - Vector3.forward * (pillarWidth * 0.5f),
                       groundPoint + Vector3.forward * (pillarWidth * 0.5f));
        column.Rise();

        DungeonRoom room = DungeonRoom.Current;
        if (room != null)
        {
            column.transform.SetParent(room.transform, true);
            room.RefreshVisibility();
        }

        column.SetLifetime(pillarVisualTime, pillarLowerTime);
    }

    // ---------------------------------------------------------------- previews

    void ShowPreview()
    {
        if (aimKind == AimKind.Firecracker)
        {
            ShowFirecrackerPreview();
            return;
        }
        if (aimKind == AimKind.Pillar)
        {
            ShowPillarPreview();
            return;
        }
        if (aimKind == AimKind.Whip)
        {
            ShowWhipPreview();
            previewShown = true;
            return;
        }
        if (movement != null && movement.SidescrollerMode)
        {
            ShowBarrierPreview();
            return;
        }

        EnsurePreview();
        FirewallPlan plan = MakePlan();

        SetPreviewBlocked(plan.Blocked);

        previewNodeA.SetActive(true);
        previewNodeA.transform.position = plan.nodeA;

        previewNodeB.SetActive(plan.twoNodes);
        if (plan.twoNodes) previewNodeB.transform.position = plan.nodeB;

        bool showWall = plan.hasWall && !plan.Blocked;
        previewWall.gameObject.SetActive(showWall);
        if (showWall)
        {
            previewWall.SetEnds(plan.wallFrom, plan.wallTo);

            // The preview wall rises out of the ground each time the preview appears
            if (!previewShown)
            {
                previewWall.SetRiseTime(previewRiseTime);
                previewWall.StartLoop(previewHoldTime, previewLowerTime, previewGapTime);
            }
        }

        previewShown = true;
    }

    void HidePreview()
    {
        previewShown = false;

        if (previewNodeA != null) previewNodeA.SetActive(false);
        if (previewNodeB != null) previewNodeB.SetActive(false);
        if (previewWall != null) previewWall.gameObject.SetActive(false);
        if (pillarPreview != null) pillarPreview.gameObject.SetActive(false);
        if (whipPreview != null) whipPreview.enabled = false;
        whipPreviewTime = 0f;
        if (barrierPreview != null)
            foreach (FirewallWall w in barrierPreview)
                if (w != null) w.gameObject.SetActive(false);
        if (firecrackerArea != null) firecrackerArea.SetActive(false);
    }

    // One flat disc on the ground where the five shots will land. It is centered between where the
    // five paths come down, and big enough to hold them all plus the random spread on top.
    void ShowFirecrackerPreview()
    {
        if (firecrackerPrefab == null) return;

        if (targeting != null)
            targeting.AimMuzzle();

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Transform aim = muzzle != null ? muzzle : transform;
        Vector3[] directions = GetFirecrackerDirections(aim);

        // Where each of the five paths comes down
        Vector3[] landings = new Vector3[directions.Length];
        Vector3 center = Vector3.zero;

        for (int i = 0; i < directions.Length; i++)
        {
            landings[i] = firecrackerPrefab.PredictImpact(
                origin,
                directions[i] * firecrackerSpeed,
                1 << gameObject.layer   // not the player's own layer
            );

            center += landings[i];
        }

        center /= landings.Length;

        // Wide enough for every landing point, plus what the random aim adds at that distance
        float radius = 0f;
        foreach (Vector3 landing in landings)
            radius = Mathf.Max(radius, FlatDistance(center, landing));

        radius += Vector3.Distance(origin, center) * Mathf.Tan(firecrackerDirectionRandomness * Mathf.Deg2Rad);
        radius = Mathf.Max(radius, firecrackerAreaMinRadius);

        if (firecrackerArea == null)
        {
            firecrackerArea = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            firecrackerArea.name = "Firecracker Landing Area";
            Destroy(firecrackerArea.GetComponent<Collider>());

            Renderer renderer = firecrackerArea.GetComponent<Renderer>();

            if (previewMaterial != null)
                renderer.sharedMaterial = previewMaterial;

            FirewallWall.Tint(renderer, previewColor);
        }

        // A cylinder is 1 wide and 2 tall, so this is a thin disc lying just above the ground
        firecrackerArea.transform.position = center + Vector3.up * 0.02f;
        firecrackerArea.transform.rotation = Quaternion.identity;
        firecrackerArea.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        firecrackerArea.SetActive(true);

        previewShown = true;
    }

    // A ghost pillar where letting go would put it: in front of the player, on the platform it
    // would lift, or under the player when Target is held
    void ShowPillarPreview()
    {
        if (pillarPreview == null)
        {
            pillarPreview = FirewallWall.Create(pillarVisualHeight, pillarWidth, pillarRiseTime,
                                                previewMaterial, previewColor, FirewallShape.Cylinder);
            pillarPreview.name = "Pillar Preview";
        }

        PillarPlan plan = MakePillarPlan();
        Vector3 basePoint = plan.point;
        float height = pillarVisualHeight;

        if (plan.platform != null)
        {
            // The column that would hold the platform up: from its resting underside to its raised one
            Vector3 under = plan.platform.UndersidePoint;
            basePoint = new Vector3(under.x, under.y - plan.platform.CurrentRise, under.z);
            height = plan.platform.RaiseHeight;
        }

        pillarPreview.gameObject.SetActive(true);
        pillarPreview.SetHeight(height);
        pillarPreview.SetEnds(basePoint - Vector3.forward * (pillarWidth * 0.5f),
                              basePoint + Vector3.forward * (pillarWidth * 0.5f));

        // Rises out of the ground each time the preview appears
        if (!previewShown)
        {
            pillarPreview.SetRiseTime(previewRiseTime);
            pillarPreview.StartLoop(previewHoldTime, previewLowerTime, previewGapTime);
        }

        previewShown = true;
    }

    // Four ghost walls around the player that follow them, each rising on a loop like the other previews
    void ShowBarrierPreview()
    {
        Vector3 center = SnapToGround(transform.position);
        (Vector3 from, Vector3 to)[] sides = BarrierSides(center);

        if (barrierPreview == null)
        {
            barrierPreview = new FirewallWall[sides.Length];

            for (int i = 0; i < sides.Length; i++)
            {
                barrierPreview[i] = FirewallWall.Create(wallHeight, wallThickness, riseTime,
                                                        previewMaterial, previewColor, FirewallShape.Quad);
                barrierPreview[i].name = "Firewall Barrier Preview";
            }
        }

        for (int i = 0; i < sides.Length; i++)
        {
            FirewallWall wall = barrierPreview[i];

            wall.gameObject.SetActive(true);
            wall.SetEnds(sides[i].from, sides[i].to);
            wall.FaceToward(center);

            if (!previewShown)
            {
                wall.SetRiseTime(previewRiseTime);
                wall.StartLoop(previewHoldTime, previewLowerTime, previewGapTime);
            }
        }

        previewShown = true;
    }

    // Red ghost nodes while the firewall can't be placed
    void SetPreviewBlocked(bool blocked)
    {
        if (blocked == previewTintBlocked) return;
        previewTintBlocked = blocked;

        Color color = blocked ? blockedColor : previewColor;
        FirewallWall.Tint(previewNodeA.GetComponentInChildren<Renderer>(true), color);
        FirewallWall.Tint(previewNodeB.GetComponentInChildren<Renderer>(true), color);
    }

    void EnsurePreview()
    {
        if (previewWall != null) return;

        previewNodeA = MakeNodeVisual("Firewall Node Preview", previewMaterial, previewColor);
        previewNodeB = MakeNodeVisual("Firewall Node Preview", previewMaterial, previewColor);
        previewWall = FirewallWall.Create(wallHeight, wallThickness, riseTime, previewMaterial, previewColor);
        previewWall.name = "Firewall Wall Preview";
    }

    // A glowing ball resting on the ground
    GameObject MakeNodeVisual(string objectName, Material material, Color color)
    {
        GameObject root = new GameObject(objectName);

        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ember";
        Destroy(ball.GetComponent<Collider>());
        ball.transform.SetParent(root.transform, false);
        ball.transform.localPosition = Vector3.up * (nodeSize * 0.5f);
        ball.transform.localScale = Vector3.one * nodeSize;

        Renderer renderer = ball.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
        FirewallWall.Tint(renderer, color);

        return root;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
