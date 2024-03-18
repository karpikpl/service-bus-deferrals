using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

class Program
{
    const string connectionString = "<your_connection_string>";
    const string queueName = "<your_queue_name>";

    static async Task Main(string[] args)
    {
        await using var client = new ServiceBusClient(connectionString);

        // create a processor that we can use to process the messages
        ServiceBusProcessor processor = client.CreateProcessor(queueName, new ServiceBusProcessorOptions());

        // add handler to process messages
        processor.ProcessMessageAsync += MessageHandler;

        // add handler to process any errors
        processor.ProcessErrorAsync += ErrorHandler;

        // start processing 
        await processor.StartProcessingAsync();

        Console.WriteLine("Press any key to stop the processing");
        Console.ReadKey();

        // stop processing 
        await processor.StopProcessingAsync();
    }

    static Task MessageHandler(ProcessMessageEventArgs args)
    {
        string body = args.Message.Body.ToString();
        Console.WriteLine($"Received: {body}");

        return Task.CompletedTask;
    }

    static Task ErrorHandler(ProcessErrorEventArgs args)
    {
        Console.WriteLine(args.Exception.ToString());
        return Task.CompletedTask;
    }
}
