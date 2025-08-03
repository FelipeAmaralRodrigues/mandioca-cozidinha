namespace MandiocaCozidinha.Services.Api.HostedServices
{
    public class QueuedHostedService : BackgroundService
    {
        private readonly ILogger<QueuedHostedService> _logger;
        public IBackgroundTaskQueue TaskQueue { get; }

        public QueuedHostedService(
            IBackgroundTaskQueue taskQueue,
            ILogger<QueuedHostedService> logger)
        {
            TaskQueue = taskQueue;
            _logger = logger;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting MandiocaCozidinha API...");
            ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
            _logger.LogInformation("Worker Threads: " + workerThreads);
            _logger.LogInformation("Completion Port Threads: " + completionPortThreads);

            ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableCompletionPortThreads);
            _logger.LogInformation("Available Worker Threads: " + availableWorkerThreads);
            _logger.LogInformation("Available Completion Port Threads: " + availableCompletionPortThreads);

            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
            _logger.LogInformation("Max Worker Threads: " + maxWorkerThreads);
            _logger.LogInformation("Max Completion Port Threads: " + maxCompletionPortThreads);

            _logger.LogInformation("Pending Work Item Count: " + ThreadPool.PendingWorkItemCount);

            var workers = new List<Task>();
            for (int i = 0; i < 4; i++)
            {
                workers.Add(BackgroundProcessing(stoppingToken));
            }
            await Task.WhenAll(workers);
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await TaskQueue.DequeueAsync(stoppingToken);

                try
                {
                    await workItem(stoppingToken);
                }
                catch (Exception ex)
                {
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queued Hosted Service is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }
}
