using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace CombatExtended.HarmonyCE;
static class Harmony_Hediff_Injury
{
    [HarmonyPatch(typeof(Hediff_Injury), nameof(Hediff_Injury.PostAdd))]
    static class Patch_PostAdd
    {
        /// <summary>
        /// Silences log spam from vanilla. Vanilla considers certain parts unhittable that CE can hit.
        /// </summary>
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> SilenceLogSpam(IEnumerable<CodeInstruction> instructions)
        {
            var patchPhase = 0;
            var emitOriginal = true;
            bool foundInjection = false;

            foreach (var instruction in instructions)
            {
                switch (patchPhase)
                {
                    // search for BodyPartRecord::coverageAbs
                    case 0:
                        if (instruction.opcode == OpCodes.Ldfld
                                && ReferenceEquals(instruction.operand, AccessTools.Field(
                                                       typeof(BodyPartRecord),
                                                       nameof(BodyPartRecord.coverageAbs))))
                        {
                            patchPhase = 1;
                        }
                        break;
                    // search for greater than check
                    case 1:
                        if (instruction.opcode == OpCodes.Bgt_Un_S)
                        {
                            // blank all instructions until the return
                            emitOriginal = false;
                            patchPhase = 2;
                            yield return instruction;
                            yield return new CodeInstruction(OpCodes.Nop);
                            foundInjection = true;
                        }

                        break;
                    // look for last return
                    case 2:
                        if (instruction.opcode == OpCodes.Ret)
                        {
                            emitOriginal = true;
                            patchPhase = -1;
                        }
                        break;
                }

                if (emitOriginal)
                {
                    yield return instruction;
                }
            }
            if (!foundInjection)
            {
                Log.Error($"Combat Extended :: Failed to find injection point when applying Patch: {HarmonyBase.GetClassName(MethodBase.GetCurrentMethod()?.DeclaringType)}");
            }
        }
    }
}
