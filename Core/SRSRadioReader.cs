using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BepInEx.Logging;
using Microsoft.Diagnostics.Runtime;



namespace NO_SRS.Core;

public class SRSRadioReader
{
    public static SRSRadioReader instance { get; private set; }
    public bool initialised { get; set; }
    
    private readonly ManualLogSource log;
    private IntPtr procHandle = IntPtr.Zero;
    private float RADIO_RECEIVING_TIMEOUT = 300.0f;

    public Dictionary<string, List<(string name, double freq)>> serverPresetChannels;
    

    private Task receivingStateTask = null;
    
    #region SRS_MEMORY_STUFF
    //addresses
    private ulong clientStateInstanceAddr;
    private ulong syncedSettingAddress;
    //state offsets
    private ulong connectedOffset;
    private ulong radioInfoOffset;
    private ulong sendingStateOffset;
    private ulong receivingStateOffset;
    //radio offsets
    private ulong radioArrayOffset;
    private ulong selectedRadioOffset;
    private ulong radioFrequencyOffset;
    private ulong radioChannelOffset;
    private ulong radioModOffset;
    private ulong radioReceivingSentByOffset;
    private ulong radioLastReceivedOffset;
    //transmission offsets
    private ulong sendingOffset;
    private ulong sendingOnOffset;
    //setting and names offsets
    private ulong serverSyncedSettingsOffset;
    private ulong presetChannelNamesOffset;
    private ulong serverPresetChannelNamesOffset;
    private ulong serverPresetFrequencysOffset;
    #endregion
    
    #region CLR_OFFSETS
    private const ulong CLR_HEADER = 8;
    private const ulong ARRAY_LENGTH_OFFSET = 8;
    private const ulong ARRAY_ELEMENTS_OFFSET = 16;
    private const ulong STRING_OFFSET = 4;
    private const ulong ENTRY_STRIDE = 24;
    private const ulong ENTRY_KEY_OFFSET = 0;
    private const ulong ENTRY_VALUE_OFFSET = 8;
    private const ulong ENTRY_NEXT_OFFSET = 20;
    private const ulong LIST_ITEMS_OFFSET = 0;
    private const ulong LIST_SIZE_OFFSET = 8;
    private const ulong DICT_COUNT_OFFSET = 48;
    private const ulong DICT_ENTRIES_OFFSET = 8;
    #endregion
    
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
        
    private const uint PROCESS_VM_READ = 0x0010;

    public static void init(ManualLogSource logger)
    {
        if (instance == null) instance = new SRSRadioReader(logger);
        logger.LogInfo("Initializing SRS Radio Reader");
        
    }

