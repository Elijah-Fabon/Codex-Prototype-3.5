using UnityEngine;

// Something the whip can grab. Put it on an object with a collider on the Hook layer. The player
// targets it (hold Target) and uses the whip to zoom to it.
[RequireComponent(typeof(Collider))]
public class HookPoint : MonoBehaviour
{
    [SerializeField] Vector3 arrivalOffset = Vector3.zero; // where the player ends up, relative to the hook
    [SerializeField] GameObject targetedVisual;            // optional: switched on while this is the target
    [SerializeField] float targetedScale = 1.25f;          // with no visual, the hook swells a little instead

    Vector3 baseScale;
    bool targeted;

    // Where the player's pivot arrives
    public Vector3 ArrivalPoint => transform.TransformPoint(arrivalOffset);

    // Where the rope attaches
    public Vector3 AnchorPoint => transform.position;

    void Awake() => baseScale = transform.localScale;

    void OnDisable() => SetTargeted(false);

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetTargeted(bool value)
    {
        if (value == targeted) return;
        targeted = value;

        if (targetedVisual != null) targetedVisual.SetActive(value);
        else transform.localScale = value ? baseScale * targetedScale : baseScale;
    }
}
