using System;
using GiddyUp;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace IsoRimGiddyUpPackZ
{
    // With IsoRim ProjectZ (stacked floors): Giddy-Up's "ride if far" (MountUtility.TryAutoMount) measures a trip as the flat
    // distance between map cells, and an upper floor is a block of rows a whole map height away, so a colonist going upstairs
    // (or hauling to a shelf there) saw a trip of 150+ cells and mounted for a walk of a few cells. The rule here:
    //   - a trip that changes floors (the rider's cell and the job's first target, or a haul's two ends, on different floors:
    //     ProjectZ.Api.SameFloor) never auto-mounts: the vertical part is stairs or a ladder (a mount carried up a ladder,
    //     then parked upstairs), and the flat part is what the rule is for;
    //   - on one floor Giddy-Up's own measure stands (cells of one floor are rows of one band: the flat distance is the true
    //     ground distance), and only animals on the rider's floor are offered (a horse on the floor above can look close
    //     across the few void rows between two floors).
    // A rider ordered to mount still mounts any animal (the player's choice). Needs ProjectZ Api 2.
    internal static class AutoMount
    {
        private static Func<IntVec3, IntVec3, Map, bool> sameFloor;
        private static Func<Map, bool> hasFloors;

        [ThreadStatic]
        private static Pawn autoRider;   // the rider whose auto-mount Giddy-Up is working out (IsMountable asks)

        public static int SkippedTrips, SkippedAnimals;   // (tests)

        // Call only after checking ProjectZ.Api.Version >= 2: the delegates name Api 2 members.
        public static bool Init(Harmony h)
        {
            sameFloor = ProjectZ.Api.SameFloor;
            hasFloors = ProjectZ.Api.HasFloors;
            var auto = AccessTools.Method("GiddyUp.MountUtility:TryAutoMount");
            var mountable = AccessTools.Method("GiddyUp.IsMountableUtility:IsMountable");
            if (auto == null || mountable == null)
            {
                return false;
            }
            h.Patch(auto, prefix: new HarmonyMethod(typeof(AutoMount), nameof(Prefix)), finalizer: new HarmonyMethod(typeof(AutoMount), nameof(Finalizer)));
            h.Patch(mountable, postfix: new HarmonyMethod(typeof(AutoMount), nameof(Mountable_Postfix)));
            return true;
        }

        public static bool Prefix(Pawn pawn, ref ThinkResult thinkResult)
        {
            autoRider = null;
            try
            {
                Map map = pawn != null ? pawn.Map : null;
                Job job = thinkResult.Job;
                if (map == null || job == null || !hasFloors(map))
                {
                    return true;
                }
                IntVec3 first, second;
                if (job.DetermineTargets(out first, out second))
                {
                    bool haul = job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer;
                    if (!sameFloor(pawn.Position, first, map) || haul && second.IsValid && !sameFloor(first, second, map))
                    {
                        SkippedTrips++;
                        return false;   // a trip between floors: on foot (Giddy-Up's own checks skipped with it)
                    }
                }
                autoRider = pawn;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim Giddy-Up Pack, ProjectZ] Checking a ride between floors failed: " + e, 0x6d0c2a3);
            }
            return true;
        }

        public static void Finalizer()
        {
            autoRider = null;
        }

        public static void Mountable_Postfix(Pawn animal, Pawn rider, ref bool __result, ref IsMountableUtility.Reason reason)
        {
            if (!__result || autoRider == null || rider != autoRider || animal == null || !animal.Spawned || animal.Map != rider.Map)
            {
                return;
            }
            if (!sameFloor(animal.Position, rider.Position, rider.Map))
            {
                SkippedAnimals++;
                __result = false;
                reason = IsMountableUtility.Reason.False;
            }
        }
    }
}
