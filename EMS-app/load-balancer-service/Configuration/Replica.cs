namespace load_balancer_service.Configuration
{
    public class Replica
    {
        public string Id { get; set; } = string.Empty;
        public int Weight { get; set; } = 1;
        public double LoadPercentage { get; set; } = 0;
        public bool IsHealthy { get; set; } = true;
    }
}