using System;

// One Tick can emit at most one shot. A slow frame never compresses a burst into a single frame.
public sealed class WeaponState
{
    public WeaponDefinition Definition { get; }
    public int Loaded { get; private set; }
    public int Reserve { get; private set; }
    public int BurstRemaining { get; private set; }
    public bool IsReloading { get; private set; }
    public float ReloadDue { get; private set; }
    public float LastShotTime { get; private set; } = float.NegativeInfinity;
    public int TotalShots { get; private set; }
    public int TotalPellets => TotalShots * Definition.Pellets;
    float nextShot;

    public WeaponState(WeaponDefinition definition, int loaded = -1, int reserve = -1)
    {
        Definition = definition ? definition : throw new ArgumentNullException(nameof(definition));
        Loaded = loaded < 0 ? definition.Capacity : Math.Min(loaded, definition.Capacity);
        Reserve = reserve < 0 ? Math.Max(0, definition.startingReserve) : reserve;
    }

    public bool StartReload(float now)
    {
        if (IsReloading || BurstRemaining > 0 || Loaded == Definition.Capacity || Reserve == 0) return false;
        IsReloading = true;
        ReloadDue = now + Math.Max(.05f, Definition.reloadSeconds) +
            (Definition.kind == WeaponKind.Shotgun ? Math.Max(0, Definition.shellReloadStartSeconds) : 0);
        return true;
    }
    public void CancelActions(float now)
    {
        if (BurstRemaining > 0) nextShot = Math.Max(nextShot, now + Definition.burstCooldown);
        BurstRemaining = 0; IsReloading = false;
    }
    public bool Tick(float now, bool pressed, bool held, bool reload)
    {
        if (IsReloading && now >= ReloadDue)
        {
            int amount = Definition.kind == WeaponKind.Shotgun ? 1 : Definition.Capacity - Loaded;
            amount = Math.Min(amount, Reserve); Loaded += amount; Reserve -= amount;
            IsReloading = Definition.kind == WeaponKind.Shotgun && Loaded < Definition.Capacity && Reserve > 0;
            if (IsReloading) ReloadDue = now + Math.Max(.05f, Definition.reloadSeconds);
        }
        if (IsReloading && Definition.kind == WeaponKind.Shotgun && pressed && Loaded > 0)
            IsReloading = false;
        if (IsReloading) return false;
        if (reload && StartReload(now)) return false;
        if (now < nextShot) return false;
        if (Definition.kind == WeaponKind.Rifle && pressed && BurstRemaining == 0 && Loaded > 0)
            BurstRemaining = Math.Min(3, Loaded);
        bool wantsShot = Definition.kind == WeaponKind.Rifle ? BurstRemaining > 0 :
            Definition.kind == WeaponKind.SubmachineGun ? held : pressed;
        if (!wantsShot || Loaded <= 0) { if (Loaded == 0) BurstRemaining = 0; return false; }
        Loaded--; TotalShots++; LastShotTime = now;
        if (BurstRemaining > 0) BurstRemaining--;
        nextShot = now + Math.Max(.02f, Definition.shotInterval);
        if (Definition.kind == WeaponKind.Rifle && BurstRemaining == 0)
            nextShot = now + Math.Max(Definition.shotInterval, Definition.burstCooldown);
        return true;
    }
}