    private SRSRadioReader(ManualLogSource logger)
    {
        log = logger;
    }
    public bool findInstance()
    {
        
        try
        {
            log.LogInfo("Finding SRS");
            var proc = Process.GetProcessesByName("SR-ClientRadio").FirstOrDefault();
            if (proc == null)
            {
                log.LogWarning("Failed to capture SR-ClientRadio");
                return false;
            }
            
            procHandle = OpenProcess(PROCESS_VM_READ, false, proc.Id);
            if (procHandle == IntPtr.Zero)
            {
                log.LogWarning("Failed to open SR-ClientRadio Process");
                return false;
            }

            var tgt = DataTarget.AttachToProcess(proc.Id, false);
            var runtime = tgt.ClrVersions[0].CreateRuntime();
            var heap = runtime.Heap;
            bool capturedClient = false;
            bool capturedServer = false;
            foreach (var obj in heap.EnumerateObjects())
            {
                switch (obj.Type?.Name)
                {
                    case "Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons.ClientStateSingleton":
                    {
                        log.LogInfo($"ClientStateSingleton found!");
                        clientStateInstanceAddr = obj.Address;
                        var type = obj.Type;
                        connectedOffset = getOffset(type, "isConnected");
                        radioInfoOffset = getOffset(type, "<DcsPlayerRadioInfo>k__BackingField");
                        sendingStateOffset = getOffset(type, "<RadioSendingState>k__BackingField");
                        receivingStateOffset = getOffset(type, "<RadioReceivingState>k__BackingField");

                        var radioField = type.GetFieldByName("<DcsPlayerRadioInfo>k__BackingField");
                        if (radioField == null)
                        {
                            log.LogWarning("Failed to find radio field");
                            return false;
                        }

                        var radioType = radioField.Type;
                        radioArrayOffset = getOffset(radioType, "radios");
                        selectedRadioOffset = getOffset(radioType, "selected");
                        log.LogInfo($"Radio Array Offset: {radioArrayOffset}; selected: {selectedRadioOffset}");
                        capturedClient = true;
                        break;
                    }
                    case "Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Singletons.SyncedServerSettings":
                    {
                        syncedSettingAddress = obj.Address;
                        log.LogInfo($"Synced Settings address: {serverSyncedSettingsOffset}");
                        var type = obj.Type;
                        serverSyncedSettingsOffset = obj.Address;
                        presetChannelNamesOffset = getOffset(type, "<ServerPresetChannels>k__BackingField");
                        log.LogInfo($"Preset Channel Names: {presetChannelNamesOffset}");
                        capturedServer = true;
                        break;
                    }
                    case "Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS.Models.DCSState.DCSRadio"
                        when radioFrequencyOffset == 0:
                    {
                        var type = obj.Type;
                        log.LogInfo($"DCSRadio type: {type}");
                        radioFrequencyOffset = getOffset(type, "freq");
                        radioChannelOffset = getOffset(type, "channel");
                        radioModOffset = getOffset(type, "modulation");
                        log.LogInfo($"Frequency: {radioFrequencyOffset}; channel: {radioChannelOffset}; modulation: {radioModOffset}");
                        break;
                    }
                    case "Ciribob.DCS.SimpleRadio.Standalone.Client.Network.Models.RadioSendingState"
                        when sendingOffset == 0:
                    {
                        var type = obj.Type;
                        log.LogInfo($"RadioSendingState type: {type}");
                        sendingOffset = getOffset(type, "<IsSending>k__BackingField");
                        sendingOnOffset = getOffset(type, "<SendingOn>k__BackingField");
                        log.LogInfo($"Sending offset: {sendingOffset}; sending on: {sendingOnOffset}");
                        break;
                    }
                    case "Ciribob.DCS.SimpleRadio.Standalone.Common.Models.RadioReceivingState"
                        when radioLastReceivedOffset == 0:
                    {
                        receivingStateOffset = obj.Address;
                        var type = obj.Type;
                        log.LogInfo($"RadioReceivingState type: {type}; 0x{receivingStateOffset}");
                        radioReceivingSentByOffset = getOffset(type, "<SentBy>k__BackingField");
                        radioLastReceivedOffset = getOffset(type, "<LastReceivedAt>k__BackingField");
                        log.LogInfo($"RadioReceivingState offsets — sentBy: {radioReceivingSentByOffset}, lastReceived: {radioLastReceivedOffset}");
                        break;
                    }
                    case "Ciribob.DCS.SimpleRadio.Standalone.Common.Models.Player.ServerPresetChannel"
                        when serverPresetFrequencysOffset == 0: //we don't use name here because name is at offset 0
                    {
                        var type = obj.Type;
                        log.LogInfo($"ServerPresetChannel type: {type}");
                        serverPresetChannelNamesOffset = getOffset(type, "<Name>k__BackingField");
                        serverPresetFrequencysOffset   = getOffset(type, "<Frequency>k__BackingField");
                        log.LogInfo($"ServerPresetChannel offsets — name: {serverPresetChannelNamesOffset}, freq: {serverPresetFrequencysOffset}");
                        break;
                    }
                }
            }

            if (capturedClient && capturedServer)
            {
                initialised = true;
                serverPresetChannels = readServerPresetChannels();
                receivingStateTask = findReceivingStateOffsets();
                return true;
            }

            return false;


        }
        catch (Exception ex)
        {
            log.LogError($"Find Instance Error: {ex.Message}");
            return false;
        }
    }

