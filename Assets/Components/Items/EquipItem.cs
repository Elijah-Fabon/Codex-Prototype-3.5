using UnityEngine;

[CreateAssetMenu(fileName = "EquipItem", menuName = "Scriptable Objects/EquipItem")]
public abstract class EquipItem : ScriptableObject
{
    public string displayName;
    public Sprite icon;
    public float cooldown = 0.3f;

    public virtual bool CanUse(EquipSystem owner) => true;
    public abstract void Use(EquipSystem owner);
}
