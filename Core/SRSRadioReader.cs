using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Microsoft.Diagnostics.Runtime;
using Debug = UnityEngine.Debug;


namespace NO_SRS.Data
{
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

    public class SRSRadioReader
    {
        public static SRSRadioReader instance { get; private set; }
        public bool initialised { get; private set; } = false;
        public Dictionary<string, List<(string name, double freq)>> serverPresetChannels;

        //List of offsets discovered using ClrMD
        //if the plugin breaks this is the FIRST place to check
        private const ulong SRS_IS_CONNECTED_OFFSET = 116;
        private const ulong DSC_PLAYER_RADIO_INFO_OFFSET = 16;
        private const ulong RADIO_SENDING_STATE_OFFSET = 40;
        private const ulong RADIO_RECEIVING_STATE_OFFSET = 48;

        private const ulong DSC_RADIO_ARRAY_OFFSET = 32;
        private const ulong SELECTED_RADIO_OFFSET = 72;

        private const ulong RADIO_FREQUENCY_OFFSET = 16;
        private const ulong RADIO_CHANNEL_OFFSET = 48;
        private const ulong RADIO_MOD_OFFSET = 64;

        private const ulong SENDING_ON_OFFSET = 8;
        private const ulong SENDING_OFFSET = 16;

        private const ulong RADIO_RECEIVING_SENT_BY_OFFSET = 0;
        private const ulong RADIO_LAST_RECEIVED_OFFSET = 8;
        //private const ulong RECEIVING_RECEIVED_ON_OFFSET   = 16;
        //private const ulong RECEIVING_IS_SECONDARY_OFFSET  = 20;
        
        private const long RADIO_RECEIVING_TIMEOUT_MS = 350;
        
        private const ulong STRING_OFFSET = 4; //C# strings are stored as [i32: len, data]
        
        //ClrMD reads offsets from the start of the fields, not from the start of the object
        //This starts at objectAddr + 8, so we add that using OBJ_HEADER
        //magic numbers bad
        private const ulong OBJ_HEADER = 8;
        
        //This is for reading stuff stored in memory sent from the server once (our server settings)
        private const ulong SERVER_SYNCED_SETTINGS_PRESET_CHANNELS_OFFSET = 32;
        
        //Dictionary offsets
        private const ulong DICT_ENTRIES_OFFSET = 8;
        private const ulong DICT_COUNT_OFFSET = 48;
        
        //Dict Entry struct offsets (note: do not use OBJ_HEADER as they are inline)
        private const ulong ENTRY_KEY_OFFSET = 0;
        private const ulong ENTRY_VALUE_OFFSET = 8;
        private const ulong ENTRY_HASH_OFFSET = 16;
        private const ulong ENTRY_NEXT_OFFSET = 20;
        private const ulong ENTRY_STRIDE = 24;
        
        //List<T> internal offsets
        private const ulong LIST_ITEMS_OFFSET = 0;
        private const ulong LIST_SIZE_OFFSET = 8;
        
        //Array interal offset
        private const ulong ARRAY_LENGTH_OFFSEt = 8;
        private const ulong ARRAY_ELEMENTS_OFFSET = 16;
        
        //ServerPresetChannel offsets
        private const ulong SERVER_PRESET_CHANNEL_NAME_OFFSET = 0;
        private const ulong SERVER_PRESET_CHANNEL_FREQ_OFFSET = 8;
        

        
        
        //use this for vomiting out only a single instance of a debug log
        //very helpful when looking for where transmit is stored
        private bool hasDumped = false;
        

        private IntPtr procHandle = IntPtr.Zero;
        private ulong clientStateInstanceAddress;
        private ulong serverSyncedSettingsAddress;
        private readonly ManualLogSource log;

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
            logger.LogInfo("[NO SRS] initalising memory reading");
        }

        public void shutdown()
        {
            if (procHandle != IntPtr.Zero) CloseHandle(procHandle);
            initialised = false;
        }

        private SRSRadioReader(ManualLogSource logger)
        {
            log = logger;
        }

