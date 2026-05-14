using System;
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

	private static Harmony harmony;
	
	#region CFG_SETTINGS
	public static ConfigEntry<bool> enablePlugin;
	public static ConfigEntry<int> width;
	public static ConfigEntry<bool> useHMD;
	public static ConfigEntry<int> HMD_PosX;
	public static ConfigEntry<int> HMD_PosY;
	public static ConfigEntry<float> HMD_X;
	public static ConfigEntry<float> HMD_Y;
	public static ConfigEntry<float> HMD_Scale;
	public static ConfigEntry<Color> HMD_Color;
	public static ConfigEntry<Color> HMD_Text_Color;
	public static ConfigEntry<Color> HMD_No_Voice_Color;
	public static ConfigEntry<Color> HMD_Recieving_Color;
	
	

	public static ConfigEntry<Vector2> DEBUG_FREQ_POS;
	public static ConfigEntry<Vector2> DEBUG_RDAIO_POS;
	public static ConfigEntry<Vector2> DEBUG_F_MHZ_POS;
	public static ConfigEntry<Vector2> DEBUG_CSPEAK_POS;
	public static ConfigEntry<Vector2> DEBUG_R_NAME_POS;
	public static ConfigEntry<string> DEBUG_TEXT;
	public static ConfigEntry<int> DEBUG_FONT_SIZE;
	internal static bool HMD => useHMD.Value;
	internal static float hmdScale => HMD_Scale.Value;
	internal static Vector2 hmdPos => new Vector2(HMD_X.Value, HMD_Y.Value);
	internal static string text => DEBUG_TEXT.Value;
	internal static int fontSize => DEBUG_FONT_SIZE.Value;
	
	
	#endregion
	

        
	private void Awake()
	{
		Logger = base.Logger;
		harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
		Resources.init();
		
		enablePlugin = Config.Bind("NONFUNCTIONAL/NEEDS FIX", "Enabled", false, "Enable/Disable the mod");
		width = Config.Bind("UNKN/OLD", "Width", 300, new ConfigDescription("Description", new AcceptableValueRange<int>(100, 300)));
		useHMD = Config.Bind("General", "UseHMD", true, "Enable/Disable the HMD functionality");
		HMD_X = Config.Bind("Position", "HMD X Position", 1600.0f, new ConfigDescription("Set X for the HMD Element", new AcceptableValueRange<float>(0, 3160.0f)));
		HMD_Y = Config.Bind("Position", "HMD Y Position", 0.0f, new ConfigDescription("Set Y for the HMD Element", new AcceptableValueRange<float>(0, 2140.0f)));
		HMD_Scale = Config.Bind("General", "HMD Scale", 1.0f, new ConfigDescription("Scale the HMD element", new AcceptableValueRange<float>(0.1f, 3.0f)));
		HMD_Color = Config.Bind("Colours", "HMD Background Colour", new Color(0.2f, 1.0f, 0.2f, 0.75f), "Set background colour for the HMD");
		HMD_Text_Color = Config.Bind("Colours", "HMD Text Colour", new Color(0.2f, 1.0f, 0.2f, 1.0f), "Set text colour for the HMD");
		HMD_No_Voice_Color = Config.Bind("Colours", "HMD No Voice Colour", new Color(1.0f, 0.1f, 0.2f, 1.0f), "Set no voice colour for the HMD");
		HMD_Recieving_Color = Config.Bind("Colours", "HMD Transmitting Colour", new Color(0.1f, 0.1f, 1.0f, 1.0f), "Set transmitting colour for the HMD");
		
		DEBUG_FREQ_POS		= Config.Bind("DEBUG", "Frequency Position", new Vector2(0, 0), "DEBUG - NOT CONNECTED");
		DEBUG_RDAIO_POS		= Config.Bind("DEBUG", "Radio Position", new Vector2(0, 0), "DEBUG - NOT CONNECTED");
		DEBUG_F_MHZ_POS		= Config.Bind("DEBUG", "MHz Position", new Vector2(0, 0), "DEBUG - NOT CONNECTED");
		DEBUG_CSPEAK_POS	= Config.Bind("DEBUG", "Current Speaker Position", new Vector2(0, 0), "DEBUG - NOT CONNECTED");
		DEBUG_R_NAME_POS	= Config.Bind("DEBUG", "Radio Name Position", new Vector2(0, 0), "DEBUG - NOT CONNECTED");
		DEBUG_FONT_SIZE = Config.Bind("DEBUG", "Font Size", 30, "DEBUG - NOT CONNECTED");
		
		
		SRSRadioReader.init(Logger);
		if (!SRSRadioReader.instance.findInstance())
		{
			Plugin.Logger.LogError("SRSRadioReader didn't find SRS");
		}
		
		try
		{
			Logger.LogInfo("Patching functions");
			harmony.PatchAll();
			
		}
		catch (Exception ex)
		{
			Logger.LogError($"Patch failed: {ex.Message}\n{ex.StackTrace}");
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
