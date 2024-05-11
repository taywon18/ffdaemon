using System.Drawing;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace FFDaemon
{        
    public enum Verbosity
    {
        Debug,
        Information,
        Error,
        Critical
    }

    public enum Flavor
    {
        Normal,
        Important,
        Ok,
        Failed,
        Progress
    }

    public class InteractivityCallbacks
    {
        public Action OnQuit { get; set; } = () => { };
        public Action OnScheduleStop { get; set; } = () => { };
        public Action OnUnscheduleStop { get; set; } = () => { };
        public Action OnForceSleep { get; set; } = () => { };
        public Action OnForceAwake { get; set; } = () => { };
        public Action OnUnforceState { get; set; } = () => { };
    }

    public static class IOManager
    {
        public static ConsoleColor DefaultBackgroundColor { get; }= ConsoleColor.Black;
        public static ConsoleColor DefaultForegroundColor { get; } = ConsoleColor.Gray;
        public static InteractivityCallbacks Callbacks { get; } = new();

        private static object Mutex = new object();

        public static Verbosity MinConsoleVerbosity = Verbosity.Debug;

        private static bool _IsProgressBarActive = false;
        private static string _ProgressBarPrefix = "";
        private static string _ProgressBarSuffix = "";
        private static float _ProgressBarValue = 0.0f;
        
        private static Timer? ProgressBarTimer = null;

        private static string ProgressText = "";
        public static bool IsProgressBarActive
        {
            get 
            {
                lock (Mutex) 
                {
                    return _IsProgressBarActive; 
                } 
            }
            set 
            {
                lock (Mutex)
                {
                    if (ProgressBarTimer == null)
                    {
                        ProgressBarTimer = new Timer(TimerHandler);
                        ResetTimer();
                    }

                    if (_IsProgressBarActive && !value)
                        EraseProgressBar();

                    if (!_IsProgressBarActive && value)
                    {
                        RefreshProgressText();
                        DrawProgressBar();
                    }                        

                    _IsProgressBarActive = value;
                }
            }
        }

        private static int animationIndex = 0;
        private const int blockCount = 10;
        private const string animation = @"|/―\";
        private static readonly TimeSpan animationInterval = TimeSpan.FromSeconds(1.0 / 8);



        public static void Debug(params object[] arguments)
        {
            Log(Verbosity.Debug, arguments);
        }

        public static void Information(params object[] arguments)
        {
            Log(Verbosity.Information, arguments);
        }

        public static void Error(params object[] arguments)
        {
            Log(Verbosity.Error, arguments);
        }

        public static void Critical(params object[] arguments)
        {
            Log(Verbosity.Critical, arguments);
        }

        public static void Log(Verbosity verbosity, params object[] arguments)
        {
            if (verbosity < MinConsoleVerbosity)
                return;

            lock (Mutex)
            {
                SetConsoleColorByVerbosity(verbosity);

                foreach (object arg in arguments)
                {
                    if (arg is Flavor flavor)
                    {
                        if (flavor == Flavor.Normal)
                            Console.ResetColor();
                        else
                            SetConsoleColorbyFlavor(flavor);

                        continue;
                    }

                    PrintFormated(arg);
                }

                if (_IsProgressBarActive)
                    EraseProgressBar();

                Console.Write(Environment.NewLine);
                Console.ResetColor();

                if (_IsProgressBarActive)
                    DrawProgressBar();
            }
        }

        public static KeyValuePair<ConsoleColor, ConsoleColor> GetConsoleColorByVerbosity(Verbosity verbosity)
        {
            if (verbosity == Verbosity.Debug)
                return KeyValuePair.Create(ConsoleColor.DarkGray, DefaultBackgroundColor);

            if (verbosity == Verbosity.Information)
                return KeyValuePair.Create(DefaultForegroundColor, DefaultBackgroundColor);

            if (verbosity == Verbosity.Error)
                return KeyValuePair.Create(ConsoleColor.DarkRed, DefaultBackgroundColor);

            if(verbosity == Verbosity.Critical)
                return KeyValuePair.Create(ConsoleColor.White, ConsoleColor.Red);

            throw new NotImplementedException();
        }

        public static void SetConsoleColorByVerbosity(Verbosity verbosity)
        {
            var colors = GetConsoleColorByVerbosity(verbosity);
            Console.ForegroundColor = colors.Key;
            Console.BackgroundColor = colors.Value;
        }

        public static ConsoleColor GetConsoleColorByFlavor(Flavor flavor)
        {
            if (flavor == Flavor.Important)
                return ConsoleColor.Cyan;

            if (flavor == Flavor.Ok)
                return ConsoleColor.Green;

            if(flavor == Flavor.Progress)
                return ConsoleColor.Blue;

            if(flavor == Flavor.Failed)
                return ConsoleColor.Red;

            throw new NotImplementedException();
        }

        public static void SetConsoleColorbyFlavor(Flavor flavor)
        {
            var color = GetConsoleColorByFlavor(flavor);
            Console.ForegroundColor = color;
        }

        private static void PrintFormated(object obj)
        {
            ConsoleColor background = Console.BackgroundColor;
            ConsoleColor foreground = Console.ForegroundColor;

            if (obj is null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("🚫 null");
            }
            else if (obj is string s)
            {
                if(s == "")
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write("(empty)");
                }
                else
                    Console.Write(s);
            }
            else if (obj is bool b)
            {
                if (b)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("✔️ true");
                }                    
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("❌ false");
                }                    
            }
            else if(obj is TimeSpan dt)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(dt.ToString());
            }
            else if(Helper.IsNumericType(obj.GetType()))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(obj.ToString());
            }
            else
                Console.Write(obj.ToString());

            Console.BackgroundColor = background;
            Console.ForegroundColor = foreground;
        }

        public static void StartInteractivity(CancellationToken tk = default)
        {
            Task.Run(async () =>
            {
                while (!tk.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(true);
                        switch (key.Key)
                        {
                            case ConsoleKey.F1:
                                Callbacks.OnForceAwake();
                                break;
                            case ConsoleKey.F2:
                                Callbacks.OnForceSleep();
                                break;                            
                            case ConsoleKey.F3:
                                Callbacks.OnUnforceState();
                                break;

                            case ConsoleKey.F6:
                                Callbacks.OnScheduleStop();
                                break;                            
                            case ConsoleKey.F7:
                                Callbacks.OnUnscheduleStop();
                                break;
                            case ConsoleKey.F8:
                                Callbacks.OnQuit();
                                break;
                            default:
                                break;
                        }
                    }
                    await Task.Delay(100);  
                }
            }).ConfigureAwait(false);
        }

        private static void TimerHandler(object? state)
        {
            lock (Mutex)
            {
                if (_IsProgressBarActive)
                {
                    RefreshProgressText();
                    DrawProgressBar();
                }

                ResetTimer();
            }
        }

        public static void SetupProgressBar(string prefix, float progress, string suffix)
        {
            lock (Mutex)
            {
                _ProgressBarPrefix = prefix;
                _ProgressBarValue = progress;
                _ProgressBarSuffix = suffix;
            }
            IsProgressBarActive = true;
        }

        private static void ResetTimer()
        {
            if(ProgressBarTimer != null)
                ProgressBarTimer.Change(animationInterval, TimeSpan.FromMilliseconds(-1));
        }

        private static void RefreshProgressText()
        {
            int progressBlockCount = (int)(_ProgressBarValue * blockCount);
            double percent = (double)(_ProgressBarValue * 100);
            string text = string.Format("[{0}{1}] {2,3:N1}% {3}",
                new string('#', progressBlockCount), new string('-', blockCount - progressBlockCount),
                percent,
                animation[animationIndex++ % animation.Length]);

            ProgressText = _ProgressBarPrefix + text + _ProgressBarSuffix;

        }

        private static void EraseProgressBar()
        {
            StringBuilder sb = new();
            for (int i = 0; i < Console.WindowWidth; i++)
                sb.Append(" ");

            Console.CursorLeft = 0;
            Console.Write(sb);
            Console.CursorLeft = 0;            
        }

        private static void DrawProgressBar()
        {
            Console.CursorLeft = 0;
            string writableText;
            if (ProgressText.Length == Console.WindowWidth)
                writableText = ProgressText;
            else if (ProgressText.Length > Console.WindowWidth)
                writableText = ProgressText.Substring(0, Console.WindowWidth - 3) + "...";
            else if (ProgressText.Length < Console.WindowWidth)
                writableText = ProgressText + new string(' ', Console.WindowWidth - ProgressText.Length);
            else
                throw new NotImplementedException();

            Console.Write(writableText);
            Console.CursorLeft = 0;
        }
    }
}
