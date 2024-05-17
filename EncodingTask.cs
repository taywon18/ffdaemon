using FFDaemon;
using FFMpegCore;

public class EncodingTask
{
    public Orchestrator Parent { get; private set; }
    Configuration conf { get => Parent.Configuration; }

    string CustomInputsArgs = conf.BaseCustomInputArguments ?? "";
    string CustomOutputArgs = conf.BaseCustomOutputArguments ?? "";
    TimeSpan TotalDuration = default;


    async Task<bool> CandidateSelector(string candidate)
    {
        FileInfo fiInput = new FileInfo(candidate);
        if (!fiInput.Exists) // skip if file doesn't exist anymore
            return false;

        IMediaAnalysis? mediaInfo = null;
        try
        {
            mediaInfo = await FFProbe.AnalyseAsync(candidate);
        }
        catch (Exception e)
        {
            FFDaemon.IOManager.Error($"Cannot analyse {candidate}: {e}");
            return false;
        }
        TotalDuration = mediaInfo.Duration;

        //Skip already converted files
        if (!ShouldEncode(mediaInfo))
            return false;

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

        return true;
    }

    //return true if convert, false otherwise
    async Task<bool> Handle()
    {
        string randomOutput = Guid.NewGuid().ToString();
        string OutputPath = Path.Combine(conf.TemporaryDirectoryPath, randomOutput + "." + conf.OutputExtension);
        var rawCandidates = Directory.GetFiles(conf.WorkingDirectoryPath, "*", searchOption: SearchOption.AllDirectories);
        var candidates = rawCandidates
            .Where(file => conf.AllowedInputs.Any(file.ToLower().EndsWith))
            .OrderBy(x =>
            {
                FileInfo fi = new(x);
                return fi.LastWriteTime;
            })
            .ToList();

        string? InputPath = await Parent.PickPath(async (candidate) => await CandidateSelector(candidate));
        if (InputPath is null)
            return false;

        FFDaemon.IOManager.Debug($"Converting {InputPath}...");

        string ConsoleBuffer = "";

        Action? CancelCurrentFFMPeg = null;
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

    bool ShouldEncode(FFMpegCore.IMediaAnalysis? media, bool verbose = false)
    {
        // skip bad analysis
        if (media == null)
            return false;

        // skip non-video files
        if (media.PrimaryVideoStream == null)
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
}
