using UnityEngine;

public interface IInteractable
{
    void Interact();
}

// Put this on any object that should react to the jump/dash button when the player stands near it.
// The same object needs a component that implements IInteractable (e.g. DungeonMapSystem).
[RequireComponent(typeof(BoxCollider))]
public class InteractZone : MonoBehaviour
{
    [SerializeField] float sidescrollerDepth = 30f; // zone is stretched this deep along the view axis in sidescroller

    BoxCollider box;
    Vector3 originalSize;
    IInteractable target;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        originalSize = box.size;
        target = GetComponent<IInteractable>();

        if (target == null)
            Debug.LogError($"{name} has an InteractZone but nothing that implements IInteractable.", this);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnEnable()
    {
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        OnPerspectiveChanged(PerspectiveShiftSystem.CurrentSidescroller,
                             PerspectiveShiftSystem.CurrentViewForward);
    }

    void OnDisable()
    {
        PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;
        box.size = originalSize;
    }

    public void Interact() => target?.Interact();

    // Same trick as the doors and platforms: stretch the trigger along the view axis so the
    // player's locked plane always passes through it, whatever depth the object is at
    void OnPerspectiveChanged(bool sidescrollerMode, Vector3 viewForward)
    {
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

    // Switched off by the dungeon map while the player is in another room
    public void SetColliderActive(bool active)
    {
        if (box == null) box = GetComponent<BoxCollider>(); // Awake hasn't run if this object is inactive
        box.enabled = active;
    }
}
