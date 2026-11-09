using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class Door : MonoBehaviour
{
    // Gameplay systems check this to freeze while the door transition plays
    public static bool IsTransitioning { get; private set; }

    [Header("Link")]
    [SerializeField] Door destinationDoor;    // the door you come out of

    [Header("Lock")]
    [SerializeField] LockType lockType;
    [SerializeField] Switch[] switches;        // singleSwitch uses the first one; multiSwitch needs all of them on
    [SerializeField] EnemyAISystem demon;      // demon: the door is hidden (and locked) while the demon is hiding
    [SerializeField] GameObject lockedVisual;  // optional: shown while the door is locked (not while it is hidden)

    [Header("Fade")]
    [SerializeField] CanvasGroup fade;        // full-screen black Image with a CanvasGroup (alpha 0)
    [SerializeField] float fadeOutTime = 0.4f;
    [SerializeField] float holdTime = 0.15f;  // time spent fully black
    [SerializeField] float fadeInTime = 0.4f;

    [Header("Sidescroller")]
    [SerializeField] float sidescrollerDepth = 30f; // trigger is stretched this deep along the view axis
    [SerializeField] float arrivalDistance = 1f;    // how far toward the camera you appear in front of the door
    [SerializeField] float upThreshold = 0.5f;      // how far up counts as pressing up

    [Header("References (auto-found if empty)")]
    [SerializeField] MovementSystem player;
    [SerializeField] InputTrackerSystem input;

    [SerializeField] string playerTag = "Player";

    [SerializeField] GameObject blockedVisual; // optional: shown while the door leads nowhere

    static readonly List<Door> all = new List<Door>();

    BoxCollider box;
    Vector3 originalSize;

    enum LockType {none, singleSwitch, multiSwitch, key, demon}
    bool sidescroller;
    bool playerInside;
    bool touching;       // set by trigger events during the current physics step
    bool ignoreUntilExit; // set when the player arrives inside this door's trigger
    bool upWasHeld;
    DungeonRoom room; // the room this door belongs to (null if it isn't inside one)
    bool runningTransition; // this door is the one currently holding IsTransitioning

    bool keyUnlocked;    // key lock: set by Unlock()
    bool lockApplied;    // the lock visuals have been set at least once
    bool lockedShown;
    bool hiddenShown;

    public bool IsBlocked => destinationDoor == null;
    public DungeonRoom Room => room;

    // A door a demon is hiding isn't drawn and can't be used
    public bool IsHidden => lockType == LockType.demon && demon != null && demon.IsHiding;

    // Locked by its own lock, or because the door it leads to is locked: the two ends of a
    // doorway are locked and unlocked together
    public bool IsLocked => OwnLocked || (destinationDoor != null && destinationDoor.OwnLocked);

    // This door's own lock, without looking at the other end
    bool OwnLocked
    {
        get
        {
            switch (lockType)
            {
                case LockType.singleSwitch:
                    return switches != null && switches.Length > 0 && switches[0] != null && !switches[0].IsActive;

                case LockType.multiSwitch:
                    if (switches == null) return false;
                    foreach (Switch s in switches)
                        if (s != null && !s.IsActive) return true;
                    return false;

                case LockType.key:
                    return !keyUnlocked;

                case LockType.demon:
                    return IsHidden;

                default:
                    return false;
            }
        }
    }

    // Key lock: whatever opens it (a key item, later) calls this
    public void Unlock() => keyUnlocked = true;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        originalSize = box.size;
        room = GetComponentInParent<DungeonRoom>();

        if (blockedVisual != null) blockedVisual.SetActive(destinationDoor == null);
        // A lock with nothing to check is left open rather than locking the door forever
        if ((lockType == LockType.singleSwitch || lockType == LockType.multiSwitch) &&
            (switches == null || switches.Length == 0))
            Debug.LogWarning($"{name}: lock is {lockType} but no switches are assigned.", this);
        if (lockType == LockType.demon && demon == null)
            Debug.LogWarning($"{name}: lock is demon but no demon is assigned.", this);

        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag(playerTag);
            if (p != null) player = p.GetComponent<MovementSystem>();
        }
        if (input == null) input = FindFirstObjectByType<InputTrackerSystem>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    // Sidescroller: in the trigger + on the ground + press up
    void Update()
    {
        RefreshLock();

        if (input == null || player == null) return;

        // Track the held state every frame (even while blocked) so holding up
        // through a fade or menu doesn't count as a new press
        bool upHeld = input.Move.y > upThreshold;
        bool upPressed = upHeld && !upWasHeld;
        upWasHeld = upHeld;

        if (!sidescroller || !upPressed) return;
        if (!playerInside || !player.Grounded) return;
        if (!CanStart() || EquipMenu.IsOpen) return;
        if (!IsBestCandidate()) return;

        StartCoroutine(Transition());
    }

    void Reset() => GetComponent<Collider>().isTrigger = true;

    void OnEnable()
    {
        all.Add(this);
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        OnPerspectiveChanged(PerspectiveShiftSystem.CurrentSidescroller,
                             PerspectiveShiftSystem.CurrentViewForward);
    }

    void OnDisable()
    {
        // Disabling stops the Transition coroutine mid-way, so it would never reach the line
        // that clears the flag. Clear it here, and un-black the screen if we were mid-fade.
        if (runningTransition)
        {
            runningTransition = false;
            IsTransitioning = false;
            if (fade != null) fade.alpha = 0f;
        }

        all.Remove(this);
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;
        box.size = originalSize;
        playerInside = false;
        touching = false;
    }

    // Same idea as PlatformSystem: stretch the trigger along the view axis so the
    // player's locked plane always passes through it, whatever depth the door is at
    void OnPerspectiveChanged(bool sidescrollerMode, Vector3 viewForward)
    {
        sidescroller = sidescrollerMode;

        if (!sidescrollerMode)
        {
            box.size = originalSize;
            return;
        }

        Vector3 local = transform.InverseTransformDirection(viewForward);
        int axis = 0;
        if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
        if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

        Vector3 size = originalSize;
        size[axis] = Mathf.Max(originalSize[axis],
                               sidescrollerDepth / Mathf.Abs(transform.lossyScale[axis]));
        box.size = size;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        touching = true;

        // Top-down: walking into the door is enough
        if (!sidescroller && !ignoreUntilExit && CanStart())
            StartCoroutine(Transition());
    }

    void OnTriggerStay(Collider other)
    {
        // Covers the trigger being resized around a player who is already standing there
        if (other.CompareTag(playerTag)) touching = true;
    }

    // OnTriggerExit is NOT sent when the player's CharacterController is disabled (which
    // Teleport does), so a door could stay "occupied" forever. Instead, every physics step
    // we keep only what Enter/Stay reported since the last step.
    void FixedUpdate()
    {
        playerInside = touching;
        touching = false;

        if (!playerInside) ignoreUntilExit = false;
    }

    // The door has to be further from the camera than the player
    bool PlayerIsInFront()
    {
        Vector3 forward = PerspectiveShiftSystem.CurrentViewForward.normalized;
        return Vector3.Dot(transform.position - player.transform.position, forward) > 0.01f;
    }

    // A door qualifies if the player is inside its trigger and in front of it (the door is
    // further from the camera than the player). Stretched triggers overlap, so several doors
    // can qualify at once; the one closest behind the player wins. Without this, the door
    // you just arrived through could lose to the door you came from.
    bool IsBestCandidate()
    {
        Vector3 forward = PerspectiveShiftSystem.CurrentViewForward.normalized;

        Door best = null;
        float bestDepth = float.MaxValue;

        foreach (Door d in all)
        {
            if (!d.playerInside || d.IsHidden) continue; // a hidden door isn't there to use

            float depth = Vector3.Dot(d.transform.position - player.transform.position, forward);
            if (depth <= 0.01f) continue; // player isn't in front of this door

            if (depth < bestDepth) { bestDepth = depth; best = d; }
        }

        return best == this;
    }


    bool CanStart()
    {
        return !IsTransitioning
            && !PerspectiveShiftSystem.IsTransitioning
            && !DungeonMapSystem.IsEditing
            && player != null
            && destinationDoor != null
            && destinationDoor != this
            && !IsLocked;
    }

    // Keeps what the door shows in step with its lock (and the lock of the door it leads to)
    void RefreshLock()
    {
        bool locked = IsLocked;
        bool hidden = IsHidden;
        if (lockApplied && locked == lockedShown && hidden == hiddenShown) return;

        lockApplied = true;
        lockedShown = locked;
        hiddenShown = hidden;

        if (lockedVisual != null) lockedVisual.SetActive(locked && !hidden);

        // Hiding and revealing is done with the room's visibility, which also covers walls and the
        // top-down door exception
        if (room != null) room.RefreshVisibility();
    }

    IEnumerator Transition()
    {
        IsTransitioning = true;
        runningTransition = true;

        yield return Fade(0f, 1f, fadeOutTime);

        Vector3 landing = destinationDoor.transform.position;
        bool flipView = false;

        if (sidescroller)
        {
            // The view is about to turn 180 degrees, so the new camera side is where we are now.
            // Landing one step that way puts the player in front of the door, inside the room.
            Vector3 forward = PerspectiveShiftSystem.CurrentViewForward.normalized;

            // Which side of the destination door is the inside of its room, along the view axis?
            float inside = 1f;
            if (destinationDoor.room != null)
            {
                Vector3 toCenter = destinationDoor.room.GetBounds().center - destinationDoor.transform.position;
                inside = Vector3.Dot(toCenter, forward) >= 0f ? 1f : -1f;
            }

            landing += forward * inside * arrivalDistance;

            // The camera has to end up on the inside looking at the door. If the inside is along
            // the current view direction, that means turning the view around.
            flipView = inside > 0f;
        }
        else
        {
            // Top-down: we land inside the destination's trigger, so don't bounce straight back
            destinationDoor.ignoreUntilExit = true;
        }

        Debug.Log($"Door '{name}' -> '{destinationDoor.name}'. Player was at {player.transform.position}, " +
          $"destination door at {destinationDoor.transform.position}, landing at {landing}", this);

        // Teleport also re-anchors the sidescroller plane at the new spot
        player.Teleport(landing, player.transform.rotation);
        if (flipView) PerspectiveShiftSystem.FlipView();

        if (destinationDoor.room != null) DungeonRoom.SetCurrent(destinationDoor.room);
        yield return new WaitForSeconds(holdTime);

        yield return Fade(1f, 0f, fadeInTime);

        runningTransition = false;
        IsTransitioning = false;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        if (fade == null) yield break;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            fade.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        fade.alpha = to;
    }

    // Link to another door, or pass null to block this one
    public void SetDestination(Door door)
    {
        destinationDoor = door;
        if (blockedVisual != null) blockedVisual.SetActive(door == null);
    }

    // Switch the trigger on or off (the dungeon map only keeps the current room's doors live)
    public void SetTriggerActive(bool active)
    {
        if (box == null) box = GetComponent<BoxCollider>(); // Awake hasn't run if this door is inactive

        box.enabled = active;
        if (!active)
        {
            touching = false;
            playerInside = false;
            ignoreUntilExit = false;
        }
    }
}
