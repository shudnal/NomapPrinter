using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static NomapPrinter.NomapPrinter;

namespace NomapPrinter
{
    internal static class InteractiveMap
    {
        private static readonly HashSet<Minimap.PinData> hiddenPins = new HashSet<Minimap.PinData>();

        private static bool active;
        private static Vector3 lastPlayerPosition;
        private static bool hasLastMapCenter;
        private static Vector3 lastMapCenter;

        internal static bool IsOpen => active && Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large;

        public static void Show()
        {
            Minimap minimap = Minimap.instance;
            Player player = Player.m_localPlayer;

            if (minimap == null || player == null)
                return;

            active = true;

            bool noMap = Game.m_noMap;
            try
            {
                Game.m_noMap = false;
                minimap.inputDelay = 1f;
                minimap.SetMapMode(Minimap.MapMode.Large);
            }
            catch
            {
                active = false;
                throw;
            }
            finally
            {
                Game.m_noMap = noMap;
            }

            lastPlayerPosition = player.transform.position;

            if (doNotCenterInteractiveMapOnPlayer.Value)
            {
                Vector3 mapCenter = hasLastMapCenter ? lastMapCenter : Vector3.zero;
                minimap.m_mapOffset = mapCenter - lastPlayerPosition;
                minimap.CenterMap(mapCenter);
                lastMapCenter = mapCenter;
                hasLastMapCenter = true;
            }

            ApplyPlayerMarkerVisibility(minimap);

            minimap.m_pinUpdateRequired = true;
            minimap.UpdatePins();
        }

        private static void ApplyPlayerMarkerVisibility(Minimap minimap)
        {
            if (!IsOpen || !hidePlayerMarkerOnInteractiveMap.Value)
                return;

            if (minimap.m_largeMarker != null)
                minimap.m_largeMarker.gameObject.SetActive(false);

            if (minimap.m_largeShipMarker != null)
                minimap.m_largeShipMarker.gameObject.SetActive(false);
        }

        private static void HidePinVisuals(Minimap.PinData pin)
        {
            if (pin == null)
                return;

            if (pin.m_uiElement != null)
                pin.m_uiElement.gameObject.SetActive(false);

            if (pin.m_NamePinData?.PinNameGameObject != null)
                pin.m_NamePinData.PinNameGameObject.SetActive(false);
        }

        private static bool ShouldKeepPinNameHidden(GameObject pinNameGameObject)
        {
            if (!IsOpen || !applyPinFiltersToInteractiveMap.Value || pinNameGameObject == null)
                return false;

            foreach (Minimap.PinData pin in hiddenPins)
            {
                if (pin?.m_NamePinData?.PinNameGameObject == pinNameGameObject)
                    return true;
            }

            return false;
        }

        private static IEnumerator KeepPinNameHidden(IEnumerator original, GameObject pinNameGameObject)
        {
            try
            {
                while (original.MoveNext())
                {
                    yield return original.Current;

                    if (ShouldKeepPinNameHidden(pinNameGameObject))
                        pinNameGameObject.SetActive(false);
                }

                if (ShouldKeepPinNameHidden(pinNameGameObject))
                    pinNameGameObject.SetActive(false);
            }
            finally
            {
                if (original is System.IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private static void ResetHiddenPins(Minimap minimap)
        {
            foreach (Minimap.PinData pin in hiddenPins)
            {
                if (pin != null)
                    minimap.DestroyPinMarker(pin);
            }

            hiddenPins.Clear();
        }

        private static void SaveCurrentMapCenter(Minimap minimap, Player player)
        {
            if (!doNotCenterInteractiveMapOnPlayer.Value || minimap == null || player == null)
                return;

            lastMapCenter = player.transform.position + minimap.m_mapOffset;
            hasLastMapCenter = true;
        }

        internal static void ResetSession()
        {
            hiddenPins.Clear();
            active = false;
            hasLastMapCenter = false;
            lastMapCenter = Vector3.zero;
            lastPlayerPosition = Vector3.zero;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
        private static class Minimap_SetMapMode_ResetInteractiveState
        {
            private static void Prefix(Minimap __instance, Minimap.MapMode mode)
            {
                if (active && __instance.m_mode == Minimap.MapMode.Large && mode != Minimap.MapMode.Large)
                    SaveCurrentMapCenter(__instance, Player.m_localPlayer);
            }

            private static void Postfix(Minimap __instance)
            {
                if (!active || __instance.m_mode == Minimap.MapMode.Large)
                    return;

                ResetHiddenPins(__instance);
                active = false;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnDestroy))]
        private static class Minimap_OnDestroy_ResetInteractiveState
        {
            private static void Postfix()
            {
                hiddenPins.Clear();
                active = false;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap))]
        private static class Minimap_UpdateMap_KeepConfiguredCenter
        {
            private static void Prefix(Minimap __instance, Player player)
            {
                if (!IsOpen || !doNotCenterInteractiveMapOnPlayer.Value || player == null)
                    return;

                Vector3 playerPosition = player.transform.position;
                __instance.m_mapOffset += lastPlayerPosition - playerPosition;
                lastPlayerPosition = playerPosition;
            }

            private static void Postfix(Minimap __instance, Player player)
            {
                if (IsOpen)
                    SaveCurrentMapCenter(__instance, player);
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerMarker))]
        private static class Minimap_UpdatePlayerMarker_ApplyInteractiveVisibility
        {
            private static void Postfix(Minimap __instance)
            {
                ApplyPlayerMarkerVisibility(__instance);
            }
        }


        [HarmonyPatch(typeof(Minimap), nameof(Minimap.DelayActivation))]
        private static class Minimap_DelayActivation_KeepFilteredPinNamesHidden
        {
            private static void Postfix(GameObject go, ref IEnumerator __result)
            {
                if (__result != null)
                    __result = KeepPinNameHidden(__result, go);
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
        private static class Minimap_UpdatePins_ApplyInteractiveFilters
        {
            private static void Postfix(Minimap __instance)
            {
                if (!IsOpen || __instance.m_pins == null)
                    return;

                foreach (Minimap.PinData pin in __instance.m_pins)
                {
                    bool shouldHide = applyPinFiltersToInteractiveMap.Value && !MapMaker.ShouldShowPin(pin);

                    if (shouldHide)
                    {
                        hiddenPins.Add(pin);
                        HidePinVisuals(pin);
                    }
                    else if (hiddenPins.Remove(pin))
                    {
                        __instance.DestroyPinMarker(pin);
                        __instance.m_pinUpdateRequired = true;
                    }
                }
            }
        }
    }
}
