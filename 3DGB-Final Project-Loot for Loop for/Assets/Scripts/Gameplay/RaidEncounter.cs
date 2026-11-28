using UnityEngine;
using UnityEngine.AI;
public class RaidEncounter : MonoBehaviour {
 public bool isCrate;
 public string crateLabel="Supply crate";
 public string lootPreview="Ammo x20 / Medical supplies x1";
 public float detectionRange=10,attackRange=1.8f;
 Transform player;NavMeshAgent agent;Animator animator;
 float nextAttack,attackEnd,hitTime;bool alerted,attacking,hitApplied;
 void Start(){var p=FindAnyObjectByType<TopDownPlayer>();if(p)player=p.transform;agent=GetComponent<NavMeshAgent>();animator=GetComponentInChildren<Animator>();if(!isCrate&&!GetComponent<RaidEnemyHealth>())gameObject.AddComponent<RaidEnemyHealth>();}
 bool Visible(){var start=transform.position+Vector3.up;var end=player.position+Vector3.up;return !Physics.Linecast(start,end,out var hit,CombatPhysics.RaycastMask,QueryTriggerInteraction.Ignore)||hit.transform.IsChildOf(player);}
 void Update(){
 if(!player)return;float distance=Vector3.Distance(transform.position,player.position);
 if(isCrate)return; // LootContainer and PlayerInventory own container interaction.
 if(!agent||!agent.isOnNavMesh)return;
 if(attacking){
 agent.isStopped=true;agent.velocity=Vector3.zero;
 if(!hitApplied&&Time.time>=hitTime){hitApplied=true;if(distance<=attackRange&&Visible()){var health=player.GetComponent<RaidHealth>();if(health)health.Damage(10);}}
 if(Time.time<attackEnd)return;
 attacking=false;if(animator)animator.CrossFadeInFixedTime("Idle",.08f);
 }
 if(distance<detectionRange&&Visible())alerted=true;if(distance>20)alerted=false;
 bool inRange=distance<=attackRange&&Visible();agent.isStopped=!alerted||inRange;
 if(alerted&&!inRange){if(NavMesh.SamplePosition(player.position,out var target,1.5f,agent.areaMask))agent.SetDestination(target.position);else agent.isStopped=true;}
 if(animator)animator.SetBool("Moving",!agent.isStopped&&agent.velocity.sqrMagnitude>.05f);
 if(alerted&&inRange&&Time.time>=nextAttack){
 var dir=player.position-transform.position;dir.y=0;if(dir.sqrMagnitude>.01f)transform.rotation=Quaternion.LookRotation(dir);
 attacking=true;hitApplied=false;hitTime=Time.time+.35f;attackEnd=Time.time+.9f;nextAttack=Time.time+1.2f;
 agent.isStopped=true;agent.ResetPath();agent.velocity=Vector3.zero;
 if(animator){animator.SetBool("Moving",false);animator.CrossFadeInFixedTime("Attack",.08f);}
 }
 }
}

