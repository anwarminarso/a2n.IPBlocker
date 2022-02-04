using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace a2n.IPBlocker
{
    public class UserAuthLog
    {
        public DateTime Date { get; set; }
        public string Server { get; set; }

        public string IPAddressString { get; set; }
        public uint AddressSort { get; set; }
        public string Message { get; set; }
        public string User { get; set; }
    }
}
