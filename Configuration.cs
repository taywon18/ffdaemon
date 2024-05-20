using CommandLine;
using FFMpegCore;
using System.Reflection;
using System.Text.Json;

namespace FFDaemon;

public class Configuration
{
    [Option('c', "config", Required = false, HelpText = "Set configuration file path")]
    public string? Config { get; set; }
    [Option('i', "interactive", Required = false, HelpText = "Set interactive mode")]
    public bool Interactive { get; set; } = true;

    [Option('d', "directory", Required = false, HelpText = "Force working directory")]
    public string WorkingDirectoryPath { get; set; } = Environment.CurrentDirectory;
    [Option('o', "output", Required = false, HelpText = "Force output directory")]
    public string? ForcedDestinationDirectoryPath { get; set; } = "../Encoded";
    [Option('t', "temporary", Required = false, HelpText = "Force temporary directory")]
    public string? TemporaryDirectoryPath { get; set; } = ".ffdeamon";


    public string[] AllowedInputs { get; set; } = new[] { ".mkv", ".avi", ".vp9", ".ts", ".mp4", ".webm" };
    [Option('x', "output-extension", Required = false, HelpText = "Set output extension")]
    public string OutputExtension { get; set; } = "mkv";
    [Option('v', "video-codec", Required = false, HelpText = "Set video codec")]
    public string TargetedVideoCodec { get; set; } = "vp9";

    [Option("input-args", Required = false, HelpText = "Set ffmpeg input base arguments")]
    public string? BaseCustomInputArguments { get; set; } = "-y -probesize 1000000000 -analyzeduration 100000000";
    [Option("output-args", Required = false, HelpText = "Set ffmpeg output base arguments")] 
    public string? BaseCustomOutputArguments { get; set; } = "";

    [Option("force-11-ar", Required = false, HelpText = "Force 1:1 aspect ratio")]
    public bool ShouldSetRatioToOneOne { get; set; } = true;
    [Option("smart-audio", Required = false, HelpText = "Set smart audio encoding")]
    public bool SmartAudioEncoding { get; set; } = true;
    [Option("keep-one-video", Required = false, HelpText = "Set if keep only one video stream")]
    public bool KeepOnlyOneVideoStream { get; set; } = true;
    [Option("remove-old-file", Required = false, HelpText = "Set if remove old files")]
    public bool ShouldRemoveOldFile { get; set; } = true;
    // for later use
    public bool ShouldSendRemovedToBin { get; set; } = false;
    [Option("kill-ffmpeg", Required = false, HelpText = "Kill ffmpeg when exited")]
    public bool ShouldKillFFMpegWhenExited { get; set; } = true;
    [Option("delete-empty-directories", Required = false, HelpText = "Delete empty directories")]
    public bool ShouldDeleteEmptyDirectories { get; set; } = true;
    [Option("delete-temporary-file", Required = false, HelpText = "Delete temporary file")]
    public bool ShouldDeleteTemporaryFile { get; set; } = true;

    [Option('h', "max-history-size", Required = false, HelpText = "Set max history size")]
    public int MaxHistorySize { get; set; } = 100;
    [Option('b', "max-ffmpeg-buffer", Required = false, HelpText = "Set max ffmpeg output buffer size")]
    public int MaxConsoleBufferSize { get; set; } = 25000;

    [Option('w', "wait-time", Required = false, HelpText = "Set time to wait between scans")]
    public TimeSpan WaitingTime { get; set; } = TimeSpan.FromSeconds(60);
    [Option("start-at", Required = false, HelpText = "Set time to start (= exit sleeping mode)")]
    public TimeSpan? StartActivityBound { get; set; } = null;
    [Option("stop-at", Required = false, HelpText = "Set time to stop (= enter in sleeping mode)")]
    public TimeSpan? StopActivityBound { get; set; } = null;
    [Option("after-start", Required = false, HelpText = "Execute this command after each start")]
    public string? ExecuteAfterStart { get; set; } = null;
    [Option("after-stop", Required = false, HelpText = "Execute this command after each start")]
    public string? ExecuteAfterStop { get; set; } = null;

