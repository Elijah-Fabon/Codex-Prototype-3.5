using System.Collections.Generic;
using UnityEngine;

public enum FirewallShape
{
    Cube,
    Cylinder,
    Quad // a flat sheet that is only visible from one side (see FaceToward)
}

// One firewall segment between two nodes, built from a cube so there's nothing to set up.
// It rises out of the ground when Rise() is called, and can follow moving end points (for previews)
public class FirewallWall : MonoBehaviour
{
    Transform body;      // the cube; its base sits on the wall's origin
    float height;
    float thickness;
    float riseTime;
    float length = 1f;
    float progress = 1f; // 0 = flat on the ground, 1 = fully risen
    FirewallShape shape;
    float facing = 1f;   // quads: +1 = the visible side faces this wall's right, -1 = its left
    // Set by EnableDamage: the wall hurts enemies that touch it
    bool dealsDamage;
    int damage;
    float hitCooldown;
    readonly Dictionary<EnemyAISystem, float> lastHit = new Dictionary<EnemyAISystem, float>();

    // Idle: standing still (up for good, or flat after a loop step). Up: fully risen, counting down.
    enum Phase { Idle, Rising, Up, Lowering, Gap, LoopRising }
    Phase phase = Phase.Idle;

    float upTime = -1f;            // seconds spent fully up before lowering; < 0 = stays up for good
    float lowerTime = 0.3f;        // how long the lowering animation takes
    bool destroyWhenLowered;       // a timed wall disappears once it is back down
    bool loop;                     // previews: rise, hold, lower, pause, repeat
    float loopGap;
    float timer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float dt = Time.deltaTime;

        switch (phase)
        {
            case Phase.Idle:
                return;

            case Phase.Rising:
                progress += dt / riseTime;
                if (progress >= 1f)
                {
                    progress = 1f;
                    timer = upTime;
                    phase = upTime >= 0f ? Phase.Up : Phase.Idle;
                }
                break;

            case Phase.Up:
                // The countdown waits out rotations, mode switches and door transitions
                if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning) return;

                timer -= dt;
                if (timer <= 0f)
                {
                    if (loop)
                    {
                        progress = 0f;
                        phase = Phase.Rising;
                    }
                    else
                    {
                        phase = Phase.Lowering;
                    }
                }
                break;

            case Phase.Lowering:
                progress -= dt / lowerTime;
                if (progress <= 0f)
                {
                    progress = 0f;

                    if (destroyWhenLowered)
                    {
                        Destroy(gameObject);
                        return;
                    }

                    timer = loopGap;
                    phase = loop ? Phase.Gap : Phase.Idle;
                }
                break;

            case Phase.Gap:
                timer -= dt;
                if (timer <= 0f)
                {
                    progress = 0f;
                    phase = Phase.Rising;
                }
                break;

            case Phase.LoopRising:
                progress += dt / riseTime;

