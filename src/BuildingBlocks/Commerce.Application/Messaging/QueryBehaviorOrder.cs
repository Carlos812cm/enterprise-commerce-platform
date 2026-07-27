namespace Commerce.Application.Messaging;

public static class QueryBehaviorOrder
{
    public const int Telemetry = -2_000;
    public const int Logging = -1_000;
    public const int Validation = 0;
    public const int Authorization = 100;
    public const int Cache = 500;
}
