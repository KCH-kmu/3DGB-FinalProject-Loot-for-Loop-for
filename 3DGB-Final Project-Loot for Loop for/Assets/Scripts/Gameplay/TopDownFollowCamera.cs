using UnityEngine;

public sealed class TopDownFollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 17, -12);
    [SerializeField] float followSharpness = 10f;
    void Start() { if (target) { transform.position = target.position + offset; transform.LookAt(target.position); } }
    void LateUpdate()
    {
        if (!target) return;
        transform.position = Vector3.Lerp(transform.position, target.position + offset,
            1f - Mathf.Exp(-followSharpness * Time.deltaTime));
        transform.rotation = Quaternion.LookRotation(-offset);
    }
}
