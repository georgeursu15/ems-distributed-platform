namespace simulator_service.Model
{
    public class SimulatorMultipleDeviceDataRequestStressTest
    {
        public List<Guid> DeviceIds { get; set; }
        public int MessagesPerDevice { get; set; }
    }
}
