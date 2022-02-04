using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace a2n.IPBlocker
{
    public class Utils
    {
        public static void ReadFileLineByLine(string filePath, Func<string, bool> funReadLine)
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
        public static void ExecuteBashCommand(string command, Func<string, bool> funReadLine)
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
    }
}
