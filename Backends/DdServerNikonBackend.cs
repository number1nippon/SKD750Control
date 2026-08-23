using System;
using System.Collections.Generic;
using System.IO;
using Nikon;
using SKD750Control.TransferProtocol;
using SKD750Control.TransferProtocol.DDServer;

namespace SKD750Control.Backends
{
    /// <summary>
    /// Camera backend that talks to a Nikon camera through a ddserver
    /// (DslrDashboardServer) network bridge using raw PTP, instead of the
    /// direct-USB nikoncswrapper MAID SDK path.
    ///
    /// IMPORTANT: ddserver forwards raw PTP; it does not implement Nikon's
    /// proprietary MAID SDK semantics. The PTP operation codes and property
    /// codes below are the commonly documented Nikon vendor-extension codes
    /// (as used by libgphoto2/digiCamControl) but have not been validated
    /// against physical hardware in this session. Capability coverage is
    /// best-effort and limited to what raw PTP/Nikon vendor commands expose;
    /// unsupported capabilities log a warning and no-op rather than throwing,
    /// so the rest of the UI keeps working.
    /// </summary>
    public class DdServerNikonBackend : ICameraBackend
    {
        // Standard PTP operation codes
        private const uint PTP_OC_InitiateCapture = 0x100E;
        private const uint PTP_OC_GetDevicePropDesc = 0x1014;
        private const uint PTP_OC_GetDevicePropValue = 0x1015;
        private const uint PTP_OC_SetDevicePropValue = 0x1016;

        // Standard PTP response codes
        private const uint PTP_RC_OK = 0x2001;
        private const uint PTP_RC_DeviceBusy = 0x2019;

        // Nikon vendor-extension operation codes (per libgphoto2 ptp.h / digiCamControl usage)
        private const uint NIKON_OC_AfDrive = 0x90C1;
        private const uint NIKON_OC_StartLiveView = 0x9201;
        private const uint NIKON_OC_EndLiveView = 0x9202;
        private const uint NIKON_OC_GetLiveViewImg = 0x9203;
        private const uint NIKON_OC_ChangeAfArea = 0x9205;

        // Best-effort mapping from MAID capability -> Nikon PTP device property code.
        // Not exhaustive; extend as needed once verified against hardware.
        private static readonly Dictionary<eNkMAIDCapability, uint> CapabilityToPtpProperty = new Dictionary<eNkMAIDCapability, uint>
        {
            { eNkMAIDCapability.kNkMAIDCapability_Sensitivity, 0x500F }, // ISO
            { eNkMAIDCapability.kNkMAIDCapability_Aperture, 0x5007 }, // FNumber
            { eNkMAIDCapability.kNkMAIDCapability_ShutterSpeed, 0x500D }, // ExposureTime
            { eNkMAIDCapability.kNkMAIDCapability_MeteringMode, 0x500B },
        };

        private DdClient _client;
        private DdServerProtocol _protocol;
        private bool _liveViewEnabled;

        /// <summary>ddserver host, e.g. "192.168.1.1".</summary>
        public string Host { get; set; }

        /// <summary>ddserver TCP port (default 4757).</summary>
        public int Port { get; set; } = 4757;

        public event Action<string> DeviceConnected;
        public event Action DeviceDisconnected;
        public event Action<byte[], string> ImageReady;
        public event Action CaptureComplete;
        public event Action<eNkMAIDCapability> CapabilityValueChanged;

        public string DeviceName => _protocol?.Model;

        public bool IsConnected => _protocol != null;

        public bool LiveViewEnabled
        {
            get { return _liveViewEnabled; }
            set
            {
                if (_protocol == null || value == _liveViewEnabled) return;
                if (value)
                {
                    uint rc = _protocol.ExecuteWithNoData(NIKON_OC_StartLiveView);
                    AppLogger.Info($"DdServerNikonBackend: StartLiveView response code=0x{rc:X4}");
                }
                else
                {
                    uint rc = _protocol.ExecuteWithNoData(NIKON_OC_EndLiveView);
                    AppLogger.Info($"DdServerNikonBackend: EndLiveView response code=0x{rc:X4}");
                }
                _liveViewEnabled = value;
            }
        }

        public void Connect()
        {
            if (string.IsNullOrEmpty(Host))
                throw new InvalidOperationException("Host must be set before connecting to a ddserver.");

            _client = new DdClient();
            if (!_client.Open(Host, Port))
                throw new Exception("No ddserver was found at " + Host + ":" + Port);

            var devices = _client.GetDevices();
            if (devices.Count == 0)
                throw new Exception("ddserver reported no connected USB device.");

            _client.Connect(devices[0]);
            _protocol = new DdServerProtocol(_client);

            DeviceConnected?.Invoke(_protocol.Model);
        }

        public void Shutdown()
        {
            try
            {
                if (_liveViewEnabled)
                    LiveViewEnabled = false;
            }
            catch { }

            _protocol?.Disconnect();
            _protocol = null;
            _client = null;
            DeviceDisconnected?.Invoke();
        }

