using UnityEngine;

// Fez: toggle the perspective
[CreateAssetMenu(menuName = "Equip/Fez")]
public class FezItem : EquipItem
{
    public override bool CanUse(EquipSystem owner) => true; // add "not mid-rotation" etc. here
    public override void Use(EquipSystem owner) => owner.Perspective.Toggle();
}