        public bool findInstance()
        {
            try
            {
                log.LogInfo("[NO SRS] Finding instance");
                var proc = Process.GetProcessesByName("SR-ClientRadio").FirstOrDefault();
                if (proc == null)
                {
                    log.LogWarning("[NO_SRS] Failed to find SRS process, is it running?");
                    return false;
                }

                procHandle = OpenProcess(PROCESS_VM_READ, false, proc.Id);
                if (procHandle == IntPtr.Zero)
                {
                    log.LogWarning("[NO_SRS] Failed to capture process");
                    return false;
                }

                using var tgt = DataTarget.AttachToProcess(proc.Id, false);
                var runtime = tgt.ClrVersions[0].CreateRuntime();

                var heap = runtime.Heap;
                
                // Loop over all objects in the heap and look for the client state singleton
                foreach (var obj in heap.EnumerateObjects())
                {
                    if (obj.Type?.Name != "Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons.ClientStateSingleton")
                        continue;

                    clientStateInstanceAddress = obj.Address;
                    log.LogInfo($"[SRS] ClientStateSingleton found at 0x{clientStateInstanceAddress:X}");
                    initialised = true;
                }
                
                foreach (var obj in heap.EnumerateObjects())
                {
                    if (obj.Type?.Name != "Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Singletons.SyncedServerSettings")
                        continue;

                    serverSyncedSettingsAddress = obj.Address;
                    log.LogInfo($"[SRS] SyncedServerSettings found at 0x{serverSyncedSettingsAddress:X}");
                    break;
                }
                
                

                if (serverSyncedSettingsAddress != 0 && clientStateInstanceAddress != 0)
                {
                    serverPresetChannels = readServerPresetRadios();
                    return true;
                }
                if (serverSyncedSettingsAddress == 0)
                {
                    log.LogError("[SRS] Failed to find the synced server settings");
                }
                log.LogWarning("[NO SRS] Failed to find singleton type");
                return false;
            }
            catch (Exception ex)
            {
                log.LogError($"[NO SRS] find instance error: {ex.Message}");
                return false;
            }
        }

