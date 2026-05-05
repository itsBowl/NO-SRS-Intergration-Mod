using System;
using HarmonyLib;
using UnityEngine;
using NO_SRS.UI;


namespace NO_SRS.Data;


public static class SRSComponent
{
    private static bool init = false;
    private static SRSWindow srsWindow;

    [HarmonyPatch(typeof(MainMenu), "Start")]
    public static class OnPlatformStart
    {
        public static void Postfix()
        {
            //if (init || !Plugin.enablePlugin.Value) return;
            Plugin.Logger.LogInfo("[SRS] Initialising window");

            try
            {
                GameObject window = new GameObject("SRSWindow");
                srsWindow = window.AddComponent<SRSWindow>();
                UnityEngine.Object.DontDestroyOnLoad(window);
                init = true;
                Plugin.Logger.LogInfo("[SRS] Window initialized");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[SRS] Error initialising windows: {e.Message}\n{e.StackTrace}");
            }
        }
    }

    [HarmonyPatch(typeof(TacScreen), "Update")]
    public static class OnPlatformUpdate
    {
        public static void Postfix()
        {
            if (!init || !Plugin.enablePlugin.Value) return;

            if (!SRSRadioReader.instance.initialised)
            {
                if (!SRSRadioReader.instance.findInstance())
                {
                    Plugin.Logger.LogError($"[SRS] Could not find SRS instance");
                }
                return;
            }

            try
            {
                srsWindow?.Update();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[SRS] Error updating windows: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
