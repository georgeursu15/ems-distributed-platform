using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shared.Events;
using monitoring_service.Infrastructure.Messaging;
using monitoring_service.Model;
using monitoring_service.Data;

namespace monitoring_service.BackgroundServices
{
    /// <summary>
    /// Background service that consumes SimulatorData events from RabbitMQ ingest queue
    /// and aggregates them every 10 minutes in-memory, flushing totals to the DB.
    /// Records are inserted with timestamps truncated to the hour.
    ///
    /// Consumes from per-replica ingest queue (e.g., ingest-queue-1)
    /// </summary>
    public class SimulatorDataConsumerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IEventConsumer _eventConsumer;
        private readonly ILogger<SimulatorDataConsumerService> _logger;
        private readonly IEventPublisher _eventPublisher;
        private readonly RabbitMqSettings _settings;

        private ConcurrentDictionary<Guid, double> _currentWindowSums = new();
        private DateTime _currentWindowStartUtc;
        private readonly object _windowLock = new();
        private readonly ConcurrentDictionary<Guid, bool> _deviceExistenceCache = new();

        private const int AggregationWindowMinutes = 10;

        public SimulatorDataConsumerService(
            IServiceProvider serviceProvider,
            IEventConsumer eventConsumer,
            ILogger<SimulatorDataConsumerService> logger,
            IEventPublisher _publisher,
            Microsoft.Extensions.Options.IOptions<RabbitMqSettings> settings)
        {
            _serviceProvider = serviceProvider;
            _eventConsumer = eventConsumer;
            _logger = logger;
            _eventPublisher = _publisher;
            _settings = settings.Value;

            _currentWindowStartUtc = TruncateToTenMinutes(DateTime.UtcNow);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Determine ingest queue name based on replica ID
            var ingestQueueName = string.Format(_settings.IngestQueuePattern, _settings.ReplicaId);

            _logger.LogInformation(
                "SimulatorDataConsumerService starting for replica {ReplicaId}, consuming from queue: {QueueName}, aggregation window: {Minutes} minutes",
                _settings.ReplicaId, ingestQueueName, AggregationWindowMinutes);

            try
            {
                var aggregatorTask = Task.Run(() => TenMinuteAggregatorLoopAsync(stoppingToken), stoppingToken);

                await _eventConsumer.StartConsumingFromQueueAsync<SimulatorDataEvent>(
                    queueName: ingestQueueName,
                    handler: HandleSimulatorDataEventAsync,
                    cancellationToken: stoppingToken);

                await aggregatorTask;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in SimulatorDataConsumerService");
            }
        }

