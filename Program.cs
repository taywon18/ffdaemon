using FFDaemon;
using FFMpegCore;
using FFMpegCore.Enums;
using System.Text;

#region Boot

//Configuration:

System.Console.CursorVisible = false;
System.Console.OutputEncoding = Encoding.UTF8;

HashSet<string> SkipBuffer = new();

Configuration conf = new Configuration();

object InteractivityMutex = new object();
bool Active = true;
bool? ForceActive = null;
bool ScheduleStop = false;

Action CancelCurrentFFMPeg = () => { };
AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnExited);

string OutputPath = Path.Combine(conf.WorkingDirectoryPath, "temporary" + "." + conf.OutputExtension);

// Main program:

PrintLogo();
conf.LoadFromCommandLine();
conf.LoadFromConfigFile();
PrintConfiguration();
SetupInteractivity();



await Start();

#endregion

#region Main Logic

async Task Start()
{
    if (conf.ForcedDestinationDirectoryPath != null && !Directory.Exists(conf.ForcedDestinationDirectoryPath))
        Directory.CreateDirectory(conf.ForcedDestinationDirectoryPath);

    if (File.Exists(OutputPath))
    {
        if (!conf.ShouldDeleteTemporaryFile)
            throw new Exception($"Safe lock: Please stop other encoding or remove file {OutputPath}.+");

        File.Delete(OutputPath);
    }
    DisableIfNeeded();

    await FirstFrame();
    while (true)
    {
        if (ScheduleStop)
            break;
        await ClassicFrame();
    }
}

async Task FirstFrame()
{
    bool isActive;

    lock (InteractivityMutex)
        isActive = Active;

    if (isActive && !await Handle())
    {
        FFDaemon.IOManager.Information(Flavor.Important, "Ready to encode", Flavor.Normal, $" future files put in ", conf.WorkingDirectoryPath);
        await Task.Delay(conf.WaitingTime);
    }
    else if (!isActive)
        FFDaemon.IOManager.Information(Flavor.Important, "Waiting ", conf.StartActivityBound, Flavor.Normal, $" for start...");
}

async Task ClassicFrame()
{
    if (DisableIfNeeded())
    {
        await Task.Delay(conf.WaitingTime);
        return;
    }

    ActivateIfNeeded();

    bool isActive;
    bool? isForcedActivity;
    lock (InteractivityMutex)
    {
        isActive = Active;
        isForcedActivity = ForceActive;
    }
        

    if (isForcedActivity == null)
    {
        if(isActive && !await Handle())
            await Task.Delay(conf.WaitingTime);
        else if(!isActive)
            await Task.Delay(conf.WaitingTime);
    }
    else if(isForcedActivity == false)
        await Task.Delay(conf.WaitingTime);
    else if (isForcedActivity == true && !await Handle())
        await Task.Delay(conf.WaitingTime);

}

#endregion

#region Additional logic

void PrintLogo()
{
    FFDaemon.IOManager.Information(
        "#########################################");
    FFDaemon.IOManager.Information("##                                     ##");
    FFDaemon.IOManager.Information("##     =====    ", Flavor.Progress, "FFDaemon", Flavor.Normal, "     =====     ##");
    FFDaemon.IOManager.Information("##                                     ##");
    FFDaemon.IOManager.Information("#########################################");
    FFDaemon.IOManager.Information();
}

void PrintConfiguration()
{
#pragma warning disable CS8604 // Existence possible d'un argument de référence null.
    FFDaemon.IOManager.Information($"Working directory: ", Flavor.Important, conf.WorkingDirectoryPath);
    FFDaemon.IOManager.Information($"Forced destination path : ", Flavor.Important, conf.ForcedDestinationDirectoryPath);

    FFDaemon.IOManager.Information($"Allowed inputs: ", Flavor.Important, string.Join(",", conf.AllowedInputs));
    FFDaemon.IOManager.Information($"Output extension: ", Flavor.Important, conf.OutputExtension);
    FFDaemon.IOManager.Information($"Targeted video codec: ", Flavor.Important, conf.TargetedVideoCodec);

    FFDaemon.IOManager.Information($"Base custom input arguments: ", Flavor.Important, conf.BaseCustomInputArguments);
    FFDaemon.IOManager.Information($"Base custom input arguments: ", Flavor.Important, conf.BaseCustomOutputArguments);

    FFDaemon.IOManager.Information($"Should set aspect ratio to 1:1: ", conf.ShouldSetRatioToOneOne);
    FFDaemon.IOManager.Information($"Should use smart audio encoding: ", conf.SmartAudioEncoding);
    FFDaemon.IOManager.Information($"Should keep only one video stream: ", conf.KeepOnlyOneVideoStream);
    FFDaemon.IOManager.Information($"Should remove old file: ", conf.ShouldRemoveOldFile);
    FFDaemon.IOManager.Information($"Should send removed to bin: ", conf.ShouldSendRemovedToBin);
    FFDaemon.IOManager.Information($"Should kill ffmpeg when exited: ", conf.ShouldKillFFMpegWhenExited);
    FFDaemon.IOManager.Information($"Should delete empty directories: ", conf.ShouldDeleteEmptyDirectories);
    FFDaemon.IOManager.Information($"Should delete temporary file at start: ", conf.ShouldDeleteTemporaryFile);

    FFDaemon.IOManager.Information($"History max size: ", conf.MaxHistorySize);
    FFDaemon.IOManager.Information($"Console buffer max size: ", conf.MaxConsoleBufferSize);
    FFDaemon.IOManager.Information($"Console minimum verbosity: ", Flavor.Important, FFDaemon.IOManager.MinConsoleVerbosity.ToString());

    FFDaemon.IOManager.Information($"Idle time: ", conf.WaitingTime);
    FFDaemon.IOManager.Information($"Start time: ", conf.StartActivityBound);
    FFDaemon.IOManager.Information($"Stop time: ", conf.StopActivityBound);

    FFDaemon.IOManager.Information();
    #pragma warning restore CS8604 // Existence possible d'un argument de référence null.
}

