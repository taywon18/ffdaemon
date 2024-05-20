using FFDeamon;
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

        private static List<ProgressBar> _ProgressBars = new();
        private static Timer? ProgressBarTimer = null;
        private static int DrawnProgressBar = 0;
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
                EraseAllProgressBar();

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

                Console.Write(Environment.NewLine);
                Console.ResetColor();

                DrawAllProgressBar();
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

        public static void RegisterProgressBar(ProgressBar bar)
        {
            lock (Mutex)
            {
                _ProgressBars.Add(bar);

                if (ProgressBarTimer == null)
                {
                    ProgressBarTimer = new Timer(TimerHandler);
                    ResetTimer();
                }
            }
        }

        public static void UnregisterProgressBar(ProgressBar bar)
        {
            lock (Mutex)
            {
                _ProgressBars.Remove(bar);
            }
        }

        private static void TimerHandler(object? state)
        {
            lock (Mutex)
            {
                
                DrawAllProgressBar();

                ResetTimer();
            }
        }
        

        private static void ResetTimer()
        {
            if(ProgressBarTimer != null)
                ProgressBarTimer.Change(animationInterval, TimeSpan.FromMilliseconds(-1));
        }

        private static void EraseAllProgressBar()
        {
            lock(Mutex)
            {
                if (DrawnProgressBar == 0)
                    return;
                
                StringBuilder fullEmptyLine = new();
                for (int i = 0; i < Console.WindowWidth; i++)
                    fullEmptyLine.Append(" ");

                int top = Console.CursorTop;
                for(int i = 0; i < DrawnProgressBar; i++)
                {
                    Console.CursorTop = top - i - 1;
                    Console.CursorLeft = 0;
                    Console.WriteLine(fullEmptyLine.ToString());
                    Console.CursorTop--;
                }
                Console.CursorLeft = 0;
                DrawnProgressBar = 0;
            }                 
        }

        private static void DrawAllProgressBar()
        {
            EraseAllProgressBar();

            lock (Mutex)
            {
                DrawnProgressBar = 0;
                foreach(var p in _ProgressBars)
                {
                    p.Draw();
                    DrawnProgressBar++;
                }                            
            }
        }
    }
}
