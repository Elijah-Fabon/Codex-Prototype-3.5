using UnityEngine;

// A trigger zone that switches the game mode when the player is fully inside it, and gives the
// fez back once the player is fully outside again. While they straddle the edge, nothing changes.
// While the player is inside, they can't use the fez themselves (no toggling the perspective),
// and rotating the view is only allowed if you tick Allow Rotation.
// The volume has to be bigger than the player in every direction, or they can never be fully inside.
[RequireComponent(typeof(Collider))]
public class ModeSwitchVolume : MonoBehaviour
{
    public enum ZoneMode { Sidescroller, TopDown, Toggle } // Toggle flips whatever mode the player is in

    [Header("Sidescroller view")]
    [SerializeField] ViewDirection facing = ViewDirection.South; // side of the player the camera sits on

    [Header("While inside")]
    [SerializeField] bool allowRotation = false; // can the player rotate the view in here?

    [Header("References (auto-found if empty)")]
    [SerializeField] PerspectiveShiftSystem perspective;
    [SerializeField] MovementSystem movement;
    [SerializeField] string playerTag = "Player";

    // How many zones currently hold the player, so walking from one zone straight into
    // another doesn't drop them back to top-down in between
    static int zonesHoldingPlayer;

    static readonly Vector3[] Axes =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    Collider volume;

    bool touching;        // any overlap with the player during the current physics step
    bool contained;       // the whole player was inside during the current physics step
    bool inside;          // the zone's state: the player is in it until they are fully out
    bool rotationLockHeld;

    // Leaving in mid-air: the locks and the switch back wait here until the player lands
    int pendingReleases;
    int pendingRotationReleases;

    void Reset() => GetComponent<Collider>().isTrigger = true;

    void Awake()
    {
        volume = GetComponent<Collider>();

        if (perspective == null) perspective = FindFirstObjectByType<PerspectiveShiftSystem>();
        if (movement == null) movement = FindFirstObjectByType<MovementSystem>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    // Runs after every zone's FixedUpdate, so walking from one zone straight into another is
    // settled before anything decides to go back to top-down
    void Update()
    {
        if (pendingReleases > 0) TryFinishExit(false);
    }

    void OnTriggerEnter(Collider other) => Track(other);
    void OnTriggerStay(Collider other) => Track(other);

    void Track(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        touching = true;
        if (FullyInside(other)) contained = true;
    }

    // True when the whole of the player's collider is inside this volume. This checks the six
    // extreme points of the player's bounding box, which is exact for a box volume that isn't rotated.
    bool FullyInside(Collider player)
    {
        Bounds b = player.bounds;

        foreach (Vector3 axis in Axes)
            if (!Contains(b.center + Vector3.Scale(axis, b.extents))) return false;

        return true;
    }

    bool Contains(Vector3 point)
        => (volume.ClosestPoint(point) - point).sqrMagnitude < 0.00001f;

    // OnTriggerExit isn't sent when the player's CharacterController is disabled (doors do that
    // when they teleport), so entering and leaving are worked out from what Enter/Stay reported
    // over each physics step instead.
    void FixedUpdate()
    {
        if (!inside && contained)
        {
            // Fully in
            inside = true;
            OnPlayerEntered();
        }
        else if (inside && !touching)
        {
            // Fully out
            inside = false;
            OnPlayerLeft();
        }

        touching = false;
        contained = false;
    }

    void OnDisable()
    {
        if (inside) OnPlayerLeft();
        TryFinishExit(true); // don't leave locks behind, landed or not
        inside = false;
        touching = false;
        contained = false;
    }

    void OnPlayerEntered()
    {
        if (perspective == null) return;
        zonesHoldingPlayer++;

        // The fez is off limits in here; rotation is up to the developer
        perspective.SetToggleLock(true);
        rotationLockHeld = !allowRotation;
        if (rotationLockHeld) perspective.SetRotationLock(true);

        // Set the facing first: from top-down it's applied instantly, and from
        // sidescroller it turns the view with the usual rotation
        perspective.FaceDirection(facing);
        if (!perspective.IsSidescroller) perspective.SetPerspective(true);
    }

    // Leaving gives the fez back. The mode stays however the zone left it.
    void OnPlayerLeft()
    {
        if (perspective == null) return;
        zonesHoldingPlayer = Mathf.Max(0, zonesHoldingPlayer - 1);

        // Hand this visit's locks over to the exit, which finishes once the player is on the ground
        pendingReleases++;
        if (rotationLockHeld) pendingRotationReleases++;
        rotationLockHeld = false;
    }

    // Releases the fez and goes back to top-down, but not while the player is still airborne
    void TryFinishExit(bool force)
    {
        if (pendingReleases == 0 || perspective == null) return;
        if (!force && movement != null && !movement.Grounded) return;

        for (int i = 0; i < pendingReleases; i++) perspective.SetToggleLock(false);
        for (int i = 0; i < pendingRotationReleases; i++) perspective.SetRotationLock(false);
        pendingReleases = 0;
        pendingRotationReleases = 0;

        // Back to top-down, unless the player is inside another zone by now
        if (zonesHoldingPlayer == 0 && perspective.IsSidescroller)
            perspective.SetPerspective(false);
    }
}
