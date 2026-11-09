using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RosaryTrailSystem : MonoBehaviour
{
    [Header("Beads")]
    [SerializeField, Min(1)] int startingBeads = 24;
    [SerializeField, Min(1)] int maximumBeads = 53;
    [SerializeField, Min(0.1f)] float beadSpacing = 0.65f;
    [SerializeField, Min(0.01f)] float beadSize = 0.12f;
    [SerializeField, Min(0.01f)] float beadReturnInterval = 0.7f;

    [Header("Bead Visual")]
    [SerializeField] GameObject beadModel;

    [Header("Detonation")]
    [SerializeField, Min(0f)] float delayPerBead = 0.12f;
    [SerializeField, Min(0.1f)] float smallAreaRadius = 0.7f;
    [SerializeField, Min(0.1f)] float smallExplosionRadius = 1.4f;
    [SerializeField, Min(0.1f)] float mediumExplosionRadius = 2.2f;
    [SerializeField, Min(0.1f)] float largeExplosionRadius = 3.2f;
    [SerializeField, Min(1)] int damage = 20;
    [SerializeField] LayerMask damageMask = ~0;

    readonly List<Vector3> trail = new();
    readonly List<GameObject> beadVisuals = new();

    int beads;
    bool laying;
    bool detonating;
    Vector3 lastBeadPosition;
    float nextBeadReturn;

    public bool IsLaying => laying;
    public int BeadCount => beads;

    void Awake()
    {
        beads = Mathf.Clamp(startingBeads, 0, maximumBeads);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (beads < maximumBeads && Time.time >= nextBeadReturn)
        {
            beads++;
            nextBeadReturn = Time.time + beadReturnInterval;
        }
    }

    public bool BeginTrail()
    {
        if (laying || detonating || beads <= 0 ||
            PerspectiveShiftSystem.IsTransitioning)
            return false;

        ClearTrail();
        laying = true;
        PlaceBead(transform.position);

        return true;
    }

    public void ContinueTrail()
    {
        if (!laying || PerspectiveShiftSystem.IsTransitioning)
            return;

        Vector3 position = transform.position;

        // Turning in place doesn't place more beads.
        if (FlatDistance(position, lastBeadPosition) < beadSpacing)
            return;

        // Moving far enough to place bead 13 auto-releases the trail.
        if (trail.Count >= 12)
        {
            ReleaseTrail();
            return;
        }

        PlaceBead(position);
    }

    public void ReleaseTrail()
    {
        if (!laying) return;

        laying = false;

        if (trail.Count == 0)
        {
            ClearTrail();
            return;
        }

        int count = trail.Count;

        float radius = count < 5 ? smallAreaRadius
            : count < 9 ? smallExplosionRadius
            : count < 12 ? mediumExplosionRadius
            : largeExplosionRadius;

        List<Vector3> points = new(trail);
        List<GameObject> visuals = new(beadVisuals);

        trail.Clear();
        beadVisuals.Clear();

        nextBeadReturn = Time.time + beadReturnInterval;
        detonating = true;

        StartCoroutine(DetonateTrail(points, visuals, count, radius));
    }

    void PlaceBead(Vector3 position)
    {
        if (beads <= 0)
        {
            ReleaseTrail();
            return;
        }

        // Snap beads to the ground where possible.
        Vector3 rayStart = position + Vector3.up * 0.5f;

        if (Physics.Raycast(
            rayStart,
            Vector3.down,
            out RaycastHit hit,
            3f,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            position = hit.point + Vector3.up * 0.025f;
        }

        trail.Add(position);
        lastBeadPosition = transform.position;

        beads--;
        nextBeadReturn = Time.time + beadReturnInterval;

        if (beadModel != null)
        {
            GameObject visual = Instantiate(beadModel, position, Quaternion.identity);

            visual.name = "Rosary Bead";
            visual.transform.localScale = Vector3.one * beadSize;

            Collider collider = visual.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            beadVisuals.Add(visual);

        } else {
            
            // Placeholder bead visual.
            GameObject visual =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            visual.name = "Rosary Bead";
            visual.transform.position = position;
            visual.transform.localScale = Vector3.one * beadSize;

            Collider collider = visual.GetComponent<Collider>();

            if (collider != null)
            Destroy(collider);

            beadVisuals.Add(visual);
        }
    }

    IEnumerator DetonateTrail(
        List<Vector3> points,
        List<GameObject> visuals,
        int count,
        float radius)
    {
        // The fuse burns backward along the trail toward the starting point.
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (i < points.Count - 1 && delayPerBead > 0f)
                yield return new WaitForSeconds(delayPerBead);

            if (i < visuals.Count && visuals[i] != null)
                Destroy(visuals[i]);
        }

        // The explosion occurs where the player originally began laying beads.
        Vector3 center = points[0];
        int damageMultiplier = count < 5 ? 1
            : count < 9 ? 2
            : count < 12 ? 3
            : 4;

        int finalDamage = damage * damageMultiplier;
        DamageAt(center, radius, finalDamage);

        foreach (GameObject visual in visuals)
        {
            if (visual != null)
                Destroy(visual);
        }

        detonating = false;
    }

    void DamageAt(Vector3 point, float radius, int amount)
    {
        Collider[] hits = Physics.OverlapSphere(
            point,
            radius,
            damageMask,
            QueryTriggerInteraction.Ignore);

        // One hit per enemy, however many colliders it has
        HashSet<object> damaged = new();

        foreach (Collider hit in hits)
        {
            // Enemies (and the player) have a DamageSystem
            DamageSystem target = hit.GetComponentInParent<DamageSystem>();
            if (target != null)
            {
                if (target.gameObject == gameObject) continue; // never hurt the player

                if (damaged.Add(target))
                    target.TakeDamage(amount);

                continue;
            }

            // Anything else that can be damaged, like a crystal switch
            IDamageable other = hit.GetComponentInParent<IDamageable>();
            if (other != null && damaged.Add(other))
                other.TakeDamage(amount);
        }
    }

    void ClearTrail()
    {
        foreach (GameObject visual in beadVisuals)
        {
            if (visual != null)
                Destroy(visual);
        }

        beadVisuals.Clear();
        trail.Clear();
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;

        return Vector3.Distance(a, b);
    }
}
