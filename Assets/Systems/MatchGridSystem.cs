using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// A grid of MatchBlocks. The player targets it, picks a block with the D-pad, starts a chain with
// the rosary, extends it through adjacent matchable blocks, and lets go to break the chain
// (if it has at least Min Chain blocks).
//
// Layout: columns run along the grid's local X and rows along local Y, centered on this object,
// and (0,0) is the bottom-left block. Like a Unity Quad, it is meant to be seen from its -Z side.
[RequireComponent(typeof(BoxCollider))]
public class MatchGridSystem : MonoBehaviour
{
    [System.Serializable]
    public class CellOverride
    {
        public Vector2Int cell;          // (0,0) is the bottom-left block
        public bool empty;               // no block here at all
        public bool locked;              // can't be matched
        public bool regenerates = true;  // comes back after being broken
    }

    [Header("Grid")]
    [SerializeField] MatchBlock blockPrefab;
    [SerializeField] int columns = 4;
    [SerializeField] int rows = 4;
    [SerializeField] Vector2 cellSize = new Vector2(1.2f, 1.2f); // distance between block centers
    [SerializeField] bool generateOnStart = true;

    [Header("Rules")]
    [SerializeField] int minChain = 3;

    [Header("Per-block options")]
    [SerializeField] CellOverride[] overrides;

    [Header("Targeting")]
    [SerializeField] float triggerDepth = 2f; // thickness of the volume the player's targeting detects

    [Header("Events")]
    [SerializeField] UnityEvent<int> onChainBroken; // passes the length of the chain

    [Header("Editor")]
    [SerializeField] bool showPreview = true; // draws the grid in the Scene view while editing (not in play mode)

    [Header("Grid Explosions")]
    [SerializeField] float delayPerBlock = 0.12f; // time between chain blocks breaking, first to last
    [SerializeField] float explosionDelay = 0.1f; // pause after the last chain block, before the explosion
    [SerializeField] float smallAreaRadius = 0.7f;
    [SerializeField] float smallExplosionRadius = 1.4f;
    [SerializeField] float mediumExplosionRadius = 2.2f;
    [SerializeField] float largeExplosionRadius = 3.2f;

    public event System.Action<int> ChainBroken;
    public event System.Action ChainFailed; // the player let go with too short a chain

    MatchBlock[,] blocks;
    Dictionary<Vector2Int, CellOverride> overrideLookup;
    readonly List<MatchBlock> chain = new List<MatchBlock>();

    BoxCollider triggerBox;
    bool targeted;
    bool breaking;                                        // a finished chain is still popping
    List<MatchBlock> breakQueue = new List<MatchBlock>(); // the finished chain, in order
    int breakIndex;
    Vector2Int cursor;

    public bool IsBreaking => breaking;
    public bool IsChaining => chain.Count > 0;
    public int MinChain => minChain;

    void Awake() => triggerBox = GetComponent<BoxCollider>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (generateOnStart) Generate();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // Coroutines stop when a room is hidden, so blocks that were broken before that
    // need their regrowth started again
    void OnEnable()
    {
        if (blocks == null) return;

        foreach (MatchBlock b in blocks)
            if (b != null && !b.Present && b.Regenerates)
                StartCoroutine(RegenerateAfterDelay(b));
    }

    // Don't leave a half-finished break behind when the room is hidden
    void OnDisable()
    {
        if (breaking && gameObject.scene.isLoaded) FinishBreakNow();
    }

    // ---------------------------------------------------------------- building

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();

        if (blockPrefab == null)
        {
            Debug.LogError("MatchGrid has no Block Prefab.", this);
            return;
        }

        overrideLookup = new Dictionary<Vector2Int, CellOverride>();
        if (overrides != null)
            foreach (CellOverride o in overrides) overrideLookup[o.cell] = o;

        blocks = new MatchBlock[columns, rows];
        for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                CellOverride o = GetOverride(c);
                if (o != null && o.empty) continue;

