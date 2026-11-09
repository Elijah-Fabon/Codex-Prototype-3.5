using UnityEngine;

// A trigger zone for a "room" in a large continuous space. While the player is fully inside, the
// camera centers on this room's anchor instead of following the player, and can optionally use its
// own FOV and camera offset. Anything not ticked is left as it is.
// Enter and leave work like ModeSwitchVolume: fully inside to enter, fully outside to leave, and
// nothing changes while the player straddles an edge. The volume has to be bigger than the player.
// If rooms overlap, the one entered most recently wins.
[RequireComponent(typeof(Collider))]
public class CameraRoomVolume : MonoBehaviour
{
    [Header("Anchor")]
    [SerializeField] Transform anchor; // the point the camera centers on; auto-found (child "CamAnchor"), else the volume's center

    [Header("Top-down (only what is ticked changes)")]
    [SerializeField] bool overrideTopDownFov;
    [SerializeField] float topDownFov = 55f;
    [SerializeField] bool overrideTopDownOffset;
    [SerializeField] Vector3 topDownOffset = new Vector3(-9f, 12f, -9f); // the camera's Follow Offset

    [Header("Sidescroller (only what is ticked changes)")]
    [SerializeField] bool overrideSideFov;
    [SerializeField] float sideFov = 1f;
    [SerializeField] bool overrideSideOffset;
    [SerializeField] Vector3 sideOffset = new Vector3(0f, 0f, -950f);

    [Header("References (auto-found if empty)")]
    [SerializeField] PerspectiveShiftSystem perspective;
    [SerializeField] string playerTag = "Player";

    static readonly Vector3[] Axes =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    Collider volume;
    PerspectiveShiftSystem.CameraOverride cameraOverride;

    bool touching;   // any overlap with the player during the current physics step
    bool contained;  // the whole player was inside during the current physics step
    bool inside;     // the player is in it until they are fully out

    void Reset() => GetComponent<Collider>().isTrigger = true;

    void Awake()
    {
        volume = GetComponent<Collider>();

        if (anchor == null) anchor = transform.Find("CamAnchor");
        if (perspective == null) perspective = FindFirstObjectByType<PerspectiveShiftSystem>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnTriggerEnter(Collider other) => Track(other);
    void OnTriggerStay(Collider other) => Track(other);

    void Track(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        touching = true;
        if (FullyInside(other)) contained = true;
    }

    // True when the whole of the player's collider is inside this volume (exact for an unrotated box)
    bool FullyInside(Collider player)
    {
        Bounds b = player.bounds;

        foreach (Vector3 axis in Axes)
            if (!Contains(b.center + Vector3.Scale(axis, b.extents))) return false;

        return true;
    }

    bool Contains(Vector3 point)
        => (volume.ClosestPoint(point) - point).sqrMagnitude < 0.00001f;

    // OnTriggerExit isn't sent when the player's CharacterController is disabled (doors do that when
    // they teleport), so entering and leaving come from what Enter/Stay reported over each step.
    void FixedUpdate()
    {
        if (!inside && contained)
        {
            inside = true;
            OnPlayerEntered();
        }
        else if (inside && !touching)
        {
            inside = false;
            OnPlayerLeft();
        }

        touching = false;
        contained = false;
    }

    void OnDisable()
    {
        if (inside) OnPlayerLeft();
        inside = false;
        touching = false;
        contained = false;
    }

    void OnPlayerEntered()
    {
        if (perspective == null) return;

        // Built fresh each visit, so changes made in the inspector while playing are picked up
        cameraOverride = new PerspectiveShiftSystem.CameraOverride
        {
            Anchor = anchor,
            Fallback = volume.bounds.center,
            TopDownFov = overrideTopDownFov ? topDownFov : (float?)null,
            TopDownOffset = overrideTopDownOffset ? topDownOffset : (Vector3?)null,
            SideFov = overrideSideFov ? sideFov : (float?)null,
            SideOffset = overrideSideOffset ? sideOffset : (Vector3?)null,
        };

        perspective.AddCameraOverride(cameraOverride);
    }

    void OnPlayerLeft()
    {
        if (perspective != null && cameraOverride != null)
            perspective.RemoveCameraOverride(cameraOverride);

        cameraOverride = null;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Transform a = anchor != null ? anchor : transform.Find("CamAnchor");
        if (a == null) return;

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
        Gizmos.DrawWireSphere(a.position, 0.4f);
        Gizmos.DrawLine(a.position, transform.position);
    }
#endif
}
