﻿using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace CombatExtended;
public class StatPart_LoadedAmmo : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (TryGetValue(req, out float num))
        {
            val += num;
        }
    }

    public override string ExplanationPart(StatRequest req)
    {
        return TryGetValue(req, out float num) ? "CE_StatsReport_LoadedAmmo".Translate() + ": " + parentStat.ValueToString(num) : null;
    }

    public bool TryGetValue(StatRequest req, out float num)
    {
        num = 0f;
        if (req.HasThing)
        {
            var ammoUser = req.Thing.TryGetComp<CompAmmoUser>();

            var launcher = req.Thing.TryGetComp<CompUnderBarrel>();
            if (launcher != null)
            {
                if (ammoUser != null && ammoUser.CurrentAmmo != null)
                {
                    num = ammoUser.CurrentAmmo.GetStatValueAbstract(parentStat) * ammoUser.CurMagCount;

                    if (parentStat == CE_StatDefOf.Bulk)
                    {
                        num *= ammoUser.Props.loadedAmmoBulkFactor;
                    }
                }

                if (launcher.usingUnderBarrel)
                {
                    num += (launcher.mainGunLoadedAmmo?.GetStatValueAbstract(parentStat) ?? 0) * launcher.mainGunMagCount;
                }
                else
                {
                    num += (launcher.UnderBarrelLoadedAmmo?.GetStatValueAbstract(parentStat) ?? 0) * launcher.UnderBarrelMagCount;
                }

                return num != 0f;
            }

            if (ammoUser != null && ammoUser.CurrentAmmo != null)
            {
                num = ammoUser.CurrentAmmo.GetStatValueAbstract(parentStat) * ammoUser.CurMagCount;

                if (parentStat == CE_StatDefOf.Bulk)
                {
                    num *= ammoUser.Props.loadedAmmoBulkFactor;
                }
            }

        }
        return num != 0f;
    }
}
