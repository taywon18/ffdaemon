using FFMpegCore;

bool Verbose = true;

Console.CursorVisible = false;

if(Verbose)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("#########################################");
    Console.WriteLine("##                                     ##");
    Console.WriteLine("##     =====      FFBOT      =====     ##");
    Console.WriteLine("##                                     ##");
    Console.WriteLine("#########################################");
    Console.ResetColor();
    Console.Write(Environment.NewLine);
}

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
bool ShouldSendRemovedToBin = true;
bool ShouldKillFFMpegWhenExited = true;
bool ShouldDeleteEmptyDirectories = true;
bool ShouldDeleteTemporaryFile = true;
int MaxHistorySize = 100;

TimeSpan WaitingTime = TimeSpan.FromSeconds(60);

if (Verbose)
{
    Console.WriteLine($"Working directory: {WorkingDirectoryPath}");
    if(ForcedDestinationDirectoryPath != null)
        Console.WriteLine($"Forced a destination path : {ForcedDestinationDirectoryPath}");

    Console.WriteLine($"Allowed inputs: {string.Join(",", AllowedInputs)}");
    Console.WriteLine($"Output extension: {OutputExtension}");
    Console.WriteLine($"Targeted video codec: {TargetedVideoCodec}");

    Console.WriteLine($"Base custom input arguments: {BaseCustomInputArguments}");
    Console.WriteLine($"Base custom input arguments: {BaseCustomOutputArguments}");
    Console.WriteLine($"Should set aspect ratio to 1:1: {ShouldSetRatioToOneOne}");
    Console.WriteLine($"Should use smart audio encoding: {SmartAudioEncoding}");
    Console.WriteLine($"Should keep only one video stream: {KeepOnlyOneVideoStream}");

    Console.WriteLine($"Idle time: {WaitingTime}");

    Console.Write(Environment.NewLine);
}

if(ForcedDestinationDirectoryPath != null && !Directory.Exists(ForcedDestinationDirectoryPath))
    Directory.CreateDirectory(ForcedDestinationDirectoryPath);

Action CancelCurrentFFMPeg = () => { };
AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnExited);

