using System;
using System.Collections.Generic;
using UnityEngine;

public class EquipSystem : MonoBehaviour
{
    [SerializeField] InputTrackerSystem input;
    [SerializeField] PerspectiveShiftSystem perspective;
    [SerializeField] Transform muzzle; // where projectiles spawn
    [SerializeField] List<EquipItem> owned = new(); // everything the player has found

    // Indexed by EquipSlot: West, North, East
    [SerializeField] EquipItem[] slots = new EquipItem[3];
    readonly float[] nextUse = new float[3];

    RosaryTrailSystem rosaryTrail;
    EquipSlot? activeRosarySlot;

    public event Action SlotsChanged;

    public IReadOnlyList<EquipItem> Owned => owned;
    public PerspectiveShiftSystem Perspective => perspective;
    public Transform Muzzle => muzzle;
    public Transform User => transform;

    void Awake()
    {
        rosaryTrail = GetComponent<RosaryTrailSystem>();

        if (rosaryTrail == null)
            rosaryTrail = gameObject.AddComponent<RosaryTrailSystem>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!activeRosarySlot.HasValue) return;

        if (PerspectiveShiftSystem.IsTransitioning ||
            Door.IsTransitioning ||
            EquipMenu.IsOpen ||
            DungeonMapSystem.IsEditing)
        {
            StopRosaryTrail();
            return;
        }

        EquipSlot slot = activeRosarySlot.Value;

        if (!rosaryTrail.IsLaying)
        {
            activeRosarySlot = null;
            return;
        }

        if (input.IsEquipHeld(slot))
            rosaryTrail.ContinueTrail();
        else
            StopRosaryTrail();
    }

    void OnEnable() => input.EquipPressed += OnEquipPressed;
    void OnDisable()
    {
        input.EquipPressed -= OnEquipPressed;

        if (activeRosarySlot.HasValue)
            StopRosaryTrail();
    }

    void OnEquipPressed(EquipSlot slot)
    {
        if (PerspectiveShiftSystem.IsTransitioning) return;

        int i = (int)slot;
        var item = slots[i];
        if (item == null || Time.time < nextUse[i] || !item.CanUse(this)) return;

        nextUse[i] = Time.time + item.cooldown;
        item.Use(this);
    }

    public EquipItem GetSlot(int index) => slots[index];

    // Pass null to empty a slot. An item already in another slot swaps places.
    public void Equip(EquipItem item, EquipSlot slot)
    {
        int i = (int)slot;

        if (item != null)
        {
            for (int j = 0; j < slots.Length; j++)
            {
                if (j != i && slots[j] == item)
                {
                    slots[j] = slots[i];
                    break;
                }
            }
        }

        slots[i] = item;
        SlotsChanged?.Invoke();
    }

    // For pickups later
    public void Give(EquipItem item)
    {
        if (item == null || owned.Contains(item)) return;
        owned.Add(item);
        SlotsChanged?.Invoke();
    }

    public void BeginRosaryTrail()
    {
        if (rosaryTrail == null || !rosaryTrail.BeginTrail())
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is RosaryItem &&
                input.IsEquipHeld((EquipSlot)i))
            {
                activeRosarySlot = (EquipSlot)i;
                break;
            }
        }
    }

    void StopRosaryTrail()
    {
        if (rosaryTrail != null)
            rosaryTrail.ReleaseTrail();

        activeRosarySlot = null;
    }
}
