namespace FFDeamon;

public class ProgressBar
{
    object Mutex { get; } = new object();

    private const string animation = @"|/―\";
    private static int animationIndex
    {
        get
        {
            return (int)(DateTime.Now.Ticks % 100L);
        }
    }
    private const int blockCount = 10;

    string? _prefix;
    public string? Prefix
    {
        get
        {
            lock(Mutex)
                return _prefix;
        }

        set
        {
            lock(Mutex)
                _prefix = value;
        }
    }

    string? _suffix;
    public string? Suffix
    {
        get
        {
            lock(Mutex)
                return _suffix;
        }

        set
        {
            lock(Mutex)
                _suffix = value;
        }
    }

    float _progress;
    public float Progress
    {
        get
        {
            lock(Mutex)
                return _progress;
        }

        set
        {
            lock(Mutex)
                _progress = value;
        }
    }

    string ProgressText
    {
        get
        {
            int progressBlockCount = (int)(Progress * blockCount);
            double percent = (double)(Progress * 100);
            string text = string.Format("[{0}{1}] {2,3:N1}% {3}",
                new string('#', progressBlockCount), new string('-', blockCount - progressBlockCount),
                percent,
                animation[animationIndex % animation.Length]);

            return Prefix + text + Suffix;
        }
    }

    public void Setup(string prefix, float progress, string suffix)
    {
        lock (Mutex)
        {
            _prefix = prefix;
            _progress = progress;
            _suffix = suffix;
        }
    }

    public void Draw()
    {
        string writableText;
        string text = ProgressText;
        if (text.Length == Console.WindowWidth)
            writableText = text;
        else if (text.Length > Console.WindowWidth)
            writableText = text.Substring(0, Console.WindowWidth - 3) + "...";
        else if (text.Length < Console.WindowWidth)
            writableText = text + new string(' ', Console.WindowWidth - text.Length);
        else
            throw new NotImplementedException();

        Console.WriteLine(writableText);
    }
}
