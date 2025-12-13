using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace PROSniffer
{
    internal class Program
    {
#pragma warning disable CS8618
        static IApplication _app;
#pragma warning restore CS8618

        static void Main(string[] args)
        {
#if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
#endif
            Thread.CurrentThread.Name = "Main Thread";
            Console.CancelKeyPress += Console_CancelKeyPress;

            ConfigurationManager.Enable(ConfigLocations.All);
            _app = Application.Create();
            _app.Init();

            MainWindow mainWindow = new(_app);
            _app.Run(mainWindow);
            mainWindow.OnQuite();
            _app.Dispose();
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            _app.RequestStop();
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

    public sealed class MainWindow : Window
    {
        readonly TextView _packetLogView;
        readonly TextView _logView;
        readonly TextField _commandInputView;
        readonly Shortcut _lastStatus;
        PROSnifferCommand? _currentCommand;
        int _beforeCursorPos = -1;

        readonly MainWorker _mainWorker;

        readonly Stack<string> _prevCommands = [];
        readonly Stack<string> _nextCommands = [];

        public MainWindow(IApplication app)
        {
            SetApp(app);
            BorderStyle = LineStyle.None;
            Title = "PROSniffer";
            Height = Dim.Fill();
            Width = Dim.Fill();

            _packetLogView = new TextView
            {
                X = Pos.X(this),
                Y = Pos.Top(this),
                Width = Dim.Fill(),
                Height = Dim.Fill(12),
                BorderStyle = LineStyle.Double,
                ReadOnly = true,
            };

            _logView = new TextView
            {
                X = Pos.X(this),
                Y = Pos.Percent(60),
                Width = Dim.Fill(),
                Height = Dim.Fill(4),
                BorderStyle = LineStyle.Single,
                ReadOnly = true,
                WordWrap = true,
            };

            _commandInputView = new TextField
            {
                X = Pos.X(this),
                Y = Pos.AnchorEnd(4),
                Width = Dim.Fill(),
                BorderStyle = LineStyle.Single,
                Data = false,
                Autocomplete = new CommandTextFieldAutocomplete(),
            };
            _commandInputView.TextChanged += (_, _) => ShowCommandSuggestions();
            _commandInputView.KeyDownNotHandled += CommandInputView_KeyDownNotHandled;
            _commandInputView.KeyDown += CommandInputView_KeyDown;
            if (_commandInputView.Autocomplete.SuggestionGenerator is ArgumentSuggestionGenerator generator)
            {
                generator.SelectedSuggestionChanged += (_) => ShowCommandSuggestions();
                generator.ViewingSuggestionChanged += CommandInputView_ViewingSuggestionChanged;
            }

            _lastStatus = new(Key.Empty, "Waiting or a command...", null);

            StatusBar statusBar = new([_lastStatus]);

            // TODO: The order matters, maybe Terminal.Gui bug.. It's annoying, maybe it doesn't let you focus a view on constructor I guess or definitely a bug...
            Add(_commandInputView, _packetLogView, _logView, statusBar);

            _mainWorker = new MainWorker();
            _mainWorker.PacketLogged += (log) =>
            {
                App?.Invoke(() =>
                {
                    AddTextViewLine(_packetLogView, log);
                });
            };
            _mainWorker.Logged += (log) =>
            {
                App?.Invoke(() =>
                {
                    AddTextViewLine(_logView, log);
                });
            };
            _mainWorker.ClearLogRequested += () =>
            {
                App?.Invoke(() =>
                {
                    _packetLogView.Text = "";
                    _packetLogView.MoveHome();

                    _logView.Text = "";
                    _logView.MoveHome();
                });
            };
            _mainWorker.StatusUpdate += (status) =>
            {
                App?.Invoke(() =>
                {
                    _lastStatus.Title = status;
                });
            };

            _logView.Text = "Available commands(try <command> help to know more about it):" + Environment.NewLine + string.Join(Environment.NewLine, MainWorker.Commands.Select(c => c.Name));
            _logView.MoveHome();
        }

        public void OnQuite()
        {
            _mainWorker.Quit();
        }

        private static void AddTextViewLine(TextView textView, string line)
        {
            textView.Text += line + Environment.NewLine;
            ScrollToEnd(textView);
            StripTextView(textView);
        }

        private static void StripTextView(TextView textView)
        {
            var lines = textView.GetAllLines();
            if (lines.Count > 10000)
            {
                lines.RemoveRange(0, 8000);
                textView.SetNeedsDraw();
            }
        }

        private static void ScrollToEnd(TextView textView)
        {
            if (textView.CurrentRow > 0 && textView.CurrentRow < textView.Lines - 1)
            {
                return;
            }
            textView.MoveEnd();
        }

        private void CommandInputView_KeyDown(object? sender, Key e)
        {
            if (e == Key.Backspace)
            {
                _beforeCursorPos = _commandInputView.CursorPosition;
            }
        }

        private void CommandInputView_KeyDownNotHandled(object? sender, Key e)
        {
            if (e == Key.Enter && _currentCommand != null)
            {
                var fullCommand = _commandInputView.Text.Trim();
                var parseResult = _currentCommand.Parse(fullCommand);
                if (parseResult.Errors.Count == 0)
                {
                    if (parseResult.Action is HelpAction action)
                    {
                        var helpOutput = new StringBuilderTextWriter();
                        parseResult.InvocationConfiguration.Output = helpOutput;
                        action.Invoke(parseResult);

                        _logView.Text = helpOutput.GetContent() + Environment.NewLine;
                        _logView.MoveHome();
                    }
                    else
                    {
                        if (_nextCommands.TryPeek(out var command) && command == fullCommand)
                        {
                            _nextCommands.Pop();
                        }
                        _prevCommands.Push(fullCommand);
                        _lastStatus.Title = "Executing: " + fullCommand;
                        _mainWorker.ProcessCommand(parseResult);
                    }
                }
                else
                {
                    _lastStatus.Title = "Multiple errors occurred, please refer to README.md";
                    _mainWorker.Logged?.Invoke(string.Join(Environment.NewLine, parseResult.Errors.Select(e => e.Message)));
                }

                _commandInputView.Text = "";
            }
            else if (e == Key.CursorUp && _prevCommands.Count > 0)
            {
                string lastCmd = _prevCommands.Pop();
                _nextCommands.Push(_commandInputView.Text);
                _commandInputView.Text = lastCmd;
                e.Handled = true;
                _commandInputView.MoveEnd();
            }
            else if (e == Key.CursorDown && _nextCommands.Count > 0)
            {
                string nextCmd = _nextCommands.Pop();
                _prevCommands.Push(_commandInputView.Text);
                _commandInputView.Text = nextCmd;
                e.Handled = true;
                _commandInputView.MoveEnd();
            }
        }

        private void CommandInputView_ViewingSuggestionChanged(CompletionItem suggesion)
        {
            _lastStatus.Title = "Selected: " + suggesion.Label + (string.IsNullOrEmpty(suggesion.Detail) ? "" : " - " + suggesion.Detail);
        }

        private void ShowCommandSuggestions()
        {
            if (!string.IsNullOrEmpty(_commandInputView.Text) && _commandInputView.Autocomplete.SuggestionGenerator is ArgumentSuggestionGenerator generator)
            {
                if (_commandInputView.Autocomplete is CommandTextFieldAutocomplete cmdAutocomplete && cmdAutocomplete.IsSuggestionSelecting)
                {
                    if (_currentCommand != null)
                    {
                        generator.AllSuggestions = [];
                    }
                    return;
                }
                var args = _commandInputView.Text.SplitArgs(false, true);
                if (_beforeCursorPos != -1 && _beforeCursorPos > _commandInputView.CursorPosition && args.Length == 1)
                {
                    _currentCommand = null;
                }
                if (_currentCommand == null && MainWorker.Commands.Find(c => c.Name.Equals(args[0], StringComparison.InvariantCultureIgnoreCase)) is PROSnifferCommand selectedCommand)
                {
                    _currentCommand = selectedCommand;
                }
                if (_currentCommand != null)
                {
                    try
                    {
                        generator.AllSuggestions = [.. _currentCommand.Parse(_commandInputView.Text)
                                                                      .GetCompletions(_commandInputView.CursorPosition)];
                    }
                    catch (InvalidOperationException)
                    {
                        generator.AllSuggestions = [];
                    }
                    return;
                }

                if (_currentCommand == null)
                {
                    generator.AllSuggestions = [.. MainWorker.Commands
                                                        .FindAll(c => c.Name.StartsWith(_commandInputView.Text, StringComparison.OrdinalIgnoreCase))
                                                        .Select(c => new CompletionItem(c.Name, insertText: c.Name, detail: c.Description ?? ""))
                                                        .Distinct()];
                    return;
                }
                generator.AllSuggestions = [];
            }
            else
            {
                _currentCommand = null;
            }
        }
    }

    public class MainWorker
    {
        public const string HELP_MSG = $"""
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

        static IEnumerable<CompletionItem> GetInterfaceCompletions(CompletionContext context)
        {
            if (!string.IsNullOrEmpty(context.WordToComplete) && !int.TryParse(context.WordToComplete, out int _))
            {
                yield break;
            }
            var idx = 0;
            foreach (var name in PortSniffer.GetInterfaces())
            {
                yield return new CompletionItem(idx.ToString() + " (" + name + ")", insertText: idx.ToString());
                idx++;
            }
        }

        static IEnumerable<CompletionItem> GetPortCompletions(CompletionContext context)
        {
            if (!string.IsNullOrEmpty(context.WordToComplete) && !int.TryParse(context.WordToComplete, out int _))
            {
                yield break;
            }
            yield return new CompletionItem("800 (Silver)", insertText: "800");
            yield return new CompletionItem("801 (Gold)", insertText: "801");
        }

        public static readonly List<PROSnifferCommand> Commands = [
            new CommandBuilder("interfaces")
                .Add(new HelpOption("help"))
                .Description("Shows all the eathernet/wireless interfaces on your machine.")
                .Build(),
            new CommandBuilder("sniff")
                .Add(new HelpOption("help"))
                .Add(new OptionBuilder<int>("interface", "i")
                    .Description("The interface index to use for sniffing.")
                    .Required(true)
                    .CompletionSource(GetInterfaceCompletions)
                    .Build())
                .Add(new OptionBuilder<ushort>("port", "p")
                    .Description("The remote port to sniff, default is 800(Silver Server), provide 801 for Gold Server.")
                    .Required(true)
                    .CompletionSource(GetPortCompletions)
                    .DefaultValueFactory(_ => Default.GAME_PORT)
                    .Build())
                .Add(new Option<string>("custom-filter", "cf") {
                    Description = "A custom filter to apply on the sniffing device, like wireshark advanced filters. eg. `cf <str>`",
                    Required = false,
                })
                .Description("Starts sniffing, if no argument is provided uses last provided arguments.")
                .Build(),
            new CommandBuilder("filter")
                .Add(new HelpOption("help"))
                .Add(new Argument<string[]>("pattern") { Description = "Custom Regex patterns to filter out received packets." })
                .Description("You can provide custom Regex pattern to filter out received packets. No <pattern> means clear previous patterns.")
                .Build(),
            new CommandBuilder("pause")
                 .Add(new HelpOption("help"))
                .Description("Pauses from printing/logging packets.")
                .Build(),
            new CommandBuilder("resume")
                .Add(new HelpOption("help"))
                .Description("Resumes from printing/logging packets.")
                .Build(),
            new CommandBuilder("clear")
                .Add(new HelpOption("help"))
                .Description("Clears the console screen, doesn't clear the internal packet log(which is used if you want to dump packets when quiting normally).")
                .Build(),
            new CommandBuilder("dump")
                .Add(new HelpOption("help"))
                .Add(new Argument<string>("file-name") { Description = "The file name to dump the packets to.", DefaultValueFactory = (_) => "" })
                .Description($"Dumps all the packets inside the {Default.DUMP_DIRECTORY} folder.")
                .Build(),
            new CommandBuilder("exit")
                .Add(new HelpOption("help"))
                .Description("Exits the program while dumping the current packet logs if enabled.")
                .Build(),
        ];

        private readonly List<string> _packetLogs = [];

        public Action<string>? PacketLogged;
        public Action? ClearLogRequested;
        public Action<string>? Logged;
        public Action<string>? StatusUpdate;

        private readonly ConcurrentQueue<string> _recvPacketsQueue = new();
        private readonly ConcurrentQueue<string> _sentPacketsQueue = new();

        private readonly List<string> _filterRegexes =
        [
            // Ignores all the chat and other player update packets...
            @"^(?!w\|\.).*$", @"^(?!=\|\.).*$"
        ];

        private RC4Sniffer? _sniffer = null;
        private bool _isRunning = true;

        private bool _pausedSniffing = false;

        private bool _dumpPackets = false;
        private string _dumpFilename = "";

        private bool _seenLoginPacket = false;

        public MainWorker()
        {
            Task.Run(Update);
        }

        private void Update()
        {
            if (!_isRunning) return;
            if (_sniffer != null)
            {
                lock (_sniffer)
                {
                    _sniffer?.Update();
                }
                UpdatePackets();
            }
            Task.Delay(1).ContinueWith((previous) => Update());
        }

        private void UpdatePackets()
        {
            if (_pausedSniffing)
            {
                return;
            }
            Debug.Assert(_sniffer != null);
            lock (_packetLogs)
            {
                while (_sentPacketsQueue.TryDequeue(out var packet))
                {
                    string timeStr = "";

                    if (packet.StartsWith("+|.|") && !_seenLoginPacket)
                    {
                        _seenLoginPacket = true;
                    }
                    else
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
                                packet = textEncoding.GetString([.. bytes.Skip(4)]);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }

                    var packetLog = $"[>] {timeStr}" + packet;
                    PacketLogged?.Invoke(packetLog);
                    _packetLogs.Add(packetLog);

                    if (packet.StartsWith("quit"))
                    {
                        TryDumpPackets();
                        StartNewSniffer(_sniffer.DeviceIndex, _sniffer.RemotePort, _sniffer.CustomFilter);
                    }
                }
                while (_recvPacketsQueue.TryDequeue(out var packet))
                {
                    foreach (var filter in _filterRegexes)
                    {
                        if (!Regex.IsMatch(packet, filter))
                        {
                            return;
                        }
                    }
                    PacketLogged?.Invoke(packet);
                    _packetLogs.Add(packet);

                    if (packet.StartsWith("quit"))
                    {
                        TryDumpPackets();
                        StartNewSniffer(_sniffer.DeviceIndex, _sniffer.RemotePort, _sniffer.CustomFilter);
                    }
                }
            }
        }

        private void StartNewSniffer(int interfaceIdx, ushort port, string? customFilter)
        {
            lock (_packetLogs)
            {
                _packetLogs.Clear();
                _seenLoginPacket = false;
                _sentPacketsQueue.Clear();
                _recvPacketsQueue.Clear();
                _sniffer?.StopSniffing();

                _sniffer = new RC4Sniffer(interfaceIdx, port, customFilter);
                _sniffer.PacketReceived += Sniffer_PacketReceived;
                _sniffer.SentPacket += Sniffer_SentPacket;
                _sniffer.StartSniffing();

                StatusUpdate?.Invoke($"Sniffing on interface index: {interfaceIdx}, port: {port}, custom filter: {customFilter ?? "none"}");
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

        public void ProcessCommand(ParseResult parseResult)
        {
            switch (parseResult.RootCommandResult.Command.Name)
            {
                case "sniff":
                    {
                        var interfaceIdx = parseResult.GetRequiredValue<int>("interface");
                        var port = parseResult.GetRequiredValue<ushort>("port");
                        var customFilter = parseResult.GetValue<string>("custom-filter");
                        ClearLogRequested?.Invoke();
                        StartNewSniffer(interfaceIdx, port, customFilter);
                    }
                    break;
                case "interfaces":
                    {
                        Logged?.Invoke("");
                        Logged?.Invoke("Interfaces:");
                        int idx = 0;
                        foreach (var inter in PortSniffer.GetInterfaces())
                        {
                            Logged?.Invoke($"[{idx++}]: {inter}");
                        }
                    }
                    break;
                case "pause":
                    if (_sniffer is null || !_sniffer.HasStarted) return;
                    _pausedSniffing = true;
                    StatusUpdate?.Invoke($"Sniffing (Paused) on interface index: {_sniffer.DeviceIndex}, port: {_sniffer.RemotePort}, custom filter: {_sniffer.CustomFilter ?? "none"}");
                    break;
                case "resume":
                    if (_sniffer is null || !_sniffer.HasStarted) return;
                    _pausedSniffing = false;
                    StatusUpdate?.Invoke($"Sniffing on interface index: {_sniffer.DeviceIndex}, port: {_sniffer.RemotePort}, custom filter: {_sniffer.CustomFilter ?? "none"}");
                    break;
                case "clear":
                    ClearLogRequested?.Invoke();
                    break;
                case "filter":
                    {
                        lock (_packetLogs)
                        {
                            var patterns = parseResult.GetValue<string[]>("pattern");
                            if (patterns == null || patterns.Length == 0)
                            {
                                _filterRegexes.Clear();
                                break;
                            }
                            foreach (var pattern in patterns)
                            {
                                _filterRegexes.Add(pattern);
                            }
                        }
                    }
                    break;
                case "dump":
                    {
                        _dumpPackets = true;
                        var fileName = parseResult.GetValue<string>("file-name");
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            _dumpFilename = fileName;
                        }
                        else
                        {
                            _dumpFilename = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";
                        }
                    }
                    break;
            }
        }

        public void Quit()
        {
            TryDumpPackets();
            lock (_packetLogs)
            {
                _isRunning = false;
            }
            if (_sniffer == null) return;
            lock (_sniffer)
            {
                _sniffer.StopSniffing();
            }
        }

        public void TryDumpPackets()
        {
            lock (_packetLogs)
            {
                if (!(_dumpPackets && _packetLogs.Count > 0))
                {
                    return;
                }
                if (!Directory.Exists(Default.DUMP_DIRECTORY))
                {
                    Directory.CreateDirectory(Default.DUMP_DIRECTORY);
                }
                string file = Path.Combine(Default.DUMP_DIRECTORY, _dumpFilename);
                File.WriteAllText(file, string.Join(Environment.NewLine, _packetLogs));
            }
        }
    }
}