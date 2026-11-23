using UnityEngine;
public class RaidHealth:MonoBehaviour {
 public int health=100;float hurtUntil;
 public void Damage(int amount){if(health<=0)return;health=Mathf.Max(0,health-amount);hurtUntil=Time.time+.25f;if(health==0)GetComponent<TopDownPlayer>().InputEnabled=false;}
 void OnGUI(){GUI.Box(new Rect(16,80,220,45),health>0?"HP: "+health:"Defeated - restart Play to retry");if(Time.time<hurtUntil){var old=GUI.color;GUI.color=new Color(1,0,0,.12f);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=old;}}
}