if (!await Handle())
{
    Console.WriteLine($"Ready to encode future files put in \"{WorkingDirectoryPath}\"");
    await Task.Delay( WaitingTime );
}
while (true)
{
    if (!await Handle())
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
        if(!fiInput.Exists) 
        {
            SkipBuffer.Add(InputPath);
            continue;
        }

        IMediaAnalysis? mediaInfo = null;
        try
        {
            mediaInfo = await FFProbe.AnalyseAsync(InputPath);
        }
        catch(Exception e)
        {
            SkipBuffer.Add(InputPath);
            Console.WriteLine($"Cannot analyse {InputPath}: {e}");
            continue;
        }
        var totalTime = mediaInfo.Duration;

        //Skip already converted files
        if(!ShouldEncode(mediaInfo))
        {
            SkipBuffer.Add(InputPath);
            continue;
        }

        if (!KeepOnlyOneVideoStream)
            throw new NotImplementedException();
        else
            CustomOutputArgs += " -map 0:v:0 -c:v libvpx-vp9";
        
        if(ShouldSetRatioToOneOne && IsBadAspectRatio(mediaInfo))
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
            for(int i = 0; i<mediaInfo.AudioStreams.Count; i++)
            {
                var audioStream = mediaInfo.AudioStreams[i];
                if(audioStream.ChannelLayout.ToLower().EndsWith("(side)"))
                    CustomOutputArgs += $" -c:a:{i} libvorbis";
                else
                    CustomOutputArgs += $" -c:a:{i} libopus";
            }

        if(mediaInfo.SubtitleStreams.Count > 0)
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

        if(Verbose)
            Console.WriteLine($"Converting {InputPath}...");

        string OutputPath = Path.Combine(WorkingDirectoryPath, "temporary" + "." + OutputExtension);
        if (File.Exists(OutputPath))
        {
            if(!ShouldDeleteTemporaryFile)
                throw new Exception($"Safe lock: Please stop other encoding or remove file {OutputPath}.+");

            File.Delete(OutputPath);
        }

        var ffmpegArgs = FFMpegArguments
            .FromFileInput(InputPath, true, options => options
                .WithCustomArgument(CustomInputsArgs)
            )
            .OutputToFile(OutputPath, false, options => options
                .WithCustomArgument(CustomOutputArgs)
            )
            .CancellableThrough(out CancelCurrentFFMPeg);
            /*.NotifyOnError((string message) =>
            {
                Console.Write($"{message}");
                Console.CursorLeft = 0;
            })
            .NotifyOnOutput((string message) =>
            {
                Console.WriteLine($"Message: {message}");
            })*/
            

        if (Verbose)
            Console.WriteLine($"Executed command: {ffmpegArgs.Arguments}");

        string relativeInputPath = Path.GetRelativePath(WorkingDirectoryPath, InputPath);
        string? InputDirRelative = null;
        if(fiInput.DirectoryName != null)
            InputDirRelative = Path.GetRelativePath(WorkingDirectoryPath, fiInput.DirectoryName);

        var progress = new ProgressBar(suffix: $" {relativeInputPath}");

        List<KeyValuePair<DateTime, TimeSpan>> History = new();
        ffmpegArgs = ffmpegArgs.NotifyOnProgress((TimeSpan current) =>
        {
            History.Add(new(DateTime.Now, current));
            while (History.Count > MaxHistorySize && History.Count > 0)
                History.RemoveAt(0);
            TimeSpan? remainRealTime = null;
            if(History.Count > 5)
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
                    : $" {relativeInputPath}, {remainRealTime}");
        });

        bool worked = await ffmpegArgs.ProcessAsynchronously(false);
        CancelCurrentFFMPeg = () => { };
        progress.Dispose();

        SkipBuffer.Add(InputPath);
        if (!worked)
        {
            Console.WriteLine($"Encoding failed for {InputPath}.");
            SkipBuffer.Add(InputPath);
            return true;
        }

        var newMediaInfo = await FFProbe.AnalyseAsync(OutputPath);
        if (ShouldEncode(newMediaInfo, true))
            Console.WriteLine($"Attention, l'analyse du fichier {OutputPath} indique que le fichier reste à encoder.");

        if (fiInput.DirectoryName == null)
            throw new Exception($"Safe lock: empty DirectoryName for {fiInput}.");

        string newInputPath;
        string newInputFileName = Path.GetFileNameWithoutExtension(InputPath) + "." + OutputExtension;
        if (ForcedDestinationDirectoryPath != null)
            newInputPath = Path.Combine(ForcedDestinationDirectoryPath, InputDirRelative, newInputFileName);
        else
            newInputPath = Path.Combine(fiInput.DirectoryName, newInputFileName);

        FileInfo newInputFi = new(newInputPath);
        if(newInputFi.DirectoryName != null && !Directory.Exists(newInputFi.DirectoryName))
            Directory.CreateDirectory(newInputFi.DirectoryName);

        if(ShouldRemoveOldFile)
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
            Console.WriteLine($"Echec, aucun fichier à l'emplacement {newInputPath}.");

        if (ShouldDeleteEmptyDirectories
        && ForcedDestinationDirectoryPath != null
        && fiInput.DirectoryName != null
        && !Directory.EnumerateFileSystemEntries(fiInput.DirectoryName).Any())
            Directory.Delete(fiInput.DirectoryName);

        Console.WriteLine($"Fichier remplacé avec succès: {InputPath}.");
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
        if (verbose) Console.WriteLine($"Le fichier a {media.VideoStreams.Count} flux videos.");
        return true;
    }        

    // encode bad codec
    if (media.PrimaryVideoStream.CodecName != TargetedVideoCodec)
    {
        if (verbose) Console.WriteLine($"Le codec video du fichier est {media.PrimaryVideoStream.CodecName}.");
        return true;
    }

    if (ShouldSetRatioToOneOne && IsBadAspectRatio(media))
    {
        if (verbose) Console.WriteLine($"Le fichier a un SampleAspectRatio de {media.PrimaryVideoStream.SampleAspectRatio.Width}:{media.PrimaryVideoStream.SampleAspectRatio.Height}.");
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

void OnExited(object? sender, EventArgs e)
{
    if (ShouldKillFFMpegWhenExited)
        CancelCurrentFFMPeg();
}