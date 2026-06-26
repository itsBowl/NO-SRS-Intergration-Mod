using HarmonyLib;

using UnityEngine;

namespace NO_SRS.Core;

[HarmonyPatch(typeof(HeadMountedDisplay), "Start")]
public class SRS_HUD
{
    private static GameObject tgt;
    public static SRS_Display instance;

    static void Postfix(HeadMountedDisplay __instance)
    {
        instance = null;

        tgt = new GameObject("SRS_HMD");
        HeadMountedDisplay hmd = SceneSingleton<HeadMountedDisplay>.i;

        instance = tgt.AddComponent<SRS_Display>();
        RectTransform Rt = null;
        //remember to check if the rectTransform exists before we try and add another one
        //this is a fatal error in unity land
        Rt = hmd.gameObject.GetComponent<RectTransform>();
        if (Rt == null) Rt = hmd.gameObject.AddComponent<RectTransform>();
        instance.rectTransform.SetParent(Rt, false);
        tgt.SetActive(true);
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