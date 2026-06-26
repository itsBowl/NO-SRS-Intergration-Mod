using System;

namespace NO_SRS.Core;

public class SRSRadio
{
    public double freq = 0;
    public int mod = 0;
    public int channel = 0;
    public bool isSending = false;
    public int sendingOn = 0;
    public int selected = 0;
    public string currentSpeaker = "";
    public bool isReceiving = false;

    public string freqMhz => Math.Round(freq / 1000000.0, 3).ToString("F3");
    public string modName => mod switch
    {
        0 => "AM", 
        1 => "FM", 
        2 => "ICM", 
        _ => "-"
    };

    public bool isDisabled => mod == 3;
    public bool isIntercom => mod == 2;
}