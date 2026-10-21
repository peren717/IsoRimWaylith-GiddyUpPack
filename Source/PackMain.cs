using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace IsoRimGiddyUpPack
{
    // Giddy-Up 2 riders in IsoRim Waylith's 3D view (needs IsoRim Api 13; older: the pack does nothing).
    //  - Each mounted rider is attached to its mount through IsoRim.Api.AddAttachments: on the back, legs left out, facing
    //    the mount's way and turning to aim. IsoRim then draws it there and makes picking, the label, the shot origin, the
    //    bubbles and first person follow it.
    //  - While IsoRim's 3D view is on, the rider is drawn on its own (Giddy-Up skips a rider's own draw and draws it from a
    //    render node in its mount's tree instead): after PawnRenderer.DynamicDrawPhaseAt was skipped for a mounted rider in
    //    the draw phase, its RenderPawnAt is called; Giddy-Up's rider node and its 2D head/neck overlay node are switched off
    //    for map drawing.
    //  - With the 3D view off nothing changes: Giddy-Up's own 2D drawing.
    // Giddy-Up is reached by name only (public StorageUtility.IsMounted / GetExtendedPawnData, ExtendedPawnData.Mount; its
    // internal render node workers); anything missing leaves that part off, logged once.
    [StaticConstructorOnStartup]
    public static class PackMain
    {
        internal const string Tag = "[IsoRim Giddy-Up Pack] ";

        static PackMain()
        {
            try
            {
                if (IsoRim.Api.Version < 13)
                {
                    Log.Message(Tag + "IsoRim Waylith has no attachments (Api 13): riders keep Giddy-Up's own look (invisible in the 3D view).");
                    return;
                }
                Riders.Setup();
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Setup failed: " + e);
            }
        }
    }

    // Everything that names Api 13 types lives here, so an older IsoRim never loads them.
    internal static class Riders
    {
        private static Func<Thing, bool> isMounted;   // GiddyUp.StorageUtility.IsMounted(Thing)
        private static Func<Pawn, Pawn> mountOf;      // GiddyUp.StorageUtility.GetExtendedPawnData(Pawn).Mount
        private static AccessTools.FieldRef<PawnRenderer, Pawn> pawnOf;

        internal static void Setup()
        {
            Type storage = AccessTools.TypeByName("GiddyUp.StorageUtility");
            MethodInfo mounted = storage != null ? AccessTools.Method(storage, "IsMounted", new[] { typeof(Thing) }) : null;
            MethodInfo data = storage != null ? AccessTools.Method(storage, "GetExtendedPawnData", new[] { typeof(Pawn) }) : null;
            PropertyInfo mount = data != null ? AccessTools.Property(data.ReturnType, "Mount") : null;
            if (mounted == null || data == null || mount == null || mount.PropertyType != typeof(Pawn))
            {
                Log.Warning(PackMain.Tag + "Giddy-Up's StorageUtility.IsMounted / GetExtendedPawnData(...).Mount not found: the pack is off.");
                return;
            }
            isMounted = (Func<Thing, bool>)Delegate.CreateDelegate(typeof(Func<Thing, bool>), mounted);
            ParameterExpression p = Expression.Parameter(typeof(Pawn), "rider");
            Expression got = Expression.Call(data, p);
            mountOf = Expression.Lambda<Func<Pawn, Pawn>>(
                Expression.Condition(Expression.Equal(got, Expression.Constant(null, data.ReturnType)), Expression.Constant(null, typeof(Pawn)), Expression.Property(got, mount)), p).Compile();
            pawnOf = AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

            // (made from its MethodInfo: a plain method group would be cached by the compiler in a static field of the Api 13
            // delegate type, which the game's startup field scan trips over on an IsoRim older than 13)
            IsoRim.Api.AddAttachments((Func<Thing, IsoRim.Attachment?>)Delegate.CreateDelegate(typeof(Func<Thing, IsoRim.Attachment?>),
                AccessTools.Method(typeof(Riders), nameof(Attachment))));

            Harmony h = new Harmony("peren.isorimwaylith.giddyuppack");
            MethodInfo phase = AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt));
            h.Patch(phase, postfix: new HarmonyMethod(typeof(Riders), nameof(DrawPhase_Postfix)));
            OffIn3D(h, "GiddyUpCore.Core.MountedRiderRenderNodeWorker");
            OffIn3D(h, "GiddyUpCore.Core.Render.OverlayRenderNodeWorker");
        }

        // The rider's place on its mount (IsoRim Api 13).
        private static IsoRim.Attachment? Attachment(Thing t)
        {
            Pawn rider = t as Pawn;
            if (rider == null || !isMounted(rider))
            {
                return null;
            }
            Pawn mount = mountOf(rider);
            if (mount == null)
            {
                return null;
            }
            // (the rider steers: a stair, ladder or lift step it takes is drawn for its mount too)
            return new IsoRim.Attachment(mount, IsoRim.AttachAnchor.Back, IsoRim.AttachPose.SitNoLegs, IsoRim.AttachFacing.Aim) { leads = true };
        }

        // A mounted rider's draw phase that Giddy-Up skipped: in the 3D view it is drawn on its own (IsoRim puts it on the
        // mount through the attachment).
        public static void DrawPhase_Postfix(PawnRenderer __instance, DrawPhase phase, Vector3 drawLoc, Rot4? rotOverride, bool neverAimWeapon, bool __runOriginal)
        {
            if (__runOriginal || phase != DrawPhase.Draw || !IsoRim.Api.ViewReady)
            {
                return;
            }
            try
            {
                Pawn pawn = pawnOf(__instance);
                if (pawn != null && isMounted(pawn))
                {
                    __instance.RenderPawnAt(drawLoc, rotOverride, neverAimWeapon);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce(PackMain.Tag + "Drawing a rider failed: " + e, 0x6d0c2a1);
            }
        }

        private static void OffIn3D(Harmony h, string worker)
        {
            Type type = AccessTools.TypeByName(worker);
            MethodInfo canDraw = type != null ? AccessTools.DeclaredMethod(type, "CanDrawNow") : null;
            if (canDraw == null)
            {
                Log.Warning(PackMain.Tag + worker + ".CanDrawNow not found: left as it is.");
                return;
            }
            h.Patch(canDraw, postfix: new HarmonyMethod(typeof(Riders), nameof(CanDrawNow_Postfix)));
        }

        // Giddy-Up's rider and overlay nodes in the mount's render tree: off while the 3D view draws the map (portraits keep them).
        public static void CanDrawNow_Postfix(PawnDrawParms parms, ref bool __result)
        {
            if (__result && IsoRim.Api.ViewReady && (parms.flags & PawnRenderFlags.Portrait) == 0)
            {
                __result = false;
            }
        }
    }
}
