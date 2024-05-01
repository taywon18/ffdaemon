using FFMpegCore;
using FFMpegCore.Enums;
using System.IO;

bool Verbose = true;

Console.CursorVisible = false;

if(Verbose)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("#########################################");
    Console.WriteLine("##                                     ##");
    Console.WriteLine("##    =====    FFCONVERT    =====      ##");
    Console.WriteLine("##                                     ##");
    Console.WriteLine("#########################################");
    Console.ResetColor();
    Console.Write(Environment.NewLine);
}

HashSet<string> SkipBuffer = new();

string WorkingDirectoryPath = Environment.CurrentDirectory;
string TempDirectoryPath = Path.GetTempPath();
if(Verbose)
{
    Console.WriteLine($"Working directory: {WorkingDirectoryPath}");
    Console.WriteLine($"Temp directory: {TempDirectoryPath}");
}


string[] AllowedInputs = new[] { ".mkv", ".avi", ".vp9", ".ts" };
string OutputExtension = "mkv";
string TargetedVideoCodec = "vp9";
string? BaseCustomInputArguments = "-y -probesize 1000000000 -analyzeduration 100000000";
string? BaseCustomOutputArguments = "";
bool ShouldSetRatioToOneOne = true;
bool SmartAudioEncoding = true;
bool KeepOnlyOneVideoStream = true;
if(Verbose)
{
    Console.WriteLine($"Should set aspect ratio to 1:1: {ShouldSetRatioToOneOne}");
    Console.WriteLine($"Allowed inputs: {string.Join(",", AllowedInputs)}");
    Console.WriteLine($"Base custom input arguments: {BaseCustomInputArguments}");
    Console.WriteLine($"Output extension: {OutputExtension}");
    Console.WriteLine($"Base custom input arguments: {BaseCustomOutputArguments}");
    Console.Write(Environment.NewLine);
}

TimeSpan WaitingTime = TimeSpan.FromSeconds(60);
while (true)
{
    if (!await Handle())
        await Task.Delay((int)WaitingTime.TotalMilliseconds);
}

//return true if convert, false otherwise
async Task<bool> Handle()
{
    var candidates = Directory.GetFiles(WorkingDirectoryPath, "*", searchOption: SearchOption.AllDirectories)
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

        var mediaInfo = await FFProbe.AnalyseAsync(InputPath);
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

                if (hasTextSubtitle)
                    CustomInputsArgs += $" -txt_format text -fix_sub_duration";
            }
        }

        Console.WriteLine($"Converting {InputPath}...");

        string OutputPath = Path.Combine(WorkingDirectoryPath, "temporary" + "." + OutputExtension);
        if (File.Exists(OutputPath))
            throw new Exception($"Safe lock: Please stop other encoding or remove file {OutputPath}.+");

        var ffmpegArgs = FFMpegArguments
            .FromFileInput(InputPath, true, options => options
                .WithCustomArgument(CustomInputsArgs)
            )
            .OutputToFile(OutputPath, false, options => options
                .WithCustomArgument(CustomOutputArgs)
            )
            /*.NotifyOnError((string message) =>
            {
                Console.Write($"{message}");
                Console.CursorLeft = 0;
            })
            .NotifyOnOutput((string message) =>
            {
                Console.WriteLine($"Message: {message}");
            })*/
            .NotifyOnProgress((double x) =>
            {
                Console.Write((x).ToString("0.0") + "%");
                Console.CursorLeft = 0;
            }, totalTime);
            
        Console.WriteLine($"Executed command: {ffmpegArgs.Arguments}");

        bool worked = await ffmpegArgs.ProcessAsynchronously(false);
        SkipBuffer.Add(InputPath);
        if (!worked)
        {
            Console.WriteLine($"Encoding failed for {InputPath}.");
            SkipBuffer.Add(InputPath);
            return true;
        }
            

        if (fiInput.DirectoryName == null)
            throw new Exception($"Safe lock: empty DirectoryName for {fiInput}.");

        string newInputPath = Path.Combine(fiInput.DirectoryName, Path.GetFileNameWithoutExtension(InputPath) + "." + OutputExtension);

        File.Delete(InputPath);
        File.Move(OutputPath, newInputPath);

        Console.WriteLine($"Fichier remplacé avec succès: {InputPath}.");
        return true;
    }

    return false;
}

bool ShouldEncode(FFMpegCore.IMediaAnalysis? media)
{
    // skip bad analysis
    if (media == null)
        return false;

    // skip non-video files
    if(media.PrimaryVideoStream == null)
        return false;

    // encode if more than 1 video stream
    if (KeepOnlyOneVideoStream && media.VideoStreams.Count > 1)
        return true;

    // encode bad codec
    if (media.PrimaryVideoStream.CodecName != TargetedVideoCodec)
        return true;

    if (ShouldSetRatioToOneOne && IsBadAspectRatio(media))
        return true;

    return false;
}

bool IsBadAspectRatio(FFMpegCore.IMediaAnalysis media)
{
    if (media.PrimaryVideoStream == null)
        throw new Exception("Cannot handle non-video steam.");

    if (media.PrimaryVideoStream.SampleAspectRatio.Width != 1
    || media.PrimaryVideoStream.SampleAspectRatio.Height != 1)
        return true;

    return false;
}