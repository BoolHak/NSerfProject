namespace NSerf.BackendService.Services;

public class NetworkTrafficMonitor
{
    public event Action<string, string, int>? OnTrafficDetected;

    public void Report(string type, string target, int bytes)
    {
        OnTrafficDetected?.Invoke(type, target, bytes);
    }
}
