using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum CodeButton { Up, Down, Left, Right, Action, RotateLeft, RotateRight }
public enum EquipSlot { West, North, East }

public class InputTrackerSystem : MonoBehaviour
{
    [SerializeField] InputActionAsset actions;

    [Tooltip("How long (seconds) a D-pad direction still counts after it is let go. Lets the thumb lift slightly before the rosary press.")]
    [SerializeField] float dPadGrace = 0.15f;

    InputActionMap mapTopDown, mapSide, mapAny;
    InputActionMap current;

    InputAction move, jumpDash;          // swap with the mode
    InputAction rotateRight, rotateLeft; // Sidescroller map
    InputAction dPad, menu, target;              // Any map
    InputAction topDownJumpDash, sideJumpDash;
    InputAction equipWest, equipNorth, equipEast;

    int consumedFrame = -1;

    // Last cardinal D-pad direction seen, and when it was last held (for the grace window)
    Vector2Int lastDPadDir;
    float lastDPadTime = float.NegativeInfinity;

    // Events: subscribe instead of polling
    public event Action RotateRightPressed;
    public event Action RotateLeftPressed;
    public event Action<Vector2Int> DPadPressed; // (0,1) up, (0,-1) down, (-1,0) left, (1,0) right
    public event Action<CodeButton> CodeButtonPressed;
    public event Action<EquipSlot> EquipPressed;      // gameplay: use the item in this slot
    public event Action<EquipSlot> MenuEquipPressed;  // menu open: assign the highlighted item to this slot
    public event Action MenuPressed;
    public event Action<EquipSlot> MapEquipPressed;  // dungeon map open: equip buttons drive the map

    // Gameplay input is ignored during a mode switch / rotation and while the menu is open
    static bool Blocked => PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning || EquipMenu.IsOpen;

    // Polled values (still available)
    public Vector2 Move => move.ReadValue<Vector2>();
    public bool JumpDashPressed => jumpDash.WasPressedThisFrame() && consumedFrame != Time.frameCount;
    public bool JumpDashHeld => jumpDash.IsPressed();
    public bool TargetHeld => target.IsPressed() && !Blocked;

    void Awake()
    {
        mapTopDown = actions.FindActionMap("TopDown", true);
        mapSide = actions.FindActionMap("Sidescroller", true);
        mapAny = actions.FindActionMap("Any", true);

        rotateRight = mapSide.FindAction("Rotate Right", true);
        rotateLeft = mapSide.FindAction("Rotate Left", true);
        dPad = mapAny.FindAction("D-Pad", true);
        menu = mapAny.FindAction("Menu", true);
        target = mapAny.FindAction("Target", true);

        topDownJumpDash = mapTopDown.FindAction("Jump/Dash", true);
        sideJumpDash = mapSide.FindAction("Jump/Dash", true);

        equipWest = mapAny.FindAction("EquipWest", true);
        equipNorth = mapAny.FindAction("EquipNorth", true);
        equipEast = mapAny.FindAction("EquipEast", true);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // Keep the grace window fresh while a direction is held
        Vector2Int held = DPadHeld;
        if (held != Vector2Int.zero)
        {
            lastDPadDir = held;
            lastDPadTime = Time.unscaledTime;
        }
    }

    void OnEnable()
    {
        rotateRight.performed += OnRotateRight;
        rotateLeft.performed += OnRotateLeft;
        dPad.performed += OnDPad;
        menu.performed += OnMenu;
        topDownJumpDash.performed += OnJumpDash;
        sideJumpDash.performed += OnJumpDash;
        equipWest.performed += OnEquipWest;
        equipNorth.performed += OnEquipNorth;
        equipEast.performed += OnEquipEast;

        mapAny.Enable();
        SetMode(false);
    }

    void OnDisable()
    {
        rotateRight.performed -= OnRotateRight;
        rotateLeft.performed -= OnRotateLeft;
        dPad.performed -= OnDPad;
        menu.performed -= OnMenu;
        topDownJumpDash.performed -= OnJumpDash;
        sideJumpDash.performed -= OnJumpDash;
        equipWest.performed -= OnEquipWest;
        equipNorth.performed -= OnEquipNorth;
        equipEast.performed -= OnEquipEast;

        actions.Disable();
    }

