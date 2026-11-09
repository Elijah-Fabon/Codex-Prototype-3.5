using UnityEngine;
using UnityEngine.UI;

// The map screen and its controls. Builds its own canvas, so there's no UI to set up by hand.
//   D-Pad or Move : move the cursor
//   Equip West    : pick up the room under the cursor / drop it (moves or swaps)
//   Equip North   : rotate the room under the cursor clockwise (only if the map allows rotating)
//   Equip East    : confirm (rooms are rotated and doors are relinked)
//   Jump / Dash   : cancel (everything goes back the way it was)
public class DungeonMapEditor : MonoBehaviour
{
    [SerializeField] DungeonMapSystem map;
    [SerializeField] InputTrackerSystem input;
    [SerializeField] float cellSize = 150f;
    [SerializeField] float spacing = 12f;

    static readonly Color normalColor = new Color(0.50f, 0.53f, 0.58f, 1f);
    static readonly Color cursorColor = new Color(0.95f, 0.85f, 0.30f, 1f);
    static readonly Color heldColor = new Color(0.95f, 0.50f, 0.15f, 1f);
    static readonly Color heldCursorColor = new Color(0.50f, 0.85f, 0.40f, 1f);
    static readonly Color doorLinkedColor = new Color(0.15f, 0.65f, 0.25f, 1f); // a door on the other side meets it
    static readonly Color doorDeadColor = new Color(0.80f, 0.20f, 0.15f, 1f);   // leads nowhere (it would be blocked)

    GameObject root;
    Image[,] cellImages;
    Image[,,] doorMarkers; // [x, y, world face]
    Image[,,] doorLines; // [x, y, world face]
    Text[,] cellTexts;
    Text hint;

    Vector2Int cursor;
    bool holding;
    Vector2Int heldFrom;
    bool moveHeld;     // the stick/keys must return to neutral between cursor steps
    int lastStepFrame = -1;

    void Reset()
    {
        map = GetComponent<DungeonMapSystem>();
        input = FindFirstObjectByType<InputTrackerSystem>();
    }