    private Dictionary<string, List<(string name, double freq)>> readServerPresetChannels()
    {
        var res = new Dictionary<string, List<(string name, double freq)>>();
        if (!initialised)
        {
            log.LogError($"Reader not initialised to read server preset radio channels");
            return res;
        }

        if (serverPresetChannels != null)
        {
            log.LogWarning($"Server Preset Channels found: {serverPresetChannels.Count}");
            return serverPresetChannels;
        }

        try
        {
            ulong settingsBase = syncedSettingAddress + CLR_HEADER;
            
            ulong dictionaryPtr = readPtr(settingsBase + presetChannelNamesOffset);

            if (dictionaryPtr == 0)
            {
                log.LogError($"Could not read synced server dictionary");
                return res;
            }
            
            ulong dictionaryBase = dictionaryPtr + CLR_HEADER;

            int count = readI32(dictionaryBase + DICT_COUNT_OFFSET);
            ulong entriesPtr = readPtr(dictionaryBase + DICT_ENTRIES_OFFSET);
            if (entriesPtr == 0 || count <= 0)
            {
                log.LogWarning($"Could not read server preset dictionary ptr/count is {count}");
                return res;
            }
            log.LogInfo($"Found {count} server preset channels");
            log.LogInfo($"Offsets:\tMHz: {serverPresetFrequencysOffset}");
            log.LogInfo($"\t\t\tName: {serverPresetChannelNamesOffset}");
            
            
            //Struct Layout: [OBJ_HEADER][methodTable][len:4][_:4][entries...]
            int entriesLength = readI32(entriesPtr + ARRAY_LENGTH_OFFSET);
            for (int i = 0; i < count; i++)
            {
                ulong entryAddress = entriesPtr + ARRAY_ELEMENTS_OFFSET + (ulong)(i * (int)ENTRY_STRIDE);

                int next = readI32(entryAddress + ENTRY_NEXT_OFFSET);
                if (next == -2) continue;

                ulong keyPtr = readPtr(entryAddress + ENTRY_KEY_OFFSET);

                string key = readStr(keyPtr, out int _);
                if (key == null) continue;

                ulong listPtr = readPtr(entryAddress + ENTRY_VALUE_OFFSET);
                if (listPtr == 0) continue;
                ulong listBase = listPtr + CLR_HEADER;

                ulong itemPtr = readPtr(listBase + LIST_ITEMS_OFFSET);
                int itemLen = readI32(listBase + LIST_SIZE_OFFSET);

                if (itemPtr == 0 || itemLen <= 0) continue;

                var channels = new List<(string name, double freq)>();

                for (int j = 0; j < itemLen; j++)
                {
                    ulong channelPtr = readPtr(itemPtr + ARRAY_ELEMENTS_OFFSET + (ulong)(j * 8));
                    if (channelPtr == 0) continue;
                    ulong channelBase = channelPtr + CLR_HEADER;

                    ulong freqAddress = channelBase + serverPresetFrequencysOffset;
                    double presetFrequency = readF64(freqAddress);
                    ulong namePtr = readPtr(channelBase + serverPresetChannelNamesOffset);
                    string name = readStr(namePtr, out _);
                    //log.LogInfo($"channelPtr: 0x{channelPtr:X}, channelBase: 0x{channelBase:X}, freqAddr: 0x{freqAddress:X}, freq: {presetFrequency}, namePtr: 0x{namePtr}, name:  {name}");
                    if (name != null)
                        channels.Add((name, presetFrequency));
                }

                if (channels.Count > 0) res[key] = channels;
            }
        }
        catch (Exception e)
        {
            log.LogError($"Read Server Preset Channels Error: {e.Message}");
        }
        
        log.LogInfo($"preset channels size: {res.Count}");
        foreach (var e in res)
        {
            log.LogInfo($"Radio: {e.Key}");
            foreach (var (name, freq) in e.Value)
                log.LogInfo($"   {name}: {freq:F3} MHz");
        }
        return res;
    }

