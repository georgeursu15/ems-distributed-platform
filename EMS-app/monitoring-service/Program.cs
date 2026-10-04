using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using monitoring_service.BackgroundServices;
using monitoring_service.Data;
using monitoring_service.Infrastructure.Messaging;
using monitoring_service.Model;

var builder = WebApplication.CreateBuilder(args);

// Configure RabbitMqSettings with explicit override logic
builder.Services.AddOptions<RabbitMqSettings>()
    .Bind(builder.Configuration.GetSection("RabbitMQ"))
    .PostConfigure(settings =>
    {
        // Environment variables take precedence
        var replicaId = Environment.GetEnvironmentVariable("ReplicaId");
        if (!string.IsNullOrEmpty(replicaId))
        {
            settings.ReplicaId = replicaId;
            Console.WriteLine($"[ENV] ReplicaId = {replicaId}");
        }

        var ingestQueue = Environment.GetEnvironmentVariable("RabbitMQ__IngestQueue");
        if (!string.IsNullOrEmpty(ingestQueue))
        {
            settings.IngestQueue = ingestQueue;
            Console.WriteLine($"[ENV] IngestQueue = {ingestQueue}");
        }
        else if (!string.IsNullOrEmpty(settings.ReplicaId))
        {
            settings.IngestQueue = $"ingest-queue-{settings.ReplicaId}";
            Console.WriteLine($"[CALC] IngestQueue = {settings.IngestQueue}");
        }

        var host = Environment.GetEnvironmentVariable("RabbitMQ__Host");
        if (!string.IsNullOrEmpty(host))
        {
            settings.HostName = host;
            Console.WriteLine($"[ENV] HostName = {host}");
        }
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
builder.Services.AddSingleton<IEventConsumer, RabbitMqEventConsumer>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

builder.Services.AddHostedService<SimulatorDataConsumerService>();
builder.Services.AddHostedService<DeviceCreatedConsumerService>();

builder.Services.AddDbContext<MonitorDbUtils>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ConsumptionDBconn")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();