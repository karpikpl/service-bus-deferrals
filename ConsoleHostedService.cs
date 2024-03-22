using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeferralDemo;

internal sealed class ConsoleHostedService : IHostedService
{
    const int EXPECTED_PROCESSING_TIME_SEC = 3 * 60;
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private int? _exitCode;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusProcessor _processor;
    private readonly ServiceBusReceiver _receiver;

    private readonly BlockingCollection<(long id, string data)> _files = [];
    private readonly Timer _deferralTimer;

    public ConsoleHostedService(
        ILogger<ConsoleHostedService> logger,
        IHostApplicationLifetime appLifetime,
        ServiceBusClient serviceBusClient,
        ServiceBusOptions serviceBusOptions)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _serviceBusClient = serviceBusClient;

        // create a processor that we can use to process the messages
        _processor = _serviceBusClient.CreateProcessor(serviceBusOptions.FilesQueueName, new ServiceBusProcessorOptions());
        _receiver = _serviceBusClient.CreateReceiver(serviceBusOptions.FilesQueueName);

        // add handler to process messages
        _processor.ProcessMessageAsync += MessageHandler;

        // add handler to process any errors
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Error processing message: {MessageId}", args.Exception.Message);
            return Task.CompletedTask;
        };

        // Start 3 worker tasks
        for (int i = 0; i < 3; i++)
        {
            Task.Run(() => ProcessFiles());
        }

        _deferralTimer = new Timer(_ => Task.Run(() => CheckDeferred()), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private async Task CheckDeferred()
    {
        try
        {
            var messages = await _receiver.PeekMessagesAsync(10);

            foreach (var message in messages)
            {
                if (message.State == ServiceBusMessageState.Deferred)
                {
                    Console.WriteLine($"Message {message.SequenceNumber} is deferred.");

                    if (message.ApplicationProperties.TryGetValue("expectedCompletion", out var expectedCompletion))

                    {
                        if (DateTime.UtcNow > (DateTime)expectedCompletion)
                        {
                            Console.WriteLine($"Message {message.SequenceNumber} has exceeded expected completion time. Dead lettering the message.");
                            var msg = await _receiver.ReceiveDeferredMessageAsync(message.SequenceNumber);
                            await _receiver.DeadLetterMessageAsync(msg, "Exceeded expected completion time.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Message {message.SequenceNumber} is missing expected completion time. Dead lettering the message.");
                        var msg = await _receiver.ReceiveDeferredMessageAsync(message.SequenceNumber);
                        await _receiver.DeadLetterMessageAsync(msg, "Missing expected completion time.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking deferred messages: {ex.Message}");
        }
    }

    private async Task ProcessFiles()
    {

        foreach (var file in _files.GetConsumingEnumerable())
        {
            string body = file.data;
            Console.WriteLine($"Received: {body}. Starting processing, this will take {EXPECTED_PROCESSING_TIME_SEC} seconds.");

            // Simulate processing time
            int secondsPassed = 0;

            while (secondsPassed < EXPECTED_PROCESSING_TIME_SEC)
            {
                await Task.Delay(30000);
                secondsPassed += 30;
                Console.WriteLine($"Processing {body}... {secondsPassed} seconds passed.");
                var deferredMsg = await _receiver.ReceiveDeferredMessageAsync(file.id);
                await _receiver.DeferMessageAsync(deferredMsg, new Dictionary<string, object> { { "percentDone", secondsPassed * 100f / EXPECTED_PROCESSING_TIME_SEC } });
            }

            // Complete the message
            Console.WriteLine($"Processing {body} done. Completing the message.");

            var msg = await _receiver.ReceiveDeferredMessageAsync(file.id);
            await _receiver.CompleteMessageAsync(msg);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Starting with arguments: {string.Join(" ", Environment.GetCommandLineArgs())}");

        _appLifetime.ApplicationStarted.Register(async () =>
        {
            await Task.Run(async () =>
            {
                var tcs = new TaskCompletionSource();
                cancellationToken.Register(() => tcs.TrySetCanceled(), false);

                try
                {
                    Console.WriteLine("Starting process with args: {0}", string.Join(" ", Environment.GetCommandLineArgs()));

                    // start processing 
                    await _processor.StartProcessingAsync();

                    // wait until the task is done
                    await tcs.Task;

                    _exitCode = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception!");
                    _exitCode = 1;
                }
                finally
                {
                    // Stop the application once the work is done
                    _appLifetime.StopApplication();
                }
            });
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug($"Exiting with return code: {_exitCode}");
        await _deferralTimer.DisposeAsync();
        await _processor.CloseAsync(cancellationToken);
        await _receiver.CloseAsync(cancellationToken);

        // Exit code may be null if the user cancelled via Ctrl+C/SIGTERM
        Environment.ExitCode = _exitCode.GetValueOrDefault(-1);
    }

    async Task MessageHandler(ProcessMessageEventArgs args)
    {
        string body = args.Message.Body.ToString();
        Console.WriteLine($"Putting: {body} on a processing queue.");

        _files.Add((args.Message.SequenceNumber, body));

        // Deferral the message
        await args.DeferMessageAsync(args.Message, new Dictionary<string, object> { { "expectedCompletion", DateTime.UtcNow.AddSeconds(EXPECTED_PROCESSING_TIME_SEC * 2) } });
    }
}