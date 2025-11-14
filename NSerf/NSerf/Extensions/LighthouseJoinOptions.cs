namespace NSerf.Extensions;

public class LighthouseJoinOptions
{
    public required string BaseUrl { get; set; }
    public required string ClusterId { get; set; }
    public required string ClusterVersionName { get; set; }
    public required long ClusterVersionNumber { get; set; }
    public required string PrivateKey { get; set; }
    public required string AesKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    public bool UseForStartJoin { get; set; } = true;
    public bool UseForRetryJoin { get; set; } = true;
}