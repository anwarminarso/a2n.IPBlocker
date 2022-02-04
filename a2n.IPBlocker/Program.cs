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
        private static IPBlocker blocker = null;
        private static IPBlockerSettings settings = null;
        private static CancellationTokenSource monitoringTaskToken;
        private static Task monitoringTask;

        static void Main(params string[] args)
        {
            blocker = new IPBlocker();
            Run().GetAwaiter().GetResult();
        }

        private static Task Run()
        {
            monitoringTaskToken = new CancellationTokenSource();
            monitoringTask = Task.Run(() =>
            {
                Console.WriteLine("Load Config");
                settings = IPBlockerSettings.LoadConfig();
                blocker.settings = settings;

                Console.WriteLine("Last Check   : {0}", settings.LastCheck);
                Console.WriteLine("Interval     : {0}", settings.Interval);
                Console.WriteLine("Verbose      : {0}", settings.Verbose);
                Console.WriteLine("LazyLoad     : {0}", settings.LazyLoad);
                var _interval = blocker.settings.Interval * 1000;
                if (_interval < 1000)
                    _interval = 1000;
                var loopCounter = 0;
                var maxCounter = 10;
                bool IsLoaded = false;
                while (!monitoringTaskToken.IsCancellationRequested)
                {
                    monitoringTaskToken.Token.WaitHandle.WaitOne(_interval);

                    if (blocker.settings.Verbose)
                        Console.WriteLine("BEGIN");
                    if (!IsLoaded)
                    {
                        if (settings.Verbose)
                            Console.WriteLine("Load Current Host (Allowed/Blocked)");
                        blocker.LoadCurrent();
                        IsLoaded = true;
                    }
                    if (settings.LazyLoad)
                        loopCounter++;

                    blocker.Execute(settings.LastCheck);

                    if (blocker.settings.Verbose)
                        Console.WriteLine("Save Config");
                    settings.SaveConfig();
                    if (settings.Verbose)
                        Console.WriteLine("END");

                    if (settings.LazyLoad)
                    {
                        if (loopCounter == maxCounter)
                        {
                            loopCounter = 0;
                            IsLoaded = false;
                        }
                    }

                }
            }, monitoringTaskToken.Token);
            return monitoringTask;
        }
    }

}