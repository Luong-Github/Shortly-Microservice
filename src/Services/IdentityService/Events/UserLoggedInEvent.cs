using MediatR;
using RabbitMQ.Client;
using System.Threading;
using System.Threading.Tasks;
using IdentityService.Events;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IdentityService.Events
{
    public class UserLoggedInEvent : INotification
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public string Token { get; set; }
        public UserLoggedInEvent(string userId, string email, string token)
        {
            UserId = userId;
            Email = email;
            Token = token;
        }
    }

    public class UserLoggedInEventHandler : INotificationHandler<UserLoggedInEvent>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserLoggedInEventHandler> _logger;

        public UserLoggedInEventHandler(IConfiguration configuration, ILogger<UserLoggedInEventHandler> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken)
        {
            var rabbitMqHost = _configuration["RabbitMQ:Host"] ?? "localhost";
            var rabbitMqPort = int.TryParse(_configuration["RabbitMQ:Port"], out var configuredPort) ? configuredPort : 5672;
            var rabbitMqUser = _configuration["RabbitMQ:Username"] ?? "guest";
            var rabbitMqPassword = _configuration["RabbitMQ:Password"] ?? "guest";

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = rabbitMqHost,
                    Port = rabbitMqPort,
                    UserName = rabbitMqUser,
                    Password = rabbitMqPassword
                };

                using var connection = factory.CreateConnection();
                using var channel = connection.CreateModel();

                channel.QueueDeclare(queue: "user-logged-in",
                                         durable: false,
                                         exclusive: false,
                                         autoDelete: false,
                                         arguments: null);

                var message = JsonSerializer.Serialize(notification);
                var body = Encoding.UTF8.GetBytes(message);

                channel.BasicPublish(exchange: "",
                                     routingKey: "user-logged-in",
                                     basicProperties: null,
                                     body: body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish user login event to RabbitMQ");
            }

            return Task.CompletedTask;
        }
    }
}