    public SRSRadio readState()
    {
        if (procHandle == IntPtr.Zero)
        {
            log.LogError($"Process handle is null");
            return null;
        }

        if (!readBool(clientStateInstanceAddr + CLR_HEADER + connectedOffset))
        {
            log.LogError($"ClientState not found");
            return null;
        }

        try
        {
            ulong singletonBase = clientStateInstanceAddr + CLR_HEADER;

            //radio info
            ulong playerRadioPtr = readPtr(singletonBase + radioInfoOffset);
            if (playerRadioPtr == 0) return null;
            ulong radioInfoBase = playerRadioPtr + CLR_HEADER;

            int selectedRadio = readI16(radioInfoBase + selectedRadioOffset);

            ulong radioArrayPtr = readPtr(radioInfoBase + radioArrayOffset);
            if (radioArrayPtr == 0)
            {
                log.LogError($"Radio Array Not Found");
                return null;
            }

            int radioCount = readI32(radioArrayPtr + ARRAY_LENGTH_OFFSET);
            if (selectedRadio < 0 || selectedRadio >= radioCount) selectedRadio = 0;

            ulong selectedRadioElementAddr = radioArrayPtr + ARRAY_ELEMENTS_OFFSET + (ulong)(selectedRadio * 8);
            ulong selectedRadioPtr = readPtr(selectedRadioElementAddr);

            if (selectedRadioPtr == 0)
            {
                log.LogError($"Selected Radio Not Found");
                return null;
            }

            ulong selectedRadioBase = selectedRadioPtr + CLR_HEADER;

            double freq = readF64(selectedRadioBase + radioFrequencyOffset);
            int mod = readI32(selectedRadioBase + radioModOffset);
            int channel = readI32(selectedRadioBase + radioChannelOffset);

            ulong sendingStatePtr = readPtr(singletonBase + sendingStateOffset);
            bool isSending = false;
            int sendingOn = -1;

            if (sendingStatePtr != 0)
            {
                ulong sendingStateBase = sendingStatePtr + CLR_HEADER;
                isSending = readBool(sendingStateBase + sendingOffset);
                sendingOn = readI32(sendingStateBase + sendingOnOffset);
            }

            string sentBy = null;
            bool isReceiving = false;

            if (receivingStateOffset != 0 && radioReceivingSentByOffset != 0 && radioLastReceivedOffset != 0)
            {
                ulong receivingArrayPtr = readPtr(singletonBase + receivingStateOffset);

                if (receivingArrayPtr != 0)
                {
                    int receivingCount = readI32(receivingArrayPtr + ARRAY_LENGTH_OFFSET);

                    if (selectedRadio >= 0 && selectedRadio < receivingCount)
                    {
                        ulong receivingElemAddr =
                            receivingArrayPtr + ARRAY_ELEMENTS_OFFSET + (ulong)(selectedRadio * 8);
                        ulong receivingPtr = readPtr(receivingElemAddr);

                        if (receivingPtr != 0)
                        {
                            ulong receivingBase = receivingPtr + CLR_HEADER;

                            ulong sentByPtr = readPtr(receivingBase + radioReceivingSentByOffset);
                            sentBy = readStr(sentByPtr, out int _);

                            long lastReceived = readI64(receivingBase + radioLastReceivedOffset);
                            isReceiving = TimeSpan.FromTicks(DateTime.Now.Ticks - lastReceived).TotalMilliseconds <
                                          RADIO_RECEIVING_TIMEOUT;
                        }
                    }
                }
            }
            else if (receivingStateOffset == 0 || radioReceivingSentByOffset == 0 || radioLastReceivedOffset == 0)
            {
                if (receivingStateTask == null || receivingStateTask.IsCompleted)
                    receivingStateTask = findReceivingStateOffsets();
            }

            if (!isReceiving) sentBy = null;

            return new SRSRadio()
            {
                freq = freq,
                mod = mod,
                channel = channel,
                selected = selectedRadio,
                isSending = isSending,
                sendingOn = sendingOn,
                currentSpeaker = sentBy ?? "No voice detected",
                isReceiving = isReceiving,
            };
        }
        catch (Exception ex)
        {
            log.LogError($"Read state failed: {ex.Message}");
            return null;
        }
    }

