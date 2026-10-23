using System;
using System.Collections.Generic;
using System.Reflection;
using GiddyUp;
using HarmonyLib;
using ProjectZ;
using RimWorld;
using Verse;
using Verse.AI;

namespace IsoRimGiddyUpPackZ
{
    // With IsoRim ProjectZ (stacked floors):
    //   - Giddy-Up's "ride if far" (MountUtility.TryAutoMount and the animal pick MountUtility.GetBestAnimal) measures with
    //     IntVec3Utility.DistanceTo, the straight line between two map cells. An upper floor is a band of rows a whole map
    //     height away, so a colonist going one floor up for a few cells saw a trip of 250+ cells and mounted, and a horse on
    //     the floor above could look close across the few void rows between two bands. In those two methods every such call
    //     measures ProjectZ.Api.Distance3D instead: the same straight line, between the cells as they stand (ground distance
    //     with the floors' height between). That is what Giddy-Up's threshold means (a straight line, not a walk), so a short
    //     trip upstairs stays on foot, a long one rides (up the stairs: mounts take stairs), and a horse on another floor
    //     counts by its real distance. On one floor nothing changes. Manual mount orders are Giddy-Up's own.
    //   - Ladders: a mount can't climb one. A rider about to step onto a ladder (ProjectZ.Api.AddStepHook) gets off first,
    //     at the ladder's foot; Giddy-Up's own dismount leaves the mount waiting for the rider there (WaitForRider), and the
    //     rider climbs on foot.
    // Needs ProjectZ Api 2.
    internal static class AutoMount
    {
        [ThreadStatic]
        private static Map map;   // the map of the auto-mount being worked out (TryAutoMount)

        public static int Measured, Dismounted;   // (tests)
        public static int Replaced;               // calls to DistanceTo swapped (startup)

        // Call only after checking ProjectZ.Api.Version >= 2: it names Api 2 members.
        public static bool Init(Harmony h)
        {
            MethodInfo auto = AccessTools.Method("GiddyUp.MountUtility:TryAutoMount");
            MethodInfo best = AccessTools.Method("GiddyUp.MountUtility:GetBestAnimal");
            if (auto == null || best == null)
            {
                return false;
            }
            HarmonyMethod tr = new HarmonyMethod(typeof(AutoMount), nameof(Transpiler));
            h.Patch(auto, prefix: new HarmonyMethod(typeof(AutoMount), nameof(Prefix)), finalizer: new HarmonyMethod(typeof(AutoMount), nameof(Finalizer)), transpiler: tr);
            h.Patch(best, transpiler: tr);
            ProjectZ.Api.AddStepHook(StepHook);
            return Replaced > 0;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo flat = AccessTools.Method(typeof(IntVec3Utility), nameof(IntVec3Utility.DistanceTo), new[] { typeof(IntVec3), typeof(IntVec3) });
            MethodInfo real = AccessTools.Method(typeof(AutoMount), nameof(Distance));
            foreach (CodeInstruction c in instructions)
            {
                if (c.Calls(flat))
                {
                    c.operand = real;
                    Replaced++;
                }
                yield return c;
            }
        }

        public static void Prefix(Pawn pawn)
        {
            map = pawn != null ? pawn.Map : null;
        }

        public static void Finalizer()
        {
            map = null;
        }

        // IntVec3Utility.DistanceTo's stand-in: the straight line between the two cells as they stand, floors apart.
        public static float Distance(IntVec3 a, IntVec3 b)
        {
            Map m = map;
            if (m == null || !ProjectZ.Api.HasFloors(m) || ProjectZ.Api.FloorOf(a, m) < 0 || ProjectZ.Api.FloorOf(b, m) < 0)
            {
                return a.DistanceTo(b);
            }
            Measured++;
            return ProjectZ.Api.Distance3D(a, b, m);
        }

        // A mounted rider about to climb a ladder gets off at its foot (the mount waits there, Giddy-Up's WaitForRider).
        private static bool StepHook(Pawn pawn, StepKind kind, Building link)
        {
            if ((kind != StepKind.LadderUp && kind != StepKind.LadderDown) || pawn == null || !pawn.IsMounted())
            {
                return true;
            }
            ExtendedPawnData data = pawn.GetExtendedPawnData();
            Pawn mount = data != null ? data.Mount : null;
            pawn.Dismount(mount, data, clearReservation: false, parkLoc: pawn.Position);
            Dismounted++;
            return true;
        }
    }
}
