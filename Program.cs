using FFDaemon;
using System.Text;

System.Console.CursorVisible = false;
System.Console.OutputEncoding = Encoding.UTF8;

Orchestrator Orchestrator = new();
AppDomain.CurrentDomain.ProcessExit += (object? sender, EventArgs e) =>
{
    if (Orchestrator.Configuration.ShouldKillFFMpegWhenExited)
        Orchestrator.StopNow();
};

// Main program:
await Orchestrator.Boot();
