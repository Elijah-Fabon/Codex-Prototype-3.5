using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ViewDirection { South, West, North, East } // side of the player the camera sits on

[DefaultExecutionOrder(100)] // run after MovementSystem so the anchor uses this frame's position
public class PerspectiveShiftSystem : MonoBehaviour
{
    [SerializeField] InputTrackerSystem input;
    [SerializeField] MovementSystem movement;
    [SerializeField] CinemachineCamera topDownCam;
    [SerializeField] CinemachineCamera sideCam;
    [SerializeField] Transform topDownCamAnchor;
    [SerializeField] Transform sideCamAnchor; // sideCam's Tracking Target
    [SerializeField] CinemachineBrain brain; // on the Main Camera
    [SerializeField] float transitionTime = 0.2f; // used for the mode blend AND the rotation
    [SerializeField] TargetingSystem targeting;

    // Room cameras (CameraRoomVolume): while any is active, the camera centers on its anchor and
    // can use its own FOV / follow offset. The most recently entered one wins.
    [Header("Room camera")]
    [SerializeField] float cameraBlendTime = 0.5f; // how long moving between rooms or changing FOV takes

    public sealed class CameraOverride
    {
        public Transform Anchor;
        public Vector3 Fallback; // used if there is no anchor
        public float? TopDownFov, SideFov;
        public Vector3? TopDownOffset, SideOffset;

        public Vector3 Point => Anchor != null ? Anchor.position : Fallback;
    }

    readonly List<CameraOverride> overrides = new List<CameraOverride>();
    CinemachineFollow topDownFollow, sideFollow;
    float topDownFovDefault, sideFovDefault;
    Vector3 topDownOffsetDefault, sideOffsetDefault;
    float topDownFovNow, sideFovNow;          // what is currently applied
    Vector3 topDownOffsetNow, sideOffsetNow;
    float overrideWeight;                     // 0 = following the player, 1 = on the room's anchor
    Vector3 overridePoint;

    const int ActivePriority = 20;
    const int InactivePriority = 10;

    static PerspectiveShiftSystem instance;

    // Platforms (and anything else) subscribe to this
    public static event Action<bool, Vector3> PerspectiveChanged;
    public static event Action<Vector3> RotationStarting; // passes the OLD view direction

    public static bool CurrentSidescroller { get; private set; }
    public static Vector3 CurrentViewForward { get; private set; } = Vector3.forward;

    // True during a mode switch or rotation. Movement, enemies, projectiles and input all check this.
    public static bool IsTransitioning => Time.time < transitionEndTime;
    static float transitionEndTime;

    public bool IsSidescroller { get; private set; }

    public ViewDirection Facing =>
        (ViewDirection)(((Mathf.RoundToInt(targetYaw / 90f) % 4) + 4) % 4);

    // Zones can lock the player's own use of the fez. Counted, so overlapping zones don't fight.
    int toggleLocks, rotationLocks;
    public bool ToggleAllowed => toggleLocks <= 0;
    public bool RotationAllowed => rotationLocks <= 0;

    float startYaw, rotateProgress = 1f;
    float targetYaw, currentYaw;

    Vector3 ViewForward => Quaternion.Euler(0f, targetYaw, 0f) * Vector3.forward;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        transitionEndTime = 0f; // clear anything left over from a previous play session

        CaptureCameraDefaults();

        // Make the camera blend between modes last exactly as long as a rotation
        var blend = brain.DefaultBlend;
        blend.Style = CinemachineBlendDefinition.Styles.EaseInOut;
        blend.Time = transitionTime * 2;
        brain.DefaultBlend = blend;

