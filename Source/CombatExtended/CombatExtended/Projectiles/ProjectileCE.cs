﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using CombatExtended.Compatibility;
using CombatExtended.Lasers;
using ProjectileImpactFX;
using RimWorld.Planet;
using CombatExtended.Utilities;

namespace CombatExtended;
public abstract class ProjectileCE : ThingWithComps
{
    #region ClassVariables
    /// <summary>
    /// Suppression is applied within this radius (x-y and z)
    /// </summary>
    protected const int SuppressionRadius = 3;

    /// <summary>
    /// Check for collision with multi-cell pawns and apply suppression in radius of this size, centered on flight path.
    /// </summary>
    protected const int collisionCheckSize = 5;

    #region Kinetic Projectiles
    public float ballisticCoefficient;
    public float mass;
    public float radius;
    public double gravity;
    public float GravityPerWidth;
    public float GravityPerHeight;
    public int fuelTicks;
    public Vector3 velocity;
    public float initialSpeed;

    // Deprecated: Remove in 1.6
    public bool lerpPosition = true;
    #endregion

    #region Drawing
    public int ticksToTruePosition;
    #endregion

    #region Origin destination
    public bool OffMapOrigin = false;

    public Vector2 origin;

    public IntVec3 OriginIV3;

    /// <summary>
    /// Calculates the destination (zero height) reached with a projectile of speed <i>shotSpeed</i> fired at <i>shotAngle</i> from height <i>shotHeight</i> starting from <i>origin</i>. Does not take into account air resistance.
    /// </summary>
    public Vector2 Destination;
    #endregion

    /// <summary>
    /// Determine whether the pawn that fired this projectile (if it was a pawn)
    /// should be considered guilty if this projectile hits a friendly target.
    /// </summary>
    /// <remarks>
    /// This effectively aims to prevent people drafting pawns and ordering them to attack friendly targets to cheese guilt.
    /// </remarks>
    protected bool InstigatorGuilty => !(launcher is Pawn launcherPawn && launcherPawn.Drafted);

    public Thing intendedTargetThing
    {
        get
        {
            return intendedTarget.Thing;
        }
    }

    /// <summary>
    /// Backing field for <see cref="DamageAmount"/>.
    /// </summary>
    protected float? damageAmount;

