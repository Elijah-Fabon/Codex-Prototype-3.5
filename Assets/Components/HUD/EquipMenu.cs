using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EquipMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    [SerializeField] InputTrackerSystem input;
    [SerializeField] EquipSystem equip;
    [SerializeField] GameObject panel;
    [SerializeField] TMP_Text[] slotTexts = new TMP_Text[3]; // West, North, East (display only)
    [SerializeField] Transform itemListRoot;               // has a Vertical Layout Group
    [SerializeField] Button itemButtonPrefab;              // a Button with a TMP_Text child

    static readonly string[] SlotNames = { "West", "North", "East" };

    readonly List<Button> itemButtons = new();
    readonly List<EquipItem> itemsShown = new();
    float previousTimeScale = 1f;
    int lastSelectedIndex = -1;

    void Awake()
    {
        IsOpen = false;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        panel.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnEnable()
    {
        input.MenuPressed += Toggle;
        input.MenuEquipPressed += OnSlotPressed;
        equip.SlotsChanged += Refresh;
    }

    void OnDisable()
    {
        input.MenuPressed -= Toggle;
        input.MenuEquipPressed -= OnSlotPressed;
        equip.SlotsChanged -= Refresh;
        if (IsOpen) Close();
    }

    void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    void Open()
    {
        IsOpen = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        panel.SetActive(true);
        RebuildList(0);
    }

    void Close()
    {
        IsOpen = false;
        Time.timeScale = previousTimeScale;

        panel.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // Pressing a slot's button assigns the highlighted item to it.
    // Pressing it again on an item that's already in that slot clears the slot.
    void OnSlotPressed(EquipSlot slot)
    {
        int index = SelectedIndex();
        if (index < 0) return;

        EquipItem item = itemsShown[index];
        bool alreadyThere = equip.GetSlot((int)slot) == item;
        equip.Equip(alreadyThere ? null : item, slot); // fires SlotsChanged, which refreshes the labels
    }

    int SelectedIndex()
    {
        if (EventSystem.current == null) return -1;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        return itemButtons.FindIndex(b => b != null && b.gameObject == selected);
    }

    void Refresh()
    {
        if (!IsOpen) return;

        // A new item was picked up: rebuild. Otherwise just update the text.
        if (equip.Owned.Count != itemButtons.Count) RebuildList(Mathf.Max(0, SelectedIndex()));
        else UpdateLabels();
    }

    void RebuildList(int selectIndex)
    {
        foreach (var b in itemButtons) Destroy(b.gameObject);
        itemButtons.Clear();
        itemsShown.Clear();

        foreach (var item in equip.Owned)
        {
            Button button = Instantiate(itemButtonPrefab, itemListRoot);
            itemButtons.Add(button);
            itemsShown.Add(item);
        }

        UpdateLabels();

        lastSelectedIndex = -1;
        if (itemButtons.Count > 0)
        {
            selectIndex = Mathf.Clamp(selectIndex, 0, itemButtons.Count - 1);
            EventSystem.current.SetSelectedGameObject(itemButtons[selectIndex].gameObject);
        }
    }

    void UpdateLabels()
    {
        // Right side: what is in each slot
        for (int i = 0; i < slotTexts.Length; i++)
        {
            EquipItem item = equip.GetSlot(i);
            slotTexts[i].text = $"{SlotNames[i]}: {(item != null ? ItemLabel(item) : "(empty)")}";
        }

        // Left side: owned items, tagged with the slots they're in
        for (int i = 0; i < itemButtons.Count; i++)
        {
            string label = ItemLabel(itemsShown[i]);
            for (int s = 0; s < SlotNames.Length; s++)
                if (equip.GetSlot(s) == itemsShown[i]) label += $"  [{SlotNames[s]}]";

            itemButtons[i].GetComponentInChildren<TMP_Text>().text = label;
        }
    }

    static string ItemLabel(EquipItem item) =>
        string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
}
