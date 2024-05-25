namespace FFDaemon;

public class Activity
{
    public Orchestrator Parent { get; }
    public Configuration Configuration => Parent.Configuration;
    object Mutex { get; } = new object();

    bool isActive { get; set; } = true;
    bool? forcedActivity { get; set; } = null;

    public Activity(Orchestrator parent)
    {
        Parent = parent;
    }

    public void SetActive(bool active)
    {
        lock(Mutex)
            isActive = active;
    }

    public void SetForcedState(bool? forced)
    {
        lock (Mutex)
            forcedActivity = forced;
    }

    public bool ActivateIfNeeded()
    {
        var now = DateTime.Now.TimeOfDay;
        lock (Mutex)
            if (!isActive
                && Configuration.StopActivityBound != null
                && Configuration.StartActivityBound != null
                && ((Configuration.StartActivityBound < Configuration.StopActivityBound && now >= Configuration.StartActivityBound && now < Configuration.StopActivityBound)
                || (Configuration.StartActivityBound > Configuration.StopActivityBound &&
                (
                    now >= Configuration.StartActivityBound || now < Configuration.StopActivityBound
                ))))
            {
                FFDaemon.IOManager.Information("Leaving ", Flavor.Important, "🔋 sleeping mode", Flavor.Normal, ".");
                isActive = true;
                if (Configuration.ExecuteAfterStart is not null)
                    System.Diagnostics.Process.Start(Configuration.ExecuteAfterStart);
                return true;
            }

        return false;
    }

    public bool DisableIfNeeded()
    {
        var now = DateTime.Now.TimeOfDay;
        lock (Mutex)
            if (isActive
            && Configuration.StopActivityBound != null
            && Configuration.StartActivityBound != null
            && (
                (Configuration.StartActivityBound < Configuration.StopActivityBound && (now < Configuration.StartActivityBound || now >= Configuration.StopActivityBound)
            )
            || (Configuration.StartActivityBound > Configuration.StopActivityBound && now >= Configuration.StopActivityBound && now < Configuration.StartActivityBound)))
            {
                FFDaemon.IOManager.Information("Entering in ", Flavor.Important, "💤 sleeping mode", Flavor.Normal, ".");
                isActive = false;
                if (Configuration.ExecuteAfterStop is not null)
                    System.Diagnostics.Process.Start(Configuration.ExecuteAfterStop);
                return true;
            }

        return false;
    }
    public bool ShouldWork()
    {
        lock(Mutex)
        {
            return (forcedActivity is not null) ? forcedActivity.Value : isActive;
        }
    }

    public Task Wait()
    {
        return Task.Delay(Configuration.WaitingTime);
    }
}
