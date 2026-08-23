using System;
using Nikon;

namespace SKD750Control.Backends
{
    /// <summary>
    /// Wraps the existing direct-USB nikoncswrapper (MAID SDK) NikonManager/NikonDevice
    /// so it can be used behind the ICameraBackend abstraction. Behavior is unchanged
    /// from the previous direct calls in MainForm.cs.
    /// </summary>
    public class UsbNikonBackend : ICameraBackend
    {
        private NikonManager _manager;
        private NikonDevice _device;

        public event Action<string> DeviceConnected;
        public event Action DeviceDisconnected;
        public event Action<byte[], string> ImageReady;
        public event Action CaptureComplete;
        public event Action<eNkMAIDCapability> CapabilityValueChanged;

        public string DeviceName => _device?.Name;

        public bool IsConnected => _device != null;

        public bool LiveViewEnabled
        {
            get { return _device != null && _device.LiveViewEnabled; }
            set { if (_device != null) _device.LiveViewEnabled = value; }
        }

        public void Connect()
        {
            // Subscribe to events BEFORE constructing NikonManager: the MAID SDK can
            // synchronously raise DeviceAdded during construction for a camera that is
            // already powered on/connected, so subscribing afterward can silently miss it.
            _manager = new NikonManager("Type0015.md3");
            _manager.DeviceAdded -= Manager_DeviceAdded;
            _manager.DeviceRemoved -= Manager_DeviceRemoved;
            _manager.DeviceAdded += Manager_DeviceAdded;
            _manager.DeviceRemoved += Manager_DeviceRemoved;
        }

        private void Manager_DeviceAdded(NikonManager sender, NikonDevice device)
        {
            _device = device;
            _device.ImageReady += Device_ImageReady;
            _device.CaptureComplete += Device_CaptureComplete;
            _device.CapabilityValueChanged += Device_CapabilityValueChanged;
            DeviceConnected?.Invoke(_device.Name);
        }

        private void Manager_DeviceRemoved(NikonManager sender, NikonDevice device)
        {
            _device = null;
            DeviceDisconnected?.Invoke();
        }

        private void Device_ImageReady(NikonDevice sender, NikonImage image)
        {
            string ext = image?.Type == NikonImageType.Jpeg ? "jpg" : "nef";
            ImageReady?.Invoke(image?.Buffer, ext);
        }

        private void Device_CaptureComplete(NikonDevice sender, int data)
        {
            CaptureComplete?.Invoke();
        }

        private void Device_CapabilityValueChanged(NikonDevice sender, eNkMAIDCapability capability)
        {
            CapabilityValueChanged?.Invoke(capability);
        }

        public void Shutdown()
        {
            if (_device != null)
            {
                try { _device.LiveViewEnabled = false; } catch { }
            }
            _manager?.Shutdown();
        }

        public byte[] GetLiveViewImage()
        {
            if (_device == null) return null;
            NikonLiveViewImage image = _device.GetLiveViewImage();
            return image?.JpegBuffer;
        }

        public void Capture()
        {
            _device?.Capture();
        }

        public void StartAutoFocus()
        {
            _device?.Start(eNkMAIDCapability.kNkMAIDCapability_AutoFocus);
        }

        public uint GetUnsigned(eNkMAIDCapability capability)
        {
            return _device.GetUnsigned(capability);
        }

        public void SetUnsigned(eNkMAIDCapability capability, uint value)
        {
            _device.SetUnsigned(capability, value);
        }

        public CameraEnumValue GetEnum(eNkMAIDCapability capability)
        {
            var nEnum = _device.GetEnum(capability);
            if (nEnum == null) return null;
            var values = new string[nEnum.Length];
            for (int i = 0; i < nEnum.Length; i++)
            {
                values[i] = nEnum[i]?.ToString();
            }
            return new CameraEnumValue
            {
                Index = nEnum.Index,
                Length = nEnum.Length,
                Values = values
            };
        }

        public void SetEnum(eNkMAIDCapability capability, CameraEnumValue value)
        {
            var nEnum = _device.GetEnum(capability);
            if (nEnum == null) return;
            nEnum.Index = value.Index;
            _device.SetEnum(capability, nEnum);
        }

        public CameraPointValue GetPoint(eNkMAIDCapability capability)
        {
            var pt = _device.GetPoint(capability);
            return new CameraPointValue(pt.x, pt.y);
        }

        public void SetPoint(eNkMAIDCapability capability, CameraPointValue value)
        {
            _device.SetPoint(capability, new NkMAIDPoint { x = value.X, y = value.Y });
        }

        public float GetFloat(eNkMAIDCapability capability)
        {
            return (float)_device.GetFloat(capability);
        }

        public CameraCapabilityInfo[] GetCapabilityInfo()
        {
            var caps = _device.GetCapabilityInfo();
            var result = new CameraCapabilityInfo[caps.Length];
            for (int i = 0; i < caps.Length; i++)
            {
                var cap = caps[i];
                result[i] = new CameraCapabilityInfo
                {
                    Id = (eNkMAIDCapability)cap.ulID,
                    Type = cap.ulType,
                    CanGetValue = cap.CanGet(),
                    CanSetValue = cap.CanSet(),
                    Description = cap.GetDescription()
                };
            }
            return result;
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
