using System;
using NuclearOption;
using UnityEngine;
using UnityEngine.UI;


namespace NO_SRS.Core;

public class SRS_Display : MonoBehaviour
{
    public RectTransform rectTransform = null;
    CanvasRenderer canvasRenderer;
    private RawImage radioBase;

    public RectTransform rectTf;
    public RectTransform topRightPannel;
    
    private Font font = Font.CreateDynamicFontFromOSFont("Consolas", 30);
   
    private Text frequencyText;
    private Text frequencyName;
    private Text currentSpeaker;
    private Text currentRadioNumber;
    private Text frequencyLabel;

    private Vector2 FREQUENCY_LABEL_OFFSET = new Vector2(-105.0f, 66.66f);
    private Vector2 RADIO_NUMBER_OFFSET = new Vector2(-30.0f, 66.66f);
    private Vector2 FREQUENCY_OFFSET = new Vector2(70.0f, 66.66f);
    private Vector2 NAME_OFFSET = new Vector2(0.0f, 0.0f);
    private Vector2 CURRENT_SPEAKER_OFFSET = new Vector2(0.0f, -66.66f);

    private int FREQUENCY_LABEL_SIZE = 35;
    private int RADIO_NUMBER_SIZE = 35;
    private int FREQUENCY_SIZE = 35;
    private int FREQ_NAME_SIZE = 25;
    private int TRANSMITTING_SIZE = 25;
    
    void Awake()
    {
        if (!SRSRadioReader.instance.initialised)
        {
            SRSRadioReader.instance.findInstance();
        }
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = gameObject.AddComponent<RectTransform>();
        }
        rectTransform.pivot = new Vector2(1, 1);
        rectTransform.SetRectSize(new Vector2(300, 200));
        rectTransform.anchorMax = new Vector2(1,1);
        rectTransform.anchorMin = new Vector2(1,1);

        canvasRenderer = gameObject.AddComponent<CanvasRenderer>();
        
        radioBase = gameObject.AddComponent<RawImage>();
        radioBase.texture = Resources.testTexture;
        radioBase.material = new Material(Shader.Find("UI/Default"));
        radioBase.color = new Color(0.2f, 0.9f, 0.2f, 1.0f);
        
        frequencyLabel = addNewTextObject("Label", FREQUENCY_LABEL_SIZE, Color.white, TextAnchor.MiddleCenter);
        frequencyText = addNewTextObject("Frequency", FREQUENCY_SIZE, Color.white, TextAnchor.MiddleCenter);
        frequencyName = addNewTextObject("FrequencyName", FREQ_NAME_SIZE, Color.white, TextAnchor.MiddleCenter);
        currentSpeaker = addNewTextObject("CurrentSpeaker", TRANSMITTING_SIZE, Color.white, TextAnchor.MiddleCenter);
        currentRadioNumber = addNewTextObject("CurrentRadioNumber", RADIO_NUMBER_SIZE, Color.white, TextAnchor.MiddleCenter);
    }

    //Added this because I need more than one of these
    Text addNewTextObject(string name, int size, Color color, TextAnchor anchor)
    {
        var obj = new GameObject("Frequency_Name");
        obj.transform.SetParent(gameObject.transform, false);
        RectTransform transform = obj.AddComponent<RectTransform>();
        transform.anchorMin = new Vector2(0, 0);
        transform.anchorMax = new Vector2(1, 1);
        transform.offsetMin = new Vector2(5, 5);
        transform.offsetMax = new Vector2(-5, -5);
        var text = obj.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        return text;
    }
    
    //TODO: HMD PANEL MOVES VERTICALLY -- resolved, issue was with bad variable that was causing a scrolling window
    void Update()
    {
        if (!Plugin.enable) return;
        float w = 300.0f * Plugin.hmdScale;
        float h = 200.0f * Plugin.hmdScale;
        canvasRenderer.SetRectSize(new Vector2(w, h));
        var radio = SRSRadioReader.instance.readState();
        
        rectTransform.anchoredPosition = new Vector3(-Plugin.hmdPos.x,
            topRightPannel.anchoredPosition.y - topRightPannel.rect.height - Plugin.hmdPos.y);
        radioBase.color = Plugin.hmdColour;
        
        frequencyLabel.text = "Freq";
        frequencyLabel.rectTransform.anchoredPosition = FREQUENCY_LABEL_OFFSET * Plugin.hmdScale;
        frequencyLabel.fontSize = Mathf.RoundToInt(FREQUENCY_LABEL_SIZE * Plugin.hmdScale);
        frequencyLabel.color = Plugin.hmdTextColour;
        
        frequencyText.text = radio.freqMhz;
        frequencyText.rectTransform.anchoredPosition = FREQUENCY_OFFSET * Plugin.hmdScale;
        frequencyText.fontSize = Mathf.RoundToInt(FREQUENCY_SIZE * Plugin.hmdScale);
        frequencyText.color = Plugin.hmdTextColour;
        
        frequencyName.text = getPresetName(radio.freq);
        frequencyName.rectTransform.anchoredPosition = NAME_OFFSET * Plugin.hmdScale;
        frequencyName.fontSize = Mathf.RoundToInt(FREQ_NAME_SIZE * Plugin.hmdScale);
        frequencyName.color = Plugin.hmdTextColour;
        
        string speaker = radio.currentSpeaker;
        if (radio.isSending)
        {
            speaker = "TRANSMITTING";
            currentSpeaker.color = Plugin.hmdTextColour;
        }
        else if (radio.isReceiving)
        {
            currentSpeaker.color = Plugin.hmdReceivingColour;
        }
        else
        {
            currentSpeaker.color = Plugin.hmdNoVoiceColour;
        }
        
        currentSpeaker.text = speaker;
        currentSpeaker.rectTransform.anchoredPosition = CURRENT_SPEAKER_OFFSET * Plugin.hmdScale;
        currentSpeaker.fontSize = Mathf.RoundToInt(TRANSMITTING_SIZE * Plugin.hmdScale);
        
        var radioNumberString = $"R{radio.selected}";
        currentRadioNumber.text = radioNumberString;
        currentRadioNumber.rectTransform.anchoredPosition = RADIO_NUMBER_OFFSET * Plugin.hmdScale;
        currentRadioNumber.fontSize = Mathf.RoundToInt(RADIO_NUMBER_SIZE * Plugin.hmdScale);
    }
    
    private string getPresetName(double freq, double tol = 1000)
    {
        foreach (var k in SRSRadioReader.instance.serverPresetChannels)
        {
            foreach (var p in k.Value)
            {
                var pFreq = p.freq * 1_000_000.0;
                if (Math.Abs(freq - pFreq) < tol)
                {
                    return p.name.Trim();
                }
            }
        }

        return "--";
    }

}