        public byte[] GetLiveViewImage()
        {
            if (_protocol == null) return null;

            // Immediately after StartLiveView the camera can respond DeviceBusy for a
            // short time while it spins up the sensor stream; retry a few times.
            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var resp = _protocol.ExecuteReadData(NIKON_OC_GetLiveViewImg);
                int dataLen = resp?.Data?.Length ?? 0;
                if (resp != null && resp.ErrorCode != PTP_RC_OK && resp.ErrorCode != 0)
                {
                    AppLogger.Warn($"DdServerNikonBackend: GetLiveViewImg attempt {attempt} returned error=0x{resp.ErrorCode:X4} dataLen={dataLen}");
                }
                if (dataLen > 0)
                {
                    // The Nikon vendor payload prefixes the JPEG with a proprietary
                    // header (offsets/size vary by camera model) before the actual
                    // JFIF stream starts. Locate the JPEG SOI marker (0xFF 0xD8) and
                    // strip everything before it so Image.FromStream can decode it.
                    byte[] jpeg = ExtractJpeg(resp.Data);
                    if (jpeg != null)
                        return jpeg;

                    AppLogger.Warn($"DdServerNikonBackend: GetLiveViewImg attempt {attempt} dataLen={dataLen} but no JPEG SOI marker found");
                }

                if (resp != null && resp.ErrorCode == PTP_RC_DeviceBusy)
                {
                    System.Threading.Thread.Sleep(50);
                    continue;
                }

                // No data and not a recognized busy condition; stop retrying this call.
                break;
            }

            AppLogger.Warn("DdServerNikonBackend: GetLiveViewImg returned no data after retries");
            return null;
        }

        /// <summary>
        /// Finds the JPEG SOI marker (0xFF 0xD8) in the raw live view payload and
        /// returns the bytes from that point onward, discarding any proprietary
        /// header the camera/ddserver prepended. Returns null if no marker found.
        /// </summary>
        private static byte[] ExtractJpeg(byte[] raw)
        {
            if (raw == null || raw.Length < 4) return null;

            for (int i = 0; i <= raw.Length - 2; i++)
            {
                if (raw[i] == 0xFF && raw[i + 1] == 0xD8)
                {
                    if (i == 0) return raw;
                    var trimmed = new byte[raw.Length - i];
                    Array.Copy(raw, i, trimmed, 0, trimmed.Length);
                    return trimmed;
                }
            }

            return null;
        }

        public void Capture()
        {
            _protocol?.ExecuteWithNoData(PTP_OC_InitiateCapture, 0, 0);
            CaptureComplete?.Invoke();
        }

        public void StartAutoFocus()
        {
            _protocol?.ExecuteWithNoData(NIKON_OC_AfDrive);
        }

        public uint GetUnsigned(eNkMAIDCapability capability)
        {
            uint propCode;
            if (!CapabilityToPtpProperty.TryGetValue(capability, out propCode))
            {
                AppLogger.Warn($"DdServerNikonBackend: no PTP property mapping for {capability}, returning 0");
                return 0;
            }
            var resp = _protocol.ExecuteReadData(PTP_OC_GetDevicePropValue, propCode);
            if (resp?.Data == null || resp.Data.Length == 0) return 0;
            return BitConverter.ToUInt32(PadTo4Bytes(resp.Data), 0);
        }

        public void SetUnsigned(eNkMAIDCapability capability, uint value)
        {
            uint propCode;
            if (!CapabilityToPtpProperty.TryGetValue(capability, out propCode))
            {
                AppLogger.Warn($"DdServerNikonBackend: no PTP property mapping for {capability}, ignoring SetUnsigned");
                return;
            }
            _protocol.ExecuteWriteData(PTP_OC_SetDevicePropValue, BitConverter.GetBytes(value), propCode);
        }

