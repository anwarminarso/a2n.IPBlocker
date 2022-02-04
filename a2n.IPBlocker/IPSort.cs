using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace a2n.IPBlocker
{
    public class IPSort
    {
        const string regexPatternTempl = "(?:[0-9]{1,3})";

        public string IPAddressString { get; set; }
        public string IPTpl { get; set; }
        public string IPRegexPattern { get; set; }
        public uint AddressSort { get; set; }

        public Regex Rgx { get; set; }
        public void ApplySort()
        {
            var addr = System.Net.IPAddress.Parse(IPAddressString);
            var bytes = addr.GetAddressBytes();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            AddressSort = BitConverter.ToUInt32(bytes, 0);
        }

        public void ApplyTpl()
        {
            string[] IPFragments = IPTpl.Replace("*", string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (IPFragments.Length != 4)
            {
                List<string> newIPs = new List<string>();
                IPRegexPattern = string.Empty;
                foreach (string ip in IPFragments)
                {
                    newIPs.Add(ip);
                    if (!string.IsNullOrEmpty(IPRegexPattern))
                        IPRegexPattern += "\\.";
                    IPRegexPattern += ip;
                }
                for (int i = IPFragments.Length; i < 4; i++)
                {
                    if (!string.IsNullOrEmpty(IPRegexPattern))
                        IPRegexPattern += "\\.";
                    IPRegexPattern += regexPatternTempl;
                    newIPs.Add("0");
                }
                IPAddressString = string.Join('.', newIPs);
            }
            else
            {
                IPRegexPattern = Regex.Escape(IPTpl);
                IPAddressString = IPTpl;
            }
            Rgx = new Regex(IPRegexPattern);
        }
    }
}
