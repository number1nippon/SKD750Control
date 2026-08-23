using System;
using Nikon;

namespace SKD750Control.Backends
{
    /// <summary>
    /// Neutral capability get/set point value (replaces NkMAIDPoint so both
    /// USB (nikoncswrapper) and network (ddserver) backends can produce/consume it).
    /// </summary>
    public struct CameraPointValue
    {
        public int X;
        public int Y;

        public CameraPointValue(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Neutral enum-capability value (replaces NikonEnum so both backends can
    /// produce/consume it without depending on nikoncswrapper's internal type).
    /// </summary>
    public class CameraEnumValue
    {
        public int Index;
        public int Length;
        public string[] Values;

        public override string ToString()
        {
            if (Values != null && Index >= 0 && Index < Values.Length)
                return Values[Index];
            return Index.ToString();
        }
    }

    /// <summary>
    /// Neutral capability info (replaces NkMAIDCapInfo for enumeration/logging purposes).
    /// </summary>
    public class CameraCapabilityInfo
    {
        public eNkMAIDCapability Id;
        public eNkMAIDCapType Type;
        public bool CanGetValue;
        public bool CanSetValue;
        public string Description;
    }

    /// <summary>
    /// Abstraction over the camera connection transport so MainForm.cs can operate
    /// identically whether the camera is reached via direct USB (nikoncswrapper / MAID SDK)
    /// or via a network ddserver (DslrDashboardServer) bridge using raw PTP.
    /// </summary>
    public interface ICameraBackend : IDisposable
    {
        /// <summary>Human readable camera/device name once connected.</summary>
        string DeviceName { get; }

        /// <summary>True once a camera is connected and ready for use.</summary>
        bool IsConnected { get; }

        /// <summary>Gets or sets whether live view streaming is enabled on the camera.</summary>
        bool LiveViewEnabled { get; set; }

        /// <summary>Raised when a camera becomes connected.</summary>
        event Action<string> DeviceConnected;

        /// <summary>Raised when the connected camera is disconnected/removed.</summary>
        event Action DeviceDisconnected;

        /// <summary>Raised when a captured still image is ready. Payload is the raw image buffer
        /// followed by a suggested file extension (e.g. "jpg" or "nef") for saving.</summary>
        event Action<byte[], string> ImageReady;

        /// <summary>Raised when an asynchronous capture operation completes.</summary>
        event Action CaptureComplete;

        /// <summary>Raised when a capability value changes on the camera (e.g. pushed by firmware).</summary>
        event Action<eNkMAIDCapability> CapabilityValueChanged;

        /// <summary>Starts trying to discover/connect to a camera using this backend's transport.</summary>
        void Connect();

        /// <summary>Disconnects and releases any resources held by this backend.</summary>
        void Shutdown();

        /// <summary>Retrieves the next available live view frame (JPEG bytes), or null if unavailable.</summary>
        byte[] GetLiveViewImage();

        /// <summary>Triggers a still image capture.</summary>
        void Capture();

        /// <summary>Starts a native (non live view) autofocus operation.</summary>
        void StartAutoFocus();

        uint GetUnsigned(eNkMAIDCapability capability);
        void SetUnsigned(eNkMAIDCapability capability, uint value);

        CameraEnumValue GetEnum(eNkMAIDCapability capability);
        void SetEnum(eNkMAIDCapability capability, CameraEnumValue value);

        CameraPointValue GetPoint(eNkMAIDCapability capability);
        void SetPoint(eNkMAIDCapability capability, CameraPointValue value);

        float GetFloat(eNkMAIDCapability capability);

        CameraCapabilityInfo[] GetCapabilityInfo();
    }
}
