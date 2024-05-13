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

//return true if convert, false otherwise
async Task<bool> Handle()
{
    var rawCandidates = Directory.GetFiles(conf.WorkingDirectoryPath, "*", searchOption: SearchOption.AllDirectories);
    var candidates = rawCandidates
        .Where(file => conf.AllowedInputs.Any(file.ToLower().EndsWith))
        .OrderBy(x =>
        {
            FileInfo fi = new(x);
            return fi.LastWriteTime;
        })
        .ToList();

    foreach (var InputPath in candidates)
    {
        string CustomInputsArgs = conf.BaseCustomInputArguments ?? "";
        string CustomOutputArgs = conf.BaseCustomOutputArguments ?? "";

        if (SkipBuffer.Contains(InputPath))
            continue;

        FileInfo fiInput = new FileInfo(InputPath);
        if (!fiInput.Exists)
        {
            SkipBuffer.Add(InputPath);
            continue;
        }

        IMediaAnalysis? mediaInfo = null;
        try
        {
            mediaInfo = await FFProbe.AnalyseAsync(InputPath);
        }
        catch (Exception e)
        {
            SkipBuffer.Add(InputPath);
            FFDaemon.IOManager.Error($"Cannot analyse {InputPath}: {e}");
            continue;
        }
        var totalTime = mediaInfo.Duration;

        //Skip already converted files
        if (!ShouldEncode(mediaInfo))
        {
            SkipBuffer.Add(InputPath);
            continue;
        }

        if (!conf.KeepOnlyOneVideoStream)
            throw new NotImplementedException();
        else
            CustomOutputArgs += " -map 0:v:0 -c:v libvpx-vp9";

        if (conf.ShouldSetRatioToOneOne 
            && mediaInfo.PrimaryVideoStream != null
            && IsBadAspectRatio(mediaInfo))
        {
            int w = (int)((double)mediaInfo.PrimaryVideoStream.Width * (double)(mediaInfo.PrimaryVideoStream.SampleAspectRatio.Width) / (double)(mediaInfo.PrimaryVideoStream.SampleAspectRatio.Height));
            int h = mediaInfo.PrimaryVideoStream.Height;
            string aspectRatio = mediaInfo.PrimaryVideoStream.DisplayAspectRatio.Width + ":" + mediaInfo.PrimaryVideoStream.DisplayAspectRatio.Height;
            CustomOutputArgs += $" -vf scale={w}:{h} -aspect {aspectRatio}";
        }

        CustomOutputArgs += " -map 0:a";
        if (!conf.SmartAudioEncoding)
            CustomOutputArgs += " -c:a libvorbis";
        else
            for (int i = 0; i < mediaInfo.AudioStreams.Count; i++)
            {
                var audioStream = mediaInfo.AudioStreams[i];
                if ((audioStream.ChannelLayout ?? "").ToLower().EndsWith("(side)"))
                    CustomOutputArgs += $" -c:a:{i} libvorbis";
                else
                    CustomOutputArgs += $" -c:a:{i} libopus";
            }

        if (mediaInfo.SubtitleStreams.Count > 0)
        {
            bool hasTextSubtitle = false;
            for (int i = 0; i < mediaInfo.SubtitleStreams.Count; i++)
            {
                var subtitleStream = mediaInfo.SubtitleStreams[i];
                CustomOutputArgs += $" -map 0:s:{i}";
                if (subtitleStream.CodecName == "dvb_teletext" || subtitleStream.CodecName == "ass" || subtitleStream.CodecName == "mov_text")
                {
                    hasTextSubtitle |= true;
                    CustomOutputArgs += $" -c:s:{i} webvtt";
                }
                else
                    CustomOutputArgs += $" -c:s:{i} copy";
            }
            if (hasTextSubtitle)
                CustomInputsArgs += $" -txt_format text -fix_sub_duration";
        }

        FFDaemon.IOManager.Debug($"Converting {InputPath}...");

        string ConsoleBuffer = "";

        var ffmpegArgs = FFMpegArguments
            .FromFileInput(InputPath, true, options => options
                .WithCustomArgument(CustomInputsArgs)
            )
            .OutputToFile(OutputPath, false, options => options
                .WithCustomArgument(CustomOutputArgs)
            )
            .CancellableThrough(out CancelCurrentFFMPeg)
            .NotifyOnError((string message) =>
            {
                if (ConsoleBuffer.Length < conf.MaxConsoleBufferSize)
                    ConsoleBuffer += message;
            })
            .NotifyOnOutput((string message) =>
            {
                if (ConsoleBuffer.Length < conf.MaxConsoleBufferSize)
                    ConsoleBuffer += message;
            });

        FFDaemon.IOManager.Debug($"Executed command: {ffmpegArgs.Arguments}");

        string relativeInputPath = Path.GetRelativePath(conf.WorkingDirectoryPath, InputPath);
        string? InputDirRelative = null;
        if (fiInput.DirectoryName != null)
            InputDirRelative = Path.GetRelativePath(conf.WorkingDirectoryPath, fiInput.DirectoryName);

        IOManager.SetupProgressBar("", 0, relativeInputPath);

        List<KeyValuePair<DateTime, TimeSpan>> History = new();
        ffmpegArgs = ffmpegArgs.NotifyOnProgress((TimeSpan current) =>
        {
            History.Add(new(DateTime.Now, current));
            while (History.Count > conf.MaxHistorySize && History.Count > 0)
                History.RemoveAt(0);
            TimeSpan? remainRealTime = null;
            if (History.Count > 5)
            {
                var firstHistoryEntry = History.First();
                var elapsedRealTimeSinceFirstEntry = DateTime.Now - firstHistoryEntry.Key;
                var encodedTimeSinceFirstEntry = current - firstHistoryEntry.Value;
                var remainEncoding = totalTime - current;

                remainRealTime = TimeSpan.FromSeconds(remainEncoding.TotalSeconds * elapsedRealTimeSinceFirstEntry.TotalSeconds / encodedTimeSinceFirstEntry.TotalSeconds);
            }
            var relativeProgress = current / totalTime;

            IOManager.SetupProgressBar("", (float)relativeProgress, remainRealTime == null
                    ? $" {relativeInputPath}"
                    : $" {relativeInputPath}, {remainRealTime.Value.ToReadableString()}");
        });

        bool worked = await ffmpegArgs.ProcessAsynchronously(false);
        CancelCurrentFFMPeg = () => { };
        IOManager.IsProgressBarActive = false;

        SkipBuffer.Add(InputPath);
        if (!worked)
        {
            FFDaemon.IOManager.Error($"Encoding failed for {InputPath}.");
            SkipBuffer.Add(InputPath);
            if (File.Exists(OutputPath))
                File.Delete(OutputPath);
            return true;
        }

        var newMediaInfo = await FFProbe.AnalyseAsync(OutputPath);
        if (ShouldEncode(newMediaInfo, true))
            FFDaemon.IOManager.Error($"Warning, file analysis for {OutputPath} mark this file as non-encoded.");

        if (fiInput.DirectoryName == null)
            throw new Exception($"Safe lock: empty DirectoryName for {fiInput}.");

        string newInputPath;
        string newInputFileName = Path.GetFileNameWithoutExtension(InputPath) + "." + conf.OutputExtension;
        if (conf.ForcedDestinationDirectoryPath != null)
            newInputPath = Path.Combine(conf.ForcedDestinationDirectoryPath, InputDirRelative ?? "", newInputFileName);
        else
            newInputPath = Path.Combine(fiInput.DirectoryName, newInputFileName);

        FileInfo newInputFi = new(newInputPath);
        if (newInputFi.DirectoryName != null && !Directory.Exists(newInputFi.DirectoryName))
            Directory.CreateDirectory(newInputFi.DirectoryName);

        if (conf.ShouldRemoveOldFile)
        {
            /*Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                InputPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                ShouldSendRemovedToBin 
                    ? Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin 
                    : Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);*/
            File.Delete(InputPath);
        }

        File.Move(OutputPath, newInputPath);
        if (!File.Exists(newInputPath))
            FFDaemon.IOManager.Error($"Move failed, cannot file any file at {newInputPath}.");

        if (conf.ShouldDeleteEmptyDirectories
        && conf.ForcedDestinationDirectoryPath != null
        && fiInput.DirectoryName != null
        && fiInput.DirectoryName != conf.WorkingDirectoryPath
        && !Directory.EnumerateFileSystemEntries(fiInput.DirectoryName).Any())
            Directory.Delete(fiInput.DirectoryName);

        FFDaemon.IOManager.Information(Flavor.Ok, "Fichier remplacé avec succès", Flavor.Normal, $": {InputPath}.");
        return true;
    }

    return false;
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


bool ShouldEncode(FFMpegCore.IMediaAnalysis? media, bool verbose = false)
{
    // skip bad analysis
    if (media == null)
        return false;

    // skip non-video files
    if(media.PrimaryVideoStream == null)
        return false;

    // encode if more than 1 video stream
    if (conf.KeepOnlyOneVideoStream && media.VideoStreams.Count > 1)
    {
        FFDaemon.IOManager.Debug($"Marked as non-encoded: found multiples video streams ({media.VideoStreams.Count}).");
        return true;
    }        

    // encode bad codec
    if (media.PrimaryVideoStream.CodecName != conf.TargetedVideoCodec)
    {
        FFDaemon.IOManager.Debug($"Marked as non-encoded: bad video codec found ({media.PrimaryVideoStream.CodecName}).");
        return true;
    }

    if (conf.ShouldSetRatioToOneOne && IsBadAspectRatio(media))
    {
        FFDaemon.IOManager.Debug($"Marked as non-encoded: bad SAR found ({media.PrimaryVideoStream.SampleAspectRatio.Width}):{media.PrimaryVideoStream.SampleAspectRatio.Height}.");
        return true;
    }

    return false;
}

bool IsBadAspectRatio(FFMpegCore.IMediaAnalysis media)
{
    if (media.PrimaryVideoStream == null)
        throw new Exception("Cannot handle non-video steam.");

    // No SAR
    if (media.PrimaryVideoStream.SampleAspectRatio.Width == 0 && media.PrimaryVideoStream.SampleAspectRatio.Height == 0)
        return false;

    if (media.PrimaryVideoStream.SampleAspectRatio.Width != 1
    || media.PrimaryVideoStream.SampleAspectRatio.Height != 1)
        return true;

    return false;
}
#endregion