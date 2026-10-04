namespace load_balancer_service.Configuration
{
    public class LoadBalancerSettings
    {
        public string Strategy { get; set; } = "ConsistentHashing";
        public string CentralQueueName { get; set; } = "device-data-queue";
        public string IngestQueuePattern { get; set; } = "ingest-queue-{0}";
        public List<Replica> Replicas { get; set; } = new List<Replica>();
        public string ExchangeName { get; set; } = "device.data";
        public string ExchangeType { get; set; } = "direct";
    }
}