                if (progress >= 1f)
                {
                    progress = 1f;
                    timer = upTime;
                    phase = Phase.Up;
                }
                break;
        }

        Apply();
    }

    public static FirewallWall Create(float height, float thickness, float riseTime,
                                      Material material, Color color, 
                                      FirewallShape shape = FirewallShape.Cube)
    {
        GameObject root = new GameObject("Firewall Wall");
        FirewallWall wall = root.AddComponent<FirewallWall>();
        wall.height = height;
        wall.thickness = thickness;
        wall.riseTime = Mathf.Max(riseTime, 0.01f);
        wall.shape = shape;

        PrimitiveType primitive = PrimitiveType.Cube;
        if (shape == FirewallShape.Cylinder) primitive = PrimitiveType.Cylinder;
        else if (shape == FirewallShape.Quad) primitive = PrimitiveType.Quad;

        GameObject bodyObject = GameObject.CreatePrimitive(primitive);
        bodyObject.name = "Body";
        Destroy(bodyObject.GetComponent<Collider>());
        bodyObject.transform.SetParent(root.transform, false);
        wall.body = bodyObject.transform;

        Renderer renderer = bodyObject.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
        Tint(renderer, color);

        wall.Apply();
        return wall;
    }

    // Works for both the default material (URP or built-in) and one you assign
    public static void Tint(Renderer renderer, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color); // URP
        block.SetColor("_Color", color);     // built-in
        renderer.SetPropertyBlock(block);
    }

    // Changes how tall the wall gets when fully risen
    public void SetHeight(float newHeight)
    {
        height = newHeight;
        Apply();
    }

    // Stretches the wall between two points. Its middle sits at the average height of the two.
    public void SetEnds(Vector3 a, Vector3 b)
    {
        Vector3 flat = b - a;
        flat.y = 0f;
        length = flat.magnitude;

        transform.position = (a + b) * 0.5f;
        if (flat.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(flat.normalized);

        Apply();
    }

    public void SetRiseTime(float newRiseTime)
    {
        riseTime = Mathf.Max(newRiseTime, 0.01f);
    }

    // Quads only: makes the visible side face a point (call after SetEnds). The back of the quad
    // isn't drawn, so a ring of quads all facing the middle reads as one wall from outside.
    public void FaceToward(Vector3 point)
    {
        facing = Vector3.Dot(point - transform.position, transform.right) >= 0f ? 1f : -1f;
        Apply();
    }

    public void EnableDamage(int damageAmount, float cooldown)
    {
        damage = damageAmount;
        hitCooldown = cooldown;
        dealsDamage = true;
    }

    void FixedUpdate()
    {
        if (!dealsDamage) return;

        // Enemies are frozen during these, so nothing happens
        if (PerspectiveShiftSystem.IsTransitioning || Door.IsTransitioning) return;

        // The wall's box as risen so far (x across it, y up, z along it)
        float h = height * Mathf.SmoothStep(0f, 1f, progress);
        if (h < 0.05f) return;

        float along = shape == FirewallShape.Cylinder ? thickness : Mathf.Max(length, 0.01f);
        Vector3 center = transform.TransformPoint(new Vector3(0f, h * 0.5f, 0f));
        Vector3 halfExtents = new Vector3(thickness, h, along) * 0.5f;

        foreach (Collider other in Physics.OverlapBox(center, halfExtents, transform.rotation,
                                                      ~0, QueryTriggerInteraction.Ignore))
            TryHit(other);
    }

    void OnTriggerEnter(Collider other) => TryHit(other);
    void OnTriggerStay(Collider other) => TryHit(other);

    // Only enemy hurtboxes count (not the player's), each at most once per cooldown
    void TryHit(Collider other)
    {
        if (!other.TryGetComponent(out HurtboxSystem hurtbox)) return;

        EnemyAISystem enemy = hurtbox.GetComponentInParent<EnemyAISystem>();
        if (enemy == null) return;

        if (lastHit.TryGetValue(enemy, out float time) && Time.time - time < hitCooldown) return;

        lastHit[enemy] = Time.time;
        hurtbox.TakeDamage(damage);
    }

    // Starts the wall flat on the ground and raises it
    public void Rise()
    {
        progress = 0f;
        phase = Phase.Rising;
        Apply();
    }

    // After it has risen it stays up for upSeconds, animates back down over lowerSeconds, then disappears
    public void SetLifetime(float upSeconds, float lowerSeconds)
    {
        upTime = upSeconds;
        lowerTime = Mathf.Max(lowerSeconds, 0.01f);
        destroyWhenLowered = true;
        loop = false;

        // Already standing (Rise() was never called): start the countdown now
        if (phase == Phase.Idle)
        {
            timer = upSeconds;
            phase = Phase.Up;
        }
    }

    // For previews: rises, holds, then immediately starts rising again until hidden.
    public void StartLoop(float holdSeconds, float lowerSeconds, float gapSeconds)
    {
        upTime = Mathf.Max(holdSeconds, 0f);
        destroyWhenLowered = false;
        loop = true;
        Rise();
    }

    // Lowers it now, whatever it was doing, and removes it at the bottom
    public void Lower(float lowerSeconds)
    {
        lowerTime = Mathf.Max(lowerSeconds, 0.01f);
        destroyWhenLowered = true;
        loop = false;
        phase = Phase.Lowering;
    }

    void Apply()
    {
        if (body == null) return;

        float h = height * Mathf.SmoothStep(0f, 1f, progress);
        if (shape == FirewallShape.Quad)
        {
            // A flat sheet along the wall; the turn about Y picks which side is the visible one
            body.localScale = new Vector3(Mathf.Max(length, 0.01f), Mathf.Max(h, 0.001f), 1f);
            body.localRotation = Quaternion.Euler(0f, facing > 0f ? -90f : 90f, 0f);
        }
        else if (body.GetComponent<MeshFilter>().sharedMesh.name.Contains("Cylinder"))
        {
            body.localScale = new Vector3(thickness, Mathf.Max(h, 0.001f) * 0.5f, thickness);
        }
        else
        {
            body.localScale = new Vector3(thickness, Mathf.Max(h, 0.001f), Mathf.Max(length, 0.01f));
        }

        body.localPosition = new Vector3(0f, h * 0.5f, 0f);
    }
}
