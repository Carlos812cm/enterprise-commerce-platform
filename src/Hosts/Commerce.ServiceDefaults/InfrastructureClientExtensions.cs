using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Commerce.ServiceDefaults;

public static class InfrastructureClientExtensions
{
    private const string PostgresConnectionName = "Postgres";
    private const string RedisConnectionName = "Redis";
    private const string RabbitMqConnectionName = "RabbitMq";

    public static IHostApplicationBuilder AddInfrastructureClients(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var clientName = $"{serviceName}@{Environment.MachineName}";

        builder.AddNpgsqlDataSource(PostgresConnectionName);

        builder
            .AddRedisClientBuilder(
                RedisConnectionName,
                configureOptions: options =>
                {
                    options.AbortOnConnectFail = false;
                    options.ConnectRetry = 3;
                    options.ConnectTimeout = 5_000;
                    options.ClientName = clientName;
                })
            .WithDistributedCache(options =>
            {
                options.InstanceName = "commerce:";
            });

        builder.Services.AddHybridCache(options =>
        {
            options.MaximumKeyLength = 512;
            options.MaximumPayloadBytes =
                2 * 1024 * 1024;

            options.ReportTagMetrics = false;
        });

        builder.AddRabbitMQClient(
            RabbitMqConnectionName,
            configureConnectionFactory: factory =>
            {
                factory.ClientProvidedName = clientName;
                factory.AutomaticRecoveryEnabled = true;
                factory.TopologyRecoveryEnabled = true;
                factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(5);
                factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(5);
                factory.RequestedHeartbeat = TimeSpan.FromSeconds(30);
            });

        return builder;
    }
}
