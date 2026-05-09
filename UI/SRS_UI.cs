using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NO_SRS.Data;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace NO_SRS.UI
{
    public class SRSWindow : MonoBehaviour
    {
        private bool showWindow = false;
        private Rect windowRect;
        private GUIStyle windowStyle;
        private bool styleInit = false;
        private SRSData srsData;
        private GUIStyle redBoxStyle;
        private GUIStyle greenBoxStyle;
        private GUIStyle blueBoxStyle;
        private float timeSinceCheck = 0;
        

        public void Update()
        {
            if (!SRSRadioReader.instance.initialised)
            {
                timeSinceCheck +=  Time.deltaTime;
            }

            if (timeSinceCheck > 2.5f)
            {
                timeSinceCheck = 0;
                SRSRadioReader.instance.findInstance();
            }
            
            showWindow = Plugin.enablePlugin.Value;
            updateSrsData();
        }

        void updateSrsData()
        {
            var radio = SRSRadioReader.instance?.readState();
            if (radio == null || radio.freq == 0)
            {
                srsData.channel = "RADIO NOT FOUND";
                srsData.transmitting = false;
                srsData.activeVoice = "—";
                return;
            }
            string channelName = getPresetName(radio.freq);
            srsData.channel = radio.isIntercom || radio.isDisabled
                ? "No active radio"
                : $"R{radio.selected} {channelName} : {radio.freqMhz} MHz {radio.modName}" +
                  (radio.channel >= 0 ? $" Ch{radio.channel}" : "");
            srsData.transmitting = radio.isSending;
            srsData.reciving = radio.isReceiving;
            srsData.activeVoice = radio.currentSpeaker;

        }

        private string getPresetName(double freq, double tol = 10000)
        {
            foreach (var k in SRSRadioReader.instance.serverPresetChannels)
            {
                foreach (var p in k.Value)
                {
                    if (Math.Abs(freq - p.freq) < tol)
                    {
                        return p.name.Trim();
                    }
                }
            }

            return "--";
        }

        void OnGUI()
        {
            if (!showWindow)
            {
                return;
            }
            
            if (windowRect.x < 0 || windowRect.y < 0 || 
                windowRect.x > Screen.width - windowRect.width || 
                windowRect.y > Screen.height - windowRect.height)
            {
                windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
            }

            if (!styleInit)
            {
                InitStyle();
            }
            
            try
            {
                windowRect = GUILayout.Window(
                    12345,
                    windowRect,
                    DrawWindow,
                    "SRS",
                    windowStyle,
                    GUILayout.MinWidth(Plugin.width.Value)
                );
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"[SRS] Error in OnGUI: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void InitStyle()
        {
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = MakeTex(2, 2, new Color(0.04f, 0.04f, 0.06f, 1f));;
            
            var redBox = MakeTex(4, 4, new Color(1.0f, 0.0f, 0.0f, 1f));
            var greenBox = MakeTex(4, 4, new Color(0.0f, 1.0f, 0.0f, 1f));
            var blueBox = MakeTex(4, 4, new Color(0.0f, 1.0f, 1.0f, 1f));
            
            redBoxStyle = new GUIStyle(GUI.skin.label);
            redBoxStyle.normal.background = redBox;
            greenBoxStyle = new GUIStyle(GUI.skin.label);
            greenBoxStyle.normal.background = greenBox;
            blueBoxStyle = new GUIStyle(GUI.skin.label);
            blueBoxStyle.normal.background = blueBox;
            
            styleInit = true;
        }

        
        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{srsData.channel}");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Box(GUIContent.none, srsData.transmitting ? greenBoxStyle : (srsData.reciving ? blueBoxStyle : redBoxStyle), GUILayout.Width(16), GUILayout.Height(16));
            GUILayout.Label($"{srsData.activeVoice}");
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        struct SRSData
        {
            public string channel;
            public string activeVoice;
            public bool transmitting;
            public bool reciving;
        }
    }
}