using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SKD750Control.TransferProtocol.DDServer
{
    /// <summary>
    /// TCP client for a ddserver (DslrDashboardServer) instance. Handles the raw
    /// framed container protocol: connecting, listing attached USB devices,
    /// opening a device session, and reading/writing containers.
    /// Ported from digiCamControl's DdClient with local dependencies removed.
    /// </summary>
    public class DdClient
    {
        private readonly object _commandLock = new object();
        private TcpClient _client;
        private Stream _innerStream;
        private DdServerDevice _lastDevice;
        private string _ip;
        private int _port;

        public bool Open(string ip, int port)
        {
            try
            {
                _ip = ip;
                _port = port;
                _client = new TcpClient(ip, port);
                _innerStream = _client.GetStream();
                string server = Encoding.ASCII.GetString(ReadPacket());
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public int ReadInt()
        {
            return _innerStream.ReadByte() | (_innerStream.ReadByte() << 8) | (_innerStream.ReadByte() << 16) |
                   (_innerStream.ReadByte() << 24);
        }

        private byte[] ReadPacket()
        {
            int length = ReadInt();
            var buff = new byte[length - 4];
            _innerStream.Read(buff, 0, length - 4);
            return buff;
        }

        public void Write(Container container)
        {
            using (var ms = new MemoryStream())
            {
                int totalsize = container.Header.Length + 4;
                ms.WriteByte((byte)(0xff & totalsize));
                ms.WriteByte((byte)(0xff & (totalsize >> 8)));
                ms.WriteByte((byte)(0xff & (totalsize >> 16)));
                ms.WriteByte((byte)(0xff & (totalsize >> 24)));
                container.Write(ms);
                _innerStream.Write(ms.ToArray(), 0, (int)ms.Length);
            }
        }

        public void Write(Container container1, Container container2)
        {
            using (var ms = new MemoryStream())
            {
                int totalsize = container1.Header.Length + container2.Header.Length + 4;
                ms.WriteByte((byte)(0xff & totalsize));
                ms.WriteByte((byte)(0xff & (totalsize >> 8)));
                ms.WriteByte((byte)(0xff & (totalsize >> 16)));
                ms.WriteByte((byte)(0xff & (totalsize >> 24)));
                container1.Write(ms);
                container2.Write(ms);
                _innerStream.Write(ms.ToArray(), 0, (int)ms.Length);
                _innerStream.Flush();
            }
        }

        public virtual Container ReadContainer(TransferCallback callback = null)
        {
            Container result = GetContainer(false, callback);
            return result;
        }

        protected Container GetContainer(bool synchronized, TransferCallback callback)
        {
            var header = new ContainerHeader(_innerStream);

            switch (header.ContainerType)
            {
                case ContainerType.DataBlock:
                    return new DataBlockContainer(header, _innerStream, callback);
                case ContainerType.ResponseBlock:
                    if (synchronized)
                        Monitor.Exit(_commandLock);
                    return new ResponseBlockContainer(header, _innerStream);
                //Give current instance as stream, because we need keep track on the distance to next header
                case ContainerType.CommandBlock:
                case ContainerType.EventBlock:
                    throw new Exception("Invalid container type. " + header.ContainerType);
            }
            throw new Exception("Unknown container type");
        }

        public List<DdServerDevice> GetDevices()
        {
            var res = new List<DdServerDevice>();
            Write(new CommandBlockContainer(0002));
            var buff = new byte[6];
            _innerStream.Read(buff, 0, 4);

            Container c = ReadContainer();
            var dataBlockContainer = c as DataBlockContainer;
            if (dataBlockContainer != null)
            {
                int index = 0;
                int devcount = (dataBlockContainer.Payload[index] | (dataBlockContainer.Payload[index + 1] << 8));
                for (int i = 0; i < devcount; i++)
                {
                    res.Add(new DdServerDevice(dataBlockContainer.Payload, index + 2));
                }
                Container cc = ReadContainer();
                _innerStream.Read(buff, 0, 4);
            }
            return res;
        }

        public bool Connect(DdServerDevice device)
        {
            _lastDevice = device;
            Write(new CommandBlockContainer(0001, (uint)device.VendorId, (uint)device.ProductId));
            int len = ReadInt();
            Container c = ReadContainer();
            Container resp = ReadContainer();
            return true;
        }

        public void Reconnect()
        {
            Open(_ip, _port);
            Connect(_lastDevice);
        }

        public bool IsConnected()
        {
            return _client != null && _client.Connected;
        }
    }
}
