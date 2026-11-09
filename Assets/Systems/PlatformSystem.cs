using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class PlatformSystem : MonoBehaviour
{
    // Every raisable platform that is switched on, so a pillar can find the nearest one
    public static readonly List<PlatformSystem> Raisables = new List<PlatformSystem>();

    [SerializeField] float sidescrollerDepth = 30f;
    [SerializeField] CharacterController player;
    [SerializeField] bool oneWay = true;
    [SerializeField] float topTolerance = 0.2f; // how far below the top still counts as "below"
    [SerializeField] bool snapOnRotate = true;

    // Settings only: the player's Pillar (FirepowerSystem) reads these and does the lifting
    [Header("Raisable (lifted by the player's Pillar)")]
    [SerializeField] bool raisable;             // tick to let a pillar lift this platform; the rest of this section only matters then
    [SerializeField] float raiseHeight = 3f;    // how far above its resting spot the pillar lifts it
    [SerializeField] float raiseSpeed = 10f;    // units per second while rising
    [SerializeField] float holdTime = 4f;       // how long it stays up before it falls
    [SerializeField] float fallGravity = -25f;  // how it falls back down
    [SerializeField] float pillarWidth = 0.8f;  // thickness of the pillar drawn under it
    [SerializeField] Material pillarMaterial;   // optional; the default primitive material is used if empty
    [SerializeField] Color pillarColor = new Color(0.55f, 0.5f, 0.45f, 1f);

    BoxCollider box;
    Vector3 originalSize;

    bool sidescroller;
    bool ignoring;
    bool active = true;

    Vector3 restLocalPosition;
    float rise;       // how far above the resting spot it is right now

    // Raise settings, for the pillar to read
    public float RaiseHeight => raiseHeight;
    public float RaiseSpeed => raiseSpeed;
    public float HoldTime => holdTime;
    public float FallGravity => fallGravity;
    public float PillarWidth => pillarWidth;
    public Material PillarMaterial => pillarMaterial;
    public Color PillarColor => pillarColor;
    public float CurrentRise => rise;

    // Can a pillar use this platform right now? False while its room is hidden.
    public bool Usable => raisable && active && isActiveAndEnabled && box != null && box.enabled;
    public bool IsActive => active;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        originalSize = box.size;
        restLocalPosition = transform.localPosition;

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.GetComponent<CharacterController>();
        }

        if (raisable)
        {
            // A collider that moves needs a kinematic body
            if (!TryGetComponent(out Rigidbody rb)) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!active || !oneWay || !sidescroller) return;

        bool feetBelowTop = player.bounds.min.y < box.bounds.max.y - topTolerance;
        SetIgnore(feetBelowTop);
    }

    void OnEnable()
    {
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        PerspectiveShiftSystem.RotationStarting += OnRotationStarting;
        if (raisable) Raisables.Add(this);
    }

    void OnDisable()
    {
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;
        PerspectiveShiftSystem.RotationStarting -= OnRotationStarting;
        Raisables.Remove(this);
        SetIgnore(false);
    }

    void OnPerspectiveChanged(bool sidescrollerMode, Vector3 viewForward)
    {
        sidescroller = sidescrollerMode;
        if (!sidescrollerMode) SetIgnore(false);

        if (!sidescrollerMode)
        {
            box.size = originalSize;
            return;
        }

        // Which local axis of this box points along the view direction?
        Vector3 local = transform.InverseTransformDirection(viewForward);
        int axis = 0;
        if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
        if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

        Vector3 size = originalSize;
        size[axis] = Mathf.Max(originalSize[axis],
                               sidescrollerDepth / Mathf.Abs(transform.lossyScale[axis]));
        box.size = size;
    }

    void SetIgnore(bool value)
    {
        if (value == ignoring) return;
        ignoring = value;
        Physics.IgnoreCollision(player, box, value);
    }

    void OnRotationStarting(Vector3 oldViewForward)
    {
        if (!active || !snapOnRotate || !sidescroller) return;

        // Standing on me? Feet at my top surface...
        if (Mathf.Abs(player.bounds.min.y - box.bounds.max.y) > 0.15f) return;

        // ...and inside my (widened) footprint
        Vector3 p = player.bounds.center;
        p.y = box.bounds.center.y;
        if (!box.bounds.Contains(p)) return;

        // Slide the player to my center along the depth axis that is about to become their movement axis
        float depth = Vector3.Dot(box.bounds.center - player.bounds.center, oldViewForward);
        player.Move(oldViewForward * depth);
    }

    // Switched off by the dungeon map while the player is in another room
    public void SetColliderActive(bool value)
    {
        if (box == null) box = GetComponent<BoxCollider>(); // Awake hasn't run if this object is inactive

        // Clear the one-way "ignore the player" state while the collider is still enabled,
        // so it's recalculated cleanly when the platform comes back
        if (!value) SetIgnore(false);

        active = value;
        box.enabled = value;
    }

    // The platform's real extent along an axis (not stretched for sidescroller), as distances
    // along that axis. Used to ease the player to the platform's depth when they land on it.
    public void GetRealRange(Vector3 axis, out float min, out float max)
    {
        Vector3 s = transform.lossyScale;
        Vector3 half = new Vector3(originalSize.x * Mathf.Abs(s.x),
                                   originalSize.y * Mathf.Abs(s.y),
                                   originalSize.z * Mathf.Abs(s.z)) * 0.5f;

        float extent = Mathf.Abs(Vector3.Dot(transform.right * half.x, axis))
                     + Mathf.Abs(Vector3.Dot(transform.up * half.y, axis))
                     + Mathf.Abs(Vector3.Dot(transform.forward * half.z, axis));

        float center = Vector3.Dot(transform.TransformPoint(box.center), axis);
        min = center - extent;
        max = center + extent;
    }

    // ---------------------------------------------------------------- pillar

    // Distance on the ground plane from a point to this platform's footprint
    public float FlatDistanceTo(Vector3 point)
    {
        Bounds b = box.bounds;
        point.y = b.center.y;
        return Vector3.Distance(b.ClosestPoint(point), point);
    }

    // Where the platform sits when nothing is holding it up
    Vector3 RestWorldPosition()
        => transform.parent != null ? transform.parent.TransformPoint(restLocalPosition) : restLocalPosition;

    // Puts the platform this far above its resting spot. Used by the pillar. When it is
    // rising, whoever is standing on it goes up with it.
    public void SetRaiseOffset(float newRise, bool carryPlayer)
    {
        float delta = newRise - rise;
        if (delta == 0f) return;

        bool carrying = carryPlayer && delta > 0f && PlayerOnTop(); // decided before it moves

        rise = newRise;
        transform.position = RestWorldPosition() + Vector3.up * rise;
        Physics.SyncTransforms(); // so the player's next move sees the platform where it is now

        if (carrying) player.Move(Vector3.up * delta);
    }

    // Feet on my top surface, and inside my footprint (widened in sidescroller)
    bool PlayerOnTop()
    {
        if (!active || player == null || !player.enabled) return false;

        Bounds b = box.bounds;
        if (Mathf.Abs(player.bounds.min.y - b.max.y) > 0.2f) return false;

        Vector3 p = player.bounds.center;
        p.y = b.center.y;
        return b.Contains(p);
    }

    // The middle of the platform's underside right now: where a pillar column would end
    public Vector3 UndersidePoint
    {
        get
        {
            Vector3 center = transform.TransformPoint(box.center);
            float bottom = center.y - originalSize.y * Mathf.Abs(transform.lossyScale.y) * 0.5f;
            return new Vector3(center.x, bottom, center.z);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        raiseHeight = Mathf.Max(0f, raiseHeight);
        raiseSpeed = Mathf.Max(0.1f, raiseSpeed);
        holdTime = Mathf.Max(0f, holdTime);
    }

    // For raisable platforms: the raised spot, drawn in the Scene view as an orange ghost of the
    // platform up where it will stop, with the pillar that would hold it
    void OnDrawGizmos()
    {
        if (!raisable) return;

        BoxCollider b = box != null ? box : GetComponent<BoxCollider>();
        if (b == null) return;

        Vector3 rest = Application.isPlaying ? RestWorldPosition() : transform.position;
        Vector3 raised = rest + Vector3.up * raiseHeight;

        // The platform at its raised height
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(raised, transform.rotation, transform.lossyScale);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.2f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        Gizmos.DrawWireCube(b.center, b.size);
        Gizmos.matrix = old;

        // The pillar under it, from the resting bottom up to the raised bottom
        Vector3 restCenter = rest + transform.TransformVector(b.center);
        float halfHeight = b.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f;
        float bottom = restCenter.y - halfHeight;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
        Gizmos.DrawWireCube(new Vector3(restCenter.x, bottom + raiseHeight * 0.5f, restCenter.z),
                            new Vector3(pillarWidth, raiseHeight, pillarWidth));
        Gizmos.DrawLine(restCenter, restCenter + Vector3.up * raiseHeight);
    }

    void OnDrawGizmosSelected()
    {
        if (!raisable) return;

        Vector3 rest = Application.isPlaying ? RestWorldPosition() : transform.position;
        UnityEditor.Handles.Label(rest + Vector3.up * (raiseHeight + 0.5f), $"Raise {raiseHeight:0.##}");
    }
#endif

}