        SetPerspective(false);
    }

    // Update is called once per frame
    void Update()
    {
        // Temporary: T toggles the perspective until there's an input action for it
        //var kb = Keyboard.current;
        //if (kb != null && kb.tKey.wasPressedThisFrame) Toggle();

        UpdateRoomCamera();
        UpdateTopDownAnchor();

        // Anchor follows the player and carries the camera's rotation
        rotateProgress = Mathf.Min(rotateProgress + Time.deltaTime / transitionTime, 1f);
        currentYaw = Mathf.Lerp(startYaw, targetYaw, Mathf.SmoothStep(0f, 1f, rotateProgress));

        sideCamAnchor.SetPositionAndRotation(ApplyRoomFocus(movement.transform.position),
                                             Quaternion.Euler(0f, currentYaw, 0f));
    }

    void OnEnable()
    {
        instance = this;
        input.RotateRightPressed += OnRotateRight;
        input.RotateLeftPressed += OnRotateLeft;
    }

    void OnDisable()
    {
        if (instance == this) instance = null;
        input.RotateRightPressed -= OnRotateRight;
        input.RotateLeftPressed -= OnRotateLeft;
        transitionEndTime = 0f;
    }

    void OnRotateRight() { if (IsSidescroller && RotationAllowed) Rotate(1); }
    void OnRotateLeft() { if (IsSidescroller && RotationAllowed) Rotate(-1); }

    public void SetPerspective(bool sidescroller, bool freeze = true)
    {
        if (freeze) BeginTransition(transitionTime * 2); // matches the camera blend time

        IsSidescroller = sidescroller;

        // Camera: raising one priority makes the Brain blend to it
        sideCam.Priority = sidescroller ? ActivePriority : InactivePriority;
        topDownCam.Priority = sidescroller ? InactivePriority : ActivePriority;

        // Input and movement
        input.SetMode(sidescroller);
        movement.SidescrollerMode = sidescroller;
        if (sidescroller) movement.SetSidescrollerPlane(ViewForward);

        Broadcast(sidescroller, ViewForward);
    }

    static void Broadcast(bool sidescroller, Vector3 viewForward)
    {
        CurrentSidescroller = sidescroller;
        CurrentViewForward = viewForward;
        PerspectiveChanged?.Invoke(sidescroller, viewForward);
    }

    static void BeginTransition(float duration) => transitionEndTime = Time.time + duration;

    public void Toggle()
    {
        if (IsTransitioning || EquipMenu.IsOpen || DungeonMapSystem.IsEditing || !ToggleAllowed) return;
        SetPerspective(!IsSidescroller);
    }

    // Turns the sidescroller view 180 degrees with the normal rotation animation.
    // Doors call this during the blackout, right after teleporting.
    public static void FlipView()
    {
        if (instance != null && instance.IsSidescroller) instance.Rotate(2, false);
    }

    void Rotate(int step, bool notifyPlatforms = true)
    {
        BeginTransition(transitionTime);

        // Platforms snap the player along the old depth axis. A door flip skips that, so it
        // can't nudge the player off the spot they were just teleported to.
        if (notifyPlatforms) RotationStarting?.Invoke(ViewForward);

        startYaw = currentYaw;   // so a press mid-turn continues smoothly
        rotateProgress = 0f;
        targetYaw += 90f * step;
        movement.SetSidescrollerPlane(ViewForward); // new plane through the player's current spot
        Broadcast(true, ViewForward);
        Debug.Log($"Camera now {Facing}");
    }

    public void SetToggleLock(bool locked) => toggleLocks = Mathf.Max(0, toggleLocks + (locked ? 1 : -1));
    public void SetRotationLock(bool locked) => rotationLocks = Mathf.Max(0, rotationLocks + (locked ? 1 : -1));

    // Turns the view to a direction. In top-down there's nothing on screen to animate, so it's
    // set instantly and the next switch to sidescroller uses it. In sidescroller it rotates.
    public void FaceDirection(ViewDirection direction)
    {
        int current = ((Mathf.RoundToInt(targetYaw / 90f) % 4) + 4) % 4;
        int delta = ((int)direction - current + 4) % 4; // clockwise steps, 0 to 3
        if (delta == 0) return;

        int step = delta == 3 ? -1 : delta;             // 3 clockwise = 1 counter-clockwise

        if (IsSidescroller)
        {
            Rotate(step); // a step of 2 is a 180 degree turn
            return;
        }

        targetYaw += 90f * step;
        startYaw = currentYaw = targetYaw;
        rotateProgress = 1f;
    }

    void UpdateTopDownAnchor()
    {
        if (topDownCamAnchor == null || movement == null)
            return;

        Vector3 playerPosition = movement.transform.position;
        Vector3 anchorPosition = playerPosition;

        if (targeting != null)
        {
            Transform target = targeting.EnemyTarget;

            if (target == null && targeting.GridTarget != null)
                target = targeting.GridTarget.transform;

            if (target != null)
            {
                anchorPosition = Vector3.Lerp(
                    playerPosition,
                    target.position,
                    0.5f);
            }
        }

        topDownCamAnchor.position = ApplyRoomFocus(anchorPosition);
    }

    public void AddCameraOverride(CameraOverride o)
    {
        if (o != null && !overrides.Contains(o)) overrides.Add(o);
    }

    public void RemoveCameraOverride(CameraOverride o) => overrides.Remove(o);

    void CaptureCameraDefaults()
    {
        topDownFollow = topDownCam.GetComponent<CinemachineFollow>();
        sideFollow = sideCam.GetComponent<CinemachineFollow>();

        topDownFovDefault = topDownFovNow = topDownCam.Lens.FieldOfView;
        sideFovDefault = sideFovNow = sideCam.Lens.FieldOfView;

        if (topDownFollow != null) topDownOffsetDefault = topDownOffsetNow = topDownFollow.FollowOffset;
        if (sideFollow != null) sideOffsetDefault = sideOffsetNow = sideFollow.FollowOffset;
    }

    // Where the camera centers: the player, or the room's anchor as the room takes over
    Vector3 ApplyRoomFocus(Vector3 followPoint)
        => overrideWeight <= 0f
            ? followPoint
            : Vector3.Lerp(followPoint, overridePoint, Mathf.SmoothStep(0f, 1f, overrideWeight));

    void UpdateRoomCamera()
    {
        float blend = Mathf.Max(cameraBlendTime, 0.01f);
        float k = 1f - Mathf.Exp(-Time.deltaTime * 5f / blend); // eases to about 99% over the blend time

        // Anchor
        bool active = overrides.Count > 0;
        if (active)
        {
            Vector3 target = overrides[overrides.Count - 1].Point;
            overridePoint = overrideWeight <= 0f ? target : Vector3.Lerp(overridePoint, target, k);
        }
        overrideWeight = Mathf.MoveTowards(overrideWeight, active ? 1f : 0f, Time.deltaTime / blend);

        // FOV and offset, only written while they are changing so the inspector can still be tweaked
        float wantTopFov = ResolveFov(false, topDownFovDefault);
        if (topDownFovNow != wantTopFov)
        {
            topDownFovNow = Approach(topDownFovNow, wantTopFov, k);
            topDownCam.Lens.FieldOfView = topDownFovNow;
        }

        float wantSideFov = ResolveFov(true, sideFovDefault);
        if (sideFovNow != wantSideFov)
        {
            sideFovNow = Approach(sideFovNow, wantSideFov, k);
            sideCam.Lens.FieldOfView = sideFovNow;
        }

        if (topDownFollow != null)
        {
            Vector3 want = ResolveOffset(false, topDownOffsetDefault);
            if (topDownOffsetNow != want)
            {
                topDownOffsetNow = Approach(topDownOffsetNow, want, k);
                topDownFollow.FollowOffset = topDownOffsetNow;
            }
        }

        if (sideFollow != null)
        {
            Vector3 want = ResolveOffset(true, sideOffsetDefault);
            if (sideOffsetNow != want)
            {
                sideOffsetNow = Approach(sideOffsetNow, want, k);
                sideFollow.FollowOffset = sideOffsetNow;
            }
        }
    }

    // The most recent room that sets this value, or the camera's own
    float ResolveFov(bool side, float fallback)
    {
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            float? v = side ? overrides[i].SideFov : overrides[i].TopDownFov;
            if (v.HasValue) return v.Value;
        }
        return fallback;
    }

    Vector3 ResolveOffset(bool side, Vector3 fallback)
    {
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            Vector3? v = side ? overrides[i].SideOffset : overrides[i].TopDownOffset;
            if (v.HasValue) return v.Value;
        }
        return fallback;
    }

    static float Approach(float current, float target, float k)
        => Mathf.Abs(current - target) < 0.001f ? target : Mathf.Lerp(current, target, k);

    static Vector3 Approach(Vector3 current, Vector3 target, float k)
        => (current - target).sqrMagnitude < 0.0001f ? target : Vector3.Lerp(current, target, k);
}
