using FFDaemon;

namespace FFDeamon;

public class Interactivity
{
    public Orchestrator Parent { get; }
    public Configuration Configuration => Parent.Configuration;

    public Interactivity(Orchestrator parent)
    {
        Parent = parent;
    }

    public void PrintInteractivity()
    {
        FFDaemon.IOManager.Information("Force awake: ", Flavor.Important, "F1");
        FFDaemon.IOManager.Information("Force sleep: ", Flavor.Important, "F2");
        FFDaemon.IOManager.Information("Restore state: ", Flavor.Important, "F3");

        FFDaemon.IOManager.Information("Schedule stop: ", Flavor.Important, "F10");
        FFDaemon.IOManager.Information("Unschedule stop: ", Flavor.Important, "F11");
        FFDaemon.IOManager.Information("Exit now: ", Flavor.Important, "F12");

        IOManager.Information();
    }

    public void SetupInteractivity()
    {
        if (!Configuration.Interactive)
            return;

        IOManager.Callbacks.OnForceAwake = () =>
        {
            IOManager.Information("Forcing ", Flavor.Important, "awakening.");
            Parent.Activity.SetForcedState(true);
        };

        IOManager.Callbacks.OnForceSleep = () =>
        {
            IOManager.Information("Forcing ", Flavor.Important, "sleep.");
            Parent.Activity.SetForcedState(false);
        };

        IOManager.Callbacks.OnUnforceState = () =>
        {
            IOManager.Information("Remove any forced state.");
            Parent.Activity.SetForcedState(null);
        };

        /*IOManager.Callbacks.OnScheduleStop = () =>
        {
            IOManager.Information("Scheduling exit at next idle time.");
            lock (Mutex)
                ScheduleStop = true;
            return;
        };

        IOManager.Callbacks.OnUnscheduleStop = () =>
        {
            IOManager.Information("Unscheduling exit.");
            lock (Mutex)
                ScheduleStop = false;
            return;
        };*/

        IOManager.Callbacks.OnQuit = () =>
        {
            IOManager.Information("Exiting now...");
            Environment.Exit(0);
            return;
        };

        IOManager.StartInteractivity();
        PrintInteractivity();
    }
}
