using System.Collections.Generic;
using HarmonyLib;

namespace NearbyCraft
{
    // The native workstation UI writes pickups, shift-clicks and take-all back
    // through this model. Automatic collection writes its transaction directly
    // to the station array and therefore cannot be counted twice by this hook.
    [HarmonyPatch(typeof(XUiM_Workstation), nameof(XUiM_Workstation.SetOutputStacks))]
    internal static class WorkshopManualOutputPatch
    {
        private sealed class Collection
        {
            internal WorkshopControllerData Controller;
            internal Dictionary<int, int> Before;
        }

        private static void Prefix(XUiM_Workstation __instance, out Collection __state)
        {
            __state = null;
            if (!NearbyCraftMod.CanUseLocalStorage || !WorkshopStore.Writable || __instance.TileEntity == null) return;
            var controller = WorkshopManager.FindOutputOwner(__instance.TileEntity);
            if (controller == null) return;
            __state = new Collection { Controller = controller, Before = WorkshopOutputAccounting.Capture(__instance.TileEntity.Output) };
        }

        private static void Postfix(XUiM_Workstation __instance, Collection __state)
        {
            if (__state == null || !WorkshopOutputAccounting.CreditRemoved(__state.Controller, __state.Before, __instance.TileEntity.Output)) return;
            string error;
            if (!WorkshopStore.SaveProgress(out error)) WorkshopManager.Status[__state.Controller.Position] = error;
        }
    }
}
