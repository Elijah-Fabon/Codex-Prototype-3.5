using UnityEngine;
using UnityEngine.UI;

public enum TargetKind { None, General, Enemy, Grid }

// Lives on the player. Picks what the player is locked onto: enemies first, otherwise a match
// grid. Enemies and grids are found with layer masks, so give each its own layer.
public class TargetingSystem : MonoBehaviour
{
    [SerializeField] InputTrackerSystem input;

    [Header("Targeting Image")]
    [SerializeField] Image targetingImage;
    [SerializeField] Camera targetingCamera;
    [SerializeField] Vector3 imageWorldOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Targets (put enemies and puzzle grids on their own layers)")]
    [SerializeField] LayerMask enemyMask;
    [SerializeField] LayerMask gridMask;
    [SerializeField] float enemyRange = 10f;
    [SerializeField] float gridRange = 4f;
    [SerializeField] float sidescrollerDepth = 30f;  // how far in depth targets count in sidescroller
    [SerializeField] float keepTargetMargin = 1.15f; // a target is kept until it's this far beyond its range
    [SerializeField] FirepowerSystem firepowers;
    
    [Header("Hooks (the whip grabs these)")]
    [SerializeField] LayerMask hookMask;
    [SerializeField] float hookRange = 8f;
    [SerializeField] LayerMask hookBlockMask; // layers that block the line to a hook (ground, walls); leave empty to skip the check

    public Transform EnemyTarget { get; private set; }
    public MatchGridSystem GridTarget { get; private set; }
    public HookPoint HookTarget { get; private set; }

    public TargetKind Kind => EnemyTarget != null ? TargetKind.Enemy
                            : HookTarget != null ? TargetKind.General
                            : GridTarget != null ? TargetKind.Grid
                            : IsTargeting ? TargetKind.General
                            : TargetKind.None;

    public bool IsTargeting => input != null && input.TargetHeld;

    public bool IsTargetingGrid => EnemyTarget == null && GridTarget != null;

    public event System.Action TargetChanged;

    int chainButtons; // the equip buttons that were held when the chain started

    Transform muzzle;                            // the player's muzzle (from the EquipSystem)
    Quaternion muzzleRest = Quaternion.identity; // where it sits when nothing is targeted

    void Reset() => input = FindFirstObjectByType<InputTrackerSystem>();

