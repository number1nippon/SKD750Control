using System.IO;

namespace SKD750Control.TransferProtocol.DDServer
{
    /// <summary>
    /// Reports byte transfer progress while reading a large data block (e.g. live view frame, image download).
    /// </summary>
    /// <param name="totalBytes">Total number of bytes expected.</param>
    /// <param name="bytesReadSoFar">Number of bytes read so far.</param>
    public delegate void TransferCallback(uint totalBytes, uint bytesReadSoFar);

    public class DataBlockContainer : Container
    {
        public byte[] Payload;

        public DataBlockContainer(ContainerHeader header, Stream payload, TransferCallback callback)
        {
            Header = header;
            Payload = new byte[Header.PayloadLength];
            int numBytes = 0;
            while (numBytes != Header.PayloadLength)
            {
                numBytes += payload.Read(Payload, numBytes, Header.PayloadLength - numBytes);
                if (callback != null)
                    callback((uint)Header.PayloadLength, (uint)numBytes);
            }
        }

        public DataBlockContainer(int commandCode, byte[] data)
        {
            Header = new ContainerHeader();
            Header.Code = commandCode;
            Header.ContainerType = ContainerType.DataBlock;
            Header.PayloadLength = data.Length;
            Payload = data;
        }

        public override void WritePayload(Stream s)
        {
            s.Write(Payload, 0, Payload.Length);
        }
    }
}
