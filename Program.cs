using ffconvert;
using FFMpegCore;
using System.Text;

#region Boot

//Configuration:

Console.CursorVisible = false;
Console.OutputEncoding = Encoding.UTF8;

HashSet<string> SkipBuffer = new();

string WorkingDirectoryPath = Environment.CurrentDirectory;
string? ForcedDestinationDirectoryPath = "../Encoded";

string[] AllowedInputs = new[] { ".mkv", ".avi", ".vp9", ".ts", ".mp4", ".webm" };
string OutputExtension = "mkv";
string TargetedVideoCodec = "vp9";
string? BaseCustomInputArguments = "-y -probesize 1000000000 -analyzeduration 100000000";
string? BaseCustomOutputArguments = "";
bool ShouldSetRatioToOneOne = true;
bool SmartAudioEncoding = true;
bool KeepOnlyOneVideoStream = true;
bool ShouldRemoveOldFile = true;
bool ShouldSendRemovedToBin = false;
bool ShouldKillFFMpegWhenExited = true;
bool ShouldDeleteEmptyDirectories = true;
bool ShouldDeleteTemporaryFile = true;
int MaxHistorySize = 100;
int MaxConsoleBufferSize = 25000;
Logger.MinConsoleVerbosity = Verbosity.Information;

TimeSpan WaitingTime = TimeSpan.FromSeconds(60);
TimeSpan? StartActivityBound = TimeSpan.FromHours(23);
TimeSpan? StopActivityBound = TimeSpan.FromHours(7);
bool Active = true;

Action CancelCurrentFFMPeg = () => { };
AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnExited);

string OutputPath = Path.Combine(WorkingDirectoryPath, "temporary" + "." + OutputExtension);

// Main program:

PrintLogo();
PrintConfiguration();
await Start();

#endregion

#region Main Logic

async Task Start()
{
    if (ForcedDestinationDirectoryPath != null && !Directory.Exists(ForcedDestinationDirectoryPath))
        Directory.CreateDirectory(ForcedDestinationDirectoryPath);

    if (File.Exists(OutputPath))
    {
        if (!ShouldDeleteTemporaryFile)
            throw new Exception($"Safe lock: Please stop other encoding or remove file {OutputPath}.+");

        File.Delete(OutputPath);
    }
    DisableIfNeeded();

    await FirstFrame();
    while (true)
        await ClassicFrame();
}

async Task FirstFrame()
{
    if (Active && !await Handle())
    {
        Logger.Information(Flavor.Important, "Ready to encode", Flavor.Normal, $" future files put in \"{WorkingDirectoryPath}\"");
        await Task.Delay(WaitingTime);
    }
    else if (!Active)
        Logger.Information(Flavor.Important, "Waiting ", StartActivityBound, Flavor.Normal, $" for start...");
}

async Task ClassicFrame()
{
    if (DisableIfNeeded())
    {
        await Task.Delay(WaitingTime);
        return;
    }

    ActivateIfNeeded();

    if (Active && !await Handle())
        await Task.Delay(WaitingTime);
}

