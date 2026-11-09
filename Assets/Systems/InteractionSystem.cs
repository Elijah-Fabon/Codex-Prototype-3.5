using UnityEngine;

// Lives on the player. When jump/dash is pressed on the ground inside an InteractZone,
// it interacts and eats the press so the player doesn't also jump or dash.
public class InteractionSystem : MonoBehaviour
{
    [SerializeField] InputTrackerSystem input;
    [SerializeField] MovementSystem movement;
    [SerializeField] CharacterController body;
    [SerializeField] LayerMask interactMask = ~0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void Reset()
    {
        movement = GetComponent<MovementSystem>();
        body = GetComponent<CharacterController>();
    }

    void OnEnable() => input.CodeButtonPressed += OnCodeButton;
    void OnDisable() => input.CodeButtonPressed -= OnCodeButton;

    // CodeButton.Action is the jump/dash button. InputTrackerSystem only raises it when input
    // isn't blocked (door/rotation transitions, equip menu).
    void OnCodeButton(CodeButton button)
    {
        if (button != CodeButton.Action) return;

        // While the map is open, jump/dash belongs to the map screen (it cancels). Also skip the
        // press that just opened or closed it, whichever handler ran first.
        if (DungeonMapSystem.IsEditing || DungeonMapSystem.EditChangedThisFrame) return;

        if (!movement.Grounded) return; // not in the air

        InteractZone zone = FindNearestZone();
        if (zone == null) return;

        zone.Interact();
        input.ConsumeJumpDash(); // MovementSystem won't see this press as a jump/dash
    }

    // Stateless check (no enter/exit bookkeeping) so teleporting can't leave stale "nearby" objects
    InteractZone FindNearestZone()
    {
        Bounds b = body.bounds;
        Collider[] hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity,
                                             interactMask, QueryTriggerInteraction.Collide);

        InteractZone best = null;
        float bestDist = float.MaxValue;

        foreach (Collider hit in hits)
        {
            InteractZone zone = hit.GetComponent<InteractZone>();
            if (zone == null) continue;

            float dist = (zone.transform.position - transform.position).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; best = zone; }
        }

        return best;
    }
}
