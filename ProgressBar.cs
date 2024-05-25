namespace FFDeamon;

public class ProgressBar
{
    object Mutex { get; } = new object();

    private string DoneCharacter = "#"; //candidates: █
    private string NotDoneCharacter = " "; //candidates: ▓░️
    private string LeftBorderCharacter = "[";
    private string RightBorderCharacter = "]";

    private const string animation = @"|/―\";
    private static int animationIndex
    {
        get
        {
            return (int)(DateTime.Now.Second);
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

    string AllProgressText
    {
        get
        {
            string text = string.Format("{0} {1} {2}",
                AllBarText,
                Percent,
                Animation);

            return Prefix + text + Suffix;
        }
    }

    string DoneBarText
    {
        get
        {
            int progressBlockCount = (int)(Progress * blockCount);
            return string.Concat(Enumerable.Repeat(DoneCharacter, progressBlockCount));
        }
    }

    string RemainBarText
    {
        get
        {
            int progressBlockCount = (int)(Progress * blockCount);
            return string.Concat(Enumerable.Repeat(NotDoneCharacter, blockCount - progressBlockCount));
        }
    }

    string AllBarText
    {
        get
        {
            int progressBlockCount = (int)(Progress * blockCount);
            return string.Format("{2}{0}{1}{3}", DoneBarText, RemainBarText, LeftBorderCharacter, RightBorderCharacter);
        }
    }

    string Percent
    {
        get
        {
            double percent = (double)(Progress * 100);
            return string.Format("{0,3:N1}%", percent);
        }
    }

    string Animation
    {
        get
        {
            return animation[animationIndex % animation.Length].ToString();
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
        string text = AllProgressText;
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
