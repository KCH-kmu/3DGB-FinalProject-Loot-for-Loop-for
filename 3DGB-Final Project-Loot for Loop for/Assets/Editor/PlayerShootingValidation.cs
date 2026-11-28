using UnityEditor;

// Weapon integration now owns animation input; keep the existing menu as an entry point.
public static class PlayerShootingValidation
{
    [MenuItem("Tools/Gameplay/Player Shooting/Run Play Check")]
    public static void Run() => WeaponPlayValidation.Run();
}
