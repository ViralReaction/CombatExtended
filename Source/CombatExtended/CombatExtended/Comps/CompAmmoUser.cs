﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using CombatExtended.Compatibility;
using CombatExtended.Utilities;
using CombatExtended.AI;

namespace CombatExtended;
public class CompAmmoUser : CompRangedGizmoGiver
{
    #region Fields

    private int curMagCountInt = 0;
    private AmmoDef currentAmmoInt = null;
    private AmmoDef selectedAmmo;

    private Thing ammoToBeDeleted;

    public Building_Turret turret;         // Cross-linked from CE turret

    internal static Type rgStance = null;       // RunAndGun compatibility, set in relevent patch if needed
    #endregion

    #region Properties

    public CompProperties_AmmoUser Props
    {
        get
        {
            return (CompProperties_AmmoUser)props;
        }
    }

    public int MagsLeft
    {
        get
        {
            if (CompInventory != null)
            {
                CompInventory.UpdateInventory();
                int count = 0;
                foreach (AmmoLink link in Props.ammoSet.ammoTypes)
                {
                    count += CompInventory.AmmoCountOfDef(link.ammo);
                }
                return count;
            }
            return 0;
        }
    }
    public int MagSize
    {
        get
        {
            WeaponPlatform platform = parent as WeaponPlatform;
            return (int)(platform?.GetStatValue(CE_StatDefOf.MagazineCapacity) ?? Props.magazineSize);
        }
    }

    public int CurAmmoCount
    {
        get
        {
            return currentAmmoInt.ammoCount;
        }
    }

    public int MagAmmoCount
    {
        get
        {
            return MagSize / CurAmmoCount;
        }
    }

    public int MagSizeOverride
    {
        get
        {
            WeaponPlatform platform = parent as WeaponPlatform;
            return (int)(platform?.GetStatValue(CE_StatDefOf.AmmoGenPerMagOverride) ?? Props.AmmoGenPerMagOverride);
        }
    }
    public int CurMagCount
    {
        get
        {
            return curMagCountInt;
        }
        set
        {
            if (curMagCountInt != value && value >= 0)
            {
                curMagCountInt = value;
                if (CompInventory != null)
                {
                    CompInventory.UpdateInventory();    //Must be positioned after curMagCountInt is updated, because it relies on that value
                }
            }
        }
    }
    public CompEquippable CompEquippable
    {
        get
        {
            return parent.GetComp<CompEquippable>();
        }
    }
    public Pawn Wielder
    {
        get
        {
            if (CompEquippable == null
                    || CompEquippable.PrimaryVerb == null
                    || CompEquippable.PrimaryVerb.caster == null
                    || ((CompEquippable?.parent?.ParentHolder as Pawn_InventoryTracker)?.pawn is Pawn holderPawn && holderPawn != CompEquippable?.PrimaryVerb?.CasterPawn))
            {
                return null;
            }
            return CompEquippable.PrimaryVerb.CasterPawn;
        }
    }
    public bool IsEquippedGun => Wielder != null;

    GunDrawExtension gunDrawExt => parent.def.GetModExtension<GunDrawExtension>();
    public Pawn Holder
    {
        get
        {
            return Wielder ?? (CompEquippable?.parent.ParentHolder as Pawn_InventoryTracker)?.pawn;
        }
    }
    public bool UseAmmo
    {
        get
        {
            return Props.ammoSet != null && AmmoUtility.IsAmmoSystemActive(Props.ammoSet);
        }
    }
    public bool IsAOEWeapon
    {
        get
        {
            return parent.def.IsAOEWeapon();
        }
    }
    public bool HasAndUsesAmmoOrMagazine
    {
        get
        {
            return !UseAmmo || HasAmmoOrMagazine;
        }
    }
    public bool HasAmmoOrMagazine
    {
        get
        {
            return (HasMagazine && CurMagCount > 0) || HasAmmo;
        }
    }
    public virtual bool CanBeFiredNow
    {
        get
        {
            return (HasMagazine && CurMagCount > 0) || (!HasMagazine && (HasAmmo || !UseAmmo));
        }
    }
    public bool HasAmmo
    {
        get
        {
            return CompInventory != null && CompInventory.ammoList.Any(x => Props.ammoSet.ammoTypes.Any(a => a.ammo == x.def));
        }
    }
    public bool HasMagazine => MagSize > 0;
    public AmmoDef CurrentAmmo
    {
        get
        {
            return UseAmmo ? currentAmmoInt : null;
        }
        set
        {
            currentAmmoInt = value;
        }
    }

