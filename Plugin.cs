using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using NO_SRS.Data;
using UnityEngine;

namespace NO_SRS;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal new static ManualLogSource Logger;

	public static Harmony harmony;
	public static ConfigEntry<bool> enablePlugin;
	public static ConfigEntry<int> width;
	

        
	private void Awake()
	{
		Logger = base.Logger;
		harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
		enablePlugin = Config.Bind("General", "Enabled", false, "Enable/Disable the mod");
		width = Config.Bind("General", "Width", 300, new ConfigDescription("Description", new AcceptableValueRange<int>(100, 300)));
		SRSRadioReader.init(Logger);
		if (!SRSRadioReader.instance.findInstance())
		{
			Plugin.Logger.LogError("[NO SRS] SRSRadioReader didn't find SRS");
		}
		{
			try
			{
				harmony.PatchAll(typeof(SRSComponent.OnPlatformStart));
				harmony.PatchAll(typeof(SRSComponent.OnPlatformUpdate));
			}
			catch (Exception ex)
			{
				Logger.LogError($"[NO_SRS] Patch failed: {ex.Message}\n{ex.StackTrace}");
			}
		}
	}

	private void OnDestroy()
	{
		SRSRadioReader.instance?.shutdown();
	}

	private void OnApplicationQuit()
	{
		SRSRadioReader.instance?.shutdown();
	}
}
