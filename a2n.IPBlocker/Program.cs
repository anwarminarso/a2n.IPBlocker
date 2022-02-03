using System.Diagnostics;
using System.IO;
using System.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace a2n.IPBlocker
{
    public class Program
    {
        private static List<IPSort> ipBlockedList = new List<IPSort>();
        private static List<IPSort> ipWhitelist = new List<IPSort>();
        private static DateTime? lastCheck = null;
        private static bool Verbose = true;
        private static int Interval = 60;
        private static string Command = "ssh";
        private static string blockedFilePath = "/etc/hosts.deny";
        private static string allowedFilePath = "/etc/hosts.allow";

        private static CancellationTokenSource monitoringTaskToken;
        private static Task monitoringTask;

        //private static Regex regexIP = new Regex("^((((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))|(((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){1,3}\\*))$");


        static void Main(params string[] args)
        {
            Run().GetAwaiter().GetResult();
        }

        private static Task Run()
        {
            monitoringTaskToken = new CancellationTokenSource();
            monitoringTask = Task.Run(() =>
            {
                Console.WriteLine("Load Config");
                LoadConfig();
                Console.WriteLine("Last Check : {0}", lastCheck);
                Console.WriteLine("Interval : {0}", Interval);
                Console.WriteLine("Verbose : {0}", Verbose);
                var _interval = Interval * 1000;
                while (!monitoringTaskToken.IsCancellationRequested)
                {
                    monitoringTaskToken.Token.WaitHandle.WaitOne(_interval);
                    if (Verbose)
                        Console.WriteLine("BEGIN");
                    LoadCurrent();
                    if (Verbose)
                        Console.WriteLine("Load Log");
                    LoadLog(lastCheck);

                    if (Verbose)
                        Console.WriteLine("Save Config");
                    SaveConfig();
                    if (Verbose)
                        Console.WriteLine("END");

                }
            }, monitoringTaskToken.Token);
            return monitoringTask;
        }
        private static void LoadCurrent()
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
                ReadFileLineByLine(blockedFilePath, procLine);
            isBlockedMode = false;
            if (File.Exists(allowedFilePath))
                ReadFileLineByLine(allowedFilePath, procLine);
        }

        private static void LoadLog(DateTime? time)
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
                lastCheck = DateTime.Now;
                ExecuteBashCommand($"journalctl -u {Command} --since \"{time.Value.ToString("yyyy-MM-dd HH:mm:ss")}\"", procLine);
            }
            else
            {
                lastCheck = DateTime.Now;
                //ExecuteBashCommand($"journalctl -u ssh", procLine);
                ExecuteBashCommand($"journalctl -u {Command}", procLine);
            }
            if (Verbose)
                Console.WriteLine("Loaded: {0}", dataLst.Count);
            var ipAttempts = (from t in dataLst
                              group t by new { t.IPAddressString, t.AddressSort, t.User, t.Message } into g
                              select new
                              {
                                  IPAddressString = g.Key.IPAddressString,
                                  g.Key.User,
                                  g.Key.Message,
                                  Attempt = g.Count(),
                                  g.Key.AddressSort
                              }).OrderBy(t => t.AddressSort).ToArray();

            if (Verbose)
            {
                foreach (var item in ipAttempts)
                    Console.WriteLine("IP: {0}, User: {1}, Attempt: {2}, Message: {3}", item.IPAddressString, item.User, item.Attempt, item.Message);
            }

            var allow1 = (from d in dataLst
                          from b in ipWhitelist.Where(t => t.Rgx.IsMatch(d.IPAddressString)).DefaultIfEmpty()
                          where b != null
                          select d).Distinct().ToArray();
            var allow2 = (from d in dataLst
                          from x in allow1.Where(t => t.IPAddressString == d.IPAddressString && t.Date == d.Date).DefaultIfEmpty()
                          where x == null && d.Message == "Accepted password"
                          select d).Distinct().ToArray();
            var allow3 = allow1.Union(allow2).ToArray();

            //List<IPSort> allowed = new List<IPSort>();
            //foreach (var item in allow2)
            //{
            //    var data = new IPSort();
            //    data.IPTpl = item.IPAddressString;
            //    data.ApplyTpl();
            //    data.ApplySort();
            //    allowed.Add(data);
            //}
            //var newAllowed = allowed.Union(ipWhitelist).OrderBy(t => t.AddressSort).ToArray();

            var blocked1 = dataLst.Where(t => !allow3.Contains(t)).ToArray();
            var blocked2 = (from d in blocked1
                            from b in ipBlockedList.Where(t => t.Rgx.IsMatch(d.IPAddressString)).DefaultIfEmpty()
                            where b != null
                            select d).Distinct().ToArray();
            var blocked3 = blocked1.Where(t => !blocked2.Contains(t)).ToArray();
            //List<IPSort> blocked = new List<IPSort>();
            //foreach (var item in blocked3)
            //{
            //    var data = new IPSort();
            //    data.IPTpl = item.IPAddressString;
            //    data.ApplyTpl();
            //    data.ApplySort();
            //    blocked.Add(data);
            //}
            //var newBlocked = blocked.Union(ipBlockedList).OrderBy(t => t.AddressSort).ToList();

            if (blocked3.Length > 0)
            {
                var ips = blocked3.Select(t => t.IPAddressString).Distinct().ToArray();
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
                        }
                    }
                }
            }
            if (allow2.Length > 0)
            {
                var ips = allow2.Select(t => t.IPAddressString).Distinct().ToArray();
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
                        }
                    }
                }
            }

            if (blocked3.Length > 0 || allow2.Length > 0)
            {
                Console.WriteLine($"Restart {Command}");
                ExecuteBashCommand($"sudo systemctl restart {Command}", (line) => { return false; });
            }
        }
        private static void ReadFileLineByLine(string filePath, Func<string, bool> funReadLine)
        {
            using (var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var sr = new StreamReader(fs))
                {
                    while (!sr.EndOfStream)
                    {
                        string line = sr.ReadLine();
                        if (funReadLine(line))
                            break;
                    }
                }
            }
        }
        private static void ExecuteBashCommand(string command, Func<string, bool> funReadLine)
        {
            var escapedArgs = command.Replace("\"", "\\\"");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            using (var sr = process.StandardOutput)
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    if (funReadLine(line))
                        break;
                }
            }
            process.WaitForExit();
        }
        private static void LoadConfig()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            string data = string.Empty;
            if (!File.Exists(filePath))
            {
                lastCheck = DateTime.Now.AddHours(-1);
                return;
            }
            try
            {
                ReadFileLineByLine(filePath, (line) =>
                {
                    data += line;
                    return false;
                });
                JObject obj = JObject.Parse(data);
                lastCheck = obj["Settings"]["LastCheck"].Value<DateTime?>();
                Interval = obj["Settings"]["Interval"].Value<int>();
                Verbose = obj["Settings"]["Verbose"].Value<bool>();
                Command = obj["Settings"]["Command"].Value<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        private static void SaveConfig()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            var data = new
            {
                Settings = new
                {
                    LastCheck = lastCheck,
                    Interval = Interval,
                    Verbose = Verbose,
                    Command = Command
                }
            };
            if (File.Exists(filePath))
                File.Delete(filePath);
            using (FileStream fs = File.Create(filePath))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.Write(JsonConvert.SerializeObject(data, Formatting.Indented));
                }
            }
        }
    }
    public class UserAuthLog
    {
        public DateTime Date { get; set; }
        public string Server { get; set; }

        public string IPAddressString { get; set; }
        public uint AddressSort { get; set; }
        public string Message { get; set; }
        public string User { get; set; }
    }
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
            string[] IPFragments = IPTpl.Split('.', StringSplitOptions.RemoveEmptyEntries);
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

        //public override int GetHashCode()
        //{
        //    return base.GetHashCode();
        //}
        //public override bool Equals(object obj)
        //{
        //    var o = obj as IPSort;
        //    if (this.Rgx != null)
        //        return Rgx.IsMatch(o.IPAddressString);
        //    else
        //        return IPAddressString == o.IPAddressString;
        //}
    }
}