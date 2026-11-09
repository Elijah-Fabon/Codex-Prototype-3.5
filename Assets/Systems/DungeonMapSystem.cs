using System;
using System.Collections.Generic;
using UnityEngine;

public class DungeonMapSystem : MonoBehaviour, IInteractable
{
    // True while the player is rearranging the map. Add this to InputTrackerSystem.Blocked
    // if gameplay input should pause while they do.
    public static bool IsEditing { get; private set; }

    [Header("Grid")]
    [SerializeField] int width = 3;
    [SerializeField] int height = 3;
    [SerializeField] DungeonRoom[] rooms;            // every room on the map; each one knows its start cell
    [SerializeField] bool moveRoomObjects = true;    // place/rotate the room objects in the scene to match the grid
    [SerializeField] DungeonRoom startRoom;          // the room the player begins in

    [Header("What the player may do")]
    [SerializeField] bool allowMoving = true;
    [SerializeField] bool allowRotating = false;     // flip these two for the rotation-only dungeon
    [SerializeField] bool allowSwap = true;          // moving onto an occupied cell swaps; off = empty cells only
    [SerializeField] bool rotateRoomObjects = true;  // physically turn each room object to match its rotation (rooms never move)

    public event Action LayoutChanged; // while editing: a map UI can redraw
    public event Action Committed;     // editing finished and the doors were relinked
    public event Action EditingStarted; // the map screen should appear
    public event Action EditingEnded;   // confirmed or cancelled; the screen should go away

    public int Width => width;
    public int Height => height;

    // Set whenever editing starts or stops, so a button handler that runs later in the same
    // frame can tell it's looking at the press that caused it
    static int changedFrame = -1;
    public static bool EditChangedThisFrame => changedFrame == Time.frameCount;

    public bool AllowMoving => allowMoving;
    public bool AllowRotating => allowRotating;

    DungeonRoom[,] grid;
    DungeonRoom[,] gridSnapshot;
    Dictionary<DungeonRoom, int> rotationSnapshot;

    void Awake() => BuildGrid();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Apply(); // lay the rooms out and link the doors for the starting arrangement

        if (startRoom != null) DungeonRoom.SetCurrent(startRoom);
        else Debug.LogWarning("DungeonMapSystem has no Start Room, so every door trigger stays active.", this);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnDisable() => IsEditing = false;

    // Hook this to whatever your interact system calls on the map
    public void Interact()
    {
        if (IsEditing) EndEditing();
        else BeginEditing();
        Debug.Log($"Dungeon map editing: {IsEditing}");
    }

    // ---------------------------------------------------------------- grid

    void BuildGrid()
    {
        grid = new DungeonRoom[width, height];

        foreach (DungeonRoom room in rooms)
        {
            if (room == null) continue;

            Vector2Int c = room.StartCell;
            if (!InBounds(c))
            {
                Debug.LogError($"{room.name} starts outside the {width}x{height} grid at {c}.", room);
                continue;
            }
            if (grid[c.x, c.y] != null)
            {
                Debug.LogError($"{room.name} and {grid[c.x, c.y].name} both start at {c}.", room);
                continue;
            }

            grid[c.x, c.y] = room;
        }
    }

    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;

    public DungeonRoom GetRoom(Vector2Int c) => InBounds(c) ? grid[c.x, c.y] : null;

    // ---------------------------------------------------------------- editing

    public void BeginEditing()
    {
        if (IsEditing) return;
        IsEditing = true;

        // Remember how things were so the player can back out
        gridSnapshot = (DungeonRoom[,])grid.Clone();
        rotationSnapshot = new Dictionary<DungeonRoom, int>();
        foreach (DungeonRoom r in grid)
            if (r != null) rotationSnapshot[r] = r.Rotation;

        changedFrame = Time.frameCount;
        EditingStarted?.Invoke();
    }

    // Done rearranging: this is when rooms are placed and doors are reassigned
    public void EndEditing()
    {
        if (!IsEditing) return;
        IsEditing = false;
        gridSnapshot = null;
        rotationSnapshot = null;

        changedFrame = Time.frameCount;
        Apply();
        EditingEnded?.Invoke();
    }

    // Back out: restore the arrangement from when editing began
    public void CancelEditing()
    {
        if (!IsEditing) return;
        IsEditing = false;

        grid = gridSnapshot;
        foreach (var pair in rotationSnapshot) pair.Key.SetRotation(pair.Value);
        gridSnapshot = null;
        rotationSnapshot = null;

        changedFrame = Time.frameCount;
        LayoutChanged?.Invoke();
        EditingEnded?.Invoke();
    }

    // Move a room into a cell. An empty target just moves; an occupied one swaps (if allowSwap)
    public bool TryMove(Vector2Int from, Vector2Int to)
    {
        if (!IsEditing || !allowMoving) return false;
        if (!InBounds(from) || !InBounds(to) || from == to) return false;

        DungeonRoom moving = grid[from.x, from.y];
        DungeonRoom other = grid[to.x, to.y];

        if (moving == null || moving.Locked) return false;
        if (other != null && (!allowSwap || other.Locked)) return false;

        grid[to.x, to.y] = moving;
        grid[from.x, from.y] = other; // null when moving into an empty cell

        LayoutChanged?.Invoke();
        return true;
    }

    // quarterTurns: +1 = clockwise, -1 = counter-clockwise
    public bool TryRotate(Vector2Int cell, int quarterTurns)
    {
        if (!IsEditing || !allowRotating) return false;

        DungeonRoom room = GetRoom(cell);
        if (room == null || room.Locked) return false;

        room.Rotate(quarterTurns);

        LayoutChanged?.Invoke();
        return true;
    }

    public bool TryGetCell(DungeonRoom room, out Vector2Int cell)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] != room) continue;
                cell = new Vector2Int(x, y);
                return true;
            }

        cell = Vector2Int.zero;
        return false;
    }

    // ---------------------------------------------------------------- applying

    void Apply()
    {
        if (rotateRoomObjects) RotateRooms();
        ApplyConnections();
        Committed?.Invoke();
    }

    // Rooms stay where they are in the scene (the doors teleport, so they never need to touch).
    // Only their rotation is applied, so the top-down camera never has to turn.
    void RotateRooms()
    {
        foreach (DungeonRoom room in rooms)
        {
            if (room == null) continue;

            Quaternion turn = Quaternion.Euler(0f, 90f * room.Rotation, 0f);
            room.transform.rotation = turn * room.BaseRotation;
            room.RefreshVisibility(); // which wall is on which side just changed
        }
    }

    // Every door looks at the neighbouring cell on the side it currently faces. If the
    // neighbour has a door on the matching opposite side, the two link up. Otherwise (no
    // room, off the grid, or a wall there) the door is blocked.
    void ApplyConnections()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                DungeonRoom room = grid[x, y];
                if (room == null) continue;

                foreach (Face face in Enum.GetValues(typeof(Face)))
                {
                    Door door = room.GetDoorAtFace(face);
                    if (door == null) continue;

                    DungeonRoom neighbour = GetRoom(new Vector2Int(x, y) + face.Offset());
                    Door across = neighbour != null ? neighbour.GetDoorAtFace(face.Opposite()) : null;

                    door.SetDestination(across); // null blocks it
                }
            }
    }

    // ---------------------------------------------------------------- debug

}