void PrintInteractivity()
{
    FFDaemon.IOManager.Information("Force awake: ", Flavor.Important, "F1");
    FFDaemon.IOManager.Information("Force sleep: ", Flavor.Important, "F2");
    FFDaemon.IOManager.Information("Restore state: ", Flavor.Important, "F3");

    FFDaemon.IOManager.Information("Schedule stop: ", Flavor.Important, "F10");
    FFDaemon.IOManager.Information("Unschedule stop: ", Flavor.Important, "F11");
    FFDaemon.IOManager.Information("Exit now: ", Flavor.Important, "F12");

    IOManager.Information();
}

void SetupInteractivity()
{
    if (!conf.Interactive)
        return;

    IOManager.Callbacks.OnForceAwake = () =>
    {
        IOManager.Information("Forcing ", Flavor.Important, "awakening.");
        lock(InteractivityMutex)
            ForceActive = true;
        return;
    };

    IOManager.Callbacks.OnForceSleep = () =>
    {
        IOManager.Information("Forcing ", Flavor.Important, "sleep.");
        lock (InteractivityMutex)
            ForceActive = false;
        return;
    };

    IOManager.Callbacks.OnUnforceState = () =>
    {
        IOManager.Information("Remove any forced state.");
        lock (InteractivityMutex)
            ForceActive = null;
        return;
    };
        
    IOManager.Callbacks.OnScheduleStop = () =>
    {
        IOManager.Information("Scheduling exit at next idle time.");
        lock (InteractivityMutex)
            ScheduleStop = true;
        return;
    };

    IOManager.Callbacks.OnUnscheduleStop = () =>
    {
        IOManager.Information("Unscheduling exit.");
        lock (InteractivityMutex)
            ScheduleStop = false;
        return;
    };

    IOManager.Callbacks.OnQuit = () =>
    {
        IOManager.Information("Exiting now...");
        Environment.Exit(0);
        return;
    };

    IOManager.StartInteractivity();
    PrintInteractivity();
}

void OnExited(object? sender, EventArgs e)
{
    if (conf.ShouldKillFFMpegWhenExited)
        CancelCurrentFFMPeg();
}

bool ActivateIfNeeded()
{
    var now = DateTime.Now.TimeOfDay;
    lock (InteractivityMutex)
        if (!Active
            && conf.StopActivityBound != null
            && conf.StartActivityBound != null
            && ((conf.StartActivityBound < conf.StopActivityBound && now >= conf.StartActivityBound && now < conf.StopActivityBound)
            || (conf.StartActivityBound > conf.StopActivityBound && 
            (
                now >= conf.StartActivityBound || now < conf.StopActivityBound
            )))
)
        {
            FFDaemon.IOManager.Debug("Leaving sleeping mode.");
            Active = true;
            if (conf.ExecuteAfterStart is not null)
                System.Diagnostics.Process.Start(conf.ExecuteAfterStart);
            return true;
        }

    return false;
}

bool DisableIfNeeded()
{
    var now = DateTime.Now.TimeOfDay;
    lock (InteractivityMutex)
        if (Active
        && conf.StopActivityBound != null
        && conf.StartActivityBound != null
        && (
            (conf.StartActivityBound < conf.StopActivityBound && (now < conf.StartActivityBound || now >= conf.StopActivityBound)
        )
        || (conf.StartActivityBound > conf.StopActivityBound && now >= conf.StopActivityBound && now < conf.StartActivityBound)))
        {
            FFDaemon.IOManager.Debug("Entering in sleeping mode.");
            Active = false;
            if(conf.ExecuteAfterStop is not null)
                System.Diagnostics.Process.Start(conf.ExecuteAfterStop);
            return true;
        }

    return false;
}

#endregion