                MatchBlock block = Instantiate(blockPrefab, transform);
                block.name = $"Block {x},{y}";
                block.transform.localPosition = CellToLocal(c);
                block.transform.localRotation = Quaternion.identity;
                block.Setup(this, c, o != null && o.locked, o == null || o.regenerates);
                blocks[x, y] = block;
            }

        FitTrigger();

        // Blocks made after a room was hidden should follow the room's visibility
        DungeonRoom room = GetComponentInParent<DungeonRoom>();
        if (room != null) room.RefreshVisibility();
    }

    // Brings back every block that has been broken
    [ContextMenu("Regenerate all")]
    public void RegenerateAll()
    {
        if (blocks == null) return;

        foreach (MatchBlock b in blocks)
            if (b != null && !b.Present) Regenerate(b);
    }

    // Brings one broken block back
    public void Regenerate(MatchBlock block)
    {
        if (block == null || block.Present) return;

        block.Revive();

        if (targeted && BlockAt(cursor) == null) SnapCursorToNearest();
        else RefreshHighlight();
    }

    void Clear()
    {
        CancelChain();
        StopAllCoroutines();
        breaking = false;
        breakQueue.Clear();
        targeted = false;

        if (blocks == null) return;
        foreach (MatchBlock b in blocks)
            if (b != null) Destroy(b.gameObject);
        blocks = null;
    }

    void FitTrigger()
    {
        if (triggerBox == null) triggerBox = GetComponent<BoxCollider>();
        triggerBox.isTrigger = true;
        triggerBox.center = Vector3.zero;
        triggerBox.size = new Vector3(columns * cellSize.x, rows * cellSize.y, triggerDepth);
    }

    Vector3 CellToLocal(Vector2Int c)
        => new Vector3((c.x - (columns - 1) / 2f) * cellSize.x, (c.y - (rows - 1) / 2f) * cellSize.y, 0f);

    CellOverride GetOverride(Vector2Int c)
    {
        if (overrideLookup == null) return null;
        overrideLookup.TryGetValue(c, out CellOverride o);
        return o;
    }

    bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < columns && c.y < rows;

    // The block at a cell, or null if there is none (empty cell, or it's been broken)
    MatchBlock BlockAt(Vector2Int c)
    {
        if (blocks == null || !InBounds(c)) return null;

        MatchBlock b = blocks[c.x, c.y];
        return b != null && b.Present ? b : null;
    }

    IEnumerable<MatchBlock> PresentBlocks()
    {
        if (blocks == null) yield break;

        foreach (MatchBlock b in blocks)
            if (b != null && b.Present) yield return b;
    }

    // ---------------------------------------------------------------- targeting

    // Can the player lock onto this grid? Only while it has a block to aim at.
    public bool CanBeTargeted
    {
        get
        {
            foreach (MatchBlock _ in PresentBlocks()) return true;
            return false;
        }
    }

    public Vector3 ClosestPoint(Vector3 point)
        => (triggerBox != null ? triggerBox.bounds : new Bounds(transform.position, Vector3.zero)).ClosestPoint(point);

    // The player locked on: put the cursor on the block nearest to them
    public void Acquire(Vector3 fromWorld)
    {
        if (targeted) return;

        MatchBlock best = null;
        float bestDist = float.MaxValue;

        foreach (MatchBlock b in PresentBlocks())
        {
            Vector3 d = b.transform.position - fromWorld;
            if (PerspectiveShiftSystem.CurrentSidescroller)
                d = Vector3.ProjectOnPlane(d, PerspectiveShiftSystem.CurrentViewForward);

            if (d.sqrMagnitude < bestDist) { bestDist = d.sqrMagnitude; best = b; }
        }

        if (best == null) return;

        targeted = true;
        cursor = best.Cell;
        RefreshHighlight();
    }

    // The player let go of the grid (or targeted something else)
    public void Release()
    {
        CancelChain();
        targeted = false;
        RefreshHighlight();
    }

    // D-pad: +x is right and +y is up as the player sees the grid
    public void MoveCursor(Vector2Int dir)
    {
        if (!targeted || dir == Vector2Int.zero) return;

        // Seen from behind, the grid's left and right swap
        Camera cam = Camera.main;
        if (cam != null && Vector3.Dot(cam.transform.forward, transform.forward) < 0f) dir.x = -dir.x;

        if (IsChaining) StepChain(dir);
        else StepFree(dir);
    }

    // Not chaining: hop to the next block in that direction, skipping gaps
    void StepFree(Vector2Int dir)
    {
        Vector2Int c = cursor;
        while (true)
        {
            c += dir;
            if (!InBounds(c)) return; // nothing further that way

            if (BlockAt(c) != null) { cursor = c; break; }
        }

        RefreshHighlight();
    }

    // Chaining: the cursor can only go to the previous bead (taking the last one off)
    // or to an unused, matchable block right next to it
    void StepChain(Vector2Int dir)
    {
        Vector2Int next = cursor + dir;
        MatchBlock b = BlockAt(next);
        if (b == null) return;

        if (chain.Count >= 2 && b == chain[chain.Count - 2])
        {
            chain[chain.Count - 1].SetBead(false);
            chain.RemoveAt(chain.Count - 1);
        }
        else if (!chain.Contains(b) && b.Matchable)
        {
            chain.Add(b);
            b.SetBead(true);
        }
        else
        {
            return;
        }

        cursor = next;
        RefreshHighlight();
    }

    void SnapCursorToNearest()
    {
        MatchBlock best = null;
        int bestDist = int.MaxValue;

        foreach (MatchBlock b in PresentBlocks())
        {
            Vector2Int d = b.Cell - cursor;
            int dist = d.x * d.x + d.y * d.y;
            if (dist < bestDist) { bestDist = dist; best = b; }
        }

        if (best != null) cursor = best.Cell;
        RefreshHighlight();
    }

    void RefreshHighlight()
    {
        if (blocks == null) return;

        foreach (MatchBlock b in blocks)
            if (b != null) b.SetTargeted(targeted && b.Present && b.Cell == cursor);
    }

    // ---------------------------------------------------------------- chaining

    // The rosary was used on the targeted block: place the first bead
    public bool BeginChain()
    {
        if (!targeted || IsChaining || breaking) return false;

        MatchBlock b = BlockAt(cursor);
        if (b == null || !b.Matchable) return false;

        chain.Add(b);
        b.SetBead(true);
        return true;
    }

    // The player let go: if the chain is long enough, its blocks pop one after another from the
    // first to the last, then the explosion goes off at the last block
    public void EndChain()
    {
        if (!IsChaining) return;

        List<MatchBlock> finished = new List<MatchBlock>(chain);
        chain.Clear();

        if (finished.Count < minChain)
        {
            foreach (MatchBlock b in finished) b.SetBead(false);
            ChainFailed?.Invoke();
            return;
        }

        // The beads stay on until each block breaks
        breaking = true;
        breakQueue = finished;
        breakIndex = 0;
        StartCoroutine(BreakChain());
    }

    IEnumerator BreakChain()
    {
        while (breakIndex < breakQueue.Count)
        {
            if (breakIndex > 0 && delayPerBlock > 0f)
                yield return new WaitForSeconds(delayPerBlock);

            BreakOne(breakQueue[breakIndex], true);
            breakIndex++;
        }

        if (breakQueue.Count >= 5 && explosionDelay > 0f)
            yield return new WaitForSeconds(explosionDelay);

        FinishBreak(true);
    }

    // Breaks whatever is left of a break in progress, instantly (used when the room is hidden)
    void FinishBreakNow()
    {
        while (breakIndex < breakQueue.Count)
        {
            BreakOne(breakQueue[breakIndex], false);
            breakIndex++;
        }

        FinishBreak(false);
    }

    void FinishBreak(bool scheduleRegrowth)
    {
        List<MatchBlock> finished = breakQueue;
        MatchBlock center = finished[finished.Count - 1];

        // Only chains of 5 or more blocks break additional blocks
        if (finished.Count >= 5) BreakExplosion(finished, center, scheduleRegrowth);

        breaking = false;
        SnapCursorToNearest();

        ChainBroken?.Invoke(finished.Count);
        onChainBroken?.Invoke(finished.Count);
    }

    void BreakOne(MatchBlock b, bool scheduleRegrowth)
    {
        if (b == null) return;

        b.SetBead(false);
        if (!b.Present || b.Locked) return; // locked blocks are never broken

        b.Break();

        if (scheduleRegrowth && b.Regenerates)
            StartCoroutine(RegenerateAfterDelay(b));
    }

    // The explosion is centered on the last block of the chain. Locked blocks are never broken.
    void BreakExplosion(List<MatchBlock> finished, MatchBlock center, bool scheduleRegrowth)
    {
        float radius = finished.Count < 5 ? smallAreaRadius
            : finished.Count < 9 ? smallExplosionRadius
            : finished.Count < 12 ? mediumExplosionRadius
            : largeExplosionRadius;

        int tier = finished.Count < 9 ? 1
            : finished.Count < 12 ? 2
            : 3;

        HashSet<MatchBlock> inChain = new HashSet<MatchBlock>(finished);
        List<MatchBlock> hit = new List<MatchBlock>();

        foreach (MatchBlock b in PresentBlocks())
        {
            if (b.Locked || inChain.Contains(b))
                continue;

            Vector2Int offset = b.Cell - center.Cell;
            int dx = Mathf.Abs(offset.x);
            int dy = Mathf.Abs(offset.y);

            bool inPattern;

            if (tier == 1)
            {
                // Small: one block in each cardinal direction.
                inPattern = (dx == 1 && dy == 0)
                         || (dx == 0 && dy == 1);
            }
            else if (tier == 2)
            {
                // Medium: two cardinal blocks and one diagonal in each direction.
                inPattern = (dx <= 2 && dy == 0)
                         || (dx == 0 && dy <= 2)
                         || (dx == 1 && dy == 1);
            }
            else
            {
                // Large: three cardinal blocks, two diagonals,
                // and all blocks within the 2-by-2 diagonal bounds.
                inPattern = (dx <= 3 && dy == 0)
                         || (dx == 0 && dy <= 3)
                         || (dx <= 2 && dy <= 2);
            }

            if (!inPattern)
                continue;

            // Use the closest point on the block so the configured
            // radius measures the actual explosion reach.
            Collider blockCollider = b.GetComponentInChildren<Collider>();

            Vector3 closestPoint = blockCollider != null
                ? blockCollider.ClosestPoint(center.transform.position)
                : b.transform.position;

            if (Vector3.Distance(center.transform.position, closestPoint) <= radius)
                hit.Add(b);
        }

        foreach (MatchBlock b in hit)
            BreakOne(b, scheduleRegrowth);
    }

    // Throw the chain away without breaking anything
    public void CancelChain()
    {
        foreach (MatchBlock b in chain) b.SetBead(false);
        chain.Clear();
    }

    IEnumerator RegenerateAfterDelay(MatchBlock block)
    {
        yield return new WaitForSeconds(block.RegenerateDelay);

        if (block != null && !block.Present) Regenerate(block);
    }

