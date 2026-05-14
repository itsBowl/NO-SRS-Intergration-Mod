using HarmonyLib;
using NO_SRS;
using NO_SRS.UI;
using UnityEngine;

namespace NO_SRS.UI;
[HarmonyPatch(typeof(HeadMountedDisplay), "Start")]
public class SRS_HUD
{
    private static GameObject target;
    public static SRS_Display instance;

    static void Prefix(HeadMountedDisplay __instance)
    {
        if (!Plugin.HMD) return;

        instance = null;

        target = new GameObject("SRS_HMD");
        HeadMountedDisplay hmd = SceneSingleton<HeadMountedDisplay>.i;
        
        instance = target.AddComponent<SRS_Display>();
        var Rt = hmd.gameObject.GetComponent<RectTransform>();
        instance.rectTransform.SetParent(Rt, false);
        target.SetActive(true);
        for (int i = 0; i < Rt.childCount; i++)
        {
            var c = Rt.GetChild(i);
            if (c.gameObject.name == "TopRightPanel")
            {
                instance.topRightPannel = c.gameObject.GetComponent<RectTransform>();
            }
        }
    }
}