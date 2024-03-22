// about bulk import: https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/tutorial-dotnet-bulk-import

using System.Reflection;
using Azure.Core.Extensions;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using DeferralDemo;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


string? directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

await Host.CreateDefaultBuilder(args)
.ConfigureAppConfiguration((env, config) =>
{
    var appAssembly = Assembly.Load(new AssemblyName(env.HostingEnvironment.ApplicationName));
    if (appAssembly != null)
    {
        config.AddUserSecrets(appAssembly, optional: true);
    }

    config.AddJsonFile("appsettings.json");
    config.AddJsonFile("appsettings.Development.json", optional: true);
    config.AddEnvironmentVariables();
})
.UseContentRoot(directoryName != null ? directoryName : string.Empty)
.ConfigureLogging(logging =>
{
})
.ConfigureServices((hostContext, services) =>
{
    services.AddAzureClients(clientBuilder =>
    {
        ServiceBusOptions? sbOptions = hostContext.Configuration.GetSection(ServiceBusOptions.ServiceBus).Get<ServiceBusOptions>();

        if (sbOptions != null)
        {
            services.AddSingleton(sbOptions);

            if (string.IsNullOrEmpty(sbOptions.ConnectionString))
                throw new InvalidOperationException(@$"The Azure Service Bus connection string is not configured; the configuration setting is either not specified or does not have a value.");

            IAzureClientBuilder<ServiceBusClient, ServiceBusClientOptions> acb;
            if (sbOptions.ConnectionString.Contains("SharedAccessKey=", StringComparison.OrdinalIgnoreCase))
                acb = clientBuilder.AddServiceBusClient(sbOptions.ConnectionString); // Connect to Azure Service Bus with secret.
            else
                acb = clientBuilder.AddServiceBusClientWithNamespace(sbOptions.ConnectionString).WithCredential(new DefaultAzureCredential()); // Connect to Azure Service Bus with managed identity.
        }
        else
        {
            throw new ArgumentNullException($"${ServiceBusOptions.ServiceBus} in appsettings.json cannot be null.");
        }
    });


    services.AddHostedService<ConsoleHostedService>();
})
.RunConsoleAsync();