    void Awake()
    {
        EquipSystem equip = GetComponent<EquipSystem>();
        muzzle = equip != null ? equip.Muzzle : null;
        if (muzzle != null) muzzleRest = muzzle.localRotation;

        if (firepowers == null)
            firepowers = GetComponent<FirepowerSystem>();

        if (input == null) input = FindFirstObjectByType<InputTrackerSystem>();

        if (targetingCamera == null)
            targetingCamera = Camera.main;

        if (targetingImage != null)
            targetingImage.gameObject.SetActive(false);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    void OnEnable() => input.DPadPressed += OnDPad;

    void OnDisable()
    {
        input.DPadPressed -= OnDPad;
        SetTargets(null, null);
        SetHook(null);
    }

    // Update is called once per frame
    void Update()
    {
        // While a chain is being drawn the grid stays locked, until every button that started it is released
        if (GridTarget != null && GridTarget.IsChaining)
        {
            if ((HeldButtons() & chainButtons) != 0) return;
            GridTarget.EndChain();
        }

        // Targeting only operates while the Target button is held.
        if (!IsTargeting)
        {
            SetTargets(null, null);
            SetHook(null);
            UpdateTargetingImage();
            return;
        }

        Transform enemy = FindEnemy();
        HookPoint hook = enemy != null ? null : FindHook();

        MatchGridSystem grid = enemy != null || hook != null ||
                       (firepowers != null && firepowers.IsUsingFirepower)
                       ? null : FindGrid();

        SetTargets(enemy, grid);
        SetHook(hook);

        UpdateTargetingImage();
    }

    void LateUpdate() => AimMuzzle();

    // Points the muzzle at whatever is targeted. With nothing targeted it goes back to where it
    // normally sits. Returns true while it is aimed at something. Things that fire from the muzzle
    // call this first, so they never use last frame's aim.
    public bool AimMuzzle()
    {
        if (muzzle == null) return false;

        if (TryGetAimPoint(out Vector3 point))
        {
            Vector3 to = point - muzzle.position;

            // In the sidescroller everything is on the player's plane, so depth doesn't count
            if (PerspectiveShiftSystem.CurrentSidescroller)
                to = Vector3.ProjectOnPlane(to, PerspectiveShiftSystem.CurrentViewForward);

            if (to.sqrMagnitude > 0.0001f)
            {
                muzzle.rotation = Quaternion.LookRotation(to);
                return true;
            }
        }

        muzzle.localRotation = muzzleRest;
        return false;
    }

    // What the muzzle should point at: the targeted enemy, else the match grid, else the hook
    bool TryGetAimPoint(out Vector3 point)
    {
        if (EnemyTarget != null)
        {
            Collider body = EnemyTarget.GetComponent<Collider>();
            point = body != null ? body.bounds.center : EnemyTarget.position + Vector3.up;
            return true;
        }

        if (GridTarget != null)
        {
            point = GridTarget.transform.position;
            return true;
        }

        if (HookTarget != null)
        {
            point = HookTarget.AnchorPoint;
            return true;
        }

        point = default;
        return false;
    }

    // The rosary calls this when it's used while a grid is targeted
    public bool TryBeginChain()
    {
        if (GridTarget == null || !GridTarget.BeginChain()) return false;

        chainButtons = HeldButtons();
        return true;
    }

    void OnDPad(Vector2Int dir)
    {
        // The dungeon map screen and transitions own the D-pad while they're running
        if (DungeonMapSystem.IsEditing || Door.IsTransitioning || PerspectiveShiftSystem.IsTransitioning) return;

        if (IsTargetingGrid) GridTarget.MoveCursor(dir);
    }

    // ---------------------------------------------------------------- finding targets

    Transform FindEnemy()
    {
        // Keep the current one while it's still in range
        if (EnemyTarget != null && Distance(EnemyTarget.position) <= enemyRange * keepTargetMargin)
            return EnemyTarget;

        Transform best = null;
        float bestDist = enemyRange;

        foreach (Collider hit in Query(enemyMask, enemyRange))
        {
            EnemyAISystem enemy = hit.GetComponentInParent<EnemyAISystem>();
            if (enemy == null || !InCurrentRoom(enemy)) continue;

            float d = Distance(hit.bounds.ClosestPoint(transform.position));
            if (d < bestDist) { bestDist = d; best = enemy.transform; }
        }

        return best;
    }

    MatchGridSystem FindGrid()
    {
        if (GridTarget != null && GridTarget.CanBeTargeted &&
            Distance(GridTarget.ClosestPoint(transform.position)) <= gridRange * keepTargetMargin)
            return GridTarget;

        MatchGridSystem best = null;
        float bestDist = gridRange;

        foreach (Collider hit in Query(gridMask, gridRange))
        {
            MatchGridSystem grid = hit.GetComponentInParent<MatchGridSystem>();
            if (grid == null || !grid.CanBeTargeted || !InCurrentRoom(grid)) continue;

            float d = Distance(grid.ClosestPoint(transform.position));
            if (d < bestDist) { bestDist = d; best = grid; }
        }

        return best;
    }

    HookPoint FindHook()
    {
        // Keep the current one while it's still in range and in sight
        if (HookTarget != null &&
            Distance(HookTarget.AnchorPoint) <= hookRange * keepTargetMargin &&
            HasLineTo(HookTarget))
            return HookTarget;

        HookPoint best = null;
        float bestDist = hookRange;

        foreach (Collider hit in Query(hookMask, hookRange))
        {
            HookPoint hook = hit.GetComponentInParent<HookPoint>();
            if (hook == null || !InCurrentRoom(hook) || !HasLineTo(hook)) continue;

            float d = Distance(hook.AnchorPoint);
            if (d < bestDist) { bestDist = d; best = hook; }
        }

        return best;
    }

    // Nothing solid between the player and the hook (skipped if no block layers are set)
    bool HasLineTo(HookPoint hook)
    {
        if (hookBlockMask.value == 0) return true;

        Vector3 from = transform.position;
        Vector3 to = hook.AnchorPoint;

        // In sidescroller only the player's plane counts, not the hook's depth
        if (PerspectiveShiftSystem.CurrentSidescroller)
        {
            Vector3 v = PerspectiveShiftSystem.CurrentViewForward.normalized;
            to -= v * Vector3.Dot(to - from, v);
        }

        return !Physics.Linecast(from, to, hookBlockMask, QueryTriggerInteraction.Ignore);
    }

    void SetTargets(Transform enemy, MatchGridSystem grid)
    {
        bool changed = false;

        if (grid != GridTarget)
        {
            if (GridTarget != null) GridTarget.Release();
            GridTarget = grid;
            if (grid != null) grid.Acquire(transform.position);
            changed = true;
        }

        if (enemy != EnemyTarget)
        {
            EnemyTarget = enemy;
            changed = true;
        }

        if (changed) TargetChanged?.Invoke();
    }

    void SetHook(HookPoint hook)
    {
        if (hook == HookTarget) return;

        if (HookTarget != null) HookTarget.SetTargeted(false);
        HookTarget = hook;
        if (hook != null) hook.SetTargeted(true);

        TargetChanged?.Invoke();
    }

    // Things in other rooms aren't targetable
    static bool InCurrentRoom(Component c)
    {
        DungeonRoom room = c.GetComponentInParent<DungeonRoom>();
        return room == null || DungeonRoom.Current == null || room == DungeonRoom.Current;
    }

    // In sidescroller the player is locked to a plane, so look along the view axis as well
    Collider[] Query(LayerMask mask, float range)
    {
        Vector3 p = transform.position;

        if (PerspectiveShiftSystem.CurrentSidescroller)
        {
            Vector3 depth = PerspectiveShiftSystem.CurrentViewForward.normalized * sidescrollerDepth;
            return Physics.OverlapCapsule(p - depth, p + depth, range, mask, QueryTriggerInteraction.Collide);
        }

        return Physics.OverlapSphere(p, range, mask, QueryTriggerInteraction.Collide);
    }

    // Distance to a point, ignoring depth in sidescroller
    float Distance(Vector3 point)
    {
        Vector3 d = point - transform.position;
        if (PerspectiveShiftSystem.CurrentSidescroller)
            d = Vector3.ProjectOnPlane(d, PerspectiveShiftSystem.CurrentViewForward);

        return d.magnitude;
    }

    int HeldButtons()
    {
        int mask = 0;
        foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            if (input.IsEquipHeld(slot)) mask |= 1 << (int)slot;

        return mask;
    }

    void UpdateTargetingImage()
    {
        if (targetingImage == null)
            return;

        // Grids use block highlighting instead of the reticle.
        bool visible = input.TargetHeld && GridTarget == null &&
                       (EnemyTarget != null || HookTarget != null);

        if (!visible)
        {
            targetingImage.gameObject.SetActive(false);
            return;
        }

        if (targetingCamera == null)
            targetingCamera = Camera.main;

        if (targetingCamera == null)
            return;

        Vector3 targetPosition = EnemyTarget != null
        ? EnemyTarget.position
        : HookTarget.AnchorPoint;

        Vector3 screenPosition = targetingCamera.WorldToScreenPoint(
            targetPosition + imageWorldOffset
        );

        bool onScreen = screenPosition.z > 0f;

        targetingImage.gameObject.SetActive(onScreen);

        if (onScreen)
            targetingImage.rectTransform.position = screenPosition;
    }
}
