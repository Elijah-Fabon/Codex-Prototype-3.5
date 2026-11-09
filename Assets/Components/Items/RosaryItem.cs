using UnityEngine;

// The rosary, by what you hold with it:
//   pillar direction + Target -> the pillar under the player, and nothing else applies
//   targeted match grid  -> starts a chain
//   D-pad direction      -> that firepower (no target needed)
//   Target button        -> the ordinary projectile
//   nothing              -> lays a bead trail
[CreateAssetMenu(menuName = "Equip/Rosary")]
public class RosaryItem : EquipItem
{
    public Projectile prefab;
    public float speed = 14f;

    public override void Use(EquipSystem owner)
    {
        // A targeted match grid takes priority: the D-pad is moving its cursor, so start a chain instead
        TargetingSystem targeting = owner.GetComponent<TargetingSystem>();
        FirepowerSystem firepowers = owner.GetComponent<FirepowerSystem>();

        // A targeted match grid takes priority: the D-pad is moving its cursor, so start a chain
        if (targeting != null && targeting.IsTargetingGrid)
        {
            targeting.TryBeginChain();
            return;
        }

        // Rosary + D-pad direction
        if (firepowers != null && firepowers.TryUseSelected(owner)) return;

        // Rosary + Target
        if (targeting != null && targeting.IsTargeting)
        {
            if (prefab == null)
            {
                Debug.Log("Rosary: no projectile assigned.");
                return;
            }

            // Fired from the muzzle, toward whatever is targeted (otherwise the way the player faces)
            bool aimed = targeting.AimMuzzle();
            Vector3 origin = owner.Muzzle != null ? owner.Muzzle.position : owner.User.position;
            Vector3 direction = aimed ? owner.Muzzle.forward : owner.User.forward;
            Quaternion rotation = aimed ? owner.Muzzle.rotation : owner.User.rotation;

            Projectile p = Instantiate(prefab, origin, rotation);
            p.Launch(direction * speed);
            return;
        }

        // Rosary alone
        owner.BeginRosaryTrail();
    }
}
