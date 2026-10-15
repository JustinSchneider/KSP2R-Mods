using System;
using System.Reflection;
using HarmonyLib;
using KSP.OAB;
using Redux.UI.Settings.Submenus;
using UnityEngine;

namespace AgencyHeritage
{
    /// <summary>
    /// The stock game can change the agency's flag (Gameplay settings) and colors (the VAB paint picker's agency
    /// button). With the mod installed, those changes open the Rebrand page instead, filled in with the new choice,
    /// so every change of identity begins an era. The stock game has no way to rename the agency.
    /// </summary>
    [HarmonyPatch]
    internal static class StockIdentityPatches
    {
        internal static AgencyHeritagePlugin Plugin;

        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [HarmonyPatch(typeof(ObjectAssemblyColorPicker), "OnSetAgencyColors")]
        [HarmonyPrefix]
        private static bool RedirectAgencyColors(ObjectAssemblyColorPicker __instance)
        {
            if (Plugin == null || !Plugin.RedirectStockIdentity || !Plugin.CanRebrand) return true;
            try
            {
                var colorBase = (Color)typeof(ObjectAssemblyColorPicker).GetField("_baseColor", InstanceFields).GetValue(__instance);
                var colorAccent = (Color)typeof(ObjectAssemblyColorPicker).GetField("_accentColor", InstanceFields).GetValue(__instance);
                Plugin.OpenRebrand(null, colorBase, colorAccent);
                return false;
            }
            catch (Exception e)
            {
                // If Redux renames these fields, fall back to the stock behaviour rather than a dead button.
                SWLog.Warning($"Couldn't redirect the VAB agency colors to the Rebrand page: {e.Message}");
                return true;
            }
        }

        [HarmonyPatch(typeof(UitkGameplaySettingsManager), "OnAgencyFlagSpriteLocationChanged")]
        [HarmonyPrefix]
        private static bool RedirectAgencyFlag(string flagSpriteLocation)
        {
            if (Plugin == null || !Plugin.RedirectStockIdentity || !Plugin.CanRebrand) return true;
            string current = Plugin.Session.GetMyAgencyFlagSpriteLocation();
            if (string.IsNullOrEmpty(flagSpriteLocation) || flagSpriteLocation == current) return true;

            // The settings sit over the pause menu, so the Rebrand page opens once the player is back in the game. Until
            // then the picker shows their choice; it re-reads the agency's real flag the next time the menu opens.
            Plugin.OpenRebrand(flagSpriteLocation, null, null);
            return false;
        }
    }
}
