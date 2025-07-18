﻿using Verse;
using UnityEngine;

namespace CombatExtended.Compatibility.SOS2Compat;
public class ShipProjectileCE : ProjectileCE_Explosive
{
    // Modified the projectile drawing since we are setting it to shoot from above the roof height and the SOS2 turret graphics are completely top down
    // so it looks weird with the normal drawing logic.
    // I can't think of another easy way to alter the drawing for just the projectiles shot by Verb_ShootShip
    public override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        if (FlightTicks == 0 && launcher != null && launcher is Pawn)
        {
            //TODO: Draw at the end of the barrel on the pawn
        }
        else
        {
            Quaternion shadowRotation = ExactRotation;
            Quaternion projectileRotation = ExactRotation;
            if (def.projectile.spinRate != 0f)
            {
                float num2 = GenTicks.TicksPerRealSecond / def.projectile.spinRate;
                var spinRotation = Quaternion.AngleAxis(Find.TickManager.TicksGame % num2 / num2 * 360f, Vector3.up);
                shadowRotation *= spinRotation;
                projectileRotation *= spinRotation;
            }
            //Projectile
            Graphics.DrawMesh(MeshPool.GridPlane(def.graphicData.drawSize), ExactPosition, projectileRotation, def.DrawMatSingle, 0);

            //Shadow - Not going to bother drawing a shadow as we're essentially rendering as if we're looking directly down on the bullet
            // (Despite the rest of rimworld not being directly top down it just matches the turrets better)

            Comps_PostDraw();
        }
    }
    // To stop blowing up your own spaceships roof if the shots miss, technically its cheating but gameplay feels too punishing if not added
    protected override bool TryCollideWithRoof(IntVec3 cell)
    {
        if (!cell.Roofed(Map))
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

        InterceptProjectile(null, ExactPosition, true); // Modified here to just remove the projectile instead of doing an impact, could maybe add some kind of impact sound
        return true;
    }

}
