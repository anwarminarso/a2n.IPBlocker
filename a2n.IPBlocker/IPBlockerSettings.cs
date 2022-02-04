using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace a2n.IPBlocker
{
    public class IPBlockerSettings
    {
        public DateTime? LastCheck { get; set; }
        public string Command { get; set; } = "ssh";
        public int Interval { get; set; } = 5;
        public bool Verbose { get; set; } = false;
        public bool LazyLoad { get; set; } = false;

        public void SaveConfig()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(filePath))
                File.Delete(filePath);
            using (FileStream fs = File.Create(filePath))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.Write(JsonConvert.SerializeObject(this, Formatting.Indented));
                }
            }
        }
        public static IPBlockerSettings LoadConfig()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            string data = string.Empty;
            IPBlockerSettings result = null;
            if (!File.Exists(filePath))
            {
                return new IPBlockerSettings() 
                { 
                    LastCheck = DateTime.Now.AddHours(-1) 
                };
            }
            try
            {
                Utils.ReadFileLineByLine(filePath, (line) =>
                {
                    data += line;
                    return false;
                });
                result = JsonConvert.DeserializeObject<IPBlockerSettings>(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new IPBlockerSettings()
                {
                    LastCheck = DateTime.Now.AddHours(-1)
                };
            }
            return result;
        }

    }
}
