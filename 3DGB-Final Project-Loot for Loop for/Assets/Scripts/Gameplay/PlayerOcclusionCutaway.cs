using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[DefaultExecutionOrder(100)]
public sealed class PlayerOcclusionCutaway : MonoBehaviour {
 public Transform target;
 [SerializeField] Shader cutawayShader;
 [SerializeField,Range(.02f,.2f)] float playerRadius=.075f;
 [SerializeField,Range(.02f,.2f)] float aimRadius=.065f;
 [SerializeField,Range(.002f,.04f)] float feather=.012f;
 [SerializeField,Min(0)] float stumpHeight=.65f;
 readonly Dictionary<Renderer,Material[]> originals=new Dictionary<Renderer,Material[]>();
 readonly Dictionary<Material,Material> replacements=new Dictionary<Material,Material>();
 Camera view;
 public Vector3 AimPoint {get;private set;}
 void OnEnable(){
 view=GetComponent<Camera>();if(!cutawayShader)cutawayShader=Shader.Find("LootForLoop/ObstacleCutaway");if(!cutawayShader){enabled=false;return;}
 foreach(var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){
 string n=r.name.ToLower();if(!(n.Contains("tree")||n.Contains("rock")||n.Contains("mountain")))continue;
 originals[r]=r.sharedMaterials;var copies=new Material[r.sharedMaterials.Length];
 for(int i=0;i<copies.Length;i++){var source=r.sharedMaterials[i];if(!source)continue;if(!replacements.TryGetValue(source,out var material)){material=new Material(cutawayShader);material.CopyPropertiesFromMaterial(source);material.name=source.name+" (Cutaway runtime)";replacements[source]=material;}copies[i]=material;}
 r.sharedMaterials=copies;r.forceRenderingOff=false;var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);block.SetFloat("_CutawayMinY",r.bounds.min.y+(n.Contains("tree")?stumpHeight:.2f));r.SetPropertyBlock(block);
 }
 }
 Vector4 ScreenTarget(Vector3 world,bool active){var p=view.WorldToViewportPoint(world);return new Vector4(p.x,p.y,p.z,active&&p.z>0?1:0);}
 void LateUpdate(){
 if(!target||!view)return;
 Shader.SetGlobalVector("_CutawayPlayer",ScreenTarget(target.position+Vector3.up*1.1f,true));
 bool aimValid=false;
 if(Mouse.current!=null){var mouse=Mouse.current.position.ReadValue();if(view.pixelRect.Contains(mouse)){
 var ray=view.ScreenPointToRay(mouse);var hits=Physics.RaycastAll(ray,250,CombatPhysics.RaycastMask,QueryTriggerInteraction.Ignore);float nearest=float.PositiveInfinity;
 foreach(var hit in hits){string n=hit.collider.name;if(!(n.Contains("Meadow")||n.Contains("Ground")||n.Contains("Floor")))continue;if(hit.distance<nearest){nearest=hit.distance;AimPoint=hit.point;aimValid=true;}}
 if(!aimValid){var plane=new Plane(Vector3.up,target.position);if(plane.Raycast(ray,out float d)){AimPoint=ray.GetPoint(d);aimValid=true;}}
 }}
 Shader.SetGlobalVector("_CutawayAim",ScreenTarget(AimPoint,aimValid));Shader.SetGlobalVector("_CutawaySettings",new Vector4(playerRadius,aimRadius,feather,view.aspect));
 }
 void OnDisable(){Shader.SetGlobalVector("_CutawayPlayer",Vector4.zero);Shader.SetGlobalVector("_CutawayAim",Vector4.zero);
 foreach(var item in originals)if(item.Key){item.Key.sharedMaterials=item.Value;var block=new MaterialPropertyBlock();item.Key.GetPropertyBlock(block);block.SetFloat("_CutawayMinY",-10000);item.Key.SetPropertyBlock(block);}
 foreach(var material in replacements.Values)if(material)Destroy(material);originals.Clear();replacements.Clear();}
}
