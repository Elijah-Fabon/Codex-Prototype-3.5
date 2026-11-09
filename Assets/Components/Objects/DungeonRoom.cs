using System.Collections.Generic;
using UnityEngine;

// Grid directions. Order is clockwise seen from above, so rotating a room one quarter turn
// clockwise moves North -> East -> South -> West -> North.
// On the map grid, North is +Y and East is +X. A room's own "north" is whichever way you
// authored it facing (world +Z is the natural choice); rotating the room turns it from there.
public enum Face { North, East, South, West }

public static class FaceExtensions
{
    // Turn clockwise by N quarter turns (negative = counter-clockwise)
    public static Face Rotate(this Face face, int quarterTurns)
        => (Face)((((int)face + quarterTurns) % 4 + 4) % 4);

    public static Face Opposite(this Face face) => face.Rotate(2);

    // Neighbouring cell offset on the map grid
    public static Vector2Int Offset(this Face face)
    {
        switch (face)
        {
            case Face.North: return new Vector2Int(0, 1);
            case Face.East: return new Vector2Int(1, 0);
            case Face.South: return new Vector2Int(0, -1);
            default: return new Vector2Int(-1, 0);
        }
    }
}

public class DungeonRoom : MonoBehaviour
{
    [Header("Doors (by face in the room's UNROTATED layout, leave empty if there is none)")]
    [SerializeField] Door north;
    [SerializeField] Door east;
    [SerializeField] Door south;
    [SerializeField] Door west;

    [Header("Walls (by face in the UNROTATED layout; everything under a wall is culled with it)")]
    [SerializeField] Transform wallNorth;
    [SerializeField] Transform wallEast;
    [SerializeField] Transform wallSouth;
    [SerializeField] Transform wallWest;

    [Header("Ceiling")]
    [SerializeField] Transform ceiling;

    [Header("Placement")]
    [SerializeField] Vector2Int startCell;
    [SerializeField, Range(0, 3)] int rotation; // quarter turns clockwise (seen from above)
    [SerializeField] bool locked;               // the player can't move or rotate this room

    Quaternion baseRotation;
    static readonly List<DungeonRoom> all = new List<DungeonRoom>();

    // The room the player is in. Only its doors have active triggers.
    public static DungeonRoom Current { get; private set; }

    Door[] doors;
    PlatformSystem[] platforms;
    InteractZone[] zones;
    Switch[] switches;

    // Visibility: the room as a whole, plus which WORLD-facing walls are shown (bit per Face)
    bool roomVisible = true;
    int wallMask = 0b1111;        // walls shown now
    int wallMaskBefore = 0b1111;  // walls shown before the current perspective shift
    bool wallsTransitional;       // during a shift both sets are shown, then only the new one
    bool topDown = true;          // current mode; doors are exempt from wall culling in top-down

    public Vector2Int StartCell => startCell;
    public int Rotation => rotation;
    public bool Locked => locked;
    public Quaternion BaseRotation => baseRotation;

