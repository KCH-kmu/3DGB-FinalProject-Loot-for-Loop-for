using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RaidEncounter))]
public sealed class RaidEnemyHealth : MonoBehaviour
{
    public int Health { get; private set; } = 100;
    public void Damage(int amount)
    {
        var encounter = GetComponent<RaidEncounter>();
        if (amount <= 0 || Health <= 0 || encounter.isCrate) return;
        Health = Mathf.Max(0, Health - amount);
        if (Health != 0) return;
        encounter.enabled = false;
        var agent = GetComponent<NavMeshAgent>();
        if (agent && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        Destroy(gameObject, .1f);
    }
}