        public CameraEnumValue GetEnum(eNkMAIDCapability capability)
        {
            uint propCode;
            if (!CapabilityToPtpProperty.TryGetValue(capability, out propCode))
            {
                AppLogger.Warn($"DdServerNikonBackend: no PTP property mapping for {capability}, returning null enum");
                return null;
            }

            var resp = _protocol.ExecuteReadData(PTP_OC_GetDevicePropDesc, propCode);
            if (resp?.Data == null || resp.Data.Length < 6)
            {
                AppLogger.Warn($"DdServerNikonBackend: GetDevicePropDesc for {capability} (0x{propCode:X4}) returned no/short data");
                return null;
            }

            try
            {
                return ParseDevicePropDesc(resp.Data);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"DdServerNikonBackend: failed to parse DevicePropDesc for {capability}", ex);
                return null;
            }
        }

        public void SetEnum(eNkMAIDCapability capability, CameraEnumValue value)
        {
            if (value?.Values == null || value.Index < 0 || value.Index >= value.Values.Length) return;
            uint parsed;
            if (uint.TryParse(value.Values[value.Index], out parsed))
            {
                SetUnsigned(capability, parsed);
            }
        }

        /// <summary>
        /// Parses a PTP GetDevicePropDesc (0x1014) response payload into a neutral
        /// enum value with the current index and the full legal value list, so
        /// +/- UI controls can step through real camera-reported values (ISO,
        /// aperture, shutter speed, etc.) instead of a single fixed entry.
        /// Layout (PIMA 15740): PropCode(2) DataType(2) GetSet(1) Default(n) Current(n)
        /// FormFlag(1) then Range(Min(n) Max(n) Step(n)) or Enumeration(Count(2) Values(n*count)).
        /// </summary>
        private static CameraEnumValue ParseDevicePropDesc(byte[] data)
        {
            int index = 0;
            /* ushort propCode = */ BitConverter.ToUInt16(data, index); index += 2;
            ushort dataType = BitConverter.ToUInt16(data, index); index += 2;
            int size = GetPtpDataTypeSize(dataType);
            /* byte getSet = */ index += 1;

            index += size; // skip factory default value
            long currentValue = ReadPtpValue(data, index, size); index += size;

            byte formFlag = data[index]; index += 1;

            var values = new List<long>();
            int currentIndex = -1;

            if (formFlag == 1) // Range form
            {
                long min = ReadPtpValue(data, index, size); index += size;
                long max = ReadPtpValue(data, index, size); index += size;
                long step = ReadPtpValue(data, index, size); index += size;
                if (step == 0) step = 1;
                for (long v = min; v <= max; v += step)
                    values.Add(v);
            }
            else if (formFlag == 2) // Enumeration form
            {
                ushort count = BitConverter.ToUInt16(data, index); index += 2;
                for (int i = 0; i < count; i++)
                {
                    values.Add(ReadPtpValue(data, index, size));
                    index += size;
                }
            }
            else
            {
                // Formless: only the current value is known.
                values.Add(currentValue);
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == currentValue) { currentIndex = i; break; }
            }
            if (currentIndex < 0)
            {
                // Camera-reported current value isn't in the list (rare); insert it.
                values.Add(currentValue);
                currentIndex = values.Count - 1;
            }

            return new CameraEnumValue
            {
                Index = currentIndex,
                Length = values.Count,
                Values = values.ConvertAll(v => v.ToString()).ToArray()
            };
        }

        private static long ReadPtpValue(byte[] data, int offset, int size)
        {
            switch (size)
            {
                case 1: return data[offset];
                case 2: return BitConverter.ToUInt16(data, offset);
                case 4: return BitConverter.ToUInt32(data, offset);
                case 8: return (long)BitConverter.ToUInt64(data, offset);
                default: return 0;
            }
        }

        private static int GetPtpDataTypeSize(ushort dataType)
        {
            switch (dataType)
            {
                case 0x0001: // INT8
                case 0x0002: // UINT8
                    return 1;
                case 0x0003: // INT16
                case 0x0004: // UINT16
                    return 2;
                case 0x0005: // INT32
                case 0x0006: // UINT32
                    return 4;
                case 0x0007: // INT64
                case 0x0008: // UINT64
                    return 8;
                default:
                    return 2; // reasonable default
            }
        }

        public CameraPointValue GetPoint(eNkMAIDCapability capability)
        {
            AppLogger.Warn($"DdServerNikonBackend: GetPoint not supported for {capability} over ddserver");
            return new CameraPointValue(0, 0);
        }

        public void SetPoint(eNkMAIDCapability capability, CameraPointValue value)
        {
            if (capability == eNkMAIDCapability.kNkMAIDCapability_ContrastAFArea)
            {
                _protocol?.ExecuteWithNoData(NIKON_OC_ChangeAfArea, (uint)value.X, (uint)value.Y);
                return;
            }
            AppLogger.Warn($"DdServerNikonBackend: SetPoint not supported for {capability} over ddserver");
        }

        public float GetFloat(eNkMAIDCapability capability)
        {
            AppLogger.Warn($"DdServerNikonBackend: GetFloat not supported for {capability} over ddserver");
            return 0f;
        }

        public CameraCapabilityInfo[] GetCapabilityInfo()
        {
            var result = new List<CameraCapabilityInfo>();
            foreach (var kvp in CapabilityToPtpProperty)
            {
                result.Add(new CameraCapabilityInfo
                {
                    Id = kvp.Key,
                    Type = eNkMAIDCapType.kNkMAIDCapType_Unsigned,
                    CanGetValue = true,
                    CanSetValue = true,
                    Description = kvp.Key.ToString()
                });
            }
            return result.ToArray();
        }

        private static byte[] PadTo4Bytes(byte[] data)
        {
            if (data.Length >= 4) return data;
            var padded = new byte[4];
            Array.Copy(data, padded, data.Length);
            return padded;
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
