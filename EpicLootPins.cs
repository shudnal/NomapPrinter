using BepInEx.Bootstrap;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using static NomapPrinter.NomapPrinter;

namespace NomapPrinter
{
    // Read Epic Loot's live pin/area pairs without a required assembly reference.
    internal static class EpicLootPins
    {
        private static bool initialized;
        private static bool warned;
        private static FieldInfo bountyPins;
        private static FieldInfo treasurePins;
        private static PropertyInfo marker;
        private static PropertyInfo area;

        internal static HashSet<Minimap.PinData> GetAdventurePins()
        {
            if (IsHeadless || !epicLootIsLoaded)
                return null;

            try
            {
                if (!initialized)
                    Initialize();
                if (bountyPins == null || treasurePins == null || marker == null || area == null)
                    return null;

                // One snapshot per print or interactive filter pass, not per pin.
                // Do not retain references across world changes or read UI toggles.
                HashSet<Minimap.PinData> pins = new HashSet<Minimap.PinData>();
                AddPins(bountyPins.GetValue(null) as IDictionary, pins);
                AddPins(treasurePins.GetValue(null) as IDictionary, pins);
                return pins;
            }
            catch (Exception exception)
            {
                if (!warned)
                {
                    warned = true;
                    LogWarning($"Could not read Epic Loot adventure pins. Using legacy icon filters.\n{exception}");
                }
                return null;
            }
        }

        private static void Initialize()
        {
            initialized = true;
            if (!Chainloader.PluginInfos.TryGetValue(epicLootGUID, out var plugin) || plugin.Instance == null)
                throw new InvalidOperationException("The Epic Loot plugin instance is not available.");

            Assembly assembly = plugin.Instance.GetType().Assembly;
            Type controller = assembly.GetType("EpicLoot.Adventure.MinimapController");
            Type pinInfo = assembly.GetType("EpicLoot.Adventure.AreaPinInfo");
            bountyPins = controller?.GetField("BountyPins", BindingFlags.Public | BindingFlags.Static);
            treasurePins = controller?.GetField("TreasureMapPins", BindingFlags.Public | BindingFlags.Static);
            marker = pinInfo?.GetProperty("Pin", BindingFlags.Public | BindingFlags.Instance);
            area = pinInfo?.GetProperty("Area", BindingFlags.Public | BindingFlags.Instance);
            if (bountyPins == null || treasurePins == null || marker == null || area == null)
                throw new InvalidOperationException("The Epic Loot adventure pin collections are not available.");
        }

        private static void AddPins(IDictionary entries, HashSet<Minimap.PinData> pins)
        {
            if (entries == null)
                return;

            foreach (DictionaryEntry entry in entries)
            {
                if (entry.Value == null)
                    continue;
                if (marker.GetValue(entry.Value) is Minimap.PinData pin)
                    pins.Add(pin);
                if (area.GetValue(entry.Value) is Minimap.PinData areaPin)
                    pins.Add(areaPin);
            }
        }
    }
}
