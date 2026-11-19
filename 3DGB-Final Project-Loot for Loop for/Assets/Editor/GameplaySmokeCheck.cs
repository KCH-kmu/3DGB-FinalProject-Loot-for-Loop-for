using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
public static class GameplaySmokeCheck {
 static bool oldRunInBackground; static InputSettings.BackgroundBehavior oldBackground; static InputSettings.EditorInputBehaviorInPlayMode oldEditor; static double started,arrival;static bool arrived;static Vector3 initial;static string report;
 [MenuItem("Tools/Gameplay/Run Smoke Check")]
 public static void Run(){if(!EditorApplication.isPlaying)throw new System.Exception("Enter Play mode first");oldRunInBackground=Application.runInBackground;Application.runInBackground=true;oldBackground=InputSystem.settings.backgroundBehavior;oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.EnableDevice(Keyboard.current);started=EditorApplication.timeSinceStartup;arrival=0;arrived=false;report="";initial=Object.FindFirstObjectByType<TopDownPlayer>().transform.position;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
 static void Tick(){
  if(!EditorApplication.isPlaying){Finish("FAIL stopped before completion");return;}
  var player=Object.FindFirstObjectByType<TopDownPlayer>();if(!player)return;
  EditorApplication.QueuePlayerLoopUpdate();var elapsed=EditorApplication.timeSinceStartup-started;
  if(SceneManager.GetActiveScene().name=="Game"){
   InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
   if(!arrived){arrived=true;arrival=EditorApplication.timeSinceStartup;report="PASS portal loaded Game; arrival="+player.transform.position+"\n";}
   if(EditorApplication.timeSinceStartup-arrival>2){var spawn=Object.FindFirstObjectByType<PlayerSpawnPoint>();var delta=Vector3.Distance(player.transform.position,spawn.transform.position);Finish(report+(delta<.5f?"PASS":"FAIL")+" spawn settling distance="+delta+" position="+player.transform.position+" players="+Object.FindObjectsByType<TopDownPlayer>(FindObjectsSortMode.None).Length);}
   return;
  }
  InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.W));
  if(elapsed>12)Finish("FAIL no transition. initial="+initial+" current="+player.transform.position);
 }
 static void Finish(string result){EditorApplication.update-=Tick;if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());Application.runInBackground=oldRunInBackground;InputSystem.settings.backgroundBehavior=oldBackground;InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor;System.IO.File.WriteAllText("Temp/GameplaySmokeCheck.txt",result);Debug.Log(result);}
}



