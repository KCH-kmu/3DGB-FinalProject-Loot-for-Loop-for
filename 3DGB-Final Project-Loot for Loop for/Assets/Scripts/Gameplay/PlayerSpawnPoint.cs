using UnityEngine;

public sealed class PlayerSpawnPoint : MonoBehaviour
{
    public TopDownPlayer player;
    void Awake()
    {
        if (!player) return;
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
