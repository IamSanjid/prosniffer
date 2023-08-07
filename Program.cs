using SharpPcap;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PROSniffer
{
    internal class Program
    {
        private static Main? _main;
        static void Main(string[] args)
        {
#if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
#endif
            Thread.CurrentThread.Name = "Main Thread";
            Console.CancelKeyPress += Console_CancelKeyPress;
            _main = new Main();
            _main.PrintHelp();

            if (args.Length > 1)
            {
                _main.ProcessCommand(string.Join(" ", args.Skip(1).ToArray()));
            }
            else
            {
                _main.ProcessCommand("i");
            }
            _main.ReadInput();
            _main.Quit();
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            _main?.Quit();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleUnhandledException(e.ExceptionObject as Exception);
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleUnhandledException(e.Exception.InnerException);
        }

        private static void HandleUnhandledException(Exception? ex)
        {
            try
            {
                if (ex != null)
                {
                    File.WriteAllText("crash_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt",
                        "PROSniffer crash report: " + Environment.NewLine + ex);
                }
                Console.WriteLine("PROSniffer encountered a fatal error. The application will now terminate.");
                Console.WriteLine("An error file has been created next to the application.");
                Environment.Exit(0);
            }
            catch
            {
            }
        }
    }

    public class Main
    {
        const string HELP_MSG = $"""
         PROSniffer

         Important note: Make sure you start to sniff the packets before logging in, otherwise won't be able to retain the proper RC4 state.
         Also make sure that no other application is connected through port 800 or 801 to some remote ipaddress other than PRO(Just close some other not really important applications?).
         You can continue writing on the standard input(console) while packets are being written on the standard output(console) to provide your next command.

         interfaces|i
            Desc: Shows all the eathernet/wireless interfaces on your machine.
         sniff i=[interface index] <p|port=[ushort]; default is 800(Silver Server), provide 801 for Gold Server> <custom filter>
            Desc: Starts sniffing, if no argument is provided uses last provided arguments. 
                  If you want to provide a custom filter like wireshark advance filters to detect PRO communication you can do that.
                  Just provide it this way: sniff i=[index] cf="your filter".
         filter|f <pattern>
            Desc: You can provide custom Regex pattern to filter out received packets. No <pattern> means clear previous patterns.
                  Btw if you don't want to print any received packets you can provide something like this `f "^(?!x)x$"`.
         pause|p|resume|r
            Desc: Pauses/Resumes from printing/logging packets.
         clear|cls
            Desc: Clears the console screen, doesn't clear the internal packet log(which is used if you want to dump packets when quiting normally).
         dump <file name> 
            Desc: Dumps all the packets inside the "{Default.DUMP_DIRECTORY}" folder.
         exit|quit|q
            Desc: Exits normally also dumps all the packets to a file if dump command was provided previously, check "{Default.DUMP_DIRECTORY}" folder.
         h|help
            Desc: Prints out this message.
         """;

        private readonly List<string> _packetLogs = new();

        private readonly ConcurrentQueue<string> _recvPacketsQueue = new();
        private readonly ConcurrentQueue<string> _sentPacketsQueue = new();

        private readonly List<string> _filterRegexes = new()
        {
            // Ignores all the chat and other player update packets...
            @"^(?!w\|\.).*$", @"^(?!=\|\.).*$"
        };

        private RC4Sniffer? _sniffer = null;
        private bool _isRunning = true;

        private bool _pausedSniffing = false;

        private bool _dumpPackets = false;
        private string _dumpFilename = "";

        private bool _firstSentPacket = false;

        public Main()
        {
            Task.Run(UpdateSniffer);
            Task.Run(Update);
        }

        private void UpdateSniffer()
        {
            if (_sniffer != null)
            {
                lock (_sniffer)
                {
                    _sniffer?.Update();
                }
            }
            Task.Delay(1).ContinueWith((previous) => UpdateSniffer());
        }

        private void Update()
        {
            if (_sniffer != null && !_pausedSniffing)
            {
                lock (_packetLogs)
                {
                    while (_sentPacketsQueue.TryDequeue(out var packet))
                    {
                        string timeStr = "";

                        if (!_firstSentPacket)
                        {
                            var textEncoding = _sniffer.GetTextEncoding();
                            var bytes = textEncoding.GetBytes(packet);

                            ReadOnlySpan<byte> first4Bytes = new(bytes, 0, sizeof(float));

                            try
                            {
                                var foundTime = BitConverter.ToSingle(first4Bytes);
                                if (float.IsNormal(foundTime))
                                {
                                    timeStr = $"[{foundTime}] ";
                                    packet = textEncoding.GetString(bytes.Skip(4).ToArray());
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                        else
                        {
                            _firstSentPacket = false;
                        }

                        var packetLog = $"[>] {timeStr}" + packet;
                        AddLog(packetLog);
                        _packetLogs.Add(packetLog);

                        if (packet == "quit")
                        {
                            StartNewSniffer(_sniffer.DeviceIndex, _sniffer.RemotePort, _sniffer.CustomFilter);
                        }
                    }
                    while (_recvPacketsQueue.TryDequeue(out var packet))
                    {
                        foreach (var filter in _filterRegexes)
                        {
                            if (!Regex.IsMatch(packet, filter))
                            {
                                goto Continue;
                            }
                        }
                        AddLog(packet);
                        _packetLogs.Add(packet);

                        if (packet == "quit")
                        {
                            StartNewSniffer(_sniffer.DeviceIndex, _sniffer.RemotePort, _sniffer.CustomFilter);
                        }
                    }
                }
            }
            Continue:
            Task.Delay(1).ContinueWith((previous) => Update());
        }

        private void StartNewSniffer(int interfaceIdx, ushort port, string? customFilter)
        {
            lock (_packetLogs)
            {
                _firstSentPacket = true;
                _sniffer?.StopSniffing();

                _sniffer = new RC4Sniffer(interfaceIdx, port, customFilter);
                _sniffer.PacketReceived += Sniffer_PacketReceived;
                _sniffer.SentPacket += Sniffer_SentPacket;
                _sniffer.StartSniffing();

                _packetLogs.Clear();
            }
        }

        private void Sniffer_SentPacket(string packet)
        {
            _sentPacketsQueue.Enqueue(packet);
        }

        private void Sniffer_PacketReceived(string packet)
        {
            _recvPacketsQueue.Enqueue(packet);
        }

        public void ReadInput()
        {
            while (_isRunning)
            {
                var cmd = Console.ReadLine();
                if (!string.IsNullOrEmpty(cmd))
                {
                    ProcessCommand(cmd);
                }
            }
        }

        public void ProcessCommand(string command)
        {
            var cmdArgs = command.SplitArgs(true);
            switch (cmdArgs[0].ToLowerInvariant())
            {
                case "exit":
                case "quit":
                case "q":
                    Quit();
                    break;
                case "help":
                case "h":
                    AddLog("");
                    AddLog("");
                    PrintHelp();
                    break;
                case "dump":
                    _dumpPackets = !_dumpPackets;
                    if (!_dumpPackets)
                    {
                        _dumpFilename = "";
                        break; // Yeah just wanted to check out this *early* switch case break lol...
                    }
                    if (cmdArgs.Length > 1)
                    {
                        _dumpFilename = cmdArgs[1];
                    }
                    else
                    {
                        _dumpFilename = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";
                    }
                    break;
                case "sniff":
#if !GOLDTEST
                    var port = Default.GAME_PORT;
                    int interfaceIdx = -1;
                    string? customFilter = null;

                    if (cmdArgs.Length > 1)
                    {
                        foreach (var rawArg in cmdArgs.Skip(1))
                        {
                            var arg = rawArg.Replace("\"", "").Replace("'", "");
                            if (arg.Contains('='))
                            {
                                var argParams = arg.Split('=');
                                if (argParams.Length != 2)
                                {
                                    continue;
                                }
                                switch (argParams[0].ToLowerInvariant())
                                {
                                    case "p":
                                    case "port":
                                        if (ushort.TryParse(argParams[1], out var newPort))
                                        {
                                            port = newPort;
                                        }
                                        break;
                                    case "i":
                                    case "interface":
                                        if (!int.TryParse(argParams[1], out interfaceIdx))
                                        {
                                            AddLog("Unexpected interface index provided!");
                                        }
                                        break;
                                    case "cf":
                                    case "custom-filter":
                                        customFilter = argParams[1];
                                        break;
                                }
                            }
                            else if (!int.TryParse(arg, out interfaceIdx))
                            {
                                AddLog("Unexpected interface index provided!");
                            }
                        }
                    }
                    else if (_sniffer != null)
                    {
                        interfaceIdx = _sniffer.DeviceIndex;
                        port = _sniffer.RemotePort;
                    }
#else
                    ushort port = 801;
                    int interfaceIdx = 5;
                    string? customFilter = null;
#endif
                    if (interfaceIdx != -1)
                    {
                        ClearLog();
                        AddLog($"Starting sniffing port: {port}, interface idx: {interfaceIdx}...");
                        StartNewSniffer(interfaceIdx, port, customFilter);
                    }
                    break;
                case "interfaces":
                case "i":
                    AddLog("");
                    AddLog("Interfaces:");
                    int idx = 0;
                    foreach (var inter in PortSniffer.GetInterfaces())
                    {
                        AddLog($"[{idx++}]: {inter}");
                    };
                    break;
                case "pause":
                case "p":
                    if (_sniffer != null)
                    {
                        _pausedSniffing = true;
                    }
                    break;
                case "resume":
                case "r":
                    _pausedSniffing = false;
                    break;
                case "clear":
                case "cls":
                    ClearLog();
                    break;
                case "filter":
                case "f":
                    lock (_packetLogs)
                    {
                        if (cmdArgs.Length <= 1)
                        {
                            _filterRegexes.Clear();
                            break;
                        }
                        foreach (var rawArg in cmdArgs)
                        {
                            var arg = rawArg.Replace("\"", "").Replace("'", "");
                            _filterRegexes.Add(arg);
                        }
                    }
                    break;
            }
        }

        public void AddLog(string log)
        {
            lock (_packetLogs)
            {
                Console.WriteLine(log);
            }
        }

        public void ClearLog()
        {
            lock (_packetLogs)
            {
                Console.Clear();
            }
        }

        public void PrintHelp()
        {
            AddLog(HELP_MSG);
        }

        public void Quit()
        {
            lock (_packetLogs)
            {
                if (_dumpPackets && _packetLogs.Count > 0)
                {
                    if (!Directory.Exists(Default.DUMP_DIRECTORY))
                    {
                        Directory.CreateDirectory(Default.DUMP_DIRECTORY);
                    }
                    string file = Path.Combine(Default.DUMP_DIRECTORY, _dumpFilename);
                    File.WriteAllText(file, string.Join(Environment.NewLine, _packetLogs));
                }
                _isRunning = false;
            }
        }
    }
}