using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WeaponRulesValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Gameplay/Weapons/Validate Rules")]
    public static void Run()
    {
        var catalog = Resources.Load<WeaponCatalog>("WeaponCatalog");
        Require(catalog && catalog.weapons.Length == 4, "Only four weapons in catalog");
        var defs = catalog.weapons;
        Require(defs[0].Capacity == 6 && defs[1].Capacity == 30 && defs[2].Capacity == 30 && defs[3].Capacity == 8, "Requested capacities");
        Require(defs[3].Pellets == 12 && defs[2].spreadHalfAngle > defs[1].spreadHalfAngle, "Shotgun pellets / SMG wider than rifle");
        var revolver = new WeaponState(defs[0]);
        Require(revolver.Tick(0, true, true, false) && revolver.Loaded == 5, "Revolver first press");
        for (int i = 1; i < 100; i++) Require(!revolver.Tick(i * .05f, false, true, false), "Revolver cannot repeat while held");
        for (int i = 1; i < 6; i++) Require(revolver.Tick(5 + i, true, true, false), "Revolver discrete press");
        Require(revolver.Loaded == 0 && revolver.TotalShots == 6 && !revolver.Tick(12, true, true, false), "Six shots then empty");
        int reserve = revolver.Reserve;
        Require(revolver.StartReload(13), "Revolver reload");
        Require(!revolver.Tick(13.1f, true, true, false) && revolver.Loaded == 0, "Cannot fire during magazine reload");
        revolver.Tick(15, false, false, false);
        Require(revolver.Loaded == 6 && revolver.Reserve == reserve - 6, "Magazine reload consumes exactly six");

        var rifle = new WeaponState(defs[1]);
        Require(rifle.Tick(0, true, true, false), "Rifle first burst round");
        Require(!rifle.Tick(.04f, false, false, false), "Burst spacing enforced");
        Require(rifle.Tick(.1f, false, false, false) && rifle.Tick(.2f, false, false, false), "Burst completes after releasing trigger");
        Require(rifle.Loaded == 27 && !rifle.Tick(1, false, true, false), "Held rifle does not repeat burst");
        for (int ammo = 1; ammo <= 2; ammo++)
        {
            var low = new WeaponState(defs[1], ammo, 0);
            for (int i = 0; i < 5; i++) low.Tick(i * .1f, i == 0, i == 0, false);
            Require(low.TotalShots == ammo && low.Loaded == 0 && low.BurstRemaining == 0, "Partial burst at low ammunition");
        }
        var canceled = new WeaponState(defs[1]);
        canceled.Tick(0, true, true, false); canceled.CancelActions(.01f);
        Require(!canceled.Tick(1, false, false, false) && canceled.Loaded == 29, "Cancel burst on UI/switch");
        var scarce = new WeaponState(defs[1], 5, 2);
        scarce.StartReload(0); scarce.Tick(3, false, false, false);
        Require(scarce.Loaded == 7 && scarce.Reserve == 0 && !scarce.StartReload(4), "Partial magazine reload with limited reserve");

        var smg = new WeaponState(defs[2]);
        for (int i = 0; i < 30; i++) Require(smg.Tick(i * .09f, i == 0, true, false), "SMG automatic shot " + i);
        Require(smg.Loaded == 0 && smg.TotalShots == 30 && !smg.Tick(4, false, true, false), "SMG capacity 30");
        Require(smg.StartReload(5), "SMG reload");
        Require(smg.Tick(7, false, true, false) && smg.Loaded == 29, "Automatic fire resumes after reload");

        var shotgun = new WeaponState(defs[3], 0, 10);
        Require(shotgun.StartReload(0), "Start shell reload");
        shotgun.Tick(.99f, false, false, false); Require(shotgun.Loaded == 0, "No early shell insertion");
        shotgun.Tick(1.01f, false, false, false); Require(shotgun.Loaded == 1 && shotgun.Reserve == 9 && shotgun.IsReloading, "Insert one shell");
        shotgun.Tick(1.67f, false, false, false); Require(shotgun.Loaded == 2 && shotgun.Reserve == 8, "Next shell individually");
        Require(shotgun.Tick(1.7f, true, true, false) && shotgun.Loaded == 1 && !shotgun.IsReloading, "Fire interrupts shell reload");
        Require(shotgun.TotalShots == 1 && shotgun.TotalPellets == 12, "Twelve pellets consume exactly one shell");
        Require(!shotgun.Tick(3, false, true, false), "Shotgun requires another trigger press");
        shotgun.StartReload(4);
        for (int i = 0; i < 10; i++) shotgun.Tick(5.01f + i * .66f, false, false, false);
        Require(shotgun.Loaded == 8 && shotgun.Reserve == 1 && !shotgun.IsReloading, "Stops at eight shells");
        var shellScarce = new WeaponState(defs[3], 0, 1);
        shellScarce.StartReload(0); shellScarce.Tick(2, false, false, false);
        Require(shellScarce.Loaded == 1 && shellScarce.Reserve == 0 && !shellScarce.IsReloading, "Stops when shells run out");
        var abort = new WeaponState(defs[3], 2, 4);
        abort.StartReload(0); abort.CancelActions(.1f); abort.Tick(10, false, false, false);
        Require(abort.Loaded == 2 && abort.Reserve == 4, "Interrupted loading cannot create ammo");

        var random = new System.Random(428);
        for (int i = 0; i < 1000; i++)
        {
            float a = (float)(random.NextDouble() * Math.PI * 2), r = (float)Math.Sqrt(random.NextDouble());
            var disk = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            float rifleAngle = Vector3.Angle(Vector3.forward, PlayerWeaponController.SpreadDirection(Vector3.forward, defs[1].spreadHalfAngle, disk));
            float smgAngle = Vector3.Angle(Vector3.forward, PlayerWeaponController.SpreadDirection(Vector3.forward, defs[2].spreadHalfAngle, disk));
            Require(rifleAngle <= defs[1].spreadHalfAngle + .001f && smgAngle <= defs[2].spreadHalfAngle + .001f && smgAngle >= rifleAngle, "Spread cone bounds");
        }
        Directory.CreateDirectory(WeaponAuthoring.Output);
        File.WriteAllText(WeaponAuthoring.Output + "/rules-checks.txt", "PASS capacities 6/30/30/8; revolver semi-auto; rifle 3-round burst and low-ammo burst; SMG held auto and wider spread; shotgun 12 pellets per shell and individual reload; reload interruption, reserve conservation, full/empty limits, input cancellation. " + DateTime.Now);
        Debug.Log("WEAPON_RULES_CHECKS: PASS");
    }
}
