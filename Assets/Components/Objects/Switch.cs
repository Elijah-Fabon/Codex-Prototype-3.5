using System;
using UnityEngine;
using UnityEngine.Events;

// A switch whose behaviour and look are picked in the editor.
//
// Activation method (also picks the model, for now):
//   stand : a floor plate (cube). The player has to be standing on it (on the ground).
//   hit   : a crystal (sphere). Anything that damages it counts: sword, projectiles, etc.
//
// Type:
//   oneTime       : turns on and stays on
//   pressurePlate : on only while it is stood on (needs the stand method)
//   timed         : turns on, then off by itself after Duration seconds; another press restarts the time
//   toggle        : every press flips it
//   torch : a post with a flame on top. Only fireballs (the rosary's projectiles) light it, and later
//           firecrackers. Swords and rosary explosions do nothing to it. Stretched like the crystal.
//
// Whatever should react listens to On Activated / On Deactivated (or StateChanged), or reads IsActive.
// The model and collider are built when the game starts.
public class Switch : MonoBehaviour, IDamageable
{
    enum Type {oneTime, pressurePlate, timed, toggle}
    enum ActivationMethod {stand, hit, torch}

    [Header("Behaviour")]
    [SerializeField] Type type = Type.oneTime;
    [SerializeField] ActivationMethod activationMethod = ActivationMethod.stand;
    [SerializeField] float duration = 5f; // timed only: how long it stays on

    [Header("Look")]
    [SerializeField] Vector3 floorSize = new Vector3(1.2f, 0.1f, 1.2f); // the floor plate
    [SerializeField] float crystalSize = 0.8f;                          // the crystal's diameter
    [SerializeField] float torchHeight = 1.2f;                          // the torch's post
    [SerializeField] float torchWidth = 0.15f;                          // the post's thickness
    [SerializeField] float flameSize = 0.4f;                            // the flame's diameter
    [SerializeField] Color postColor = new Color(0.35f, 0.22f, 0.12f, 1f);
    [SerializeField] Color offColor = new Color(0.5f, 0.5f, 0.55f, 1f);
    [SerializeField] Color onColor = new Color(0.3f, 1f, 0.5f, 1f);

    [Header("Sidescroller")]
    [SerializeField] float sidescrollerDepth = 30f;  // the plate, crystal or torch is stretched this deep along the view axis

    [Header("Floor switch only")]
    [SerializeField] float triggerHeight = 0.5f;     // how far above the floor a standing player is noticed
    [SerializeField] string playerTag = "Player";

    [Header("Events")]
    [SerializeField] UnityEvent onActivated;
    [SerializeField] UnityEvent onDeactivated;

    public event Action<bool> StateChanged; // true = turned on, false = turned off

    public bool IsActive => activated;

    bool activated;
    float timer;

    Renderer modelRenderer;
    Collider hitbox;          // the plate's trigger or the crystal's solid collider
    BoxCollider box;                 // floor plate (a trigger) or torch (solid)
    Vector3 boxSize;
    CapsuleCollider crystalCollider; // hit switch only: a sphere in top-down, a long capsule in sidescroller
    Vector3 triggerSize;
    bool roomActive = true;   // the dungeon map switches this off while the player is in another room

    MovementSystem player;
    bool touching;            // set by trigger events during the current physics step
    bool standing;            // the player has been on the plate since the last physics step

    void Awake()
    {
        if (type == Type.pressurePlate) activationMethod = ActivationMethod.stand;

        BuildModel();
        ApplyColor();

        GameObject p = GameObject.FindGameObjectWithTag(playerTag);
        if (p != null) player = p.GetComponent<MovementSystem>();
    }

    void OnEnable()
    {
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        OnPerspectiveChanged(PerspectiveShiftSystem.CurrentSidescroller,
                             PerspectiveShiftSystem.CurrentViewForward);
    }

    void OnDisable()
    {
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;
        if (box != null) box.size = triggerSize;
        if (crystalCollider != null) StretchCrystal(false, Vector3.forward);
        touching = false;
        standing = false;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (type != Type.timed || !activated) return;

        // The timer waits while a rotation, door or the map has taken over, like everything else
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning || DungeonMapSystem.IsEditing) return;