    public bool EmptyMagazine => HasMagazine && CurMagCount == 0;
    public int MissingToFullMagazine
    {
        get
        {
            if (!HasMagazine)
            {
                return 0;
            }
            if (SelectedAmmo == CurrentAmmo)
            {
                return MagSize - CurMagCount;
            }
            return MagSize;
        }
    }

    public bool FullMagazine
    {
        get
        {
            if (UseAmmo)
            {
                return HasMagazine && SelectedAmmo == CurrentAmmo && CurMagCount >= MagSize;
            }
            return CurMagCount >= MagSize;
        }
    }

    public ThingDef CurAmmoProjectile => Props.ammoSet?.ammoTypes?.FirstOrDefault(x => x.ammo == CurrentAmmo)?.projectile ?? parent.def.Verbs.FirstOrDefault().defaultProjectile;
    public CompInventory CompInventory
    {
        get
        {
            return Holder.TryGetComp<CompInventory>();
        }
    }
    private IntVec3 Position
    {
        get
        {
            if (IsEquippedGun)
            {
                return Wielder.Position;
            }
            else if (turret != null)
            {
                return turret.Position;
            }
            else if (Holder != null)
            {
                return Holder.Position;
            }
            else
            {
                return parent.Position;
            }
        }
    }
    private Map Map
    {
        get
        {
            if (Holder != null)
            {
                return Holder.MapHeld;
            }
            else if (turret != null)
            {
                return turret.MapHeld;
            }
            else
            {
                return parent.MapHeld;
            }
        }
    }
    public bool ShouldThrowMote => Props.throwMote && MagSize > 1;

    public AmmoDef SelectedAmmo
    {
        get
        {
            return selectedAmmo;
        }
        set
        {
            selectedAmmo = value;
            if (!HasMagazine && CurrentAmmo != value)
            {
                currentAmmoInt = value;
            }
        }
    }

    #endregion Properties

    #region Methods

    public override void Initialize(CompProperties vprops)
    {
        base.Initialize(vprops);

        //spawnUnloaded checks have all been moved to methods calling ResetAmmoCount.
        //curMagCountInt = Props.spawnUnloaded && UseAmmo ? 0 : MagSize;

        // Initialize ammo with default if none is set
        if (UseAmmo)
        {
            if (Props.ammoSet.ammoTypes.NullOrEmpty())
            {
                Log.Error(parent.Label + " has no available ammo types");
            }
            else
            {
                if (currentAmmoInt == null)
                {
                    currentAmmoInt = (AmmoDef)Props.ammoSet.ammoTypes[0].ammo;
                }
                if (selectedAmmo == null)
                {
                    selectedAmmo = currentAmmoInt;
                }
            }
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref curMagCountInt, "count", 0);
        Scribe_Defs.Look(ref currentAmmoInt, "currentAmmo");
        Scribe_Defs.Look(ref selectedAmmo, "selectedAmmo");
    }

    private void AssignJobToWielder(Job job)
    {
        if (Wielder.drafter != null)
        {
            Wielder.jobs.TryTakeOrderedJob(job);
        }
        else
        {
            ExternalPawnDrafter.TakeOrderedJob(Wielder, job);
        }
    }


    /// <summary>
    /// <para>Reduces ammo count and updates inventory if necessary, call this whenever ammo is consumed by the gun (e.g. firing a shot, clearing a jam). </para>
    /// <para>Has an optional argument for the amount of ammo to consume per shot, which defaults to 1; this caters for special cases such as different sci-fi weapons using up different amounts of the same energy cell ammo type per shot, or a double-barrelled shotgun that fires both cartridges at the same time (projectile treated as a single, more powerful bullet)</para>
    /// </summary>
    public void Notify_ShotFired(int ammoConsumedPerShot = 1)
    {
        ammoConsumedPerShot = (ammoConsumedPerShot > 0) ? ammoConsumedPerShot : 1;

        if (!IsEquippedGun && turret == null)
        {
            Log.Error(parent.ToString() + " tried reducing its ammo count without a wielder");
        }

        // Mag-less weapons feed directly from inventory
        if (!HasMagazine)
        {
            if (ammoToBeDeleted != null)
            {
                ammoToBeDeleted.Destroy();
                ammoToBeDeleted = null;
                CompInventory.UpdateInventory();
            }

            return;
        }

        if (curMagCountInt <= 0)
        {
            Log.Error($"{parent} tried reducing its ammo count when already empty");
        }
        // Reduce ammo count and update inventory
        CurMagCount = (curMagCountInt - ammoConsumedPerShot < 0) ? 0 : curMagCountInt - ammoConsumedPerShot;
    }