    /// <summary>
    /// Return the damage dealt by this projectile scaled by the quality multiplier of its launcher.
    /// </summary>
    public virtual float DamageAmount
    {
        get
        {
            if (this.damageAmount == null)
            {
                // Apply a multiplier to bullet damage based on the quality of the weapon that fired it
                var weaponDamageMultiplier = equipment?.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier) ?? 1f;
                this.damageAmount = def.projectile.GetDamageAmount(weaponDamageMultiplier, null);
            }

            return ((float)this.damageAmount) * RemainingKineticEnergyPct;
        }
    }
    public virtual float PenetrationAmount
    {
        get
        {
            var projectilePropsCE = (ProjectilePropertiesCE)def.projectile;
            var isSharpDmg = def.projectile.damageDef.armorCategory == DamageArmorCategoryDefOf.Sharp;

            float penetrationAmount = (equipment?.GetStatValue(StatDefOf.RangedWeapon_DamageMultiplier) ?? 1f) * (isSharpDmg ? projectilePropsCE.armorPenetrationSharp : projectilePropsCE.armorPenetrationBlunt);

            float speedFactor = isSharpDmg ? RemainingSpeedPct : RemainingKineticEnergyPct;
            return penetrationAmount * speedFactor;
        }
    }
    public virtual DamageInfo DamageInfo => new DamageInfo(
                def.projectile.damageDef,
                DamageAmount,
                PenetrationAmount, //Armor Penetration
                ExactRotation.eulerAngles.y,
                launcher,
                null,
                def,
                instigatorGuilty: InstigatorGuilty);

    public float RemainingKineticEnergyPct => TrajectoryWorker is BallisticsTrajectoryWorker ? (shotSpeed * shotSpeed) / (initialSpeed * initialSpeed) : 1f;
    public float RemainingSpeedPct => TrajectoryWorker is BallisticsTrajectoryWorker ? (shotSpeed) / (initialSpeed) : 1f;

    /// <summary>
    /// Reference to the weapon that fired this projectile, may be null.
    /// </summary>
    public Thing equipment;

    public ThingDef equipmentDef;
    public Thing launcher;
    public LocalTargetInfo intendedTarget;
    public float minCollisionDistance;
    public bool canTargetSelf;
    public bool castShadow = true;
    public bool logMisses = true;
    protected bool ignoreRoof;

    public GlobalTargetInfo globalTargetInfo = GlobalTargetInfo.Invalid;
    public GlobalTargetInfo globalSourceInfo = GlobalTargetInfo.Invalid;

    #region Vanilla
    public bool landed;
    // TODO: // Remove in 1.5
    public int intTicksToImpact;
    public int ticksToImpact
    {
        get
        {
            return intTicksToImpact;
        }
        set
        {
            intTicksToImpact = value;
        }
    }


    protected Sustainer ambientSustainer;

    public virtual bool AnimalsFleeImpact => false;

    #endregion

    protected float suppressionAmount;
    public Thing mount; // GiddyUp compatibility, ignore collisions with pawns the launcher is mounting
    public float AccuracyFactor;

    #region Height
    public virtual float Height
    {
        get
        {
            return ExactPosition.y;
        }
    }
    #endregion

    #region Ticks/Seconds
    public float startingTicksToImpact;

    public int FlightTicks = 0;

    #endregion

    #region Position
    private Vector3 exactPosition;

    public virtual Vector2 Vec2Position()
    {
        Log.ErrorOnce($"{this}.Vec2Position() is deprecated and will be removed in 1.6", 50021 + def.projectile.GetHashCode());
        return new Vector2(ExactPosition.x, ExactPosition.z);
    }

    /// <summary>
    /// Exact x,y,z (x,height,y) position in terms of Vec2Position.x, .y (lerped origin to Destination) and Height.
    /// </summary>
    public virtual Vector3 ExactPosition
    {
        set
        {
            exactPosition = value;
            if (Map == null || value.InBounds(Map))
            {
                Position = ((Vector3)exactPosition).ToIntVec3();
            }
        }
        get
        {
            return exactPosition;
        }
    }

    public Vector3 ExactMinusLastPos
    {
        get
        {
            Log.ErrorOnce("ExactMinusLastPos is deprecated and will be removed in 1.5", 50022);
            return (ExactPosition - LastPos);
        }
    }


    public override Vector3 DrawPos
    {
        get
        {
            return TrajectoryWorker.ExactPosToDrawPos(ExactPosition, FlightTicks, ticksToTruePosition, def.Altitude);
        }
    }

    public Vector3 LastPos;
    protected DangerTracker _dangerTracker = null;
    protected DangerTracker DangerTracker
    {
        get
        {
            return _dangerTracker ?? (_dangerTracker = Map.GetDangerTracker());
        }
    }

    private int lastShotLine = -1;
    private Ray shotLine;
    public Ray ShotLine
    {
        get
        {
            if (lastShotLine != FlightTicks)
            {
                shotLine = new Ray(LastPos, ExactPosition - LastPos);
                lastShotLine = FlightTicks;
            }
            return shotLine;
        }
    }
    #endregion

    #region Angle
    /// <summary>
    /// Based on equations of motion
    /// </summary>
    private Quaternion? _drawRotation = null;
    public virtual Quaternion DrawRotation
    {
        get
        {
            if (_drawRotation != null)
            {
                return (Quaternion)_drawRotation;
            }
            Vector2 w = (Destination - origin);

            var vx = w.x / startingTicksToImpact;

            var vy = (w.y - shotHeight) / startingTicksToImpact
                     + shotSpeed * Mathf.Sin(shotAngle) / GenTicks.TicksPerRealSecond
                     - (GravityPerWidth * FlightTicks) / (GenTicks.TicksPerRealSecond * GenTicks.TicksPerRealSecond);

            return Quaternion.AngleAxis(
                       Mathf.Rad2Deg * Mathf.Atan2(-vy, vx) + 90f
                       , Vector3.up);
        }
    }

    public virtual Quaternion ExactRotation
    {
        get
        {
            return Quaternion.AngleAxis(shotRotation, Vector3.down);
        }
    }
    #endregion

    /// <summary>
    /// Angle off the ground [radians].
    /// </summary>
    public float shotAngle = 0f;
    /// <summary>
    /// Angle rotation between shooter and positive y-vector [degrees]. North: 0f, East: 90f, South: 180f, West: 270f.
    /// </summary>
    public float shotRotation = 0f;
    /// <summary>
    /// Shot height in vertical cells. Humans start their shot at 0.85f [vcells].
    /// </summary>
    public float shotHeight = 0f;
    /// <summary>
    /// The assigned shot speed [cells/s] (not speed in y axis or x-z plane), in general equal to the projectile.def.speed value.
    /// </summary>
    public float shotSpeed = -1f;

    protected Material[] shadowMaterial;
    protected Material ShadowMaterial
    {
        get
        {
            if (shadowMaterial == null)
            {
                //Get fully black version of this.Graphic
                if (Graphic is Graphic_Collection g)
                {
                    shadowMaterial = GetShadowMaterial(g);
                }
                else
                {
                    shadowMaterial = new Material[1];
                    shadowMaterial[0] = Graphic.GetColoredVersion(ShaderDatabase.Transparent, Color.black, Color.black).MatSingle;
                }
            }

            return shadowMaterial[Rand.Range(0, this.shadowMaterial.Length)];
        }
    }
    #endregion

    /*
     * *** End of class variables ***
    */

    #region Methods

    #region Expose
    /// <summary>
    /// Saves new variables shotAngle, shotHeight, shotSpeed.
    /// </summary>
    public override void ExposeData()
    {
        base.ExposeData();

        if (Scribe.mode == LoadSaveMode.Saving && launcher != null && launcher.Destroyed)
        {
            launcher = null;
        }
        CE_Scriber.Late(this, (id) =>
        {
            Scribe_References.Look<Thing>(ref launcher, "launcher_" + id);
        });
        Scribe_TargetInfo.Look(ref globalSourceInfo, "globalSourceInfo");
        Scribe_TargetInfo.Look(ref globalTargetInfo, "globalTargetInfo");
        Scribe_TargetInfo.Look(ref intendedTarget, "intendedTarget");

        Scribe_Values.Look<Vector2>(ref origin, "origin", default(Vector2), true);
        Scribe_References.Look<Thing>(ref launcher, "launcher");
        Scribe_References.Look<Thing>(ref equipment, "equipment");
        Scribe_Values.Look<int>(ref intTicksToImpact, "ticksToImpact", 0, true);
        Scribe_Values.Look<float>(ref startingTicksToImpact, "startingTicksToImpact", 0, true);
        Scribe_Defs.Look<ThingDef>(ref equipmentDef, "equipmentDef");
        Scribe_Values.Look<bool>(ref landed, "landed");
        //Here be new variables
        Scribe_Values.Look(ref shotAngle, "shotAngle", 0f, true);
        Scribe_Values.Look(ref shotRotation, "shotRotation", 0f, true);
        Scribe_Values.Look(ref shotHeight, "shotHeight", 0f, true);
        Scribe_Values.Look(ref shotSpeed, "shotSpeed", 0f, true);
        Scribe_Values.Look<bool>(ref canTargetSelf, "canTargetSelf");
        Scribe_Values.Look<bool>(ref logMisses, "logMisses", true);
        Scribe_Values.Look<bool>(ref castShadow, "castShadow", true);
        Scribe_Values.Look(ref ignoreRoof, "ignoreRoof", true);
        Scribe_Values.Look(ref ticksToTruePosition, "ticksToTruePosition");

        Scribe_Values.Look(ref ballisticCoefficient, "ballisticCoefficient");
        Scribe_Values.Look(ref mass, "mass");
        Scribe_Values.Look(ref radius, "radius");
        Scribe_Values.Look(ref gravity, "gravity");
        Scribe_Values.Look(ref fuelTicks, "fuelTicks");
        Scribe_Values.Look(ref velocity, "velocity");
        Scribe_Values.Look(ref initialSpeed, "initialSpeed");


        //To fix landed grenades sl problem
        Scribe_Values.Look(ref exactPosition, "exactPosition");
        // Scribe_Values.Look(ref GravityPerWidth, "gravityFactor", CE_Utility.GravityConst / CE_Utility.MetersPerCellWidth);
        Scribe_Values.Look(ref LastPos, "lastPosition");
        Scribe_Values.Look(ref FlightTicks, "flightTicks");
        Scribe_Values.Look(ref OriginIV3, "originIV3", new IntVec3(this.origin));
        Scribe_Values.Look(ref Destination, "destination", this.origin + Vector2.up.RotatedBy(shotRotation) * DistanceTraveled);
        // To insure saves don't get affected..
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {

            GravityPerHeight = this.Props.GravityPerHeight;
            GravityPerWidth = this.Props.GravityPerWidth;
        }
    }
    #endregion

    #region Throw
    public virtual void Throw(Thing launcher, Vector3 origin, Vector3 heading, Thing equipment = null)
    {
        this.ExactPosition = LastPos = origin;
        this.shotHeight = origin.y;
        this.origin = new Vector2(origin.x, origin.z);
        this.OriginIV3 = new IntVec3(this.origin);
        this.Destination = this.origin + Vector2.up.RotatedBy(shotRotation) * DistanceTraveled;
        this.shotSpeed = Math.Max(heading.magnitude, def.projectile.speed);
        var projectileProperties = def.projectile as ProjectilePropertiesCE;
        this.castShadow = projectileProperties.castShadow;
        this.velocity = heading;
        this.launcher = launcher;
        this.equipment = equipment;
        //For explosives/bullets, equipmentDef is important
        equipmentDef = (equipment != null) ? equipment.def : null;

        if (!def.projectile.soundAmbient.NullOrUndefined())
        {
            var info = SoundInfo.InMap(this, MaintenanceType.PerTick);
            ambientSustainer = def.projectile.soundAmbient.TrySpawnSustainer(info);
        }
        ballisticCoefficient = projectileProperties.ballisticCoefficient.RandomInRange;
        mass = projectileProperties.mass.RandomInRange;
        radius = projectileProperties.diameter.RandomInRange / 2000; // half the diameter and mm -> m
        gravity = projectileProperties.Gravity;
        GravityPerWidth = projectileProperties.GravityPerWidth;
        GravityPerHeight = projectileProperties.GravityPerHeight;
        fuelTicks = projectileProperties.fuelTicks;
        initialSpeed = shotSpeed;
        var worker = (def.projectile as ProjectilePropertiesCE).TrajectoryWorker;
        if (worker is BallisticsTrajectoryWorker)
        {
            forcedTrajectoryWorker = worker;
        }
        else
        {
            Log.ErrorOnce($"Tried setting a non-Ballistic TrajectoryWorker ({worker}) on a projectile being Thrown. Falling back to default.", 50019);
            forcedTrajectoryWorker = ProjectilePropertiesCE.defaultBallisticTrajectoryWorker;
        }
    }
    #endregion

    #region Raycast
    public virtual void RayCast(Thing launcher, VerbProperties verbProps, Vector2 origin, float shotAngle, float shotRotation, float shotHeight = 0f, float shotSpeed = -1f, float spreadDegrees = 0f, float aperatureSize = 0.03f, Thing equipment = null)
    {

        float magicSpreadFactor = Mathf.Sin(0.06f / 2 * Mathf.Deg2Rad) + aperatureSize;
        float magicLaserDamageConstant = 1 / (magicSpreadFactor * magicSpreadFactor * 3.14159f);

        ProjectilePropertiesCE pprops = def.projectile as ProjectilePropertiesCE;
        shotRotation = Mathf.Deg2Rad * shotRotation + (float)(3.14159 / 2.0f);
        Vector3 direction = new Vector3(Mathf.Cos(shotRotation) * Mathf.Cos(shotAngle), Mathf.Sin(shotAngle), Mathf.Sin(shotRotation) * Mathf.Cos(shotAngle));
        Vector3 origin3 = new Vector3(origin.x, shotHeight, origin.y);
        Map map = launcher.Map;
        Vector3 destination = direction * verbProps.range + origin3;
        this.shotAngle = shotAngle;
        this.shotHeight = shotHeight;
        this.shotRotation = shotRotation;
        this.launcher = launcher;
        this.origin = origin;
        this.equipment = equipment;
        equipmentDef = equipment?.def ?? null;
        Ray ray = new Ray(origin3, direction);
        var lbce = this as LaserBeamCE;
        float spreadRadius = Mathf.Sin(spreadDegrees / 2.0f * Mathf.Deg2Rad);

        LaserGunDef defWeapon = equipmentDef as LaserGunDef;
        Vector3 muzzle = ray.GetPoint((defWeapon == null ? 0.9f : defWeapon.barrelLength));
        var it_bounds = CE_Utility.GetBoundsFor(intendedTargetThing);
        for (int i = 1; i < verbProps.range; i++)
        {
            float spreadArea = (i * spreadRadius + aperatureSize) * (i * spreadRadius + aperatureSize) * 3.14159f;
            if (pprops.damageFalloff)
            {
                lbce.DamageModifier = 1 / (magicLaserDamageConstant * spreadArea);
            }

            Vector3 tp = ray.GetPoint(i);
            if (tp.y < 0)
            {
                destination = tp;
                landed = true;
                ExactPosition = tp;
                Position = ExactPosition.ToIntVec3();
                break;
            }
            var iv3 = tp.ToIntVec3();
            if (!iv3.InBounds(map))
            {
                tp = ray.GetPoint(i - 1);
                ExactPosition = tp;
                destination = tp;
                landed = true;
                LastPos = destination;
                Position = ExactPosition.ToIntVec3();

                lbce.SpawnBeam(muzzle, destination);
                RayCastSuppression(muzzle.ToIntVec3(), destination.ToIntVec3());
                lbce.Impact(null, muzzle);
                return;

            }
            foreach (Thing thing in Map.thingGrid.ThingsListAtFast(iv3))
            {
                if (this == thing)
                {
                    continue;
                }
                var bounds = CE_Utility.GetBoundsFor(thing);
                if (!bounds.IntersectRay(ray, out var dist))
                {
                    continue;
                }
                if (i < 2 && thing != intendedTargetThing)
                {
                    continue;
                }

                if (thing is Plant plant)
                {
                    if (!Rand.Chance(thing.def.fillPercent * plant.Growth))
                    {
                        continue;
                    }
                }
                else if (thing is Building)
                {
                    if (!Rand.Chance(thing.def.fillPercent))
                    {
                        continue;
                    }
                }
                ExactPosition = tp;
                destination = tp;
                landed = true;
                LastPos = destination;
                Position = ExactPosition.ToIntVec3();

                lbce.SpawnBeam(muzzle, destination);
                RayCastSuppression(muzzle.ToIntVec3(), destination.ToIntVec3());

                lbce.Impact(thing, muzzle);

                return;

            }

        }
        if (lbce != null)
        {
            lbce.SpawnBeam(muzzle, destination);
            RayCastSuppression(muzzle.ToIntVec3(), destination.ToIntVec3());
            Destroy(DestroyMode.Vanish);
            return;
        }
    }

    protected void RayCastSuppression(IntVec3 muzzle, IntVec3 destination, Map map = null)
    {
        if (muzzle == destination)
        {
            return;
        }

        var projectilePropsCE = def.projectile as ProjectilePropertiesCE;
        if (projectilePropsCE.suppressionFactor <= 0f ||
            projectilePropsCE.airborneSuppressionFactor <= 0f)
        {
            return;
        }

        map ??= base.Map;
        foreach (Pawn pawn in muzzle.PawnsNearSegment(destination, map, SuppressionRadius, false, true).Except(muzzle.PawnsInRange(map, SuppressionRadius)))
        {
            ApplySuppression(pawn);
        }
    }
    #endregion

    #region Launch
    /// <summary>
    /// Physics-enabled Launch() method.
    /// </summary>
    /// <param name="launcher">The Thing that launched this projectile.</param>
    /// <param name="origin">The origin of the projectile (different from the launcher for e.g grenade fragments)</param>
    /// <param name="shotAngle">Angle off the ground [radians].</param>
    /// <param name="shotRotation">Rotation between shooter and destination [degrees].</param>
    /// <param name="shotHeight">The shot height, usually the max height of the non-pawn caster, a portion of the height of the pawn caster OR zero. (default: 0)</param>
    /// <param name="shotSpeed">The shot speed (default: def.projectile.speed)</param>
    /// <param name="equipment">The equipment used to fire the projectile.</param>
    /// <param name="distance">The distance to the estimated intercept point</param>
    /// <remarks>
    /// Note that the launcher may not be spawned at all, e.g. for projectiles launched by enemy bases as retaliation.
    /// </remarks>
    public virtual void Launch(Thing launcher, Vector2 origin, float shotAngle, float shotRotation, float shotHeight = 0f, float shotSpeed = -1f, Thing equipment = null, float distance = -1)
    {
        this.shotAngle = shotAngle;
        this.shotHeight = shotHeight;
        this.shotRotation = shotRotation;
        this.shotSpeed = Math.Max(shotSpeed, def.projectile.speed);
        this.ticksToTruePosition = (def.projectile as ProjectilePropertiesCE).TickToTruePos;
        if (def.projectile is ProjectilePropertiesCE props)
        {
            this.castShadow = props.castShadow;
            ballisticCoefficient = props.ballisticCoefficient.RandomInRange;
            mass = props.mass.RandomInRange;
            radius = props.diameter.RandomInRange / 2000; // half the diameter and mm -> m
            gravity = props.Gravity;
            GravityPerWidth = props.GravityPerWidth;
            GravityPerHeight = props.GravityPerHeight;
            fuelTicks = props.fuelTicks;
        }
        if (shotHeight >= CollisionVertical.WallCollisionHeight && launcher.Spawned && Position.Roofed(launcher.Map))
        {
            ignoreRoof = true;
        }
        Launch(launcher, origin, equipment);
    }

    public virtual void Launch(Thing launcher, Vector2 origin, Thing equipment = null)
    {
        this.launcher = launcher;
        this.origin = origin;
        this.OriginIV3 = new IntVec3(origin);
        this.Destination = origin + Vector2.up.RotatedBy(shotRotation) * DistanceTraveled;
        velocity = TrajectoryWorker.GetInitialVelocity(shotSpeed, shotRotation, shotAngle);
        initialSpeed = shotSpeed = velocity.magnitude * GenTicks.TicksPerRealSecond;
        this.equipment = equipment;
        //For explosives/bullets, equipmentDef is important
        equipmentDef = (equipment != null) ? equipment.def : null;

        if (Map != null && !def.projectile.soundAmbient.NullOrUndefined())
        {
            var info = SoundInfo.InMap(this, MaintenanceType.PerTick);
            ambientSustainer = def.projectile.soundAmbient.TrySpawnSustainer(info);
        }
        this.startingTicksToImpact = TrajectoryWorker.GetFlightTime(shotAngle, shotSpeed, GravityPerWidth, shotHeight) * GenTicks.TicksPerRealSecond;
        this.ticksToImpact = Mathf.CeilToInt(this.startingTicksToImpact);
        this.ExactPosition = this.LastPos = new Vector3(origin.x, shotHeight, origin.y);

    }
    #endregion

    #region Collisions
    public virtual void InterceptProjectile(object interceptor, Vector3 impactPosition, bool destroyCompletely = false)
    {
        ExactPosition = impactPosition;
        landed = true;
        ticksToImpact = 0;
        if (destroyCompletely)
        {
            this.Destroy(DestroyMode.Vanish);
        }
        else
        {
            this.Impact(null);
        }
    }
    public virtual void InterceptProjectile(object interceptor, Vector3 shieldPosition, float shieldRadius, bool destroyCompletely = false)
    {
        InterceptProjectile(interceptor, BlockerRegistry.GetExactPosition(OriginIV3.ToVector3(), ExactPosition, shieldPosition, shieldRadius * shieldRadius));
    }

    protected bool CheckIntercept(Thing interceptorThing, CompProjectileInterceptor interceptorComp, bool withDebug = false)
    {
        Vector3 shieldPosition = interceptorThing.Position.ToVector3ShiftedWithAltitude(0.5f);
        float radius = interceptorComp.Props.radius;
        float blockRadius = radius + def.projectile.SpeedTilesPerTick + 0.1f;
        float radiusSq = blockRadius * blockRadius;

        var newExactPos = ExactPosition;

        if ((newExactPos - shieldPosition).sqrMagnitude > radiusSq)
        {
            return false;
        }
        if (!interceptorComp.Active)
        {
            return false;
        }

        if (interceptorComp.Props.interceptGroundProjectiles && def.projectile.flyOverhead)
        {
            return false;
        }

        if (interceptorComp.Props.interceptAirProjectiles && !def.projectile.flyOverhead)
        {
            return false;
        }

        if ((launcher == null || !launcher.HostileTo(interceptorThing)) && !interceptorComp.debugInterceptNonHostileProjectiles && !interceptorComp.Props.interceptNonHostileProjectiles)
        {
            return false;
        }
        if (!interceptorComp.Props.interceptOutgoingProjectiles && (shieldPosition - LastPos).sqrMagnitude <= radius * radius)
        {
            return false;
        }
        if (CE_Utility.IntersectionPoint(
                LastPos,
                newExactPos,
                shieldPosition,
                radius,
                out Vector3[] sect,
                // Don't normalize away the 3D component of the projectile position when checking for collisions
                // between indirect fire projectiles and shields that protect against them
                // (e.g. mortar shells targeting a high-shield).
                spherical: interceptorComp.Props.interceptAirProjectiles && def.projectile.flyOverhead
        ))
        {
            ExactPosition = newExactPos = sect.OrderBy(x => (OriginIV3.ToVector3() - x).sqrMagnitude).First();
            landed = true;
        }
        else
        {
            return false;
        }
        interceptorComp.lastInterceptAngle = LastPos.AngleToFlat(interceptorThing.TrueCenter());
        interceptorComp.lastInterceptTicks = Find.TickManager.TicksGame;

        var projectileProperties = def.projectile as ProjectilePropertiesCE;
        var areWeLucky = Rand.Chance(projectileProperties?.empShieldBreakChance ?? 0);
        if (areWeLucky && interceptorComp.Props.disarmedByEmpForTicks > 0)
        {
            // If the chance check for this EMP projectile succeeds, break the shield using the appropriate damage type
            // (primary if the primary damage is EMP itself and secondary if EMP damage is only a secondary effect.)
            // Note that empShieldBreakChance defaults to 1 even for non-EMP projectiles, so a non-EMP projectile
            // may still technically pass the chance check.
            var empDamageDef = def.projectile.damageDef == DamageDefOf.EMP
                               ? def.projectile.damageDef
                               : projectileProperties?.secondaryDamage?.Select(sd => sd.def).FirstOrDefault(sdDef => sdDef == DamageDefOf.EMP);

            if (empDamageDef != null)
            {
                interceptorComp.BreakShieldEmp(new DamageInfo(empDamageDef, empDamageDef.defaultDamage));

                // Ensure we reset hit points for Biotech's new shields if broken by EMP
                interceptorComp.currentHitPoints = 0;
                interceptorComp.startedChargingTick = Find.TickManager.TicksGame;
            }
        }

        // Handle Biotech's new shields used e.g. on the Centurion mech, which, unlike mech cluster shields, can only take
        // a finite amount of damage before breaking.
        // This simply mirrors the corresponding vanilla logic - we apply the incoming damage from our projectile to the shield
        // and break it if we manage to decrease its hitpoints to zero or lower.
        if (interceptorComp.currentHitPoints > 0)
        {
            interceptorComp.currentHitPoints -= Mathf.FloorToInt(this.DamageAmount);

            if (interceptorComp.currentHitPoints <= 0)
            {
                interceptorComp.currentHitPoints = 0;
                interceptorComp.startedChargingTick = Find.TickManager.TicksGame;
                interceptorComp.BreakShieldHitpoints(new DamageInfo(projectileProperties.damageDef, this.DamageAmount));
                return true;
            }
        }

        Effecter eff = new Effecter(EffecterDefOf.Interceptor_BlockedProjectile);
        eff.Trigger(new TargetInfo(newExactPos.ToIntVec3(), interceptorThing.Map, false), TargetInfo.Invalid);
        eff.Cleanup();
        return true;
    }

    //Removed minimum collision distance
    protected virtual bool CheckForCollisionBetween()
    {
        bool collided = false;
        Map localMap = this.Map; // Saving the map in case CheckCellForCollision->...->Impact destroys the projectile, thus setting this.Map to null
        var lastPosIV3 = LastPos.ToIntVec3();
        var newPosIV3 = ExactPosition.ToIntVec3();

        List<Thing> list = base.Map.listerThings.ThingsInGroup(ThingRequestGroup.ProjectileInterceptor);
        for (int i = 0; i < list.Count; i++)
        {
            if (!CheckIntercept(list[i], list[i].TryGetComp<CompProjectileInterceptor>()))
            {
                continue;
            }

            // Have high-shields absorb intercepted indirect fire projectiles without detonating them.
            // Since CE simulates a 3D trajectory for such shells but explosions occur on ground level,
            // detonating the shell would cause damage to objects under the high-shield.
            if (def.projectile.flyOverhead)
            {
                this.Destroy();
                return true;
            }

            landed = true;
            this.Impact(null);
            return true;
        }
        if (BlockerRegistry.CheckForCollisionBetweenCallback(this, LastPos, ExactPosition))
        {
            return true;
        }

        #region Sanity checks
        if (ticksToImpact < 0 || def.projectile.flyOverhead)
        {
            return false;
        }

        if (!lastPosIV3.InBounds(Map) || !newPosIV3.InBounds(Map))
        {
            return false;
        }

        if (Controller.settings.DebugDrawInterceptChecks)
        {
            Map.debugDrawer.FlashLine(lastPosIV3, newPosIV3);
        }
        #endregion

        // Iterate through all cells between the last and the new position
        // INCLUDING[!!!] THE LAST AND NEW POSITIONS!
        var cells = GenSight.PointsOnLineOfSight(lastPosIV3, newPosIV3).Union(new[] { lastPosIV3, newPosIV3 }).Distinct().OrderBy(x => (x.ToVector3Shifted() - LastPos).MagnitudeHorizontalSquared());

        //Order cells by distance from the last position
        foreach (var cell in cells)
        {
            if (CheckCellForCollision(cell))
            {
                newPosIV3 = cell;
                collided = true;
                break;
            }

            if (Controller.settings.DebugDrawInterceptChecks)
            {
                Map.debugDrawer.FlashCell(cell, 1, "o");
            }
        }

        // Apply suppression. The height here is NOT that of the bullet in CELL,
        // it is the height at the END OF THE PATH. This is because SuppressionRadius
        // is not considered an EXACT limit.
        if (ExactPosition.y <= SuppressionRadius)
        {
            RayCastSuppression(lastPosIV3, newPosIV3, localMap);
        }

        return collided;
    }

    /// <summary>
    /// Cache field holding things that a projectile might collide with.
    /// </summary>
    private static readonly List<Thing> potentialCollisionCandidates = new List<Thing>();

    /// <summary>
    /// Checks whether a collision occurs along flight path within this cell.
    /// </summary>
    /// <param name="cell">Where to check for collisions in</param>
    /// <returns>True if collision occured, false otherwise</returns>
    protected bool CheckCellForCollision(IntVec3 cell)
    {
        if (BlockerRegistry.CheckCellForCollisionCallback(this, cell, launcher))
        {
            return true;
        }
        var roofChecked = false;

        potentialCollisionCandidates.Clear();

        foreach (var thing in Map.thingGrid.ThingsListAtFast(cell))
        {
            if (thing is Pawn || thing.def.Fillage != FillCategory.None)
            {
                potentialCollisionCandidates.AddDistinct(thing);
            }
        }

        //Find pawns in adjacent cells and append them to main list
        var rot4 = Rot4.FromAngleFlat(shotRotation);
        if (rot4.rotInt > 1)
        {
            //For some reason south and west returns incorrect adjacent cells collection
            rot4 = rot4.Opposite;
        }

        if (Controller.settings.DebugDrawInterceptChecks)
        {
            Map.debugDrawer.debugCells.Clear();
            Map.debugDrawer.DebugDrawerUpdate();
        }
        //Iterate through adjacent cells and find all the pawns
        foreach (var curCell in GenAdj.CellsAdjacentCardinal(cell, rot4, new IntVec2(collisionCheckSize, 0)))
        {
            if (curCell == cell || !curCell.InBounds(Map))
            {
                continue;
            }

            foreach (var thing in Map.thingGrid.ThingsListAtFast(curCell))
            {
                if (thing is Pawn)
                {
                    potentialCollisionCandidates.AddDistinct(thing);
                }
            }

            if (Controller.settings.DebugDrawInterceptChecks)
            {
                Map.debugDrawer.FlashCell(curCell, 0.7f);
            }
        }

        //If the last position is above the wallCollisionHeight, we should check for roof intersections first
        if (LastPos.y > CollisionVertical.WallCollisionHeight)
        {
            if (TryCollideWithRoof(cell))
            {
                return true;
            }
            roofChecked = true;
        }

        potentialCollisionCandidates.SortBy(thing => (thing.DrawPos - LastPos).sqrMagnitude);

        foreach (var thing in potentialCollisionCandidates)
        {
            if (thing is ProjectileCE)
            {
                continue;
            }

            if ((thing == launcher || thing == mount) && !canTargetSelf)
            {
                continue;
            }

            // Check for collision
            if (thing == intendedTargetThing || def.projectile.alwaysFreeIntercept || thing.Position.DistanceTo(OriginIV3) >= minCollisionDistance)
            {
                if (!CanCollideWith(thing, out _))
                {
                    continue;
                }
                if (BlockerRegistry.CheckForCollisionBetweenCallback(this, LastPos, thing.TrueCenter()))
                {
                    return true;
                }
                var lastPosIV3 = LastPos.ToIntVec3();
                var newPosIV3 = thing.TrueCenter().ToIntVec3();
                // Iterate through all cells between the last and the THING
                // INCLUDING[!!!] THE LAST AND NEW POSITIONS!
                var cells = GenSight.PointsOnLineOfSight(lastPosIV3, newPosIV3).Union(new[] { lastPosIV3, newPosIV3 }).Distinct().OrderBy(x => (x.ToVector3Shifted() - LastPos).MagnitudeHorizontalSquared());
                foreach (var _cell in cells)
                {
                    bool colided = false;
                    colided = BlockerRegistry.CheckCellForCollisionCallback(this, _cell, launcher);
                    if (Controller.settings.DebugDrawInterceptChecks && Map != null)
                    {
                        Map.debugDrawer.FlashCell(_cell, 1, "a");
                    }
                    if (colided)
                    {
                        return true;
                    }
                }
                if (TryCollideWith(thing))
                {
                    return true;
                }
            }
        }

        //Finally check for intersecting with a roof (again).
        if (!roofChecked && TryCollideWithRoof(cell))
        {
            return true;
        }
        return false;
    }

    protected virtual bool TryCollideWithRoof(IntVec3 cell)
    {
        if (!cell.Roofed(Map) || ignoreRoof)
        {
            return false;
        }

        var bounds = CE_Utility.GetBoundsFor(cell, cell.GetRoof(Map));

        float dist;
        if (!bounds.IntersectRay(ShotLine, out dist))
        {
            return false;
        }
        if (dist * dist > (ExactPosition - LastPos).sqrMagnitude)
        {
            return false;
        }

        var point = ShotLine.GetPoint(dist);
        ExactPosition = point;
        landed = true;

        if (Controller.settings.DebugDrawInterceptChecks)
        {
            MoteMakerCE.ThrowText(cell.ToVector3Shifted(), Map, "x", Color.red);
        }
        Impact(null);
        return true;
    }
    protected virtual bool CanCollideWith(Thing thing, out float dist)
    {
        dist = -1f;
        if (globalTargetInfo.IsValid)
        {
            return false;
        }
        if (thing == launcher && !canTargetSelf)
        {
            return false;
        }

        var bounds = CE_Utility.GetBoundsFor(thing);
        if (bounds.Contains(LastPos) || bounds.Contains(ExactPosition))
        {
            dist = 0f;
            return true;
        }
        if (!bounds.IntersectRay(ShotLine, out dist))
        {
            return false;
        }
        if (dist * dist > (ExactPosition - LastPos).sqrMagnitude)
        {
            return false;
        }
        return true;
    }
    /// <summary>
    /// Tries to impact the thing based on whether it intersects the given flight path. Trees have RNG chance to not collide even on intersection.
    /// </summary>
    /// <param name="thing">What to impact</param>
    /// <returns>True if impact occured, false otherwise</returns>
    protected bool TryCollideWith(Thing thing)
    {

        if (!CanCollideWith(thing, out var dist))
        {
            return false;
        }
        // Trees and bushes have RNG chance to collide
        if (thing is Plant plant)
        {
            if (plant.positionInt == LastPos.ToIntVec3())
            {
                return false;
            }
            //Prevents trees near the shooter (e.g the shooter's cover) to be hit
            var accuracyFactor = def.projectile.alwaysFreeIntercept ? 1 : (thing.Position - OriginIV3).LengthHorizontal / 40 * AccuracyFactor;
            var chance = thing.def.fillPercent * accuracyFactor;
            if (Controller.settings.DebugShowTreeCollisionChance)
            {
                MoteMakerCE.ThrowText(thing.Position.ToVector3Shifted(), thing.Map, chance.ToString());
            }
            if (!Rand.ChanceSeeded(chance, thing.HashOffsetTicks()))
            {
                return false;
            }
        }

        var point = ShotLine.GetPoint(dist);
        if (!point.InBounds(Map))
        {
            if (OffMapOrigin)
            {
                landed = true;
                Destroy(DestroyMode.Vanish);
                return true;
            }
            else
            {
                Log.Error("TryCollideWith out of bounds point from ShotLine: obj " + thing.ThingID + ", proj " + ThingID + ", dist " + dist + ", point " + point);
            }
        }

        if (BlockerRegistry.BeforeCollideWithCallback(this, thing))
        {
            return true;
        }
        ExactPosition = point;
        landed = true;
        if (Controller.settings.DebugDrawInterceptChecks)
        {
            MoteMakerCE.ThrowText(thing.Position.ToVector3Shifted(), thing.Map, "x", Color.red);
        }
        Impact(thing);
        return true;
    }
    #endregion

    /// <summary>
    /// Applies suppression based off of damage and suppression multiplier to pawns which don't have a shield belt or one is broken;
    /// </summary>
    /// <param name="pawn">Which pawn to suppress</param>
    /// <param name="suppressionMultiplier">How much to multiply the projectile's damage by before using it as suppression</param>
    protected void ApplySuppression(Pawn pawn, float suppressionMultiplier = 1f)
    {
        if (pawn == null)
        {
            return;
        }
        var propsCE = def.projectile as ProjectilePropertiesCE;

        if (propsCE.suppressionFactor <= 0f || (!landed && propsCE.airborneSuppressionFactor <= 0f))
        {
            return;
        }

        CompShield shield = pawn.TryGetComp<CompShield>();
        if (pawn.RaceProps?.Humanlike ?? false)
        {
            // check for shield user

            var wornApparel = pawn.apparel.WornApparel;
            for (var i = 0; i < wornApparel.Count; i++)
            {
                var personalShield = wornApparel[i].TryGetComp<CompShield>();
                if (personalShield != null)
                {
                    shield = personalShield;
                    break;
                }
            }
        }
        //Add suppression
        var compSuppressable = pawn.TryGetComp<CompSuppressable>();
        if (compSuppressable != null
                && pawn.Faction != launcher?.Faction
                && (shield == null || shield.ShieldState == ShieldState.Resetting)
                && !compSuppressable.IgnoreSuppresion(OriginIV3))
        {
            suppressionAmount = def.projectile.damageAmountBase * suppressionMultiplier;

            suppressionAmount *= propsCE.suppressionFactor;
            if (!landed)
            {
                suppressionAmount *= propsCE.airborneSuppressionFactor;
            }

            var explodeRadius = propsCE.explosionRadius;
            if (explodeRadius == 0f)
            {
                var comp = this.TryGetComp<CompExplosiveCE>()?.props as CompProperties_ExplosiveCE;
                if (comp != null)
                {
                    explodeRadius = comp.explosiveRadius;
                    suppressionAmount = comp.damageAmountBase;
                }
            }

            if (explodeRadius == 0f)
            {
                var penetrationAmount = propsCE?.armorPenetrationSharp ?? 0f;
                var armorMod = penetrationAmount <= 0 ? 0 : 1 - Mathf.Clamp(pawn.GetStatValue(CE_StatDefOf.AverageSharpArmor) * 0.5f / penetrationAmount, 0, 1);
                suppressionAmount *= armorMod;
            }
            else
            {
                // Larger suppression amount at distances compared to linear interpolation.
                var dPosX = ExactPosition.x - pawn.DrawPos.x;
                var dPosZ = ExactPosition.z - pawn.DrawPos.z;
                // Affected by the ratio of distance from the explosion/projectile to the max suppression radius raised to the power of two.
                var totalRadius = explodeRadius + SuppressionRadius;
                var distanceFactor = Mathf.Clamp01(1f - (dPosX * dPosX + dPosZ * dPosZ) / (totalRadius * totalRadius));
                suppressionAmount *= distanceFactor;
            }
            compSuppressable.AddSuppression(suppressionAmount, OriginIV3);
        }
    }

    public List<Vector3> cachedPredictedPositions;
    public virtual IEnumerable<Vector3> PredictedPositions
    {
        get
        {
            if (cachedPredictedPositions == null)
            {
                cachedPredictedPositions = TrajectoryWorker.PredictPositions(this, GenTicks.TicksPerRealSecond).ToList();
            }
            return cachedPredictedPositions;
        }
    }

    protected Vector3 MoveForward()
    {
        return TrajectoryWorker.MoveForward(this);
    }

    protected virtual bool ShouldCollideWithSomething => (TrajectoryWorker is LerpedTrajectoryWorker && ticksToImpact <= 0) || ExactPosition.y <= 0f;
    #region Tick/Draw
    public override void Tick()
    {
        base.Tick();
        if (landed)
        {
            return;
        }
        if (lerpPosition != (TrajectoryWorker is LerpedTrajectoryWorker))
        {
            Log.WarningOnce($"ProjectileCE.lerpPosition value changed in {this}. Setting or referencing this field is deprecated. Please report this.", 50004 + def.projectile.GetHashCode());
        }
        if (TrajectoryWorker is BallisticsTrajectoryWorker && DamageAmount < 0.01f && mass < 1f) // We've stopped, and won't restart.
        {
            Destroy(DestroyMode.Vanish);
            return;
        }

        TrajectoryWorker.NotifyTicked(this);
        LastPos = ExactPosition;
        ticksToImpact--;
        FlightTicks++;
        Vector3 nextPosition = MoveForward();
        _drawRotation = Quaternion.LookRotation(nextPosition - ExactPosition);

        if (!nextPosition.InBounds(Map))
        {
            if (globalTargetInfo.IsValid)
            {
                TravelingShell shell = (TravelingShell)WorldObjectMaker.MakeWorldObject(CE_WorldObjectDefOf.TravelingShell);
                if (launcher?.Faction != null)
                {
                    shell.SetFaction(launcher.Faction);
                }
                shell.Tile = Map.Tile;
                shell.SpawnSetup();
                Find.World.worldObjects.Add(shell);
                shell.launcher = launcher;
                shell.equipmentDef = equipmentDef;
                shell.globalSource = new GlobalTargetInfo(OriginIV3, Map);
                shell.globalSource.tileInt = Map.Tile;
                shell.globalSource.mapInt = Map;
                shell.globalSource.worldObjectInt = Map.Parent;
                shell.shellDef = def;
                shell.globalTarget = globalTargetInfo;
                if (!shell.TryTravel(Map.Tile, globalTargetInfo.Tile))
                {
                    Log.Error($"CE: Travling shell {this.def} failed to launch!");
                    shell.Destroy();
                }
            }
            Destroy();
            return;
        }
        ExactPosition = nextPosition;
        if (CheckForCollisionBetween())
        {
            return;
        }
        Position = nextPosition.ToIntVec3();
        if (globalTargetInfo.IsValid)
        {
            return;
        }
        if (ticksToImpact == 60 && Find.TickManager.CurTimeSpeed == TimeSpeed.Normal && def.projectile.soundImpactAnticipate != null)
        {
            def.projectile.soundImpactAnticipate.PlayOneShot(this);
        }
        //TODO : It appears that the final steps in the arc (past ticksToImpact == 0) don't CheckForCollisionBetween.
        if (ShouldCollideWithSomething)
        {
            ImpactSomething();
            return;
        }
        if (ambientSustainer != null)
        {
            ambientSustainer.Maintain();
        }

        if (def.HasModExtension<TrailerProjectileExtension>())
        {
            var trailer = def.GetModExtension<TrailerProjectileExtension>();
            if (trailer != null)
            {
                if (ticksToImpact % trailer.trailerMoteInterval == 0)
                {
                    for (int i = 0; i < trailer.motesThrown; i++)
                    {
                        TrailThrower.ThrowSmoke(DrawPos, trailer.trailMoteSize, Map, trailer.trailMoteDef);
                    }
                }
            }
        }
        float distToOrigin = OriginIV3.DistanceTo(positionInt);
        float dangerFactor = (def.projectile as ProjectilePropertiesCE).dangerFactor;
        if (dangerFactor > 0f && nextPosition.y < CollisionVertical.WallCollisionHeight && distToOrigin > 3)
        {
            DangerTracker?.Notify_BulletAt(Position, def.projectile.damageAmountBase * dangerFactor);
        }
        //If a flyoverhead ignore roof projectile is descending, enable roof check.
        if (ignoreRoof && def.projectile.flyOverhead && shotAngle < 0)
        {
            ignoreRoof = false;
        }
    }

    /// <summary>
    /// Draws projectile if at least a tick away from caster (or always if no caster)
    /// </summary>
    public override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        if (FlightTicks == 0 && launcher != null && launcher is Pawn)
        {
            //TODO: Draw at the end of the barrel on the pawn
        }
        else
        {
            Quaternion shadowRotation = ExactRotation;
            Quaternion projectileRotation = DrawRotation;
            if (def.projectile.spinRate != 0f)
            {
                float num2 = GenTicks.TicksPerRealSecond / def.projectile.spinRate;
                var spinRotation = Quaternion.AngleAxis(Find.TickManager.TicksGame % num2 / num2 * 360f, Vector3.up);
                shadowRotation *= spinRotation;
                projectileRotation *= spinRotation;
            }
            //Projectile
            //Graphics.DrawMesh(MeshPool.plane10, DrawPos, DrawRotation, def.DrawMatSingle, 0);
            Graphics.DrawMesh(MeshPool.GridPlane(def.graphicData.drawSize), drawLoc, projectileRotation, def.DrawMatSingle, 0);

            //Shadow
            if (castShadow)
            {
                //TODO : EXPERIMENTAL Add edifice height
                var shadowPos = new Vector3(ExactPosition.x,
                                            def.Altitude - 0.001f,
                                            ExactPosition.z);
                //EXPERIMENTAL: + (new CollisionVertical(ExactPosition.ToIntVec3().GetEdifice(Map))).Max);

                //TODO : Vary ShadowMat plane
                //Graphics.DrawMesh(MeshPool.plane08, shadowPos, ExactRotation, ShadowMaterial, 0);
                Graphics.DrawMesh(MeshPool.GridPlane(def.graphicData.drawSize), shadowPos, shadowRotation, ShadowMaterial, 0);
            }

            Comps_PostDraw();
        }
    }
    #endregion

    #region Impact
    //Modified collision with downed pawns
    public void ImpactSomething()
    {
        if (BlockerRegistry.ImpactSomethingCallback(this, launcher))
        {
            return;
        }
        var pos = ExactPosition.ToIntVec3();

        //Not modified, just mortar code
        if (def.projectile.flyOverhead)
        {
            var roofDef = Map.roofGrid.RoofAt(pos);
            if (roofDef != null)
            {
                if (roofDef.isThickRoof)
                {
                    def.projectile.soundHitThickRoof.PlayOneShot(new TargetInfo(pos, Map));
                    Destroy();
                    return;
                }
                if (pos.GetEdifice(Map) == null || pos.GetEdifice(Map).def.Fillage != FillCategory.Full)
                {
                    RoofCollapserImmediate.DropRoofInCells(pos, Map);
                }
            }
        }

        // FIXME : Early opt-out
        Thing thing = pos.GetFirstPawn(Map);
        if (thing != null && TryCollideWith(thing))
        {
            return;
        }

        var list = Map.thingGrid.ThingsListAt(pos).Where(t => t is Pawn || t.def.Fillage != FillCategory.None).ToList();
        if (list.Count > 0)
        {
            foreach (var thing2 in list)
            {
                if (TryCollideWith(thing2))
                {
                    return;
                }
            }
        }

        ExactPosition = ExactPosition;
        landed = true;
        Impact(null);
    }

    public virtual void Impact(Thing hitThing)
    {
        //if(cameraShakingInit > 0f && Find.CameraDriver != null)
        //{
        //    Find.CameraDriver.shaker.DoShake(cameraShakingInit);
        //}
        GenClamor.DoClamor(this, 12f, ClamorDefOf.Impact);
        if (Controller.settings.EnableExtraEffects)
        {
            ImpactFleckThrower.ThrowFleck(ExactPosition,
                    Position,
                    Map,
                    def.projectile as ProjectilePropertiesCE,
                    def, hitThing, shotRotation);
        }
        var ignoredThings = new List<Thing>();

        //Spawn things from preExplosionSpawnThingDef != null
        if (Position.IsValid
                && def.projectile.preExplosionSpawnChance > 0
                && def.projectile.preExplosionSpawnThingDef != null
                && (Controller.settings.EnableAmmoSystem || !(def.projectile.preExplosionSpawnThingDef is AmmoDef))
                && Rand.Value < def.projectile.preExplosionSpawnChance)
        {
            var thingDef = def.projectile.preExplosionSpawnThingDef;

            if (thingDef.IsFilth && Position.Walkable(Map))
            {
                FilthMaker.TryMakeFilth(Position, Map, thingDef);
            }
            else if (Controller.settings.ReuseNeolithicProjectiles)
            {
                var reusableAmmo = ThingMaker.MakeThing(thingDef);
                reusableAmmo.stackCount = 1;
                reusableAmmo.SetForbidden(true, false);
                Thing existingStack = Position.GetRegion(Map)?.listerThings.ThingsOfDef(thingDef).FirstOrFallback(thing => thing.stackCount < thing.def.stackLimit, null);
                GenPlace.TryPlaceThing(reusableAmmo, existingStack != null ? existingStack.positionInt : Position, Map, ThingPlaceMode.Near);
                LessonAutoActivator.TeachOpportunity(CE_ConceptDefOf.CE_ReusableNeolithicProjectiles, reusableAmmo, OpportunityType.GoodToKnow);
                ignoredThings.Add(reusableAmmo);
            }
        }

        var explodePos = ExactPosition;

        if (!explodePos.ToIntVec3().IsValid)
        {
            Destroy();
            return;
        }

        if (def.projectile.explosionEffect != null)
        {
            Effecter effecter = def.projectile.explosionEffect.Spawn();
            effecter.Trigger(new TargetInfo(explodePos.ToIntVec3(), Map, false), new TargetInfo(explodePos.ToIntVec3(), Map, false));
            effecter.Cleanup();
        }
        if (def.projectile.landedEffecter != null)
        {
            def.projectile.landedEffecter.Spawn(Position, Map, 1f).Cleanup();
        }
        ProjectilePropertiesCE projectileCE = def.projectile as ProjectilePropertiesCE;
        float effectScale = projectileCE.detonateEffectsScaleOverride > 0 ? projectileCE.detonateEffectsScaleOverride : projectileCE.explosionRadius * 2;
        if (projectileCE.detonateMoteDef != null)
        {
            MoteMaker.MakeStaticMote(DrawPos, Map, CE_ThingDefOf.Mote_BigExplode, effectScale);
        }
        if (projectileCE.detonateFleckDef != null)
        {
            FleckCreationData dataStatic = FleckMaker.GetDataStatic(DrawPos, MapHeld, projectileCE.detonateFleckDef, effectScale);
            MapHeld.flecks.CreateFleck(dataStatic);
        }
        var projectilePropsCE = (def.projectile as ProjectilePropertiesCE);

        var explodingComp = this.TryGetComp<CompExplosiveCE>();

        if (explodingComp == null)
        {
            foreach (var comp in GetComps<CompFragments>())
            {
                comp.Throw(explodePos, Map, launcher);
            }
        }

        //If the comp exists, it'll already call CompFragments
        if (explodingComp != null || (def.projectile.explosionRadius > 0f && def.projectile.damageDef != null))
        {
            float explosionSuppressionRadius = SuppressionRadius + (def.projectile.applyDamageToExplosionCellsNeighbors ? 1.5f : 0f);
            //Handle anything explosive
            if (hitThing is Pawn pawn && pawn.Dead)
            {
                ignoredThings.Add(pawn.Corpse);
            }

            var suppressThings = new List<Pawn>();
            float dangerAmount = 0f;
            var dir = new float?(origin.AngleTo(new Vector2(ExactPosition.x, ExactPosition.z)));

            // Opt-out for things without explosionRadius
            if (def.projectile.explosionRadius > 0f)
            {
                GenExplosionCE.DoExplosion(
                    explodePos.ToIntVec3(),
                    Map,
                    def.projectile.explosionRadius,
                    def.projectile.damageDef,
                    launcher,
                    Mathf.FloorToInt(DamageAmount),
                    def.projectile.GetExplosionArmorPenetration(),
                    def.projectile.soundExplode,
                    equipmentDef,
                    def,
                    intendedTarget: null,
                    def.projectile.postExplosionSpawnThingDef,
                    def.projectile.postExplosionSpawnChance,
                    def.projectile.postExplosionSpawnThingCount,
                    def.projectile.postExplosionGasType,
                    null,
                    255,
                    def.projectile.applyDamageToExplosionCellsNeighbors,
                    def.projectile.preExplosionSpawnThingDef,
                    def.projectile.preExplosionSpawnChance,
                    def.projectile.preExplosionSpawnThingCount,
                    def.projectile.explosionChanceToStartFire,
                    def.projectile.explosionDamageFalloff,
                    dir,
                    ignoredThings,
                    postExplosionSpawnThingDefWater: def.projectile.postExplosionSpawnThingDefWater,
                    screenShakeFactor: def.projectile.screenShakeFactor,
                    height: explodePos.y);

                dangerAmount = def.projectile.damageAmountBase;

                // Apply suppression around impact area
                if (explodePos.y < SuppressionRadius)
                {
                    explosionSuppressionRadius += def.projectile.explosionRadius;

                    if (projectilePropsCE.suppressionFactor > 0f)
                    {
                        suppressThings.AddRange(explodePos.ToIntVec3().PawnsInRange(Map,
                                                explosionSuppressionRadius));
                    }
                }
            }
            if (explodingComp != null)
            {
                dangerAmount = (explodingComp.props as CompProperties_ExplosiveCE).damageAmountBase;
                explodingComp.Explode(this, explodePos, Map, 1f, dir, ignoredThings);

                if (explodePos.y < SuppressionRadius)
                {
                    explosionSuppressionRadius += (explodingComp.props as CompProperties_ExplosiveCE).explosiveRadius;

                    if (projectilePropsCE.suppressionFactor > 0f)
                    {
                        suppressThings.AddRange(explodePos.ToIntVec3().PawnsInRange(Map,
                                                SuppressionRadius + (explodingComp.props as CompProperties_ExplosiveCE).explosiveRadius));
                    }
                }
            }

            foreach (var thing in suppressThings)
            {
                ApplySuppression(thing);
            }

            if (projectilePropsCE.dangerFactor > 0f)
            {
                DangerTracker.Notify_DangerRadiusAt(Position,
                                                    explosionSuppressionRadius - SuppressionRadius,
                                                    dangerAmount * projectilePropsCE.dangerFactor);
            }
        }
        else
        {
            if (projectilePropsCE.dangerFactor > 0f)
            {
                DangerTracker?.Notify_BulletAt(ExactPosition.ToIntVec3(),
                                               def.projectile.damageAmountBase * projectilePropsCE.dangerFactor);
            }
        }

        Destroy();
    }
    #endregion

    #region Ballistics

    private LegacyTrajectoryWorker CheckForLegacyProjectile()
    {
        Type type = this.GetType();
        if (type == typeof(ProjectileCE) || type == typeof(BulletCE)) // early out for most common case
        {
            return null;
        }
        bool v2p;
        try
        {
            var vec2position_m = type.GetMethod(nameof(Vec2Position), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            v2p = vec2position_m.GetBaseDefinition().DeclaringType != vec2position_m.DeclaringType;
        }
        catch (System.Reflection.AmbiguousMatchException) // Still using 1.3 era code
        {
            v2p = true;
        }
        if (!v2p)
        {
            return null;
        }

        var exactPosition_m = type.GetProperty("ExactPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var exactPosition_gm = exactPosition_m.GetGetMethod();
        var ep = exactPosition_gm.GetBaseDefinition() != exactPosition_gm;

        if (v2p)
        {
            return new LegacyTrajectoryWorker(this, v2p, ep);
        }
        return null;
    }

    public BaseTrajectoryWorker TrajectoryWorker
    {
        get
        {
            if (forcedTrajectoryWorker == null)
            {
                if (CheckForLegacyProjectile() is LegacyTrajectoryWorker worker)
                {
                    Log.WarningOnce($"{this} uses legacy collision code. Falling back to legacy TrajectoryWorker", this.def.GetHashCode());
                    forcedTrajectoryWorker = worker;
                }
                else if (!lerpPosition)
                {
                    Log.ErrorOnce($"Setting lerpPosition in ProjectileCE directly for {this} is deprecated, set the trajectoryWorker instead", 50003 + def.projectile.GetHashCode());
                    forcedTrajectoryWorker = ProjectilePropertiesCE.defaultBallisticTrajectoryWorker;
                }
                else if (def.projectile is ProjectilePropertiesCE propertiesCE)
                {
                    if (propertiesCE.lerpPosition != "")
                    {
                        Log.ErrorOnce($"Setting lerpPosition in ProjectilePropertiesCE for {this} is deprecated, set the trajectoryWorker instead", 50002 + def.projectile.GetHashCode());
                    }
                    forcedTrajectoryWorker = propertiesCE.TrajectoryWorker;
                }
                else
                {
                    Log.WarningOnce($"{this} properties is not ProjectilePropertiesCE, please contact CE team", this.def.GetHashCode());
                    forcedTrajectoryWorker = ProjectilePropertiesCE.defaultLerpedTrajectoryWorker;
                }
                lerpPosition = forcedTrajectoryWorker is LerpedTrajectoryWorker;
            }
            return forcedTrajectoryWorker;
        }
    }

    internal BaseTrajectoryWorker forcedTrajectoryWorker;

    /// <summary>
    /// Calculated rounding to three decimales the output of h0 + v * sin(a0) * t - g/2 * t^2 with {h0 -> shotHeight, v -> shotSpeed, a0 -> shotAngle, t -> ticks/GenTicks.TicksPerRealSecond, g -> GravityFactor}. Called roughly each tick for impact checks and for drawing.
    /// </summary>
    /// <param name="ticks">Integer ticks, since the only time value which is not an integer (accessed by StartingTicksToImpact) has height zero by definition.</param>
    /// <returns>Projectile height at time ticks in ticks.</returns>
    protected float GetHeightAtTicks(int ticks)
    {
        var seconds = ((float)ticks) / GenTicks.TicksPerRealSecond;
        return (float)Math.Round(shotHeight + shotSpeed * Mathf.Sin(shotAngle) * seconds - (GravityPerWidth * seconds * seconds) / 2f, 3);
    }

    /// <summary>
    /// Calculates the time in seconds the arc characterized by <i>angle</i>, <i>shotHeight</i> takes to traverse at speed <i>velocity</i> - e.g until the height reaches zero. Does not take into account air resistance.
    /// </summary>
    /// <param name="velocity">Projectile velocity in cells per second.</param>
    /// <param name="angle">Shot angle in radians off the ground.</param>
    /// <param name="shotHeight">Height from which the projectile is fired in vertical cells.</param>
    /// <returns>Time in seconds that the projectile will take to traverse the given arc.</returns>


    /// <summary>
    /// Calculates the range reachable with a projectile of speed <i>velocity</i> fired at <i>angle</i> from height <i>shotHeight</i>. Does not take into account air resistance.
    /// </summary>
    /// <param name="velocity">Projectile velocity in cells per second.</param>
    /// <param name="angle">Shot angle in radians off the ground.</param>
    /// <param name="shotHeight">Height from which the projectile is fired in vertical cells.</param>
    /// <returns>Distance in cells that the projectile will fly at the given arc.</returns>
    protected float DistanceTraveled => TrajectoryWorker.DistanceTraveled(shotHeight, shotSpeed, shotAngle, GravityPerWidth);

    #endregion

    protected static Material[] GetShadowMaterial(Graphic_Collection g)
    {
        var collection = g.subGraphics;
        var shadows = collection.Select(item => item.GetColoredVersion(ShaderDatabase.Transparent, Color.black, Color.black).MatSingle).ToArray();

        return shadows;
    }
    public ProjectilePropertiesCE Props => (ProjectilePropertiesCE)def.projectile;
    #endregion
}
