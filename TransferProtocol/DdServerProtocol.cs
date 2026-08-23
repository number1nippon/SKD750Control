using System;
using System.IO;
using System.Text;
using SKD750Control.TransferProtocol.DDServer;

namespace SKD750Control.TransferProtocol
{
    /// <summary>
    /// Result of a raw PTP data-phase command executed over a ddserver connection.
    /// </summary>
    public class MTPDataResponse
    {
        public byte[] Data;
        public uint ErrorCode;
    }

    /// <summary>
    /// Executes raw PTP commands over a ddserver (DslrDashboardServer) TCP bridge.
    /// This is the raw-PTP equivalent of the direct MAID-SDK calls the USB backend
    /// uses via nikoncswrapper; it does not understand MAID-specific vendor
    /// capability semantics, only the PTP operation/response/data container framing.
    /// Ported from digiCamControl's DdServerProtocol with local dependencies removed.
    /// </summary>
    public class DdServerProtocol
    {
        private DdClient _client;
        private uint _sessionId;
        private readonly object _syncRoot = new object();

        public string Model { get; private set; }
        public string Manufacturer { get; private set; }
        public string SerialNumber { get; private set; }
        public bool IsConnected { get; set; }

        public MTPDataResponse ExecuteReadBigData(uint code, Stream stream, TransferCallback callback, params uint[] parameters)
        {
            lock (_syncRoot)
            {
                ReconnectIfNeeded();
                DataBlockContainer data;
                _client.Write(new CommandBlockContainer((int)code, parameters));
                int len = _client.ReadInt();
                Container resp = _client.ReadContainer(callback);
                if (resp.Header.Length >= len - 4)
                {
                    return new MTPDataResponse { ErrorCode = (uint)resp.Header.Code };
                }

                data = (DataBlockContainer)resp;
                resp = _client.ReadContainer();
                return new MTPDataResponse { Data = data.Payload, ErrorCode = (uint)data.Header.Code };
            }
        }

        public MTPDataResponse ExecuteReadData(uint code, params uint[] parameters)
        {
            lock (_syncRoot)
            {
                ReconnectIfNeeded();
                DataBlockContainer data;
                _client.Write(new CommandBlockContainer((int)code, parameters));
                int len = _client.ReadInt();
                Container resp = _client.ReadContainer();
                if (resp.Header.Length >= len - 4)
                {
                    return new MTPDataResponse { ErrorCode = (uint)resp.Header.Code };
                }

                data = (DataBlockContainer)resp;
                resp = _client.ReadContainer();
                // resp here is the ResponseBlockContainer for this transaction; its Code
                // is the actual PTP response code (e.g. 0x2001 = OK), not the echoed
                // operation code that DataBlockContainer.Header.Code holds.
                return new MTPDataResponse { Data = data.Payload, ErrorCode = (uint)resp.Header.Code };
            }
        }

        public uint ExecuteWithNoData(uint code, params uint[] parameters)
        {
            lock (_syncRoot)
            {
                ReconnectIfNeeded();
                _client.Write(new CommandBlockContainer((int)code, parameters));
                int len = _client.ReadInt();
                Container resp = _client.ReadContainer();
                if (resp.Header.Length >= len - 4)
                {
                    return (uint)resp.Header.Code;
                }

                resp = _client.ReadContainer();
                return (uint)resp.Header.Code;
            }
        }

        public uint ExecuteWriteData(uint code, byte[] data, params uint[] parameters)
        {
            lock (_syncRoot)
            {
                ReconnectIfNeeded();
                _client.Write(new CommandBlockContainer((int)code, parameters), new DataBlockContainer((int)code, data));
                int len = _client.ReadInt();
                Container resp = _client.ReadContainer();
                if (resp.Header.Length >= len - 4)
                {
                    return (uint)resp.Header.Code;
                }

                resp = _client.ReadContainer();
                return (uint)resp.Header.Code;
            }
        }

        public void Disconnect()
        {
        }

        public DdServerProtocol(DdClient client)
        {
            Init(client);
        }

        public void Init(DdClient client)
        {
            _client = client;
            LoadDeviceInfo();
            OpenSession();
        }

        public void OpenSession()
        {
            _sessionId++;
            ExecuteWithNoData(0x1002, _sessionId);
        }

        private void LoadDeviceInfo()
        {
            var res = ExecuteReadData(0x1001);
            int index = 2 + 4 + 2;
            int vendorDescCount = res.Data[index];
            index += vendorDescCount * 2;
            index += 3;
            int commandsCount = res.Data[index];
            index += 2;
            // load commands
            for (int i = 0; i < commandsCount; i++)
            {
                index += 2;
            }
            index += 2;
            int eventcount = res.Data[index];
            index += 2;
            // load events
            for (int i = 0; i < eventcount; i++)
            {
                index += 2;
            }
            index += 2;
            int propertycount = res.Data[index];
            index += 2;
            // load properties codes
            for (int i = 0; i < propertycount; i++)
            {
                index += 2;
            }
            index += 2;
            int formatscount = res.Data[index];
            index += 2;
            // load properties codes
            for (int i = 0; i < formatscount; i++)
            {
                index += 2;
            }
            index += 2;
            int imageformatscount = res.Data[index];
            index += 2;
            // load properties codes
            for (int i = 0; i < imageformatscount; i++)
            {
                index += 2;
            }
            index += 2;
            int strlen1 = res.Data[index] * 2;
            index += 1;
            Manufacturer = Encoding.Unicode.GetString(res.Data, index, strlen1 - 2);
            index += strlen1;
            int strlen2 = res.Data[index] * 2;
            index += 1;
            Model = Encoding.Unicode.GetString(res.Data, index, strlen2 - 2);
            index += strlen2;
            int strlen3 = res.Data[index] * 2;
            index += 1;
            index += strlen3;
            int strlen4 = res.Data[index] * 2;
            index += 1;
            SerialNumber = Encoding.Unicode.GetString(res.Data, index, strlen4 - 2);
        }

        private void ReconnectIfNeeded()
        {
            if (!_client.IsConnected())
            {
                _client.Reconnect();
                Init(_client);
            }
        }
    }
}
