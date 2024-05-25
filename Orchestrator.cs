using FFDeamon;

namespace FFDaemon;

public class Orchestrator
{
    public Configuration Configuration { get; set; }
    public Activity Activity { get; }
    public Interactivity Interactivity { get; }
    object Mutex = new object();
    HashSet<string> SkipBuffer = new ();

    bool ShouldStop = false;

    int _targetTaskCount = 0;
    public int TargetTaskCount { 
        get
        {
            lock (Mutex)
                return _targetTaskCount;
        }
        set
        {
            lock(Mutex)
                _targetTaskCount = value;
        }
    }
    HashSet<EncodingTask> RunningTasks = new();

    public Orchestrator()
    {
        Configuration = new();
        Activity = new(this);
        Interactivity = new(this);
    }

    public void ScheduleStop(bool shouldStop = true)
    {
        lock(Mutex)
            ShouldStop = shouldStop;
    }

    public async Task Boot()
    {
        PrintLogo();
        Configuration.LoadFromCommandLine();
        Configuration.LoadFromConfigFile();
        Configuration.Print();
        Interactivity.SetupInteractivity();
        _targetTaskCount = Configuration.InstanceCount;

        if (Configuration.ForcedDestinationDirectoryPath != null && !Directory.Exists(Configuration.ForcedDestinationDirectoryPath))
            Directory.CreateDirectory(Configuration.ForcedDestinationDirectoryPath);

        if (Directory.Exists(Configuration.TemporaryDirectoryPath))
        {
            var oldTemporaries = Directory.EnumerateFiles(Configuration.TemporaryDirectoryPath).ToList();
            if (oldTemporaries.Count != 0)
            {
                if (!Configuration.ShouldDeleteTemporaryFile)
                    throw new Exception($"Safe lock: Please stop other encoding or remove any file in dir {Configuration.TemporaryDirectoryPath}.");

                foreach (var i in oldTemporaries)
                    File.Delete(i);
            }
        }
        else
            Directory.CreateDirectory(Configuration.TemporaryDirectoryPath);

        Activity.DisableIfNeeded();

        await FirstFrame();
        while (true)
        {
            lock(Mutex)
                if (ShouldStop)
                    break;

            await ClassicFrame();
        }
    }

    public void StopNow()
    {
        lock (Mutex)
            foreach (var t in RunningTasks)
                t.InterruptIfNeeded();
    }

    #region Frames
    async Task FirstFrame()
    {
        bool isActive = Activity.ShouldWork();

        if (!isActive)
        {
            FFDaemon.IOManager.Information(Flavor.Important, "Waiting ", Configuration.StartActivityBound, Flavor.Normal, $" for start...");
            return;
        }

        var encoding = await AddNewIfNeeded();
        if (encoding is null)
        {
            FFDaemon.IOManager.Information(Flavor.Ok, "Started", Flavor.Normal, $", waiting for candidate file in {Configuration.WorkingDirectoryPath} 😁.");
            await Activity.Wait();
            return;
        }

        lock (Mutex)
            RunningTasks.Add(encoding);
    }

    async Task ClassicFrame()
    {
        if (Activity.DisableIfNeeded())
        {
            await Activity.Wait();
            return;
        }
        Activity.ActivateIfNeeded();

        if(!Activity.ShouldWork())
        {
            await Activity.Wait();
            return;
        }

        var encoding = await AddNewIfNeeded();
        if(encoding is null)
        {
            await Activity.Wait();
            return;
        }

        lock (Mutex)
            RunningTasks.Add(encoding);
    }
    #endregion

    #region Task management
    async Task<EncodingTask?> AddNewIfNeeded()
    {
        if (CountTasks() >= TargetTaskCount)
            return null;

        return await AddNew();
    }

    async Task<EncodingTask?> AddNew()
    {
        EncodingTask et = new(this);
        if (!await et.Prepare())
            return null;

        et.FireAndForget();
        return et;
    }

    int CountTasks()
    {
        lock (Mutex)
            return RunningTasks.Count;
    }

    public bool Remove(EncodingTask task)
    {
        lock(Mutex)
        {
            task.InterruptIfNeeded();
            //todo: Remove if needed
            return RunningTasks.Remove(task);
        }
    }
    #endregion

    #region Searching path
    public List<string> GetFileCandidates()
    {
        var rawCandidates = Directory.GetFiles(Configuration.WorkingDirectoryPath, "*", searchOption: SearchOption.AllDirectories);
        var candidates = rawCandidates
            .Where(file => Configuration.AllowedInputs.Any(file.ToLower().EndsWith))
            .OrderBy(x =>
            {
                FileInfo fi = new(x);
                return fi.LastWriteTime;
            })
            .ToList();

        return candidates;
    }

    public async Task<string?> PickPath(Func<string, Task<bool>> picker)
    {
        var candidates = GetFileCandidates();
        foreach(var candidate in candidates)
        {
            lock(Mutex)
                if (SkipBuffer.Contains(candidate))
                    continue;

            var r = await picker(candidate);
            lock(Mutex)
            {
                SkipBuffer.Add(candidate);
                if (r)
                    return candidate;
            }
        }

        return null;
    }
    #endregion

    #region Logs
    void PrintLogo()
    {
        FFDaemon.IOManager.Information("#########################################");
        FFDaemon.IOManager.Information("##                                     ##");
        FFDaemon.IOManager.Information("##     =====    ", Flavor.Progress, "FFDaemon", Flavor.Normal, "     =====     ##");
        FFDaemon.IOManager.Information("##                                     ##");
        FFDaemon.IOManager.Information("#########################################");
        FFDaemon.IOManager.Information();
    }
    #endregion
}
