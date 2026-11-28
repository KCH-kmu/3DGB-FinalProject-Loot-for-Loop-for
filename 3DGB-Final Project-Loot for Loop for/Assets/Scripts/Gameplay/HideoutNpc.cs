using UnityEngine;

public sealed class HideoutNpc : MonoBehaviour
{
    public string npcId;
    public string displayName;
    public string roleName;
    [TextArea(2, 5)] public string[] dialogue;
    public Transform interactionPoint;
    public Color accent = Color.white;
    public const float InteractionRange = 1.65f;

    public bool CanTalk(TopDownPlayer player)
    {
        if (!isActiveAndEnabled || !player || !interactionPoint) return false;
        Vector3 delta = player.transform.position - interactionPoint.position;
        if (Mathf.Abs(delta.y) > 1.5f || new Vector2(delta.x, delta.z).sqrMagnitude > InteractionRange * InteractionRange)
            return false;
        // Only the customer side of the counter is an interaction zone.
        if (Vector3.Dot(player.transform.position - transform.position, transform.forward) < 1.35f) return false;
        Vector3 from = player.transform.position + Vector3.up * .8f;
        Vector3 to = interactionPoint.position + Vector3.up * .8f;
        foreach (var hit in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(player.transform) && !hit.transform.IsChildOf(transform)) return false;
        return true;
    }
}
