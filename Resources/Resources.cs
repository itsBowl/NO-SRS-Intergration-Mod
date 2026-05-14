using System.IO;
using BepInEx;
using UnityEngine;


namespace NO_SRS;

public static class Resources
{
    public static Texture2D testTexture;

    public static void init()
    {
        testTexture = loadTexture("base.png", 300, 200);
    }

    static Texture2D loadTexture(string path, int w, int h)
    {
        Texture2D tmp = new Texture2D(w, h);
        byte[] data = loadAsset(path);
        ImageConversion.LoadImage(tmp, data);
        return tmp;
    }

    static byte[] loadAsset(string p)
    {
        Plugin.Logger.LogInfo($"Loading asset: {p}");
        string path = Path.Combine(Paths.PluginPath, "NO_SRS", "assets", p);
        byte[] ret = File.ReadAllBytes(path);
        return ret;
    }
}