    void Awake()
    {
        baseRotation = transform.rotation; // the pose you authored it in = rotation 0
        doors = GetComponentsInChildren<Door>(true);
        platforms = GetComponentsInChildren<PlatformSystem>(true);
        zones = GetComponentsInChildren<InteractZone>(true);
        switches = GetComponentsInChildren<Switch>(true);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    // Once a perspective shift or rotation finishes, drop the walls that were only shown for it
    void Update()
    {
        if (wallsTransitional && !PerspectiveShiftSystem.IsTransitioning)
        {
            wallsTransitional = false;
            RefreshVisibility();
        }
    }

    void OnEnable()
    {
        all.Add(this);
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        OnPerspectiveChanged(PerspectiveShiftSystem.CurrentSidescroller,
                             PerspectiveShiftSystem.CurrentViewForward);
    }

    void OnDisable()
    {
        all.Remove(this);
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;
    }

    // ---------------------------------------------------------------- rotation and doors

    public void SetRotation(int quarterTurns) => rotation = ((quarterTurns % 4) + 4) % 4;
    public void Rotate(int quarterTurns) => SetRotation(rotation + quarterTurns);

    // Door by its face in the unrotated layout. A door whose object is switched off in the
    // scene counts as not being there, so every room can have all four slots filled and
    // simply disable the doors it shouldn't have.
    public Door GetDoorLocal(Face localFace)
    {
        Door door;
        switch (localFace)
        {
            case Face.North: door = north; break;
            case Face.East: door = east; break;
            case Face.South: door = south; break;
            default: door = west; break;
        }

        return door != null && door.gameObject.activeSelf ? door : null;
    }

    // Door that currently points toward this world direction, after the room's rotation.
    // A door built on local face L ends up on world face L + rotation.
    public Door GetDoorAtFace(Face worldFace) => GetDoorLocal(worldFace.Rotate(-rotation));

    // Call when the player enters a room: its doors switch on and it becomes visible,
    // every other room's doors switch off and it stops rendering
    public static void SetCurrent(DungeonRoom room)
    {
        Current = room;
        foreach (DungeonRoom r in all)
        {
            bool here = r == room;
            r.SetDoorsActive(here);
            r.SetHitboxesActive(here);
            r.SetVisible(here);
        }
    }

    void SetDoorsActive(bool active)
    {
        foreach (Door d in doors) d.SetTriggerActive(active);
    }

    // Platforms and interact zones stretch their colliders deep along the view axis in
    // sidescroller, so ones in other rooms could reach the player's plane. Off unless we're here.
    void SetHitboxesActive(bool active)
    {
        foreach (PlatformSystem p in platforms) p.SetColliderActive(active);
        foreach (InteractZone z in zones) z.SetColliderActive(active);
        foreach (Switch s in switches) s.SetColliderActive(active);
    }

    // ---------------------------------------------------------------- culling

    // Stops (or resumes) drawing everything in the room. The objects stay active, so scripts,
    // colliders and doors keep working. forceRenderingOff is separate from Renderer.enabled,
    // so anything that toggles renderers itself isn't overridden. Bounds are unaffected.
    void SetVisible(bool visible)
    {
        roomVisible = visible;
        RefreshVisibility();
    }

    // Top-down hides the west and south walls. Sidescroller shows only the wall the camera
    // is facing. A perspective shift shows the old and new sets together, then settles.
    void OnPerspectiveChanged(bool sidescroller, Vector3 viewForward)
    {
        int newMask = WallMask(sidescroller, viewForward);

        topDown = !sidescroller;

        // If another shift lands mid-shift, keep everything shown so far until it settles
        wallMaskBefore = wallsTransitional ? (wallMaskBefore | wallMask) : wallMask;
        wallMask = newMask;
        wallsTransitional = PerspectiveShiftSystem.IsTransitioning;

        RefreshVisibility();
    }

    // Which WORLD faces have their wall shown
    static int WallMask(bool sidescroller, Vector3 viewForward)
    {
        if (!sidescroller)
            return (1 << (int)Face.North) | (1 << (int)Face.East); // west and south are culled

        // The wall the camera is facing is the one in the view direction (+Z = north, +X = east)
        float yaw = Mathf.Atan2(viewForward.x, viewForward.z) * Mathf.Rad2Deg;
        int face = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
        return 1 << face;
    }

    // Applies room visibility and wall visibility. Also call it after the room's rotation changes.
    public void RefreshVisibility()
    {
        int shown = wallsTransitional ? (wallMask | wallMaskBefore) : wallMask;

        // Everything in the room follows the room's own visibility...
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = !roomVisible;

        // ...then each wall, and everything under it, also needs its face to be shown.
        // A wall built on local face L sits on world face L + rotation.
        for (int i = 0; i < 4; i++)
        {
            Transform wall = GetWallLocal((Face)i);
            if (wall == null) continue;

            Face world = ((Face)i).Rotate(rotation);
            bool show = roomVisible && (shown & (1 << (int)world)) != 0;

            foreach (Renderer r in wall.GetComponentsInChildren<Renderer>(true))
                r.forceRenderingOff = !show;
        }

        // Top-down exception: doors on a culled wall stay visible (only the room's own visibility applies)
        if (topDown)
        {
            foreach (Door d in doors)
                foreach (Renderer r in d.GetComponentsInChildren<Renderer>(true))
                    r.forceRenderingOff = !roomVisible;
        }

        // A door a demon is hiding isn't drawn at all
        foreach (Door d in doors)
        {
            if (!d.IsHidden) continue;

            foreach (Renderer r in d.GetComponentsInChildren<Renderer>(true))
                r.forceRenderingOff = true;
        }

        // The ceiling is hidden in top-down and visible in sidescroller.
        // Room visibility still takes priority.
        if (ceiling != null)
        {
            bool showCeiling = roomVisible && !topDown;

            foreach (Renderer r in ceiling.GetComponentsInChildren<Renderer>(true))
                r.forceRenderingOff = !showCeiling;
        }
    }

    Transform GetWallLocal(Face localFace)
    {
        switch (localFace)
        {
            case Face.North: return wallNorth;
            case Face.East: return wallEast;
            case Face.South: return wallSouth;
            default: return wallWest;
        }
    }

    // World-space bounds of everything visible in the room. Doors use it to tell which
    // side of themselves is the inside of the room.
    public Bounds GetBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool first = true;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }

        return b;
    }
}
