using UnityEngine;

public enum WeaponKind { Revolver, Rifle, SubmachineGun, Shotgun }

[CreateAssetMenu(menuName = "Loot for Loop/Weapon")]
public sealed class WeaponDefinition : ScriptableObject
{
    public WeaponKind kind;
    public string displayName;
    [Min(.02f)] public float shotInterval = .1f;
    [Min(.02f)] public float burstCooldown = .35f;
    [Min(.05f)] public float reloadSeconds = 2;
    [Min(0)] public float shellReloadStartSeconds = .3f;
    [Range(0, 30)] public float spreadHalfAngle = 1;
    [Min(1)] public int damage = 20;
    [Min(1)] public float range = 60;
    [Min(0)] public int startingReserve = 60;
    public GameObject modelPrefab;
    public Vector3 gripOffset;
    public Vector3 modelEuler;
    public Vector3 muzzleLocalPosition = new Vector3(0, 0, .5f);
    public int Capacity => kind == WeaponKind.Revolver ? 6 : kind == WeaponKind.Shotgun ? 8 : 30;
    public int Pellets => kind == WeaponKind.Shotgun ? 12 : 1;
}
