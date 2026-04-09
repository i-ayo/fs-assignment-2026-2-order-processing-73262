using RabbitMQ.Client;

namespace ShippingService.Messaging;

/// <summary>
/// Builds a TLS-capable <see cref="ConnectionFactory"/> from the "RabbitMQ" config section.
/// Throws loudly if any required credential is absent — no silent no-op allowed.
/// </summary>
public static class RabbitMqConnectionFactory
{
    public static ConnectionFactory Create(IConfiguration config, string sectionKey = "RabbitMQ")
    {
        var section   = config.GetSection(sectionKey);
        var host      = section["Host"];
        var username  = section["Username"];
        var password  = section["Password"];
        var vhost     = section["VirtualHost"];
        var portStr   = section["Port"];
        var useTlsStr = section["UseTls"];

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:Host' is missing. Set CloudAMQP hostname.");

        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:Username' is missing.");

        if (string.IsNullOrWhiteSpace(password) || password == "REPLACE_ME")
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:Password' is not set. Replace REPLACE_ME with real password.");

        if (string.IsNullOrWhiteSpace(vhost))
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:VirtualHost' is missing.");

        var port   = int.TryParse(portStr, out var p) ? p : 5672;
        var useTls = bool.TryParse(useTlsStr, out var t) && t;

        var factory = new ConnectionFactory
        {
            HostName                 = host,
            Port                     = useTls ? 5671 : port,
            UserName                 = username,
            Password                 = password,
            VirtualHost              = vhost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval  = TimeSpan.FromSeconds(10),
        };

        if (useTls)
        {
            factory.Ssl = new SslOption
            {
                Enabled    = true,
                ServerName = host,
                AcceptablePolicyErrors = System.Net.Security.SslPolicyErrors.None,
            };
        }

        return factory;
    }
}
