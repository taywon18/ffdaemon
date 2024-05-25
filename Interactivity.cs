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
        FFDaemon.IOManager.Information("Force awake: ", Flavor.Important, Configuration.Keys.ForceAwakeKey);
        FFDaemon.IOManager.Information("Force sleep: ", Flavor.Important, Configuration.Keys.ForceSleepKey);
        FFDaemon.IOManager.Information("Restore state: ", Flavor.Important, Configuration.Keys.UnforceStateKey);

        FFDaemon.IOManager.Information("Schedule stop: ", Flavor.Important, Configuration.Keys.ScheduleStopKey);
        FFDaemon.IOManager.Information("Unschedule stop: ", Flavor.Important, Configuration.Keys.UnscheduleStopKey);
        FFDaemon.IOManager.Information("Exit now: ", Flavor.Important, Configuration.Keys.QuitNowKey);

        FFDaemon.IOManager.Information("Increment FFMpeg instance count: ", Flavor.Important, Configuration.Keys.IncrementFfmpegCount);
        FFDaemon.IOManager.Information("Decrement FFMpeg instance count: ", Flavor.Important, Configuration.Keys.DecrementFfmpegCount);

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

        IOManager.Callbacks.OnScheduleStop = () =>
        {
            IOManager.Information("Scheduling exit at next idle time.");
            Parent.ScheduleStop(true);
        };

        IOManager.Callbacks.OnUnscheduleStop = () =>
        {
            IOManager.Information("Unscheduling exit.");
            Parent.ScheduleStop(false);
        };

        IOManager.Callbacks.OnQuit = () =>
        {
            IOManager.Information("Exiting now...");
            Environment.Exit(0);
        };

        IOManager.Callbacks.OnIncrementInstances = () =>
        {
            Parent.TargetTaskCount++;
            IOManager.Information("Incrementing instance count to ", Flavor.Important, Parent.TargetTaskCount);
        };

        IOManager.Callbacks.OnDecrementInstances = () =>
        {
            if(Configuration.InstanceCount <= 1)
            {
                IOManager.Information("Cannot decrement instance count less that 1");

            }
            else
            {
                Parent.TargetTaskCount--;
                IOManager.Information("Decrementing instance count to ", Flavor.Important, Parent.TargetTaskCount);
            }
        };

        IOManager.StartInteractivity(Parent);
        PrintInteractivity();
    }
}
