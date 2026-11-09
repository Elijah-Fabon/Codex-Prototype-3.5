using UnityEngine;

public class MatchBlock : MonoBehaviour
{
    [Header("Options (the grid's Cell Overrides can change these per block)")]
    [SerializeField] bool locked;              // locked blocks can't be matched
    [SerializeField] bool regenerates = true;  // comes back after being broken
    [SerializeField] float regenerateDelay = 2f;

    [Header("Visuals (all optional)")]
    [SerializeField] GameObject beadVisual;    // shown while a bead sits on this block
    [SerializeField] GameObject targetVisual;  // shown while this is the targeted block
    [SerializeField] GameObject lockedVisual;  // shown while locked
    [SerializeField] Renderer tintRenderer;    // tinted while locked; found automatically if empty
    [SerializeField] bool tintWhenLocked = true;
    [SerializeField] Color lockedColor = new Color(0.35f, 0.35f, 0.38f, 1f);
    [SerializeField] Color targetedColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] Color chainColor = new Color(0.3f, 1f, 0.6f, 1f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    static readonly int ColorId = Shader.PropertyToID("_Color");         // built-in

    MaterialPropertyBlock propertyBlock;
    Vector3 baseScale;
    bool present;
    bool hasBead;
    bool targeted;

    public MatchGridSystem Grid { get; private set; }
    public Vector2Int Cell { get; private set; }

    public bool Present => present;
    public bool Locked => locked;
    public bool Matchable => present && !locked;
    public bool Regenerates => regenerates;
    public float RegenerateDelay => regenerateDelay;

    void Awake()
    {
        baseScale = transform.localScale;
        if (tintRenderer == null) tintRenderer = GetComponentInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    // Called by the grid right after it creates the block
    public void Setup(MatchGridSystem grid, Vector2Int cell, bool isLocked, bool doesRegenerate)
    {
        Grid = grid;
        Cell = cell;
        locked = isLocked;
        regenerates = doesRegenerate;

        present = true;
        hasBead = false;
        targeted = false;
        UpdateVisuals();
    }

    // Lock or unlock at runtime (e.g. a switch that frees a block)
    public void SetLocked(bool value)
    {
        locked = value;
        UpdateVisuals();
    }

    // Brings this block back if it was broken
    public void Regenerate()
    {
        if (Grid != null) Grid.Regenerate(this);
    }

    public void Break()
    {
        present = false;
        hasBead = false;
        targeted = false;
        gameObject.SetActive(false);
    }

    public void Revive()
    {
        present = true;
        hasBead = false;
        targeted = false;
        gameObject.SetActive(true);
        UpdateVisuals();
    }

    public void SetBead(bool value)
    {
        hasBead = value;
        UpdateVisuals();
    }

    public void SetTargeted(bool value)
    {
        if (targeted == value) return;
        targeted = value;
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (propertyBlock == null) return; // Setup ran before Awake (can't happen via the grid)

        if (tintRenderer != null)
        {
            if (locked && tintWhenLocked)
            {
                tintRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, lockedColor);
                propertyBlock.SetColor(ColorId, lockedColor);
                tintRenderer.SetPropertyBlock(propertyBlock);
            }
            else if (targeted || hasBead)
            {
                tintRenderer.GetPropertyBlock(propertyBlock);

                Color highlight = targeted ? targetedColor : chainColor;

                propertyBlock.SetColor(BaseColorId, highlight);
                propertyBlock.SetColor(ColorId, highlight);

                tintRenderer.SetPropertyBlock(propertyBlock);
            }
            else
            {
                tintRenderer.SetPropertyBlock(null); // back to the material's own colour
            }
        }

        if (lockedVisual != null) lockedVisual.SetActive(locked);
        if (beadVisual != null) beadVisual.SetActive(hasBead);
        if (targetVisual != null) targetVisual.SetActive(targeted);

        // Without dedicated visuals, show the state by scaling the block
        float scale = 1f;
        if (targetVisual == null && targeted) scale *= 1.15f;
        if (beadVisual == null && hasBead) scale *= 1.06f;
        transform.localScale = baseScale * scale;
    }
}
