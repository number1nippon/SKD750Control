using System;

namespace SKD750Control.TransferProtocol.DDServer
{
    public class InvalidContainerException : Exception
    {
        public InvalidContainerException(string msg) : base(msg)
        {
        }
    }
}