    [Option('n', "instance-number", Required = false, HelpText = "Set the number of concurrent ffmpeg processes")]
    public int InstanceCount { get; set; } = 1;


    public void LoadFromConfigFile()
    {
        if(String.IsNullOrEmpty(Config))
        {
            IOManager.Debug("Not any configuration file path set.");
            return;
        }

        if(!File.Exists(Config))
        {
            IOManager.Error("Cannot find configuration file at path ", Flavor.Important, Config);
            return;
        }

        try
        {
            string content = File.ReadAllText(Config);
            var config = JsonSerializer.Deserialize<Configuration>(content);

            if (config == null)
            {
                IOManager.Error("Cannot load configuration from file at ", Flavor.Important, Config);
                return;
            }

            ImportFrom(config);
        }
        catch(Exception ex)
        {
            IOManager.Error("Cannot load configuration from file at ", Flavor.Important, Config, Flavor.Normal, ", exception thrown: ", ex);
            return;
        }

    }

    public void LoadFromCommandLine()
    {
        var res = Parser.Default.ParseArguments<Configuration>(Environment.GetCommandLineArgs());
        if(res == null)
        {
            IOManager.Error("Argument parsing failed.");
            return;
        }
        ImportFrom(res.Value);
    }

    private void ImportFrom(Configuration other)
    {
        foreach (var p in GetType().GetProperties())
        {
            if (p.GetCustomAttribute<CommandLine.OptionAttribute>() == null)
                continue;

            var v = p.GetValue(other);
            p.SetValue(this, v);
        }
    }

    public void Print()
    {
#pragma warning disable CS8604 // Existence possible d'un argument de référence null.
        FFDaemon.IOManager.Information($"Working directory: ", Flavor.Important, WorkingDirectoryPath);
        FFDaemon.IOManager.Information($"Forced destination path : ", Flavor.Important, ForcedDestinationDirectoryPath);

        FFDaemon.IOManager.Information($"Allowed inputs: ", Flavor.Important, string.Join(",", AllowedInputs));
        FFDaemon.IOManager.Information($"Output extension: ", Flavor.Important, OutputExtension);
        FFDaemon.IOManager.Information($"Targeted video codec: ", Flavor.Important, TargetedVideoCodec);

        FFDaemon.IOManager.Information($"Base custom input arguments: ", Flavor.Important, BaseCustomInputArguments);
        FFDaemon.IOManager.Information($"Base custom input arguments: ", Flavor.Important, BaseCustomOutputArguments);

        FFDaemon.IOManager.Information($"Should set aspect ratio to 1:1: ", ShouldSetRatioToOneOne);
        FFDaemon.IOManager.Information($"Should use smart audio encoding: ", SmartAudioEncoding);
        FFDaemon.IOManager.Information($"Should keep only one video stream: ", KeepOnlyOneVideoStream);
        FFDaemon.IOManager.Information($"Should remove old file: ", ShouldRemoveOldFile);
        FFDaemon.IOManager.Information($"Should send removed to bin: ", ShouldSendRemovedToBin);
        FFDaemon.IOManager.Information($"Should kill ffmpeg when exited: ", ShouldKillFFMpegWhenExited);
        FFDaemon.IOManager.Information($"Should delete empty directories: ", ShouldDeleteEmptyDirectories);
        FFDaemon.IOManager.Information($"Should delete temporary file at start: ", ShouldDeleteTemporaryFile);

        FFDaemon.IOManager.Information($"History max size: ", MaxHistorySize);
        FFDaemon.IOManager.Information($"Console buffer max size: ", MaxConsoleBufferSize);
        FFDaemon.IOManager.Information($"Console minimum verbosity: ", Flavor.Important, FFDaemon.IOManager.MinConsoleVerbosity.ToString());

        FFDaemon.IOManager.Information($"Max concurrent FFMPEG instance count: ", InstanceCount);

        FFDaemon.IOManager.Information($"Idle time: ", WaitingTime);
        FFDaemon.IOManager.Information($"Start time: ", StartActivityBound);
        FFDaemon.IOManager.Information($"Stop time: ", StopActivityBound);

        FFDaemon.IOManager.Information();
#pragma warning restore CS8604 // Existence possible d'un argument de référence null.
    }
}
