using UnityEngine;
using System.Collections.Generic;
[DefaultExecutionOrder(100)]
public sealed class PlayerOcclusionCutaway : MonoBehaviour {
 public Transform target;
 readonly List<Renderer> candidates=new List<Renderer>();
 readonly HashSet<Renderer> hidden=new HashSet<Renderer>();
 void Start(){foreach(var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){string n=r.name.ToLower();if(n.Contains("tree")||n.Contains("rock")||n.Contains("mountain"))candidates.Add(r);}}
 void LateUpdate(){if(!target)return;Vector3 end=target.position+Vector3.up;Vector3 direction=end-transform.position;float distance=direction.magnitude;var ray=new Ray(transform.position,direction.normalized);
 foreach(var r in candidates){if(!r)continue;bool blocks=r.bounds.IntersectRay(ray,out float hit)&&hit<distance-.4f;
 if(blocks&&!r.forceRenderingOff){r.forceRenderingOff=true;hidden.Add(r);}else if(!blocks&&hidden.Remove(r))r.forceRenderingOff=false;}}
 void OnDisable(){foreach(var r in hidden)if(r)r.forceRenderingOff=false;hidden.Clear();}
}