        public SRSRadio readState()
        {
            
            //Some notes (for me, primarily)
            //Ptr = pointer, this is *not* the base address of the fields for an object, but the start of the object
            //Base = this is the base address of the fields for the object pointed to by the representative Ptr
            //      can be accessed with: Base = Ptr + OBJ_HEADER; see OBJ_HEADER for more info
            
            if (!initialised)
            {
                if (!hasDumped) log.LogWarning("[NO SRS] Reader not initialised");
                hasDumped = true;
                return null;
            }
            if (procHandle == IntPtr.Zero)
            {
                if (!hasDumped) log.LogWarning("[NO SRS] Process handle not found/is zero");
                hasDumped = true;
                return null;
            }
            if (!readBool(clientStateInstanceAddress + OBJ_HEADER + SRS_IS_CONNECTED_OFFSET))
            {
                if (!hasDumped) log.LogWarning("[NO SRS] SRS is not connected");
                CloseHandle(procHandle);
                initialised = false;
                serverPresetChannels = null;
                if (!hasDumped) log.LogWarning("[NO SRS] Dumping handle, attempting to reinitialise process");
                hasDumped = true;
                return null;
            }

            //checking the error field
            if (readBool(clientStateInstanceAddress + OBJ_HEADER + SRS_IS_CONNECTED_OFFSET + 1))
            {
                if (!hasDumped) log.LogError($"[NO SRS] SRS connection error");
                hasDumped = true;
            }

            try
            {
                hasDumped = false;
                ulong baseAddr = clientStateInstanceAddress + OBJ_HEADER; //Singleton address
                ulong playerRadioInfoPtr = readPtr(baseAddr + DSC_PLAYER_RADIO_INFO_OFFSET);    //DSCPlayerRadioInfo 

                if (playerRadioInfoPtr == 0) return null;
                ulong playerRadioInfoBase = playerRadioInfoPtr + OBJ_HEADER;    //DSCPlayerRadioInfo fields

                int selectedRadioIndex = readI16(playerRadioInfoBase + SELECTED_RADIO_OFFSET);    //Index of current radio

                ulong dscRadioArrayPtr = readPtr(playerRadioInfoBase + DSC_RADIO_ARRAY_OFFSET);    //Pointer to DSCRadio array
                if (dscRadioArrayPtr == 0)
                {
                    log.LogError($"[NO SRS] Could not read radio array pointer, {baseAddr}->{playerRadioInfoPtr} + {DSC_RADIO_ARRAY_OFFSET}->{dscRadioArrayPtr}");
                    return null;
                }
                
                
                
                int numberOfRadios = readI32(dscRadioArrayPtr + 8);
                if (selectedRadioIndex < 0 || selectedRadioIndex >= numberOfRadios) selectedRadioIndex = 0;

                ulong selectedRadioAddress = dscRadioArrayPtr + 16 + (ulong)(selectedRadioIndex * 8);
                ulong currentRadioPtr = readPtr(selectedRadioAddress);
                
                if (currentRadioPtr == 0)
                {
                    log.LogError($"[NO SRS] Could not read radio element pointer");
                    return null;
                }
                ulong currentRadioBase = currentRadioPtr + OBJ_HEADER;

                double freq = readF64(currentRadioBase + RADIO_FREQUENCY_OFFSET);
                int mod = readI32(currentRadioBase + RADIO_MOD_OFFSET);
                int channel = readI32(currentRadioBase + RADIO_CHANNEL_OFFSET);

                ulong sendingStatePtr = readPtr(clientStateInstanceAddress + OBJ_HEADER + RADIO_SENDING_STATE_OFFSET);
                bool isSending = false;
                int sendingOn = -1;
                
                if (sendingStatePtr != 0)
                {
                    ulong radioSendingStateBase = sendingStatePtr + OBJ_HEADER;
                    isSending = readBool(radioSendingStateBase + SENDING_OFFSET);
                    sendingOn = readI32(radioSendingStateBase + SENDING_ON_OFFSET);
                }
                
                string sentBy = null;
                bool isReceiving = false;
                ulong reciveingArrayPtr = readPtr(clientStateInstanceAddress + OBJ_HEADER + RADIO_RECEIVING_STATE_OFFSET);
                long lastReceived = 0;
                
                if (reciveingArrayPtr != 0)
                {
                    int receivingLength = readI32(reciveingArrayPtr + OBJ_HEADER);

                    if ((selectedRadioIndex >= 0) && (selectedRadioIndex < receivingLength))
                    {
                        ulong receivingElementAddress = reciveingArrayPtr + 16 + (ulong)(selectedRadioIndex * 8);
                        
                        ulong reciveingElementPtr = readPtr(receivingElementAddress);

                        if (reciveingElementPtr != 0)
                        {
                            ulong recivingElementBase = reciveingElementPtr + OBJ_HEADER;
                            
                            ulong sentByPtr = readPtr(recivingElementBase + RADIO_RECEIVING_SENT_BY_OFFSET);
                            int readBytes = 0;
                            sentBy = readStr(sentByPtr, out readBytes);
                            //log.LogWarning($"[NO SRS] string size: {readI32(sentByPtr + OBJ_HEADER)}, read {readBytes} bytes; {sentBy}");

                            lastReceived = readI64(recivingElementBase + RADIO_LAST_RECEIVED_OFFSET);
                            isReceiving = TimeSpan.FromTicks(DateTime.Now.Ticks - lastReceived).TotalMilliseconds <
                                          RADIO_RECEIVING_TIMEOUT_MS;
                        }
                    }
                }
                else
                {
                    log.LogError($"[NO SRS] Could not read recieving array pointer");
                }

                if (!isReceiving)
                {
                    sentBy = null;
                }

                if (sentBy == null)
                {
                    sentBy = "No voice detected";
                }

                return new SRSRadio
                {
                    freq = freq,
                    mod = mod,
                    channel = channel,
                    selected = selectedRadioIndex,
                    isSending = isSending,
                    sendingOn = sendingOn,
                    currentSpeaker = sentBy,
                    isReceiving = isReceiving,
                    
                };

            }
            catch (Exception ex)
            {
                log.LogError($"[NO SRS] readState failure: {ex.Message}");
                initialised = false;
                return null;
            }
        }
        
        public void diagnoseArrayLayout(ulong radiosArrayPtr)
        {
            log.LogInfo($"[SRS] radiosArrayPtr: 0x{radiosArrayPtr:X}");
            
            var buf = new byte[128];
            ReadProcessMemory(procHandle, (IntPtr)radiosArrayPtr, buf, 128, out int read);
    
            log.LogInfo($"[SRS] Bytes read: {read}");
            
            for (int i = 0; i < 128; i += 8)
            {
                var hex = BitConverter.ToString(buf, i, 8).Replace("-", " ");
                var asLong = BitConverter.ToUInt64(buf, i);
                log.LogInfo($"[SRS] +{i:D3}: {hex}  (0x{asLong:X})");
            }
        }

