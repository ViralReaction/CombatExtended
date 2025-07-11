﻿using System;
using Verse;
using RimWorld;
using UnityEngine;
using System.Collections.Generic;
using CombatExtended.Utilities;

namespace CombatExtended;
//Cloned from vanilla, completely unmodified
public class ProjectileCE_Explosive : ProjectileCE
{
    public int ticksToDetonation;
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref this.ticksToDetonation, "ticksToDetonation", 0, false);
    }
    public override void Tick()
    {
        base.Tick();
        if (this.ticksToDetonation > 0)
        {
            this.ticksToDetonation--;
            if (this.ticksToDetonation <= 0)
            {
                //Explosions are all handled in base
                base.Impact(null);
                return;
            }
            if ((def.projectile as ProjectilePropertiesCE).suppressionFactor > 0f && landed)
            {
                foreach (var thing in ExactPosition.ToIntVec3().PawnsInRange(Map,
                            SuppressionRadius + def.projectile.explosionRadius +
                                (def.projectile.applyDamageToExplosionCellsNeighbors ? 1.5f : 0f)))
                {
                    ApplySuppression(thing, 1f - (ticksToDetonation / def.projectile.explosionDelay));
                }
            }
        }
    }
    public override void Impact(Thing hitThing)
    {
        // Snap to target so we hit multi-tile pawns with our explosion
        if (hitThing is Pawn)
        {
            var newPos = hitThing.DrawPos;
            newPos.y = ExactPosition.y;
            ExactPosition = newPos;
            Position = ExactPosition.ToIntVec3();
        }
        if (def.projectile.explosionDelay == 0)
        {
            //Explosions are all handled in base
            base.Impact(hitThing);
            return;
        }
        landed = true;
        ticksToDetonation = def.projectile.explosionDelay;
        float dangerFactor = (def.projectile as ProjectilePropertiesCE).dangerFactor;
        if (dangerFactor > 0f)
        {
            DangerTracker.Notify_DangerRadiusAt(Position,
                    def.projectile.explosionRadius +
                        (def.projectile.applyDamageToExplosionCellsNeighbors ? 1.5f : 0f),
                    def.projectile.damageAmountBase * dangerFactor);

            GenExplosion.NotifyNearbyPawnsOfDangerousExplosive(this, this.def.projectile.damageDef,
                    this.launcher?.Faction);
        }
    }
}
