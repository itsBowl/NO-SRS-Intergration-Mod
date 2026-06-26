using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using NO_SRS.Core;
using UnityEngine;

namespace NO_SRS;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal new static ManualLogSource Logger;

	private static Harmony harmony;
	
	#region CFG_SETTINGS

	private static ConfigEntry<bool>  _enable;
	private static ConfigEntry<int>   _width;				
	private static ConfigEntry<float> _HMD_PosX;
	private static ConfigEntry<float> _HMD_PosY;
	private static ConfigEntry<float> _HMD_Scale;

	private static ConfigEntry<Color> _HMD_Colour;
	private static ConfigEntry<Color> _HMD_Text_Colour;
	private static ConfigEntry<Color> _HMD_No_Voice_Colour;
	private static ConfigEntry<Color> _HMD_Receiving_Colour;
	
	internal static bool enable => _enable.Value;
	internal static Vector2 hmdPos => new Vector2(_HMD_PosX.Value, _HMD_PosY.Value);
	internal static float hmdScale => _HMD_Scale.Value;
	internal static Color hmdColour => _HMD_Colour.Value;
	internal static Color hmdTextColour => _HMD_Text_Colour.Value;
	internal static Color hmdNoVoiceColour => _HMD_No_Voice_Colour.Value;
	internal static Color hmdReceivingColour => _HMD_Receiving_Colour.Value;
	internal static float width => _width.Value;
	
	#endregion
        
	private void Awake()
	{
		Logger = base.Logger;
		harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
		Resources.init();

		_enable = Config.Bind<bool>("General", "Enable/Disable", false);
		_HMD_PosX = Config.Bind<float>("Translation", "HMD X Position", 1600.0f, 
			new ConfigDescription("Set X for the HMD Element", new AcceptableValueRange<float>(0, 3160.0f)));
		_HMD_PosY = Config.Bind("Translation", "HMD Y Position", 0.0f, 
			new ConfigDescription("Set Y for the HMD Element", new AcceptableValueRange<float>(0, 2140.0f)));
		_HMD_Scale = Config.Bind("General", "HMD Scale", 1.0f, 
			new ConfigDescription("Scale the HMD element", new AcceptableValueRange<float>(0.1f, 3.0f)));
		_HMD_Colour = Config.Bind("Colours", "HMD Background Colour", 
			new Color(0.2f, 1.0f, 0.2f, 0.75f), "Set background colour for the HMD");
		_HMD_Text_Colour = Config.Bind("Colours", "HMD Text Colour", 
			new Color(0.2f, 1.0f, 0.2f, 1.0f), "Set text colour for the HMD");
		_HMD_No_Voice_Colour = Config.Bind("Colours", "HMD No Voice Colour", 
			new Color(1.0f, 0.1f, 0.2f, 1.0f), "Set no voice colour for the HMD");
		_HMD_Receiving_Colour = Config.Bind("Colours", "HMD Transmitting Colour", 
			new Color(0.1f, 0.1f, 1.0f, 1.0f), "Set transmitting colour for the HMD");
		
		SRSRadioReader.init(Logger);
		if (!SRSRadioReader.instance.findInstance())
		{
			Logger.LogError($"SRSRadioReader failed to find SRS");
		}

		try
		{
			harmony.PatchAll();
		}
		catch (Exception e)
		{
			Logger.LogError($"Patch failed: {e.Message}\n{e.StackTrace}");
		}
	}

	private void OnDestroy()
	{
		
	}

	private void OnApplicationQuit()
	{
		
	}
}