        private Dictionary<string, List<(string name, double freq)>> readServerPresetRadios()
        {
            var res =  new Dictionary<string, List<(string name, double freq)>>();
            if (!initialised || serverSyncedSettingsAddress == 0)
            {
                log.LogError($"[NO SRS] Reader not initialised or server sync settings not found!");
                return res;
            }

            try
            {
                ulong settingsBase = serverSyncedSettingsAddress + OBJ_HEADER;

                ulong dictPtr = readPtr(settingsBase + SERVER_SYNCED_SETTINGS_PRESET_CHANNELS_OFFSET);
                if (dictPtr == 0)
                {
                    log.LogError($"[NO SRS] could not read server preset dictionary pointer");
                    return res;
                }
                
                ulong dictBase = dictPtr + OBJ_HEADER;

                int count = readI32(dictBase + DICT_COUNT_OFFSET);
                ulong entriesPtr = readPtr(dictBase + DICT_ENTRIES_OFFSET);
                if (entriesPtr == 0 || count <= 0)
                {
                    log.LogWarning($"[NO SRS] Could not read server preset dictionary pointer/Count is {count}");
                    return res;
                }
                //Struct Layout: [OBJ_HEADER][methodTable][len:4][_:4][entries...]
                int entriesLength = readI32(entriesPtr + ARRAY_LENGTH_OFFSEt);
                for (int i = 0; i < count; i++)
                {
                    ulong entryAddres = entriesPtr + ARRAY_ELEMENTS_OFFSET + (ulong)(i * (int)ENTRY_STRIDE);

                    int next = readI32(entryAddres + ENTRY_NEXT_OFFSET);
                    if (next == -2) continue;

                    ulong keyPtr = readPtr(entryAddres + ENTRY_KEY_OFFSET);

                    string key = readStr(keyPtr, out int _);
                    if (key == null) continue;

                    ulong listPtr = readPtr(entryAddres + ENTRY_VALUE_OFFSET);
                    if (listPtr == 0) continue;
                    ulong listBase = listPtr + OBJ_HEADER;
                    
                    ulong itemPtr = readPtr(listBase + LIST_ITEMS_OFFSET);
                    int itemLen = readI32(listBase + LIST_SIZE_OFFSET);

                    if (itemPtr == 0 || itemLen <= 0) continue;
                    
                    var channels = new List<(string name, double freq)>();

                    for (int j = 0; j < itemLen; j++)
                    {
                        ulong channelPtr = readPtr(itemPtr + ARRAY_ELEMENTS_OFFSET + (ulong)(j * 8));
                        if (channelPtr == 0) continue;
                        ulong channelBase = channelPtr + OBJ_HEADER;
                        
                        ulong freqAddress    = channelBase + SERVER_PRESET_CHANNEL_FREQ_OFFSET;
                        double presetFrequency = readF64(freqAddress);
                        ulong namePtr  = readPtr(channelBase + SERVER_PRESET_CHANNEL_NAME_OFFSET);
                        string name    = readStr(namePtr, out _);
                        log.LogInfo($"[SRS] channelPtr: 0x{channelPtr:X}, channelBase: 0x{channelBase:X}, freqAddr: 0x{freqAddress:X}, freq: {presetFrequency}, namePtr: 0x{namePtr}, name:  {name}");
                        if (name != null)
                            channels.Add((name, presetFrequency));
                    }
                    if (channels.Count > 0) res[key] = channels;
                }
            }
            catch (Exception e)
            {
                log.LogError($"[NO SRS] Exception Reading Preset Channels: {e.Message}");
            }
            log.LogInfo($"[NO SRS] preset channels size: {res.Count}");
            foreach (var e in res)
            {
                log.LogInfo($"[SRS] Radio: {e.Key}");
                foreach (var (name, freq) in e.Value)
                    log.LogInfo($"[SRS]   {name}: {freq:F3} MHz");
            }
            return res;
        }
        
        private string readStr(ulong addr, out int readBytes)
        {
            ulong strBase = addr + OBJ_HEADER;
            
            int l = readI32(strBase);
            if (l is <= 0 or > 1024)
            {
                readBytes = 0;
                return null;
            }

            var buf = new byte[l * 2];
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
    
    
}