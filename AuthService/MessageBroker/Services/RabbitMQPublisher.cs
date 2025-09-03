using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace AuthService.MessageBroker.Services
{
    public class RabbitMQPublisher<T> : IRabbitMQPublisher<T>
    {
        private readonly RabbitMQSetting _rabbitMqSetting;

        public RabbitMQPublisher(IOptions<RabbitMQSetting> rabbitMqSetting)
        {
            _rabbitMqSetting = rabbitMqSetting.Value;
        }

        public async Task PublishMessageAsync(T message, string queueName)
        {
            var factory = new ConnectionFactory
            {
                HostName = _rabbitMqSetting.HostName ?? throw new ArgumentNullException("Missing RabbitMQ Hostname"),
                UserName = _rabbitMqSetting.UserName ?? throw new ArgumentNullException("Missing RabbitMQ Username"),
                Password = _rabbitMqSetting.Password ?? throw new ArgumentNullException("Missing RabbitMQ Password")
            };

            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

            var messageJson = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(messageJson);

            await channel.BasicPublishAsync("", queueName, body);
        }
    }
}
