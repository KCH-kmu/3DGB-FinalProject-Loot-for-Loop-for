using UnityEngine;
// Use RaycastMask for hitscan weapons and sight; put physical bullets on Projectile.
public static class CombatPhysics {
 public static int RaycastMask => Physics.DefaultRaycastLayers & ~LayerMask.GetMask("WaterMovementBarrier", "Projectile");
}
