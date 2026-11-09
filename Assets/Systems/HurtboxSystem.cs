using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class HurtboxSystem : MonoBehaviour, IDamageable
{
    [SerializeField] DamageSystem owner;            // found on a parent if left empty
    [SerializeField] float sidescrollerDepth = 30f;

    BoxCollider box;
    Vector3 originalSize;
    Quaternion originalLocalRotation;

    bool sidescroller;
    Vector3 viewForward = Vector3.forward;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        originalSize = box.size;
        if (owner == null) owner = GetComponentInParent<DamageSystem>();

        // A collider that moves every frame should have a kinematic Rigidbody
        if (!TryGetComponent(out Rigidbody rb)) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnEnable()
    {
        PerspectiveShiftSystem.PerspectiveChanged += OnPerspectiveChanged;
        OnPerspectiveChanged(PerspectiveShiftSystem.CurrentSidescroller,
                             PerspectiveShiftSystem.CurrentViewForward);
    }

    void OnDisable() => PerspectiveShiftSystem.PerspectiveChanged -= OnPerspectiveChanged;

    void OnPerspectiveChanged(bool sidescrollerMode, Vector3 view)
    {
        sidescroller = sidescrollerMode;
        viewForward = view;

        if (!sidescroller)
        {
            box.size = originalSize;
            return;
        }

        // Which local axis of this box points along the view direction?
        //Vector3 local = transform.InverseTransformDirection(viewForward);
        //int axis = 0;
        //if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
        //if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

        Vector3 size = originalSize;
        //size[axis] = Mathf.Max(originalSize[axis],
        //                       sidescrollerDepth / Mathf.Abs(transform.lossyScale[axis]));
        size.z = Mathf.Max(originalSize.z, sidescrollerDepth / Mathf.Abs(transform.lossyScale.z));
        box.size = size;
    }

    // After the enemy has rotated this frame, undo the inherited rotation
    void LateUpdate()
    {
        if (sidescroller)
            transform.rotation = Quaternion.LookRotation(viewForward, Vector3.up);
    }

    public void TakeDamage(int amount)
    {
        if (owner != null) owner.TakeDamage(amount);
    }
}
