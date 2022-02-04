using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace a2n.IPBlocker
{
    public class IPBlocker
    {
        private const string blockedFilePath = "/etc/hosts.deny";
        private const string allowedFilePath = "/etc/hosts.allow";

        private List<IPSort> ipBlockedList = new List<IPSort>();
        private List<IPSort> ipWhitelist = new List<IPSort>();

        public IPBlockerSettings settings { get; set; }
        public IPBlocker()
        {
        }
        private UserAuthLog[] LoadLog(DateTime? time)
        {
            List<UserAuthLog> dataLst = new List<UserAuthLog>();
            Func<string, bool> procLine = (line) =>
            {
                if (line.Contains(": Failed password") || line.Contains(": Invalid user") || line.Contains(": Accepted password") || line.Contains(": Unable to negotiate") || line.Contains(": refused connect"))
                {
                    var lineDataArr = line.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                    if (lineDataArr.Length != 14 && lineDataArr.Length != 12 && lineDataArr.Length != 22 && lineDataArr.Length != 10)
                        return false;
                    UserAuthLog data = new UserAuthLog();
                    try
                    {
                        //Aug 10 00:04:14 slmserver sshd[356152]: refused connect from 138.68.40.92 (138.68.40.92)
                        data.Date = Convert.ToDateTime(string.Format("{0} {1} {2} {3}", lineDataArr[0], lineDataArr[1], DateTime.Now.Year, lineDataArr[2]));
                        data.Server = lineDataArr[3];
                        if (lineDataArr.Length == 22)
                            data.Message = string.Format("{0} {1} {2}", lineDataArr[5], lineDataArr[6], lineDataArr[7]);
                        else
                            data.Message = string.Format("{0} {1}", lineDataArr[5], lineDataArr[6]);
                        if (data.Message.ToLower() == "invalid user")
                        {
                            data.User = lineDataArr[7];
                            data.IPAddressString = lineDataArr[9];
                        }
                        else if (data.Message.ToLower() == "failed password" || data.Message.ToLower() == "accepted password")
                        {
                            data.User = lineDataArr[8];
                            data.IPAddressString = lineDataArr[10];
                        }
                        else if (data.Message.ToLower() == "refused connect")
                        {
                            data.IPAddressString = lineDataArr[8];
                        }
                        else
                        {
                            data.IPAddressString = lineDataArr[9];
                        }
                        dataLst.Add(data);
                        var addr = System.Net.IPAddress.Parse(data.IPAddressString);
                        var bytes = addr.GetAddressBytes();
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(bytes);
                        data.AddressSort = BitConverter.ToUInt32(bytes, 0);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
                return false;
            };
            if (time != null)
            {
                settings.LastCheck = DateTime.Now;
                Utils.ExecuteBashCommand($"journalctl -u {settings.Command} --since \"{time.Value.ToString("yyyy-MM-dd HH:mm:ss")}\"", procLine);
            }
            else
            {
                settings.LastCheck = DateTime.Now;
                Utils.ExecuteBashCommand($"journalctl -u {settings.Command}", procLine);
            }
            return dataLst.ToArray();
        }
        public void LoadCurrent()
        {
            ipBlockedList.Clear();
            ipWhitelist.Clear();
            bool isBlockedMode = true;
            Func<string, bool> procLine = (line) =>
            {
                if (string.IsNullOrEmpty(line))
                    return false;
                if (!line.StartsWith("ALL"))
                    return false;
                string[] vals = line.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                if (vals.Length == 2)
                {
                    try
                    {
                        var data = new IPSort();
                        data.IPTpl = vals[1];
                        data.ApplyTpl();
                        data.ApplySort();
                        if (isBlockedMode)
                            ipBlockedList.Add(data);
                        else
                            ipWhitelist.Add(data);
                    }
                    catch (Exception)
                    {
                    }
                }
                return false;
            };

            if (File.Exists(blockedFilePath))
                Utils.ReadFileLineByLine(blockedFilePath, procLine);
            isBlockedMode = false;
            if (File.Exists(allowedFilePath))
                Utils.ReadFileLineByLine(allowedFilePath, procLine);
        }
        public void Execute(DateTime? time)
        {
            if (settings == null)
                settings = IPBlockerSettings.LoadConfig();
            UserAuthLog[] dataLogs = LoadLog(time);

            if (settings.Verbose)
                Console.WriteLine("Loaded: {0}", dataLogs.Length);
            var ipAttempts = (from t in dataLogs
                              group t by new { t.IPAddressString, t.AddressSort, t.User, t.Message } into g
                              select new
                              {
                                  IPAddressString = g.Key.IPAddressString,
                                  g.Key.User,
                                  g.Key.Message,
                                  Attempt = g.Count(),
                                  g.Key.AddressSort
                              }).OrderBy(t => t.AddressSort).ToArray();

            if (settings.Verbose)
            {
                foreach (var item in ipAttempts)
                    Console.WriteLine("IP: {0}, User: {1}, Attempt: {2}, Message: {3}", item.IPAddressString, item.User, item.Attempt, item.Message);
            }

            var existing_allowed = (from d in dataLogs
                                    from b in ipWhitelist.Where(t => t.Rgx.IsMatch(d.IPAddressString)).DefaultIfEmpty()
                                    where b != null
                                    select d).Distinct().ToArray();

            var new_allowed = (from d in dataLogs
                               from x in existing_allowed.Where(t => t.IPAddressString == d.IPAddressString && t.Date == d.Date).DefaultIfEmpty()
                               where x == null && d.Message == "Accepted password"
                               select d).Distinct().ToArray();

            var all_allowed = existing_allowed.Union(new_allowed).ToArray(); // new allowed + existing allowed

            var all_blocked = dataLogs.Where(t => !all_allowed.Contains(t)).ToArray();
            var existing_blocked = (from d in all_blocked
                                    from b in ipBlockedList.Where(t => t.Rgx.IsMatch(d.IPAddressString)).DefaultIfEmpty()
                                    where b != null
                                    select d).Distinct().ToArray();
            var new_blocked = all_blocked.Where(t => !existing_blocked.Contains(t)).ToArray();

            if (new_blocked.Length > 0)
            {
                var ips = new_blocked.Select(t => t.IPAddressString).Distinct().ToArray();
                Console.WriteLine("New Blocked: {0}", ips.Length);
                if (!File.Exists(blockedFilePath))
                {
                    using (var fs = File.Create(blockedFilePath))
                    {
                    }
                }
                using (FileStream fs = File.Open(blockedFilePath, FileMode.Append))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        foreach (var item in ips)
                        {
                            sw.WriteLine("ALL: {0}", item);
                            var ipSort = new IPSort()
                            {
                                IPTpl = item
                            };
                            ipSort.ApplyTpl();
                            ipSort.ApplySort();
                            ipBlockedList.Add(ipSort);
                        }
                    }
                }

            }
            if (new_allowed.Length > 0)
            {
                var ips = new_allowed.Select(t => t.IPAddressString).Distinct().ToArray();
                Console.WriteLine("New Allowed: {0}", ips.Length);
                if (!File.Exists(blockedFilePath))
                {
                    using (var fs = File.Create(allowedFilePath))
                    {
                    }
                }
                using (FileStream fs = File.Open(allowedFilePath, FileMode.Append))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        foreach (var item in ips)
                        {
                            sw.WriteLine("ALL: {0}", item);
                            var ipSort = new IPSort()
                            {
                                IPTpl = item
                            };
                            ipSort.ApplyTpl();
                            ipSort.ApplySort();
                            ipWhitelist.Add(ipSort);
                        }
                    }
                }
            }

            if (new_allowed.Length > 0 || new_blocked.Length > 0)
            {
                Console.WriteLine($"Restart {settings.Command}");
                Utils.ExecuteBashCommand($"sudo systemctl restart {settings.Command}", (line) => { return false; });

            }
        }
    }
}
