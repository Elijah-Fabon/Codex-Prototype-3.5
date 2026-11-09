using UnityEngine;

// Sword: hit whatever is in front
[CreateAssetMenu(menuName = "Equip/Sword")]
public class SwordItem : EquipItem
{
    public float reach = 1.5f, radius = 1f;
    public int damage = 1;
    public LayerMask hitMask;

    public override void Use(EquipSystem owner)
    {
        Transform t = owner.User;
        Vector3 center = t.position + t.forward * reach;
        foreach (var col in Physics.OverlapSphere(center, radius, hitMask))
            if (col.TryGetComponent(out IDamageable d)) d.TakeDamage(damage);
    }
}