//return true if convert, false otherwise
async Task<bool> Handle()
{
    var rawCandidates = Directory.GetFiles(WorkingDirectoryPath, "*", searchOption: SearchOption.AllDirectories);
    var candidates = rawCandidates
        .Where(file => AllowedInputs.Any(file.ToLower().EndsWith))
        .ToList();

    foreach (var InputPath in candidates)
    {
        string CustomInputsArgs = BaseCustomInputArguments;
        string CustomOutputArgs = BaseCustomOutputArguments;

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
            Logger.Error($"Cannot analyse {InputPath}: {e}");
            continue;
        }
        var totalTime = mediaInfo.Duration;

        //Skip already converted files
        if (!ShouldEncode(mediaInfo))
        {
            SkipBuffer.Add(InputPath);
            continue;
        }

        if (!KeepOnlyOneVideoStream)
            throw new NotImplementedException();
        else
            CustomOutputArgs += " -map 0:v:0 -c:v libvpx-vp9";

        if (ShouldSetRatioToOneOne && IsBadAspectRatio(mediaInfo))
        {
            int w = (int)((double)mediaInfo.PrimaryVideoStream.Width * (double)(mediaInfo.PrimaryVideoStream.SampleAspectRatio.Width) / (double)(mediaInfo.PrimaryVideoStream.SampleAspectRatio.Height));
            int h = mediaInfo.PrimaryVideoStream.Height;
            string aspectRatio = mediaInfo.PrimaryVideoStream.DisplayAspectRatio.Width + ":" + mediaInfo.PrimaryVideoStream.DisplayAspectRatio.Height;
            CustomOutputArgs += $" -vf scale={w}:{h} -aspect {aspectRatio}";
        }

        CustomOutputArgs += " -map 0:a";
        if (!SmartAudioEncoding)
            CustomOutputArgs += " -c:a libvorbis";
        else
            for (int i = 0; i < mediaInfo.AudioStreams.Count; i++)
            {
                var audioStream = mediaInfo.AudioStreams[i];
                if (audioStream.ChannelLayout.ToLower().EndsWith("(side)"))
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
                if (subtitleStream.CodecName == "dvb_teletext" || subtitleStream.CodecName == "ass")
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

        Logger.Debug($"Converting {InputPath}...");

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
                if (ConsoleBuffer.Length < MaxConsoleBufferSize)
                    ConsoleBuffer += message;
            })
            .NotifyOnOutput((string message) =>
            {
                if (ConsoleBuffer.Length < MaxConsoleBufferSize)
                    ConsoleBuffer += message;
            });



        Logger.Debug($"Executed command: {ffmpegArgs.Arguments}");

        string relativeInputPath = Path.GetRelativePath(WorkingDirectoryPath, InputPath);
        string? InputDirRelative = null;
        if (fiInput.DirectoryName != null)
            InputDirRelative = Path.GetRelativePath(WorkingDirectoryPath, fiInput.DirectoryName);

        var progress = new ProgressBar(suffix: $" {relativeInputPath}");

        List<KeyValuePair<DateTime, TimeSpan>> History = new();
        ffmpegArgs = ffmpegArgs.NotifyOnProgress((TimeSpan current) =>
        {
            History.Add(new(DateTime.Now, current));
            while (History.Count > MaxHistorySize && History.Count > 0)
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


            progress.Report(relativeProgress
                , remainRealTime == null
                    ? $" {relativeInputPath}"
                    : $" {relativeInputPath}, {remainRealTime.Value.ToReadableString()}");
        });

        bool worked = await ffmpegArgs.ProcessAsynchronously(false);
        CancelCurrentFFMPeg = () => { };
        progress.Dispose();

        SkipBuffer.Add(InputPath);
        if (!worked)
        {
            Logger.Error($"Encoding failed for {InputPath}.");
            SkipBuffer.Add(InputPath);
            if (File.Exists(OutputPath))
                File.Delete(OutputPath);
            return true;
        }

        var newMediaInfo = await FFProbe.AnalyseAsync(OutputPath);
        if (ShouldEncode(newMediaInfo, true))
            Logger.Error($"Warning, file analysis for {OutputPath} mark this file as non-encoded.");

        if (fiInput.DirectoryName == null)
            throw new Exception($"Safe lock: empty DirectoryName for {fiInput}.");

        string newInputPath;
        string newInputFileName = Path.GetFileNameWithoutExtension(InputPath) + "." + OutputExtension;
        if (ForcedDestinationDirectoryPath != null)
            newInputPath = Path.Combine(ForcedDestinationDirectoryPath, InputDirRelative, newInputFileName);
        else
            newInputPath = Path.Combine(fiInput.DirectoryName, newInputFileName);

        FileInfo newInputFi = new(newInputPath);
        if (newInputFi.DirectoryName != null && !Directory.Exists(newInputFi.DirectoryName))
            Directory.CreateDirectory(newInputFi.DirectoryName);

        if (ShouldRemoveOldFile)
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
            Logger.Error($"Move failed, cannot file any file at {newInputPath}.");

        if (ShouldDeleteEmptyDirectories
        && ForcedDestinationDirectoryPath != null
        && fiInput.DirectoryName != null
        && fiInput.DirectoryName != WorkingDirectoryPath
        && !Directory.EnumerateFileSystemEntries(fiInput.DirectoryName).Any())
            Directory.Delete(fiInput.DirectoryName);

        Logger.Information(Flavor.Ok, "Fichier remplacé avec succès", Flavor.Normal, $": {InputPath}.");
        return true;
    }

    return false;
}

