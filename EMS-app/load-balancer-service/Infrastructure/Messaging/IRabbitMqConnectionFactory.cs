using RabbitMQ.Client;

namespace load_balancer_service.Infrastructure.Messaging
{

    public interface IRabbitMqConnectionFactory
    {
         IConnection CreateConnection();
    }
}