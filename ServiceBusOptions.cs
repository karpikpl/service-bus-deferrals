namespace DeferralDemo;

public class ServiceBusOptions
{
    public const string ServiceBus = "ServiceBus";
    public string ConnectionString { get; set; } = string.Empty;
    public string FilesQueueName { get; set; } = string.Empty;
}
