using UnityEngine;
using UnityEditor;
using System.Linq;
public static class WaterBarrierValidation {
 [MenuItem("Tools/Gameplay/Validate Water Barrier Faces")]
 public static void Validate(){
 if(EditorApplication.isPlaying)throw new System.Exception("Run in Edit mode");
 var barriers=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.name.StartsWith("VisibleWater_")).ToArray();
 if(barriers.Length!=2)throw new System.Exception("Expected two water barriers in Game scene");
 foreach(var c in barriers){var mesh=c.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;double volume=0;for(int i=0;i<t.Length;i+=3)volume+=Vector3.Dot(v[t[i]],Vector3.Cross(v[t[i+1]],v[t[i+2]]))/6.0;if(volume<=0)throw new System.Exception("Inward water collision faces: "+c.name);}
 var go=new GameObject("WaterBarrierValidationProbe");
 try{go.transform.position=new Vector3(-6.898592f,1.36691523f,-3.969099f);var cc=go.AddComponent<CharacterController>();cc.height=2.1f;cc.center=Vector3.up*1.05f;cc.radius=.32f;cc.skinWidth=.032f;cc.stepOffset=.3f;cc.slopeLimit=45;Physics.SyncTransforms();cc.Move(Vector3.down*.5f);var start=go.transform.position;
 for(int i=0;i<40;i++)cc.Move((Vector3.forward*4.5f+Vector3.down*2)*.016f);var far=go.transform.position;if(far.z-start.z<2)throw new System.Exception("Cannot exit lake-side pocket toward land");
 for(int i=0;i<40;i++)cc.Move((Vector3.back*4.5f+Vector3.down*2)*.016f);
 var error=Vector3.ProjectOnPlane(go.transform.position-start,Vector3.up).magnitude;if(error>.2f)throw new System.Exception("Lake-side roundtrip failed: "+error);
 Debug.Log("WATER_BARRIER_VALIDATION_PASS: outward faces, land exit and return; horizontal error="+error);
 }finally{Object.DestroyImmediate(go);}
 }
}
