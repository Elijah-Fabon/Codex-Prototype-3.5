using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] float radius = 0.15f;
    [SerializeField] float lifetime = 3f;
    [SerializeField] int damage = 1;
    [SerializeField] LayerMask hitMask = ~0; // set this to exclude the Player layer
    [SerializeField] LayerMask groundMask;
    [SerializeField] bool gravity = false;

    [Header("Explosion")]
    [SerializeField] bool explode = false;
    [SerializeField] float timer = 2.5f;
    [SerializeField] float explosionRadius = 1.5f;

    Vector3 velocity;
    Rigidbody rb;
    float fuseTimer;
    bool hasExploded;
    bool frozen;
    Vector3 frozenVelocity;
    Vector3 frozenAngularVelocity;

    public float Timer => timer;
    public float ExplosionRadius => explosionRadius;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        // Frozen during a rotation or mode switch (a Rigidbody has to be held still as well)
        bool transitioning = PerspectiveShiftSystem.IsTransitioning;
        if (rb != null) SetFrozen(transitioning);
        if (transitioning) return;

        if (explode)
        {
            fuseTimer -= Time.deltaTime;

            if (fuseTimer <= 0f)
            {
                Explode();
                return;
            }
        }

        // Rigidbody projectile
        if (rb != null) return;

        Vector3 step = velocity * Time.deltaTime;
        float dist = step.magnitude;
        if (dist <= 0f) return;

        // Sweep along this frame's movement so we never skip over something
        if (Physics.SphereCast(transform.position, radius, step / dist, out RaycastHit hit,
                               dist, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.TryGetComponent(out IDamageable d)) d.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        transform.position += step;
    }

    // Holds a Rigidbody still and lets it carry on from the same speed afterwards
    void SetFrozen(bool freeze)
    {
        if (freeze == frozen) return;
        frozen = freeze;

        if (freeze)
        {
            frozenVelocity = rb.linearVelocity;
            frozenAngularVelocity = rb.angularVelocity;
            rb.isKinematic = true;
        }
        else
        {
            rb.isKinematic = false;
            rb.linearVelocity = frozenVelocity;
            rb.angularVelocity = frozenAngularVelocity;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Ignore collisions with layers excluded by hitMask.
        if (((1 << collision.gameObject.layer) & hitMask) == 0)
            return;

        if (explode && ((1 << collision.gameObject.layer) & groundMask) != 0)
            return;

        if (explode)
        {
            Explode();
            return;
        }

        if (collision.collider.TryGetComponent(out IDamageable d))
            d.TakeDamage(damage);

        if (collision.collider.TryGetComponent(out Switch torch))
            torch.Ignite();

        Destroy(gameObject);
    }

    public void Launch(Vector3 v)
    {
        if (rb != null)
        {
            rb.useGravity = gravity;
            rb.linearVelocity = v;
        }
        else
        {
            velocity = v;
        }
        if (v.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(v);
        fuseTimer = timer;
        if (!explode)
            Destroy(gameObject, lifetime);
    }

    public void SetFuse(float duration)
    {
        fuseTimer = Mathf.Max(0.1f, duration);
    }

    void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            explosionRadius,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        HashSet<object> damaged = new HashSet<object>();

        foreach (Collider hit in hits)
        {
            DamageSystem body = hit.GetComponentInParent<DamageSystem>();
            if (body != null)
            {
                if (damaged.Add(body)) body.TakeDamage(damage);
            }
            else
            {
                // Anything else that can be damaged, like a crystal switch
                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null && damaged.Add(damageable)) damageable.TakeDamage(damage);
            }

            if (hit.TryGetComponent(out Switch torch))
                torch.Ignite();

            if (hit.TryGetComponent(out Projectile neighbor))
            {
                if (neighbor != this && neighbor.explode)
                    neighbor.BoostUpward();
            }
        }

        Destroy(gameObject);
    }

    void BoostUpward()
    {
        if (rb != null && !rb.isKinematic)
            rb.AddForce(Vector3.up * 8f, ForceMode.Impulse);
    }

    public Vector3 PredictImpact(Vector3 origin, Vector3 initialVelocity, int ignoreLayers = 0)
    {
        int mask = (hitMask.value | groundMask.value) & ~ignoreLayers;
        Vector3 position = origin;
        Vector3 predictedVelocity = initialVelocity;

        float elapsed = 0f;
        float stepTime = 0.02f;
        float predictionTime = explode ? timer : lifetime;

        while (elapsed < predictionTime)
        {
            float dt = Mathf.Min(stepTime, predictionTime - elapsed);
            if (gravity)
                predictedVelocity += Physics.gravity * dt;

            Vector3 step = predictedVelocity * dt;
            float distance = step.magnitude;

            if (distance > 0f &&
                Physics.SphereCast(position, radius, step.normalized,
                    out RaycastHit hit, distance, mask,
                    QueryTriggerInteraction.Ignore))
            {
                return position + step.normalized * hit.distance;
            }

            position += step;
            elapsed += dt;
        }

        return position;
    }
}
