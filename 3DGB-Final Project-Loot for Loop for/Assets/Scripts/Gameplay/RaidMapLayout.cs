using UnityEngine;

// Records the editor-applied mapping so authoring checks can use original map coordinates.
public sealed class RaidMapLayout : MonoBehaviour
{
    public Vector3 originalCenter;
    public Vector2 horizontalScale = Vector2.one;
    public Vector2 originalSize;
    public Vector2 targetSize;

    public Vector3 MapPoint(Vector3 position) => new Vector3(
        originalCenter.x + (position.x - originalCenter.x) * horizontalScale.x,
        position.y,
        originalCenter.z + (position.z - originalCenter.z) * horizontalScale.y);

    public void ScaleGeometry(Transform root)
    {
        root.position = MapPoint(root.position);
        root.localScale = Vector3.Scale(root.localScale, new Vector3(horizontalScale.x, 1, horizontalScale.y));
    }
}
