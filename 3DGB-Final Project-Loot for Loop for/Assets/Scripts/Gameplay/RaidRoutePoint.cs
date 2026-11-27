using UnityEngine;

public sealed class RaidRoutePoint : MonoBehaviour
{
    public string pointLabel;
    [Min(.5f)] public float extractionRadius = 1.7f;
    public Renderer[] markers = System.Array.Empty<Renderer>();
    public Material dormantMaterial, entryMaterial, exitMaterial;
    public bool IsExtraction { get; private set; }

    public void SetRole(bool entry, bool extraction)
    {
        IsExtraction = extraction;
        var material = extraction ? exitMaterial : entry ? entryMaterial : dormantMaterial;
        foreach (var marker in markers) if (marker) marker.sharedMaterial = material;
    }

    public bool Contains(Vector3 position)
    {
        var delta = position - transform.position;
        return Mathf.Abs(delta.y) < 2 && new Vector2(delta.x, delta.z).sqrMagnitude <= extractionRadius * extractionRadius;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Application.isPlaying ? (IsExtraction ? Color.green : Color.gray) : Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .1f, extractionRadius);
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 2);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.8f, pointLabel);
#endif
    }
}