        timer -= Time.deltaTime;
        if (timer <= 0f) SetState(false);
    }

    // ---------------------------------------------------------------- building

    // A placeholder model: a flat cube for the floor plate, a sphere for the crystal. The collider
    // sits on this object (not the model) so a hit finds this script.
    void BuildModel()
    {
        switch (activationMethod)
        {
            case ActivationMethod.stand: BuildFloor(); break;
            case ActivationMethod.hit: BuildCrystal(); break;
            default: BuildTorch(); break;
        }

        hitbox.enabled = roomActive;
    }

    GameObject MakePart(PrimitiveType shape, string partName)
    {
        GameObject part = GameObject.CreatePrimitive(shape);
        part.name = partName;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(transform, false);
        return part;
    }

    // A flat cube resting on the floor. Its trigger reaches a little above it, so the player
    // standing on it (feet on the floor) overlaps it.
    void BuildFloor()
    {
        GameObject model = MakePart(PrimitiveType.Cube, "Floor Switch Model");
        model.transform.localScale = floorSize;
        model.transform.localPosition = Vector3.up * (floorSize.y * 0.5f);
        modelRenderer = model.GetComponent<Renderer>();

        boxSize = new Vector3(floorSize.x, triggerHeight, floorSize.z);
        box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = boxSize;
        box.center = Vector3.up * (triggerHeight * 0.5f);
        hitbox = box;
    }

    // A sphere. The collider is solid, so projectiles and swings can hit it. It is a capsule that is
    // exactly a sphere until sidescroller stretches it along the view axis.
    void BuildCrystal()
    {
        GameObject model = MakePart(PrimitiveType.Sphere, "Crystal Model");
        float radius = crystalSize * 0.5f;
        model.transform.localScale = Vector3.one * crystalSize;
        model.transform.localPosition = Vector3.up * radius;
        modelRenderer = model.GetComponent<Renderer>();

        crystalCollider = gameObject.AddComponent<CapsuleCollider>();
        crystalCollider.isTrigger = false;
        crystalCollider.radius = radius;
        crystalCollider.center = Vector3.up * radius;
        crystalCollider.direction = 1;
        crystalCollider.height = crystalSize;
        hitbox = crystalCollider;
    }

    // A post with a flame sphere on top. One solid box covers both, so a fireball that hits the post
    // lights it too; sidescroller stretches it along the view axis.
    void BuildTorch()
    {
        // Unity's cylinder is 2 tall and 1 wide, hence the half scale
        GameObject post = MakePart(PrimitiveType.Cylinder, "Torch Post");
        post.transform.localScale = new Vector3(torchWidth, torchHeight * 0.5f, torchWidth);
        post.transform.localPosition = Vector3.up * (torchHeight * 0.5f);
        FirewallWall.Tint(post.GetComponent<Renderer>(), postColor);

        GameObject flame = MakePart(PrimitiveType.Sphere, "Torch Flame");
        flame.transform.localScale = Vector3.one * flameSize;
        flame.transform.localPosition = Vector3.up * (torchHeight + flameSize * 0.3f);
        modelRenderer = flame.GetComponent<Renderer>();

        float width = Mathf.Max(torchWidth, flameSize);
        float height = torchHeight + flameSize * 0.8f; // to the top of the flame
        boxSize = new Vector3(width, height, width);

        box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = false; // solid, so the fireball's sweep finds it
        box.size = boxSize;
        box.center = Vector3.up * (height * 0.5f);
        hitbox = box;
    }

    void ApplyColor()
    {
        if (modelRenderer != null) FirewallWall.Tint(modelRenderer, activated ? onColor : offColor);
    }

    // ---------------------------------------------------------------- pressing

    // Hit method: anything that damages the crystal presses it
    public void TakeDamage(int amount)
    {
        if (activationMethod == ActivationMethod.hit) Press();
    }

    // Torch method: only fire lights it. Fireballs call this when they hit; firecrackers can later.
    public void Ignite()
    {
        if (activationMethod == ActivationMethod.torch) Press();
    }

    void OnTriggerEnter(Collider other) => Track(other);
    void OnTriggerStay(Collider other) => Track(other);

    void Track(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        // Standing means on the ground, not jumping over it
        if (player == null || player.Grounded) touching = true;
    }

    // OnTriggerExit isn't sent when the player's CharacterController is disabled (doors do that when
    // they teleport), so standing on the plate is worked out from what Enter/Stay reported over each
    // physics step instead.
    void FixedUpdate()
    {
        bool onPlate = touching;
        touching = false;

        if (onPlate == standing) return;
        standing = onPlate;

        if (standing) Press();
        else Release();
    }

    // A press: stepped onto the plate, or the crystal was hit
    void Press()
    {
        switch (type)
        {
            case Type.oneTime:
                SetState(true);
                break;

            case Type.pressurePlate:
                SetState(true);
                break;

            case Type.timed:
                timer = duration;
                SetState(true);
                break;

            case Type.toggle:
                SetState(!activated);
                break;
        }
    }

    // Stepped off the plate. Only a pressure plate cares.
    void Release()
    {
        if (type == Type.pressurePlate) SetState(false);
    }

    // Turns it on or off and tells whoever is listening. Also handy for resetting a puzzle.
    public void SetState(bool value)
    {
        if (value == activated) return;

        activated = value;
        ApplyColor();

        if (value) onActivated?.Invoke();
        else onDeactivated?.Invoke();

        StateChanged?.Invoke(value);
    }

    // ---------------------------------------------------------------- dungeon and perspective

    // Same trick as the doors and zones: stretch the plate's trigger along the view axis so the
    // player's locked plane always passes through it, whatever depth the plate is at
    void OnPerspectiveChanged(bool sidescrollerMode, Vector3 viewForward)
    {
        if (box != null)
        {
            if (!sidescrollerMode)
            {
                box.size = boxSize;
            }
            else
            {
                int axis = LocalViewAxis(viewForward);
                Vector3 size = boxSize;
                size[axis] = Mathf.Max(boxSize[axis], sidescrollerDepth / Mathf.Abs(transform.lossyScale[axis]));
                box.size = size;
            }
        }

        if (crystalCollider != null) StretchCrystal(sidescrollerMode, viewForward);
    }

    // The crystal's capsule lies along the view axis, so its cross-section on the player's plane is
    // still the crystal's circle and it reaches through every depth
    void StretchCrystal(bool sidescrollerMode, Vector3 viewForward)
    {
        if (!sidescrollerMode)
        {
            crystalCollider.direction = 1;
            crystalCollider.height = crystalSize;
            return;
        }

        int axis = LocalViewAxis(viewForward);
        crystalCollider.direction = axis;
        crystalCollider.height = Mathf.Max(crystalSize, sidescrollerDepth / Mathf.Abs(transform.lossyScale[axis]));
    }

    // Which local axis of this object points along the view direction? (0 = X, 1 = Y, 2 = Z)
    int LocalViewAxis(Vector3 viewForward)
    {
        Vector3 local = transform.InverseTransformDirection(viewForward);
        int axis = 0;
        if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
        if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;
        return axis;
    }

    // Switched off by the dungeon map while the player is in another room
    public void SetColliderActive(bool value)
    {
        roomActive = value; // Awake hasn't run if this object is inactive; it applies this when it builds
        if (hitbox != null) hitbox.enabled = value;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        duration = Mathf.Max(0f, duration);
        triggerHeight = Mathf.Max(0.1f, triggerHeight);

        // A pressure plate is about standing on it
        if (type == Type.pressurePlate) activationMethod = ActivationMethod.stand;
    }

    // Draws the model and trigger in the Scene view (not in play mode) so it can be placed without
    // pressing Play
    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        if (activationMethod == ActivationMethod.stand)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(Vector3.up * (floorSize.y * 0.5f), floorSize);

            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.35f);
            Gizmos.DrawWireCube(Vector3.up * (triggerHeight * 0.5f),
                                new Vector3(floorSize.x, triggerHeight, floorSize.z));
        }
        else
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(Vector3.up * (crystalSize * 0.5f), crystalSize * 0.5f);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