    // The menu button must still work while the menu is open (that's how you close it),
    // so it only checks for a transition, not Blocked.
    void OnMenu(InputAction.CallbackContext _)
    {
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning || DungeonMapSystem.IsEditing) return;
        MenuPressed?.Invoke();
    }

    // Called by the interaction system so the press that opened something doesn't also jump/dash
    public void ConsumeJumpDash() => consumedFrame = Time.frameCount;

    void OnJumpDash(InputAction.CallbackContext _)
    {
        if (Blocked) return;
        CodeButtonPressed?.Invoke(CodeButton.Action);
    }

    // Each handler checks Blocked once at the top. A rotate press itself starts the freeze,
    // so checking again after RotateRightPressed fires would wrongly drop the code button.
    void OnRotateRight(InputAction.CallbackContext _)
    {
        if (Blocked || DungeonMapSystem.IsEditing) return;
        RotateRightPressed?.Invoke();
        CodeButtonPressed?.Invoke(CodeButton.RotateRight);
    }

    void OnRotateLeft(InputAction.CallbackContext _)
    {
        if (Blocked || DungeonMapSystem.IsEditing) return;
        RotateLeftPressed?.Invoke();
        CodeButtonPressed?.Invoke(CodeButton.RotateLeft);
    }

    void OnDPad(InputAction.CallbackContext ctx)
    {
        Vector2 v = ctx.ReadValue<Vector2>();
        if (v == Vector2.zero) return; // ignore the release

        // Snap to one cardinal direction so diagonals don't count twice
        Vector2Int dir = Mathf.Abs(v.x) > Mathf.Abs(v.y)
            ? new Vector2Int(v.x > 0 ? 1 : -1, 0)
            : new Vector2Int(0, v.y > 0 ? 1 : -1);

        // Remember it here too, so a tap shorter than a frame is still caught by the grace window
        lastDPadDir = dir;
        lastDPadTime = Time.unscaledTime;

        DPadPressed?.Invoke(dir);

        CodeButton button = dir.x > 0 ? CodeButton.Right
                  : dir.x < 0 ? CodeButton.Left
                  : dir.y > 0 ? CodeButton.Up
                  : CodeButton.Down;
        CodeButtonPressed?.Invoke(button);
    }

    public void SetMode(bool sidescroller)
    {
        current?.Disable();
        current = sidescroller ? mapSide : mapTopDown;
        current.Enable();

        move = current.FindAction("Move", true);
        jumpDash = current.FindAction("Jump/Dash", true);
    }

    void OnEquipWest(InputAction.CallbackContext _) => RaiseEquip(EquipSlot.West);
    void OnEquipNorth(InputAction.CallbackContext _) => RaiseEquip(EquipSlot.North);
    void OnEquipEast(InputAction.CallbackContext _) => RaiseEquip(EquipSlot.East);

    // With the menu open the slot buttons assign items; otherwise they use them
    void RaiseEquip(EquipSlot slot)
    {
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning) return;

        if (DungeonMapSystem.IsEditing) MapEquipPressed?.Invoke(slot);
        else if (EquipMenu.IsOpen) MenuEquipPressed?.Invoke(slot);
        else EquipPressed?.Invoke(slot);
    }

    public bool IsEquipHeld(EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.West: return equipWest.IsPressed();
            case EquipSlot.North: return equipNorth.IsPressed();
            default: return equipEast.IsPressed();
        }
    }

    // The held direction if there is one; otherwise the last direction held within the grace window.
    // (0,0) if neither, or while input is blocked.
    public Vector2Int DPadHeldWithGrace
    {
        get
        {
            Vector2Int held = DPadHeld;
            if (held != Vector2Int.zero) return held;

            if (Blocked || DungeonMapSystem.IsEditing) return Vector2Int.zero;
            if (Time.unscaledTime - lastDPadTime <= dPadGrace) return lastDPadDir;

            return Vector2Int.zero;
        }
    }

    // Call after a remembered direction has been used, so one tap can't trigger twice
    public void ConsumeDPadGrace() => lastDPadTime = float.NegativeInfinity;

    // The D-pad direction being held right now, snapped to one cardinal direction.
    // (0,0) when nothing is held, or while input is blocked (menus, transitions, the dungeon map).
    public Vector2Int DPadHeld
    {
        get
        {
            if (Blocked || DungeonMapSystem.IsEditing) return Vector2Int.zero;

            Vector2 v = dPad.ReadValue<Vector2>();
            if (v == Vector2.zero) return Vector2Int.zero;

            return Mathf.Abs(v.x) > Mathf.Abs(v.y)
                ? new Vector2Int(v.x > 0 ? 1 : -1, 0)
                : new Vector2Int(0, v.y > 0 ? 1 : -1);
        }
    }
}
