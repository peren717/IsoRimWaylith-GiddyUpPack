using System;
using System.Reflection;
using GiddyUp;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace IsoRimGiddyUpPackZ
{
    // With IsoRim ProjectZ (stacked floors): Giddy-Up's auto-hitch spot (MountUtility.FindPlaceToDismount, "Auto": no drop
    // spot or pen close enough) is searched round the rider's destination by distance only, so on a map whose floors are
    // only a few rows apart it can pick a cell on another floor (the mount is parked there and the rider walks the stairs).
    // When it did, the search is done again with the same tests on the destination's floor (ProjectZ.Api.OnDifferentFloors);
    // with nothing found there Giddy-Up's spot is kept. Drop spots and pens the player chose are left alone.
    [StaticConstructorOnStartup]
    public static class PackZ
    {
        private const string Tag = "[IsoRim Giddy-Up Pack, ProjectZ] ";

        static PackZ()
        {
            Harmony h = new Harmony("peren.isorimwaylith.giddyuppack.z");
            try
            {
                MethodInfo find = AccessTools.Method("GiddyUp.MountUtility:FindPlaceToDismount");
                if (find == null)
                {
                    Log.Warning(Tag + "Giddy-Up's MountUtility.FindPlaceToDismount not found: parking is left as Giddy-Up does it.");
                }
                else
                {
                    h.Patch(find, postfix: new HarmonyMethod(typeof(Parking), nameof(Parking.Postfix)));
                }
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Setup failed (parking is left as Giddy-Up does it): " + e);
            }
            // Riding between floors (AutoMount): ProjectZ Api 2 (floor queries); with an older IsoRimZ Giddy-Up rides as before.
            try
            {
                if (ProjectZ.Api.Version < 2)
                {
                    Log.Warning(Tag + "IsoRimZ is older than Api 2: colonists may still mount up for short trips between floors.");
                }
                else if (!AutoMount.Init(h))
                {
                    Log.Warning(Tag + "Giddy-Up's MountUtility.TryAutoMount or GetBestAnimal not found (or no distance to measure): riding between floors is left as Giddy-Up does it.");
                }
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Setup failed (riding between floors is left as Giddy-Up does it): " + e);
            }
        }
    }

    internal static class Parking
    {
        private const int Rings = 7;     // as Giddy-Up's own search: radius 5, 10, ... 35
        private const int RingStep = 5;

        public static void Postfix(Pawn rider, Area areaNoMount, IntVec3 riderDestination, ref IntVec3 parkLoc, Pawn animal,
            ref MountUtility.DismountLocationType dismountLocationType, ref bool __result)
        {
            try
            {
                Map map = rider != null ? rider.Map : null;
                if (!__result || dismountLocationType != MountUtility.DismountLocationType.Auto || map == null || !parkLoc.IsValid
                    || !riderDestination.IsValid || !ProjectZ.Api.OnDifferentFloors(parkLoc, riderDestination, map))
                {
                    return;
                }
                Predicate<IntVec3> ok = c => c.InBounds(map) && !ProjectZ.Api.OnDifferentFloors(c, riderDestination, map) && c.Standable(map)
                    && (animal == null || c.GetDangerFor(animal, map) == Danger.None) && !c.Fogged(map) && (areaNoMount == null || !areaNoMount[c])
                    && rider.CanReserveAndReach(c, PathEndMode.OnCell, Danger.None);
                for (int i = 1; i <= Rings; i++)
                {
                    IntVec3 c;
                    if (CellFinder.TryFindRandomCellNear(riderDestination, map, i * RingStep, ok, out c))
                    {
                        parkLoc = c;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim Giddy-Up Pack, ProjectZ] Keeping the parking spot on the rider's floor failed: " + e, 0x6d0c2a2);
            }
        }
    }
}
