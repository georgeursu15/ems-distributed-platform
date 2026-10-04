using Microsoft.AspNetCore.Mvc;
using Shared.Events;
using simulator_service.Infrastructure.Messaging;
using simulator_service.Model;
using System.Security.Cryptography;
namespace simulator_service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SimulatorController : ControllerBase
    {
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<SimulatorController> _logger;
        private readonly RabbitMqSettings _settings;

        private const string SimulatorDataRoutingKey = "monitor.data";
        private const string DeviceRoutingKey = "monitoring.device.created";

        public SimulatorController(
            IEventPublisher eventPublisher,
            ILogger<SimulatorController> logger,
            Microsoft.Extensions.Options.IOptions<RabbitMqSettings> settings)
        {
            _eventPublisher = eventPublisher;
            _logger = logger;
            _settings = settings.Value;
        }

        /// <summary>
        /// Publishes simulated device data to the central device data queue for load balancing
        /// </summary>
        /// <param name="request">The simulator data to publish</param>
        /// <returns>Result of the publish operation</returns>
        [HttpPost("publish")]
        public async Task<IActionResult> PublishSimulatorData([FromBody] SimulatorDataRequest request)
        {
            var correlationId = Guid.NewGuid().ToString();
            int dataCount = request.DataCount;
            var random = new Random();
            try
            {
                for (int i = 0; i < dataCount; i++)
                {
                    int value = random.Next(0, 101);
                    var simulatorEvent = new SimulatorDataEvent
                    {
                        DeviceId = request.DeviceId,
                        ConsumptionValue = value,
                        Timestamp = DateTime.UtcNow,
                        CorrelationId = correlationId
                    };

                    // Publish to central queue for load balancing
                    await _eventPublisher.PublishToQueueAsync(simulatorEvent, _settings.CentralQueueName);

                    await Task.Delay(TimeSpan.FromMinutes(10));


                    _logger.LogInformation(
                        "Published simulator data to central queue: DeviceId={DeviceId}, Queue={QueueName}, CorrelationId={CorrelationId}, Index={Index}",
                        request.DeviceId, _settings.CentralQueueName, correlationId, i);
                }

                return Ok(new
                {
                    message = "Simulator data published successfully",
                    correlationId,
                    deviceId = request.DeviceId,
                    publishedCount = dataCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish simulator data: DeviceId={DeviceId}, CorrelationId={CorrelationId}",
                    request.DeviceId, correlationId);
                return StatusCode(500, new
                {
                    message = "Failed to publish simulator data",
                    correlationId,
                    error = ex.Message
                });
            }
        }


        [HttpPost("multipledevices")]
        public async Task<IActionResult> PublishMultipleTestDevices([FromBody] SimulatorMultipleDeviceDataRequestStressTest request)
        {
            var correlationId = Guid.NewGuid().ToString();
            int deviceCount = request.DeviceIds.Count;
            var random = new Random();
            int totalPublished = 0;

            try
            {
                _logger.LogInformation(
                    "Starting stress test:  {DeviceCount} devices, {MessagesPerDevice} messages each, CorrelationId={CorrelationId}",
                    deviceCount, request.MessagesPerDevice, correlationId);

                // Publish messages for each device
                var tasks = request.DeviceIds.Select(async deviceId =>
                {
                    int publishedForDevice = 0;
                    for (int i = 0; i < request.MessagesPerDevice; i++)
                    {
                        int value = random.Next(0, 101);
                        var simulatorEvent = new SimulatorDataEvent
                        {
                            DeviceId = deviceId,
                            ConsumptionValue = value,
                            Timestamp = DateTime.UtcNow,
                            CorrelationId = correlationId
                        };

                        // Publish to central queue for load balancing
                        await _eventPublisher.PublishToQueueAsync(simulatorEvent, _settings.CentralQueueName);
                        publishedForDevice++;

                        // Optional: Small delay to avoid overwhelming the system
                        // Remove or adjust based on your stress test needs
                        // await Task.Delay(10);
                    }

                    _logger.LogInformation(
                        "Completed publishing for DeviceId={DeviceId}, Messages={Count}",
                        deviceId, publishedForDevice);

                    return publishedForDevice;
                });

                var results = await Task.WhenAll(tasks);
                totalPublished = results.Sum();

                _logger.LogInformation(
                    "Stress test completed: TotalMessages={TotalPublished}, CorrelationId={CorrelationId}",
                    totalPublished, correlationId);

                return Ok(new
                {
                    message = "Multiple device simulator data published successfully",
                    correlationId,
                    deviceCount,
                    messagesPerDevice = request.MessagesPerDevice,
                    totalPublished
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish multiple device simulator data: DeviceCount={DeviceCount}, CorrelationId={CorrelationId}, Published={Published}",
                    deviceCount, correlationId, totalPublished);
                return StatusCode(500, new
                {
                    message = "Failed to publish multiple device simulator data",
                    correlationId,
                    totalPublished,
                    error = ex.Message
                });
            }
        }

        [HttpPost("testdevice")]
        public async Task<IActionResult> PublishTestDevice()
        {
            var correlationId = Guid.NewGuid().ToString();
            var deviceid = Guid.NewGuid();
            try
            {
                var deviceEvent = new CreatedDeviceEvent
                {
                    id = deviceid.ToString(),
                    CorrelationId = correlationId
                };
                await _eventPublisher.PublishAsync(deviceEvent, DeviceRoutingKey);
                _logger.LogInformation(
                    "Published test device event: DeviceId={DeviceId}, CorrelationId={CorrelationId}",
                    deviceid, correlationId);
                return Ok(new
                {
                    message = "Test device event published successfully",
                    correlationId,
                    deviceId = deviceid
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish test device event: DeviceId={DeviceId}, CorrelationId={CorrelationId}",
                    deviceid, correlationId);
                return StatusCode(500, new
                {
                    message = "Failed to publish test device event",
                    correlationId,
                    error = ex.Message
                });
            }
        }
    }
}