#if UNITY_EDITOR
    // ---------------------------------------------------------------- editor preview

    // Keeps the trigger volume matching the grid while editing, not only after Generate runs
    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);

        if (!Application.isPlaying) FitTrigger();
    }

    // Draws a ghost of every block in the Scene view so you can place the grid without pressing Play.
    // Cyan = normal, grey = locked, yellow = doesn't regenerate, red cross = empty cell (nothing spawns).
    void OnDrawGizmos()
    {
        if (!showPreview || Application.isPlaying) return;

        // Same override lookup Generate uses
        Dictionary<Vector2Int, CellOverride> lookup = new Dictionary<Vector2Int, CellOverride>();
        if (overrides != null)
            foreach (CellOverride o in overrides) lookup[o.cell] = o;

        // Use the block prefab's own mesh if it has one, otherwise a plain box
        Mesh mesh = null;
        Matrix4x4 meshOffset = Matrix4x4.identity;
        Vector3 prefabScale = Vector3.one;
        if (blockPrefab != null)
        {
            prefabScale = blockPrefab.transform.localScale;

            MeshFilter mf = blockPrefab.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                mesh = mf.sharedMesh;
                meshOffset = blockPrefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            }
        }

        Matrix4x4 gridToWorld = transform.localToWorldMatrix;
        Vector3 boxSize = new Vector3(cellSize.x * 0.9f, cellSize.y * 0.9f, Mathf.Min(cellSize.x, cellSize.y) * 0.25f);

        for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                lookup.TryGetValue(c, out CellOverride o);
                Vector3 local = CellToLocal(c);

                if (o != null && o.empty)
                {
                    Gizmos.matrix = gridToWorld;
                    Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
                    float h = Mathf.Min(cellSize.x, cellSize.y) * 0.25f;
                    Gizmos.DrawLine(local + new Vector3(-h, -h, 0f), local + new Vector3(h, h, 0f));
                    Gizmos.DrawLine(local + new Vector3(-h, h, 0f), local + new Vector3(h, -h, 0f));
                    continue;
                }

                Color color = new Color(0.2f, 0.9f, 1f);                                  // normal
                if (o != null && o.locked) color = new Color(0.6f, 0.6f, 0.65f);          // locked
                else if (o != null && !o.regenerates) color = new Color(1f, 0.85f, 0.2f); // one-shot

                if (mesh != null)
                {
                    Gizmos.matrix = gridToWorld * Matrix4x4.TRS(local, Quaternion.identity, prefabScale) * meshOffset;

                    Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
                    Gizmos.DrawMesh(mesh);
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.9f);
                    Gizmos.DrawWireMesh(mesh);
                }
                else
                {
                    Gizmos.matrix = gridToWorld;
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
                    Gizmos.DrawCube(local, boxSize);
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.9f);
                    Gizmos.DrawWireCube(local, boxSize);
                }
            }

        Gizmos.matrix = Matrix4x4.identity;
    }

    // While the grid is selected: label each cell with the coordinates the Cell Overrides use,
    // and mark the front (the side the grid is meant to be seen from)
    void OnDrawGizmosSelected()
    {
        if (!showPreview || Application.isPlaying) return;

        Matrix4x4 gridToWorld = transform.localToWorldMatrix;

        for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
                UnityEditor.Handles.Label(gridToWorld.MultiplyPoint3x4(CellToLocal(new Vector2Int(x, y))), $"{x},{y}");

        Vector3 center = transform.position;
        Vector3 front = center - transform.forward * Mathf.Max(cellSize.x, cellSize.y);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(center, front);
        UnityEditor.Handles.Label(front, "front");
    }
#endif
}