    public bool Notify_PostShotFired()
    {
        if (!HasAmmoOrMagazine)
        {
            DoOutOfAmmoAction();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Check whether ammo is available for firing a shot.
    /// </summary>
    /// <remarks>
    /// For weapons without a magazine, this may update the currently selected ammo type
    /// if we ran out of the currently selected ammo type but have different, compatible, types
    /// available in the inventory.
    /// </remarks>
    /// <returns></returns>
    public bool TryPrepareShot()
    {
        if (HasMagazine)
        {
            // If magazine is empty, return false
            if (curMagCountInt <= 0)
            {
                CurMagCount = 0;
                return false;
            }

            return true;
        }

        if (UseAmmo)
        {
            if (!TryFindAmmoInInventory(out ammoToBeDeleted))
            {
                return false;
            }
            if (ammoToBeDeleted.def != CurrentAmmo)
            {
                currentAmmoInt = ammoToBeDeleted.def as AmmoDef;
            }

            if (ammoToBeDeleted.stackCount > 1)
            {
                ammoToBeDeleted = ammoToBeDeleted.SplitOff(1);
            }
        }

        return true;
    }

    // used as a rate limiter
    private int _lastReloadJobTick = -1;

    [Compatibility.Multiplayer.SyncMethod]
    private void SyncedTryForceReload()
    {
        turret.TryForceReload();
    }

    [Compatibility.Multiplayer.SyncMethod]
    private void SyncedTryStartReload()
    {
        TryStartReload();
    }


    // really only used by pawns (JobDriver_Reload) at this point... TODO: Finish making sure this is only used by pawns and fix up the error checking.
    /// <summary>
    /// Overrides a Pawn's current activities to start reloading a gun or turret.  Has a code path to resume the interrupted job.
    /// </summary>
    public void TryStartReload()
    {
        //Don't reload when changing player pawn ammo in god mode
        if (DebugSettings.godMode && Wielder != null && (Wielder.IsColonistPlayerControlled || Wielder.IsColonyMech) && selectedAmmo != CurrentAmmo)
        {
            return;
        }


        if (Wielder?.jobs.curDriver is IJobDriver_Tactical)
        {
            return;
        }

        if (!HasMagazine)
        {
            if (!CanBeFiredNow)
            {
                DoOutOfAmmoAction();
            }
            return;
        }
        if (!IsEquippedGun && turret == null)
        {
            return;
        }

        // secondary branch for if we ended up being called up by a turret somehow...
        if (turret != null)
        {
            turret.TryOrderReload();
            return;
        }

        // R&G compatibility, prevents an initial attempt to reload while moving
        if (Wielder.stances.curStance.GetType() == rgStance)
        {
            return;
        }

        if (UseAmmo)
        {
            TryUnload();

            // Check for ammo
            if (IsEquippedGun && !HasAmmo)
            {
                DoOutOfAmmoAction();
                return;
            }
        }

        if (Props.reloadOneAtATime && UseAmmo && selectedAmmo == CurrentAmmo && CurMagCount == MagSize)
        {
            //Because reloadOneAtATime weapons don't dump their mag at the start of a reload, have to stop the reloading process here if the mag was already full
            return;
        }

        // Issue reload job
        if (IsEquippedGun && _lastReloadJobTick != GenTicks.TicksGame && (Wielder.jobs.curJob?.def ?? null) != CE_JobDefOf.ReloadWeapon)
        {
            Job reloadJob = TryMakeReloadJob();
            if (reloadJob == null)
            {
                return;
            }
            _lastReloadJobTick = GenTicks.TicksGame;
            reloadJob.playerForced = true;
            Wielder.jobs.StartJob(reloadJob, JobCondition.InterruptForced, null, Wielder.CurJob?.def != reloadJob.def, true);
        }
    }

    public void DropCasing(int count)
    {
        //For revolvers and break actions
        if (gunDrawExt != null && gunDrawExt.DropCasingWhenReload && CompEquippable?.PrimaryVerb is Verb_ShootCE verbCE && verbCE.VerbPropsCE.ejectsCasings)
        {
            for (int i = 0; i < count; i++)
            {
                verbCE.ExternalCallDropCasing(i);
            }
        }
    }

    // used by both turrets (JobDriver_ReloadTurret) and pawns (JobDriver_Reload).
    /// <summary>
    /// Used to unload the weapon.  Ammo will be dumped to the unloading Pawn's inventory or the ground if insufficient space.  Any ammo that can't be dropped
    /// on the ground is destroyed (with a warning).
    /// </summary>
    /// <returns>bool, true indicates the weapon was already in an unloaded state or the unload was successful.  False indicates an error state.</returns>
    /// <remarks>
    /// Failure to unload occurs if the weapon doesn't use a magazine.
    /// </remarks>
    public bool TryUnload(bool forceUnload = false)
    {
        Thing outThing;
        return TryUnload(out outThing, forceUnload);
    }

    public bool TryUnload(out Thing droppedAmmo, bool forceUnload = false)
    {
        droppedAmmo = null;
        if (!HasMagazine || (Holder == null && turret == null))
        {
            return false;    // nothing to do as we are in a bad state;
        }

        if (!UseAmmo || curMagCountInt == 0)
        {
            return true;    // nothing to do but we aren't in a bad state either.  Claim success.
        }

        if (Props.reloadOneAtATime && !forceUnload && selectedAmmo == CurrentAmmo && turret == null)
        {
            //For reloadOneAtATime weapons that haven't been explicitly told to unload, and aren't switching their ammo type, skip unloading.
            //The big advantage of a shotguns' reload mechanism is that you can add more shells without unloading the already loaded ones.
            return true;
        }

        // Add remaining ammo back to inventory
        Thing ammoThing = ThingMaker.MakeThing(currentAmmoInt);
        ammoThing.stackCount = curMagCountInt / CurAmmoCount;

        Thing remainderThing = null;
        int remainder = curMagCountInt % CurAmmoCount;
        if (remainder > 0)
        {
            if (currentAmmoInt.HasComp(typeof(CompApparelReloadable)))
            {
                remainderThing = ThingMaker.MakeThing(currentAmmoInt);
                remainderThing.TryGetComp<CompApparelReloadable>().remainingCharges = remainder;
            }
            else if (currentAmmoInt.partialUnloadAmmoDef != null)
            {
                remainderThing = ThingMaker.MakeThing(currentAmmoInt.partialUnloadAmmoDef);
                remainderThing.stackCount = remainder;
            }
        }

        bool doDrop = false;
        if (CompInventory != null)
        {
            doDrop = (curMagCountInt / CurAmmoCount) != CompInventory.container.TryAdd(ammoThing, ammoThing.stackCount);    // TryAdd should report how many ammoThing.stackCount it stored.
            if (remainderThing != null)
            {
                CompInventory.container.TryAdd(remainderThing, remainder);
            }
        }
        else
        {
            doDrop = true;    // Inventory was null so no place to shift the ammo besides the ground.
        }
        if (doDrop)
        {
            // NOTE: If we get here from ThingContainer.TryAdd() it will have modified the ammoThing.stackCount to what it couldn't take.

            if (!GenThing.TryDropAndSetForbidden(ammoThing, Position, Map, ThingPlaceMode.Near, out droppedAmmo, turret.Faction != Faction.OfPlayer))
            {
                Log.Warning(String.Concat(this.GetType().Assembly.GetName().Name + " :: " + this.GetType().Name + " :: ",
                                          "Unable to drop ", ammoThing.LabelCap, " on the ground, thing was destroyed."));
            }
            if (remainderThing != null)
            {
                if (!GenThing.TryDropAndSetForbidden(remainderThing, Position, Map, ThingPlaceMode.Near, out _, turret.Faction != Faction.OfPlayer))
                {
                    Log.Warning(String.Concat(this.GetType().Assembly.GetName().Name + " :: " + this.GetType().Name + " :: ",
                                              "Unable to drop ", remainderThing.LabelCap, " on the ground, thing was destroyed."));
                }
            }
        }
        // don't forget to set the clip to empty...
        CurMagCount = 0;
        return true;
    }

    /// <summary>
    /// Used to fetch a reload job for the weapon this comp is on.  Sets storedInfo to null (as if no job being replaced).
    /// </summary>
    /// <returns>Job using JobDriver_Reload</returns>
    /// <remarks>TryUnload() should be called before this in most cases.</remarks>
    public Job TryMakeReloadJob()
    {

        if (!HasMagazine || (Holder == null && turret == null))
        {
            return null;    // the job couldn't be created.
        }

        return JobMaker.MakeJob(CE_JobDefOf.ReloadWeapon, Holder, parent);
    }

    private void DoOutOfAmmoAction()
    {
        //Don't stow weapon for player pawns when in god mode, can be ammoying when testing single shot weapons.
        if (this.parent.def.weaponTags.Contains("NoSwitch") || (DebugSettings.godMode && Wielder != null && (Wielder.IsColonistPlayerControlled || Wielder.IsColonyMech)))
        {
            return;
        }

        if (ShouldThrowMote)
        {
            MoteMakerCE.ThrowText(Position.ToVector3Shifted(), Map, "CE_OutOfAmmo".Translate() + "!");
        }

        if (IsEquippedGun && CompInventory != null && (Wielder.CurJob == null || Wielder.CurJob.def != JobDefOf.Hunt))
        {
            if (CompInventory.TryFindViableWeapon(out ThingWithComps weapon, useAOE: !Holder.IsColonist))
            {
                Holder.jobs.StartJob(JobMaker.MakeJob(CE_JobDefOf.EquipFromInventory, weapon), JobCondition.InterruptForced, resumeCurJobAfterwards: true);
                return;
            }
            if (!Holder.IsColonist || !parent.def.IsAOEWeapon())
            {
                TryPickupAmmo();
            }
        }
        CompInventory?.SwitchToNextViableWeapon(!this.parent.def.weaponTags.Contains("NoSwitch"), !Holder.IsColonist, stopJob: false);
        CompAffectedByFacilities compAffectedByFacilities = turret?.TryGetComp<CompAffectedByFacilities>();
        if (compAffectedByFacilities != null)
        {
            foreach (Thing building in compAffectedByFacilities.linkedFacilities)
            {
                if (building is Building_AutoloaderCE container && container.StartReload(this))
                {
                    break;
                }
            }
        }
    }

    public bool TryPickupAmmo()
    {
        if (!Holder.RaceProps.Humanlike)
        {
            return false;
        }
        if (Holder.MentalState != null)
        {
            return false;
        }
        IEnumerable<AmmoDef> supportedAmmo = Props.ammoSet.ammoTypes.Select(a => a.ammo);
        foreach (Thing thing in Holder.Position.AmmoInRange(Holder.Map, 6).Where(t => t is AmmoThing ammo
                 && supportedAmmo.Contains(ammo.AmmoDef)
                 && (!Holder.IsColonist || (!ammo.IsForbidden(Holder) && ammo.Position.AdjacentTo8WayOrInside(Holder)))))
        {
            if (!Holder.CanReserve(thing))
            {
                continue;
            }
            if (!Holder.CanReach(thing, PathEndMode.InteractionCell, Danger.Unspecified, false, false))
            {
                continue;
            }
            if (CompInventory.CanFitInInventory(thing, out int count))
            {
                Thing ammo = thing;
                if (!ammo.Position.AdjacentTo8WayOrInside(Holder))
                {
                    Job pickupAmmo = JobMaker.MakeJob(JobDefOf.TakeInventory, ammo);
                    pickupAmmo.count = count;
                    Holder.jobs.StartJob(pickupAmmo, JobCondition.InterruptForced, resumeCurJobAfterwards: false);
                }
                else
                {
                    ammo = thing.SplitOff(count);
                    CompInventory.container.TryAddOrTransfer(ammo);
                }
                Job reload = TryMakeReloadJob();
                Holder.jobs.jobQueue.EnqueueFirst(reload);
                return true;
            }
        }
        return false;
    }

    public void LoadAmmo(Thing ammo = null, bool emptyMag = false)
    {
        Building_AutoloaderCE AutoLoader = null;
        if (parent is Building_AutoloaderCE)
        {
            AutoLoader = parent as Building_AutoloaderCE;
        }

        if (Holder == null && turret == null && AutoLoader == null)
        {
            Log.Error(parent.ToString() + " tried loading ammo with no owner");
            return;
        }
        int newMagCount;
        if (UseAmmo)
        {
            Thing ammoThing;
            bool ammoFromInventory = false;
            if (ammo == null)
            {
                if (!TryFindAmmoInInventory(out ammoThing))
                {
                    DoOutOfAmmoAction();
                    return;
                }
                ammoFromInventory = true;
            }
            else
            {
                ammoThing = ammo;
            }
            currentAmmoInt = (AmmoDef)ammoThing.def;

            CompApparelReloadable compReloadable = ammoThing.TryGetComp<CompApparelReloadable>();
            // If there's more ammo in inventory than the weapon can hold, or if there's greater than 1 bullet in inventory if reloading one at a time
            if ((Props.reloadOneAtATime ? 1 : MagAmmoCount) < ammoThing.stackCount)
            {
                if (Props.reloadOneAtATime)
                {
                    newMagCount = curMagCountInt + (compReloadable?.remainingCharges ?? CurAmmoCount);
                    ammoThing.stackCount--;
                }
                else
                {
                    newMagCount = MagSize;
                    ammoThing.stackCount -= MagAmmoCount;
                }
            }
            // If there's less ammo in inventory than the weapon can hold, or if there's only one bullet left if reloading one at a time
            else
            {
                int newAmmoCount = ammoThing.stackCount;
                if (!emptyMag)   //Turrets are reloaded without unloading the mag first (if using same ammo type), to support very high capacity magazines
                {
                    newAmmoCount += curMagCountInt;
                }
                newMagCount = Props.reloadOneAtATime ? curMagCountInt + (compReloadable?.remainingCharges ?? CurAmmoCount) : newAmmoCount;
                if (ammoFromInventory)
                {
                    CompInventory.container.Remove(ammoThing);
                }
                else if (!ammoThing.Destroyed)
                {
                    ammoThing.Destroy();
                }
            }
        }
        else
        {
            newMagCount = (Props.reloadOneAtATime) ? (curMagCountInt + 1) : MagSize;
        }
        CurMagCount = newMagCount;
        if (turret != null)
        {
            turret.SetReloading(false);
        }
        if (AutoLoader != null)
        {
            AutoLoader.isReloading = false;
        }
        if (parent.def.soundInteract != null)
        {
            parent.def.soundInteract.PlayOneShot(new TargetInfo(Position, Map, false));
        }
    }

    /// <summary>
    /// Resets current ammo count to a full magazine. Intended use is pawn/turret generation where we want raiders/enemy turrets to spawn with loaded magazines. DO NOT
    /// use for regular reloads, those should be handled through LoadAmmo() instead.
    /// </summary>
    /// <param name="newAmmo">Currently loaded ammo type will be set to this, null will load currently selected type.</param>
    public void ResetAmmoCount(AmmoDef newAmmo = null)
    {
        if (newAmmo != null)
        {
            currentAmmoInt = newAmmo;
            selectedAmmo = newAmmo;
        }
        CurMagCount = MagSize;
    }

    public bool TryFindAmmoInInventory(out Thing ammoThing)
    {
        ammoThing = null;
        if (CompInventory == null)
        {
            return false;
        }

        // Try finding suitable ammoThing for currently set ammo first
        ammoThing = CompInventory.ammoList.Find(thing => thing.def == selectedAmmo);
        if (ammoThing != null)
        {
            return true;
        }

        if (Props.reloadOneAtATime && CurMagCount > 0)
        {
            //Current mag already has a few rounds in, and the inventory doesn't have any more of that type.
            //If we let this method pick a new selectedAmmo below, it would convert the already loaded rounds to a different type,
            //so for OneAtATime weapons, we stop the process here here.
            return false;
        }

        // Try finding ammo from different type
        foreach (AmmoLink link in Props.ammoSet.ammoTypes)
        {
            ammoThing = CompInventory.ammoList.Find(thing => thing.def == link.ammo);
            if (ammoThing != null)
            {
                selectedAmmo = (AmmoDef)link.ammo;
                return true;
            }
        }
        return false;
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        var mannableComp = turret?.GetMannable();

        // Only show ammo status for colonists under player control (i.e. not mental breaking / slave rebelling) and colony mechs.
        // Note that we use IsColonyMech rather than IsColonyMechPlayerControlled for checking whether the pawn is a colony mech,
        // as the latter would only apply to mechs currently controlled by a mechanitor.
        var isColonyMechOrColonist = Wielder != null && (Wielder.IsColonistPlayerControlled || Wielder.IsColonyMech);
        var isPlayerControlled = isColonyMechOrColonist || (turret?.Faction == Faction.OfPlayer && (mannableComp != null || UseAmmo));

        if (isPlayerControlled)
        {
            GizmoAmmoStatus ammoStatusGizmo = new GizmoAmmoStatus { compAmmo = this };
            yield return ammoStatusGizmo;

            Action action = null;
            if (IsEquippedGun)
            {
                action = SyncedTryStartReload;
            }
            else if (mannableComp != null)
            {
                action = SyncedTryForceReload;
            }

            // Check for teaching opportunities
            string tag;
            if (turret == null)
            {
                if (HasMagazine)
                {
                    tag = "CE_Reload";    // Teach reloading weapons with magazines
                }
                else
                {
                    tag = "CE_ReloadNoMag";    // Teach about mag-less weapons
                }
            }
            else
            {
                if (mannableComp == null)
                {
                    tag = "CE_ReloadAuto";    // Teach about auto-turrets
                }
                else
                {
                    tag = "CE_ReloadManned";    // Teach about reloading manned turrets
                }
            }
            LessonAutoActivator.TeachOpportunity(ConceptDef.Named(tag), turret, OpportunityType.GoodToKnow);

            Command_Reload reloadCommandGizmo = new Command_Reload
            {
                compAmmo = this,
                action = action,
                defaultLabel = HasMagazine ? (string)"CE_ReloadLabel".Translate() : "",
                defaultDesc = "CE_ReloadDesc".Translate(),
                icon = CurrentAmmo == null ? ContentFinder<Texture2D>.Get("UI/Buttons/Reload", true) : selectedAmmo.IconTexture(),
                tutorTag = tag
            };
            yield return reloadCommandGizmo;

            // God mode gizmos for emptying and filling the magazine
            if (DebugSettings.godMode)
            {
                Command_Action devSetAmmoToMinCommandGizmo = new Command_Action
                {
                    action = delegate { CurMagCount = 0; },
                    defaultLabel = "DEV: Set ammo to 0"
                };
                yield return devSetAmmoToMinCommandGizmo;

                Command_Action devSetAmmoToMaxCommandGizmo = new Command_Action
                {
                    action = delegate { CurrentAmmo = SelectedAmmo; CurMagCount = MagSize; },
                    defaultLabel = "DEV: Set ammo to max"
                };
                yield return devSetAmmoToMaxCommandGizmo;
            }
        }
    }

    public override string TransformLabel(string label)
    {
        string ammoSet = UseAmmo && Controller.settings.ShowCaliberOnGuns ? " (" + (string)Props.ammoSet.LabelCap + ")" : "";
        return label + ammoSet;
    }

    /*
    public override string GetDescriptionPart()
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("CE_MagazineSize".Translate() + ": " + GenText.ToStringByStyle(MagSize, ToStringStyle.Integer));
        stringBuilder.AppendLine("CE_ReloadTime".Translate() + ": " + GenText.ToStringByStyle((Props.reloadTime), ToStringStyle.FloatTwo) + " s");
        if (UseAmmo)
        {
            // Append various ammo stats
            stringBuilder.AppendLine("CE_AmmoSet".Translate() + ": " + Props.ammoSet.LabelCap + "\n");
            foreach(var cur in Props.ammoSet.ammoTypes)
            {
                string label = string.IsNullOrEmpty(cur.ammo.ammoClass.LabelCapShort) ? cur.ammo.ammoClass.LabelCap : cur.ammo.ammoClass.LabelCapShort;
                stringBuilder.AppendLine(label + ":\n" + cur.projectile.GetProjectileReadout());
            }
        }
        return stringBuilder.ToString().TrimEndNewlines();
    }
    */

    #endregion Methods
}