    void Awake()
    {
        if (map == null) map = FindFirstObjectByType<DungeonMapSystem>();
        if (input == null) input = FindFirstObjectByType<InputTrackerSystem>();

        BuildUI();
        root.SetActive(false);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    void OnEnable()
    {
        map.EditingStarted += OnEditingStarted;
        map.EditingEnded += OnEditingEnded;
        map.LayoutChanged += Refresh;
        input.DPadPressed += OnDPad;
        input.MapEquipPressed += OnMapEquip;
        input.CodeButtonPressed += OnCodeButton;
    }

    void OnDisable()
    {
        map.EditingStarted -= OnEditingStarted;
        map.EditingEnded -= OnEditingEnded;
        map.LayoutChanged -= Refresh;
        input.DPadPressed -= OnDPad;
        input.MapEquipPressed -= OnMapEquip;
        input.CodeButtonPressed -= OnCodeButton;
    }

    // ---------------------------------------------------------------- input

    void OnEditingStarted()
    {
        holding = false;

        // Start on the room the player is standing in
        if (DungeonRoom.Current == null || !map.TryGetCell(DungeonRoom.Current, out cursor))
            cursor = Vector2Int.zero;

        moveHeld = input.Move.magnitude > 0.3f; // don't step just because a key is still down
        root.SetActive(true);
        Refresh();
    }

    void OnEditingEnded()
    {
        holding = false;
        root.SetActive(false);
    }

    void OnDPad(Vector2Int dir) => MoveCursor(dir);

    // The Move input (stick / WASD) also moves the cursor, one step per push
    // Update is called once per frame
    void Update()
    {
        if (!DungeonMapSystem.IsEditing) return;

        Vector2 m = input.Move;
        if (m.magnitude < 0.3f) { moveHeld = false; return; }
        if (moveHeld || m.magnitude < 0.6f) return;

        moveHeld = true;
        if (lastStepFrame == Time.frameCount) return; // the D-Pad already stepped this frame

        MoveCursor(Mathf.Abs(m.x) > Mathf.Abs(m.y)
            ? new Vector2Int(m.x > 0f ? 1 : -1, 0)
            : new Vector2Int(0, m.y > 0f ? 1 : -1));
    }

    void MoveCursor(Vector2Int dir)
    {
        if (!DungeonMapSystem.IsEditing) return;
        lastStepFrame = Time.frameCount;

        Vector2Int next = cursor + dir;
        if (!map.InBounds(next)) return;

        cursor = next;
        Refresh();
    }

    void OnMapEquip(EquipSlot slot)
    {
        if (!DungeonMapSystem.IsEditing) return;

        switch (slot)
        {
            case EquipSlot.West: PickUpOrDrop(); break;
            case EquipSlot.North: map.TryRotate(cursor, 1); break;
            case EquipSlot.East: map.EndEditing(); break;
        }
    }

    // Jump/dash cancels
    void OnCodeButton(CodeButton button)
    {
        if (button != CodeButton.Action) return;

        // Ignore the very press that opened the map
        if (!DungeonMapSystem.IsEditing || DungeonMapSystem.EditChangedThisFrame) return;

        input.ConsumeJumpDash(); // so cancelling doesn't also jump or dash
        map.CancelEditing();
    }

    void PickUpOrDrop()
    {
        if (!holding)
        {
            if (!map.AllowMoving) return;

            DungeonRoom room = map.GetRoom(cursor);
            if (room == null || room.Locked) return;

            holding = true;
            heldFrom = cursor;
        }
        else
        {
            // Dropping where it came from just puts it down. If the move isn't allowed
            // (e.g. swapping with a locked room), keep holding it.
            if (cursor == heldFrom || map.TryMove(heldFrom, cursor))
                holding = false;
        }

        Refresh();
    }

    // ---------------------------------------------------------------- display

    void Refresh()
    {
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                DungeonRoom room = map.GetRoom(c);

                cellTexts[x, y].text = room == null ? "empty" : room.Locked ? "locked" : "";

                // One marker per side, using the door that currently faces that side (rotation included).
                // Green = a door on the neighbouring room meets it, red = it leads nowhere and would be blocked.
                for (int f = 0; f < 4; f++)
                {
                    Face face = (Face)f;
                    Door door = room != null ? room.GetDoorAtFace(face) : null;

                    Image marker = doorMarkers[x, y, f];
                    Image line = doorLines[x, y, f];

                    // No door means neither a marker nor a path line.
                    bool hasDoor = door != null;
                    marker.gameObject.SetActive(hasDoor);
                    line.gameObject.SetActive(false);

                    if (!hasDoor) continue;

                    DungeonRoom neighbour = map.GetRoom(c + face.Offset());
                    bool linked = neighbour != null && neighbour.GetDoorAtFace(face.Opposite()) != null;
                    line.gameObject.SetActive(linked);

                    Color color = linked ? doorLinkedColor : doorDeadColor;

                    marker.color = color;
                    line.color = color;
                }

                bool isCursor = c == cursor;
                bool isHeld = holding && c == heldFrom;
                cellImages[x, y].color = isCursor && isHeld ? heldCursorColor
                                       : isCursor ? cursorColor
                                       : isHeld ? heldColor
                                       : normalColor;
            }

        string controls = holding
            ? "Move: choose a cell      West: drop here      East: confirm      Jump: cancel"
            : "Move: choose a cell      West: pick up      North: rotate      East: confirm      Jump: cancel";
        hint.text = controls + "\nGreen door: connects      Red door: leads nowhere";
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("DungeonMapCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        root = canvasGo;

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // above the equip menu (0), below the fade (100)

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        float panelW = map.Width * cellSize + (map.Width + 1) * spacing;
        float panelH = map.Height * cellSize + (map.Height + 1) * spacing + 80f; // room for the hint line

        RectTransform panel = MakeImage(canvasGo.transform, "Panel", new Color(0f, 0f, 0f, 0.8f));
        panel.sizeDelta = new Vector2(panelW, panelH);

        cellImages = new Image[map.Width, map.Height];
        cellTexts = new Text[map.Width, map.Height];
        doorMarkers = new Image[map.Width, map.Height, 4];
        doorLines = new Image[map.Width, map.Height, 4];

        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
            {
                RectTransform cell = MakeImage(panel, $"Cell {x},{y}", normalColor);
                cell.sizeDelta = new Vector2(cellSize, cellSize);

                // y = 0 is the bottom row, so the grid reads the way it sits on the map
                cell.anchoredPosition = new Vector2(
                    (x - (map.Width - 1) / 2f) * (cellSize + spacing),
                    (y - (map.Height - 1) / 2f) * (cellSize + spacing) + 40f);

                cellImages[x, y] = cell.GetComponent<Image>();

                for (int f = 0; f < 4; f++)
                    doorLines[x, y, f] = MakeDoorLine(cell, (Face)f);

                Text label = MakeText(cell, "Label", 22, TextAnchor.MiddleCenter);
                Stretch(label.rectTransform);
                label.color = Color.black;
                cellTexts[x, y] = label;
                for (int f = 0; f < 4; f++)
                    doorMarkers[x, y, f] = MakeDoorMarker(cell, (Face)f);
            }

        hint = MakeText(panel, "Hint", 20, TextAnchor.MiddleCenter);
        RectTransform hr = hint.rectTransform;
        hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
        hr.pivot = new Vector2(0.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 12f);
        hr.sizeDelta = new Vector2(panelW - 20f, 60f);
    }

    // A short bar on the cell's edge, standing for a door on that (world) side
    Image MakeDoorMarker(Transform cell, Face face)
    {
        RectTransform r = MakeImage(cell, $"Door {face}", doorDeadColor);
        float length = cellSize * 0.4f;
        const float thickness = 14f;

        switch (face)
        {
            case Face.North:
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f);
                r.sizeDelta = new Vector2(length, thickness);
                break;
            case Face.South:
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0f);
                r.sizeDelta = new Vector2(length, thickness);
                break;
            case Face.East:
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 0.5f);
                r.sizeDelta = new Vector2(thickness, length);
                break;
            default: // West
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0.5f);
                r.sizeDelta = new Vector2(thickness, length);
                break;
        }

        r.anchoredPosition = Vector2.zero;
        return r.GetComponent<Image>();
    }

    Image MakeDoorLine(Transform cell, Face face)
    {
        RectTransform r = MakeImage(
            cell,
            $"Path {face}",
            doorDeadColor
        );

        const float thickness = 6f;

        // The line starts at the center and extends halfway toward the edge.
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0f);
        r.sizeDelta = new Vector2(thickness, cellSize * 0.5f);
        r.anchoredPosition = Vector2.zero;

        switch (face)
        {
            case Face.North:
                r.localRotation = Quaternion.identity;
                break;

            case Face.East:
                r.localRotation = Quaternion.Euler(0f, 0f, -90f);
                break;

            case Face.South:
                r.localRotation = Quaternion.Euler(0f, 0f, 180f);
                break;

            case Face.West:
                r.localRotation = Quaternion.Euler(0f, 0f, 90f);
                break;
        }

        return r.GetComponent<Image>();
    }

    static RectTransform MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false; // never steal clicks from other UI

        return (RectTransform)go.transform;
    }

    static Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;

        return text;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
