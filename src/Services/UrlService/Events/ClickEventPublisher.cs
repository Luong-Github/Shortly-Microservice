using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace UrlService.Events
{
    /// <summary>
    /// Event publisher for URL click tracking using RabbitMQ.
    /// Publishes click events async to decouple analytics processing from request handling.
    /// </summary>
    public interface IClickEventPublisher
    {
        /// <summary>
        /// Publishes a click event asynchronously.
        /// Does not block on success - fire-and-forget pattern for performance.
        /// </summary>
        Task PublishClickEventAsync(string? userId, string shortCode, string? ipAddress = null, string? userAgent = null);
    }

    public class ClickEventPublisher : IClickEventPublisher
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<ClickEventPublisher> _logger;
        private readonly string _queueName = "click_events";
        private readonly string _exchangeName = "url_events";

        public ClickEventPublisher(IConfiguration configuration, ILogger<ClickEventPublisher> logger)
        {
            _logger = logger;

            // Use configuration from appsettings instead of hardcoded localhost
            var rabbitMqHost = configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost";
            var rabbitMqPort = configuration.GetValue<int>("RabbitMQ:Port", 5672);
            var rabbitMqUser = configuration.GetValue<string>("RabbitMQ:Username") ?? "guest";
            var rabbitMqPassword = configuration.GetValue<string>("RabbitMQ:Password") ?? "guest";

            var factory = new ConnectionFactory()
            {
                HostName = rabbitMqHost,
                Port = rabbitMqPort,
                UserName = rabbitMqUser,
                Password = rabbitMqPassword,
                // Connection pooling settings
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                DispatchConsumersAsync = true
            };

            try
            {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                // Declare exchange and queue for durability
                _channel.ExchangeDeclare(exchange: _exchangeName, type: ExchangeType.Topic, durable: true);
                _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: "click.*");

                _logger.LogInformation("RabbitMQ connection established to {RabbitMqHost}:{RabbitMqPort}", rabbitMqHost, rabbitMqPort);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to establish RabbitMQ connection");
                throw;
            }
        }

        public async Task PublishClickEventAsync(string? userId, string shortCode, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var clickEvent = new
                {
                    UserId = userId ?? "anonymous",
                    ShortCode = shortCode,
                    IPAddress = ipAddress,
                    UserAgent = userAgent,
                    Timestamp = DateTime.UtcNow
                };

                var messageBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clickEvent));

                // Create basic properties for persistence and content type
                var properties = _channel.CreateBasicProperties();
                properties.ContentType = "application/json";
                properties.DeliveryMode = 2; // Persistent
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                // Publish to exchange with routing key
                _channel.BasicPublish(
                    exchange: _exchangeName,
                    routingKey: $"click.{shortCode}",
                    basicProperties: properties,
                    body: messageBody);

                _logger.LogInformation("Click event published: ShortCode={ShortCode}, UserId={UserId}", shortCode, userId);

                // Complete async - in production, you'd use Task.Run to not block
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish click event for ShortCode={ShortCode}", shortCode);
                // Don't rethrow - event publishing should not block the user's request
                // In production, implement a retry mechanism or dead-letter queue
            }
        }

        public void Dispose()
        {
            try
            {
                _channel?.Close();
                _channel?.Dispose();
                _connection?.Close();
                _connection?.Dispose();
                _logger.LogInformation("RabbitMQ connection closed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing RabbitMQ connection");
            }
        }
    }
}