        private async Task<bool> HandleSimulatorDataEventAsync(SimulatorDataEvent @event)
        {
            try
            {
                var deviceGuid = @event.DeviceId;

                if (deviceGuid == Guid.Empty)
                    return false;

                if (!_deviceExistenceCache.TryGetValue(deviceGuid, out var exists))
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<MonitorDbUtils>();
                    exists = await db.Devices.AnyAsync(d => d.Id == deviceGuid);
                    _deviceExistenceCache[deviceGuid] = exists;
                }
                if (!exists)
                {
                    _logger.LogWarning("Received SimulatorDataEvent for unknown DeviceId={DeviceId}, CorrelationId={CorrelationId}",
                        @event.DeviceId, @event.CorrelationId);
                    return true;
                }

                var timestamp = @event.Timestamp != default ? @event.Timestamp : DateTime.UtcNow;
                var eventWindow = TruncateToTenMinutes(timestamp);

                if (eventWindow != _currentWindowStartUtc)
                {
                    lock (_windowLock)
                    {
                        if (eventWindow != _currentWindowStartUtc)
                        {
                            var toFlush = _currentWindowSums;
                            var windowToFlush = _currentWindowStartUtc;
                            _currentWindowSums = new ConcurrentDictionary<Guid, double>();
                            _currentWindowStartUtc = eventWindow;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await FlushSumsToDatabaseAsync(toFlush, windowToFlush);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error flushing sums for window {Window}", windowToFlush);
                                }
                            });
                        }
                    }
                }

                var energy = @event.ConsumptionValue;
                _currentWindowSums.AddOrUpdate(deviceGuid, energy, (_, old) => old + energy);

                _logger.LogInformation("Accumulated reading: DeviceId={DeviceId}, Added={Energy}, WindowStartUtc={Window}",
                    deviceGuid, energy, _currentWindowStartUtc);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process SimulatorData event: DeviceId={DeviceId}, CorrelationId={CorrelationId}",
                    @event.DeviceId, @event.CorrelationId);
                return false;
            }
        }

        private async Task TenMinuteAggregatorLoopAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;
                    var nextWindowBoundary = TruncateToTenMinutes(now).AddMinutes(AggregationWindowMinutes);
                    var delay = nextWindowBoundary - now;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }

                    ConcurrentDictionary<Guid, double> toFlush;
                    DateTime windowToFlush;

                    lock (_windowLock)
                    {
                        windowToFlush = _currentWindowStartUtc;
                        toFlush = _currentWindowSums;
                        _currentWindowSums = new ConcurrentDictionary<Guid, double>();
                        _currentWindowStartUtc = TruncateToTenMinutes(DateTime.UtcNow);
                    }

                    try
                    {
                        await FlushSumsToDatabaseAsync(toFlush, windowToFlush);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error flushing sums for window {Window}", windowToFlush);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await FinalFlushOnShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in TenMinuteAggregatorLoopAsync");
            }
        }

        private async Task FlushSumsToDatabaseAsync(ConcurrentDictionary<Guid, double> sumsSnapshot, DateTime windowStartUtc)
        {
            if (sumsSnapshot == null || sumsSnapshot.IsEmpty)
            {
                _logger.LogInformation("No readings to flush for window {Window}", windowStartUtc);
                return;
            }

            var hourTimestamp = TruncateToHour(windowStartUtc);

            _logger.LogInformation("Flushing {Count} device totals for window {Window}, inserting with hour timestamp {Hour}",
                sumsSnapshot.Count, windowStartUtc, hourTimestamp);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbUtils>();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                foreach (var kv in sumsSnapshot)
                {
                    var deviceId = kv.Key;
                    var totalEnergy = kv.Value;

                    var deviceModel = await db.Devices.FindAsync(deviceId);
                    if (deviceModel != null)
                    {
                        var deviceConsumption = deviceModel.Consumption;
                        var deviceUserId = deviceModel.UserId;
                        var deviceConsumptionDouble = Convert.ToDouble(deviceConsumption);

                        if (totalEnergy > deviceConsumptionDouble)
                        {
                            await _eventPublisher.PublishAsync(new Notification
                            {
                                UserId = deviceUserId.ToString(),
                                Title = "Consumption Limit Exceeded",
                                Message = $"Device {deviceId} exceeded consumption limit of {deviceConsumption} with total {totalEnergy} at {hourTimestamp}.",
                                Type = "Warning",
                                Timestamp = DateTime.UtcNow
                            }, "user_notifications");
                        }
                    }

                    var consumption = new Consumption
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = deviceId,
                        Timestamp = hourTimestamp, 
                        ConsumptionValue = totalEnergy
                    };

                    db.Add(consumption);
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();

                _logger.LogInformation("Successfully flushed {Count} device totals for window {Window} with hour timestamp {Hour}",
                    sumsSnapshot.Count, windowStartUtc, hourTimestamp);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task FinalFlushOnShutdownAsync()
        {
            ConcurrentDictionary<Guid, double> toFlush;
            DateTime windowToFlush;

            lock (_windowLock)
            {
                toFlush = _currentWindowSums;
                windowToFlush = _currentWindowStartUtc;
                _currentWindowSums = new ConcurrentDictionary<Guid, double>();
            }

            try
            {
                await FlushSumsToDatabaseAsync(toFlush, windowToFlush);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing final flush on shutdown for window {Window}", windowToFlush);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SimulatorDataConsumerService stopping...");
            return base.StopAsync(cancellationToken);
        }

        private static DateTime TruncateToTenMinutes(DateTime dt)
        {
            var totalMinutes = dt.Hour * 60 + dt.Minute;
            var truncatedMinutes = (totalMinutes / AggregationWindowMinutes) * AggregationWindowMinutes;
            var hour = truncatedMinutes / 60;
            var minute = truncatedMinutes % 60;
            return new DateTime(dt.Year, dt.Month, dt.Day, hour, minute, 0, DateTimeKind.Utc);
        }

        private static DateTime TruncateToHour(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc);
        }
    }
}