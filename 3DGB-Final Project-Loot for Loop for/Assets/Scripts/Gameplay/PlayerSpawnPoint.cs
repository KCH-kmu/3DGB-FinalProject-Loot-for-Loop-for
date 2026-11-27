using UnityEngine;

public sealed class PlayerSpawnPoint : MonoBehaviour
{
    public TopDownPlayer player;
    public RaidRouteDirector raidRoutes;
    void Awake()
    {
        if (!player) return;
        if (raidRoutes && raidRoutes.SpawnPoint)
            transform.SetPositionAndRotation(raidRoutes.SpawnPoint.transform.position, raidRoutes.SpawnPoint.transform.rotation);
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.SetPositionAndRotation(transform.position, transform.rotation);
        controller.enabled = true;
    }
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, .5f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2);
    }
}
