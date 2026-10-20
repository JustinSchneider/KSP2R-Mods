using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace MechJebLib.Utils
{
    public class MechJebLibException : Exception
    {
        public MechJebLibException() { }
        public MechJebLibException(string message) : base(message) { }
        public MechJebLibException(string message, Exception inner) : base(message, inner) { }
    }
}
