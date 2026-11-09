using UnityEngine;
using UnityEngine.UI;

// Shows which firepower is selected: a cross in the bottom-left corner with a cursor that sits on
// the end of the arm for the selected power, or in the middle when nothing is selected.
//   up = whip, right = firecrackers, left = firewall, down = pillar
// Builds its own canvas, like the dungeon map screen, so there's no UI to set up by hand. Put it on
// any object in the scene.
public class Firepowerhud : MonoBehaviour
{
    [SerializeField] FirepowerSystem firepowers;
    [SerializeField] Vector2 margin = new Vector2(150f, 150f); // the cross's middle, from the bottom-left corner
    [SerializeField] float armLength = 70f;                    // from the middle to the end of each arm
    [SerializeField] float armThickness = 10f;
    [SerializeField] float cursorSize = 44f;
    [SerializeField] float cursorSpeed = 16f;                  // how fast the cursor slides (higher = snappier)
    [SerializeField] bool showLabels = true;

    static readonly Color barColor = new Color(0.85f, 0.85f, 0.85f, 0.45f);
    static readonly Color dotColor = new Color(0.85f, 0.85f, 0.85f, 0.7f);
    static readonly Color selectedColor = new Color(0.95f, 0.85f, 0.30f, 1f); // matches the map cursor
    static readonly Color labelColor = new Color(1f, 1f, 1f, 0.6f);

    // The four arms, in the order they are built
    static readonly Firepower[] powers =
    {
        Firepower.Whip, Firepower.Firecrackers, Firepower.Firewall, Firepower.Pillar
    };

    static readonly string[] names = { "Whip", "Crackers", "Firewall", "Pillar" };

    Canvas canvas;
    RectTransform cursor;
    Image[] dots;
    Text[] labels;
    Sprite discSprite;
    Sprite ringSprite;

    Vector2 cursorPosition;
    Firepower shown = (Firepower)(-1); // forces the first highlight

    void Reset() => firepowers = FindFirstObjectByType<FirepowerSystem>();

    void Awake()
    {
        if (firepowers == null) firepowers = FindFirstObjectByType<FirepowerSystem>();

        discSprite = MakeCircleSprite(64, 0f);
        ringSprite = MakeCircleSprite(64, 6f);

        BuildUI();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    void OnDestroy()
    {
        if (discSprite != null) Destroy(discSprite.texture);
        if (ringSprite != null) Destroy(ringSprite.texture);
    }

    // Unscaled time, so the cursor still moves during a hit stop or a pause
    // Update is called once per frame
    void Update()
    {
        if (firepowers == null) return;

        // The map screen covers everything
        canvas.enabled = !DungeonMapSystem.IsEditing;

        Firepower selected = firepowers.Selected;

        Vector2 target = (Vector2)DirectionFor(selected) * armLength;
        cursorPosition = Vector2.Lerp(cursorPosition, target, 1f - Mathf.Exp(-cursorSpeed * Time.unscaledDeltaTime));
        cursor.anchoredPosition = cursorPosition;

        if (selected != shown)
        {
            shown = selected;
            Highlight(selected);
        }
    }

    // Which way each power sits on the cross (the same directions the D-pad uses)
    static Vector2Int DirectionFor(Firepower power)
    {
        switch (power)
        {
            case Firepower.Whip: return Vector2Int.up;
            case Firepower.Firecrackers: return Vector2Int.right;
            case Firepower.Firewall: return Vector2Int.left;
            case Firepower.Pillar: return Vector2Int.down;
            default: return Vector2Int.zero; // neutral: the middle
        }
    }

    // The selected arm's end and label light up; the rest are dim
    void Highlight(Firepower selected)
    {
        for (int i = 0; i < powers.Length; i++)
        {
            bool on = powers[i] == selected;
            dots[i].color = on ? selectedColor : dotColor;

            if (labels[i] != null)
                labels[i].color = on ? selectedColor : labelColor;
        }
    }

    // ---------------------------------------------------------------- building the UI

    void BuildUI()
    {
        var canvasGo = new GameObject("FirepowerHUDCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1; // under the equip menu (0) and the map (50)

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // The cross sits in the bottom-left corner; everything below is placed from its middle
        RectTransform cross = MakeImage(canvasGo.transform, "Cross", Color.clear, null);
        cross.anchorMin = cross.anchorMax = Vector2.zero;
        cross.pivot = new Vector2(0.5f, 0.5f);
        cross.anchoredPosition = margin;
        cross.sizeDelta = Vector2.zero;

        // The two bars
        float barLength = armLength * 2f + armThickness;
        MakeImage(cross, "Horizontal", barColor, null).sizeDelta = new Vector2(barLength, armThickness);
        MakeImage(cross, "Vertical", barColor, null).sizeDelta = new Vector2(armThickness, barLength);

        // A dot at the middle (neutral) and at the end of each arm
        float dotSize = armThickness * 2.4f;
        MakeImage(cross, "Middle", dotColor, discSprite).sizeDelta = Vector2.one * armThickness * 1.8f;

        dots = new Image[powers.Length];
        labels = new Text[powers.Length];

        for (int i = 0; i < powers.Length; i++)
        {
            Vector2 end = (Vector2)DirectionFor(powers[i]) * armLength;

            RectTransform dot = MakeImage(cross, names[i] + " End", dotColor, discSprite);
            dot.sizeDelta = Vector2.one * dotSize;
            dot.anchoredPosition = end;
            dots[i] = dot.GetComponent<Image>();

            if (!showLabels) continue;

            // Above the end for the top and the sides, below it for the bottom
            bool below = powers[i] == Firepower.Pillar;
            Text label = MakeText(cross, names[i] + " Label", 20, TextAnchor.MiddleCenter);
            label.rectTransform.sizeDelta = new Vector2(140f, 30f);
            label.rectTransform.anchoredPosition = end + Vector2.up * (below ? -dotSize - 6f : dotSize + 6f);
            labels[i] = label;
        }

        // The cursor goes last so it draws on top
        cursor = MakeImage(cross, "Cursor", selectedColor, ringSprite);
        cursor.sizeDelta = Vector2.one * cursorSize;
    }

    static RectTransform MakeImage(Transform parent, string name, Color color, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false; // never steal clicks from other UI

        return (RectTransform)go.transform;
    }

    static Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;

        return text;
    }

    // A white circle (or ring, if ringWidth > 0) with a soft edge, so no art is needed
    static Sprite MakeCircleSprite(int size, float ringWidth)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        float radius = size * 0.5f;
        float inner = radius - ringWidth;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));

                float alpha = Mathf.Clamp01(radius - distance);                     // the outer edge
                if (ringWidth > 0f) alpha *= Mathf.Clamp01(distance - inner);      // the hole in the middle

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