    private async Task findReceivingStateOffsets()
    {
        await Task.Run(() =>
        {
            try
            {
                var proc = Process.GetProcessesByName("SR-RadioClient").FirstOrDefault();
                if (proc == null) return;

                var tgt = DataTarget.AttachToProcess(proc.Id, false);
                var runtime = tgt.ClrVersions[0].CreateRuntime();

                foreach (var obj in runtime.Heap.EnumerateObjects())
                {
                    if (obj.Type?.Name == "Ciribob.DCS.SimpleRadio.Standalone.Common.Models.RadioDeceivingState")
                        continue;

                    radioReceivingSentByOffset = getOffset(obj.Type, "<SentBy>k__BackingField");
                    radioLastReceivedOffset = getOffset(obj.Type, "<LastReceived>k__BackingField");
                    log.LogInfo(
                        $"RadioDecieing state offsets: {radioReceivingSentByOffset}, {radioLastReceivedOffset}");
                    return;
                }

                log.LogWarning($"Failed to find receiving state again");
            }
            catch (Exception e)
            {
                log.LogError($"Error finding receiving state offsets: {e.Message}");
            }
        });
    }

    private ulong getOffset(ClrType type, string name)
    {
        var field = type?.GetFieldByName(name);
        if (field == null)
        {
            log.LogWarning($"Could not find {name} in type {type?.Name}");
            return 0UL;
        }
        return (ulong)field.Offset;
    }
    
    private string readStr(ulong addr, out int readBytes)
        {
            ulong strBase = addr + CLR_HEADER;
            
            int l = readI32(strBase);
            if (l is <= 0 or > 1024)
            {
                readBytes = 0;
                return null;
            }

            var buf = new byte[l * 2];
            //l*s beacuse C# stores strings as 2 byte char pairs
            //this took me way to long to debug
            ReadProcessMemory(procHandle, (IntPtr)(strBase + STRING_OFFSET), buf, (l * 2), out readBytes);
            return System.Text.Encoding.Unicode.GetString(buf);
        }

        private ulong readPtr(ulong addr)
        {
            var buff = new byte[8];
            ReadProcessMemory(procHandle, (IntPtr)addr, buff, 8, out _);
            return BitConverter.ToUInt64(buff, 0);
        }

        private double readF64(ulong addr)
        {
            var buff = new byte[8];
            ReadProcessMemory(procHandle, (IntPtr)addr, buff, 8, out _);
            return BitConverter.ToDouble(buff, 0);
        }

        private long readI64(ulong addr)
        {
            var buf = new byte[8];
            ReadProcessMemory(procHandle, (IntPtr)addr, buf, 8, out _);
            return BitConverter.ToInt64(buf, 0);
        }

        private uint readU32(ulong addr)
        {
            var buf = new byte[4];
            ReadProcessMemory(procHandle, (IntPtr)addr, buf, 4, out _);
            return BitConverter.ToUInt32(buf, 0);
        }

        private int readI32(ulong addr)
        {
            var buf = new byte[4];
            ReadProcessMemory(procHandle, (IntPtr)addr, buf, 4, out _);
            return BitConverter.ToInt32(buf, 0);
        }

        private short readI16(ulong addr)
        {
            var buf = new byte[2];
            ReadProcessMemory(procHandle, (IntPtr)addr, buf, 2, out _);
            return BitConverter.ToInt16(buf, 0);
        }

        private bool readBool(ulong addr)
        {
            var buf = new byte[1];
            ReadProcessMemory(procHandle, (IntPtr)addr, buf, 1, out _);
            return buf[0] == 1;
        }
    
}