#endregion

#region Additional logic

void PrintLogo()
{
    Logger.Information(
        "#########################################");
    Logger.Information("##                                     ##");
    Logger.Information("##     =====      ", Flavor.Progress, "FFBOT", Flavor.Normal, "      =====     ##");
    Logger.Information("##                                     ##");
    Logger.Information("#########################################");
    Logger.Information();
}

void PrintConfiguration()
{
    Logger.Information($"Working directory: ", Flavor.Important, WorkingDirectoryPath);
    Logger.Information($"Forced destination path : ", Flavor.Important, ForcedDestinationDirectoryPath);

    Logger.Information($"Allowed inputs: ", Flavor.Important, string.Join(",", AllowedInputs));
    Logger.Information($"Output extension: ", Flavor.Important, OutputExtension);
    Logger.Information($"Targeted video codec: ", Flavor.Important, TargetedVideoCodec);

    Logger.Information($"Base custom input arguments: ", Flavor.Important, BaseCustomInputArguments);
    Logger.Information($"Base custom input arguments: ", Flavor.Important, BaseCustomOutputArguments);

    Logger.Information($"Should set aspect ratio to 1:1: ", ShouldSetRatioToOneOne);
    Logger.Information($"Should use smart audio encoding: ", SmartAudioEncoding);
    Logger.Information($"Should keep only one video stream: ", KeepOnlyOneVideoStream);
    Logger.Information($"Should remove old file: ", ShouldRemoveOldFile);
    Logger.Information($"Should send removed to bin: ", ShouldSendRemovedToBin);
    Logger.Information($"Should kill ffmpeg when exited: ", ShouldKillFFMpegWhenExited);
    Logger.Information($"Should delete empty directories: ", ShouldDeleteEmptyDirectories);
    Logger.Information($"Should delete temporary file at start: ", ShouldDeleteTemporaryFile);

    Logger.Information($"History max size: ", MaxHistorySize);
    Logger.Information($"Console buffer max size: ", MaxConsoleBufferSize);
    Logger.Information($"Console minimum verbosity: ", Flavor.Important, Logger.MinConsoleVerbosity.ToString());

    Logger.Information($"Idle time: ", WaitingTime);
    Logger.Information($"Start time: ", StartActivityBound);
    Logger.Information($"Stop time: ", StopActivityBound);

    Logger.Information();
}

void OnExited(object? sender, EventArgs e)
{
    if (ShouldKillFFMpegWhenExited)
        CancelCurrentFFMPeg();
}

bool ActivateIfNeeded()
{
    var now = DateTime.Now.TimeOfDay;
    if (StopActivityBound != null
        && StartActivityBound != null
        && (StartActivityBound < StopActivityBound && now >= StartActivityBound && now < StopActivityBound)
        || (StartActivityBound > StopActivityBound && now >= StartActivityBound || now < StopActivityBound)
)
    {
        Logger.Debug("Leaving sleeping mode.");
        Active = true;
        return true;
    }

    return false;
}

bool DisableIfNeeded()
{
    var now = DateTime.Now.TimeOfDay;
    if (Active
    && StopActivityBound != null
    && StartActivityBound != null
    && (StartActivityBound < StopActivityBound && now < StartActivityBound || now >= StopActivityBound)
    || (StartActivityBound > StopActivityBound && now >= StopActivityBound && now < StartActivityBound))
    {
        Logger.Debug("Entering in sleeping mode.");
        Active = false;
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
    if (KeepOnlyOneVideoStream && media.VideoStreams.Count > 1)
    {
        Logger.Debug($"Marked as non-encoded: found multiples video streams ({media.VideoStreams.Count}).");
        return true;
    }        

    // encode bad codec
    if (media.PrimaryVideoStream.CodecName != TargetedVideoCodec)
    {
        Logger.Debug($"Marked as non-encoded: bad video codec found ({media.PrimaryVideoStream.CodecName}).");
        return true;
    }

    if (ShouldSetRatioToOneOne && IsBadAspectRatio(media))
    {
        Logger.Debug($"Marked as non-encoded: bad SAR found ({media.PrimaryVideoStream.SampleAspectRatio.Width}):{media.PrimaryVideoStream.SampleAspectRatio.Height}.");
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