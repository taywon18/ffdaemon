using System.Drawing;

namespace ffconvert
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

    public static class Logger
    {
        public static ConsoleColor DefaultBackgroundColor { get; }= ConsoleColor.Black;
        public static ConsoleColor DefaultForegroundColor { get; } = ConsoleColor.Gray;

        private static object Mutex = new object();

        public static Verbosity MinConsoleVerbosity = Verbosity.Debug;

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

                    Console.Write(arg.ToString());
                }

                Console.Write(Environment.NewLine);
                Console.ResetColor();
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
                return ConsoleColor.White;

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
    }
}
