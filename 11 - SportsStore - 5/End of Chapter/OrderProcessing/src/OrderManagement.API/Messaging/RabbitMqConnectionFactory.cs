using RabbitMQ.Client;

namespace OrderManagement.API.Messaging;

/// <summary>
/// Builds a <see cref="ConnectionFactory"/> from the "RabbitMQ" configuration section.
/// Supports both plain AMQP (localhost dev) and TLS/AMQPS (CloudAMQP / LavinMQ).
///
/// Required config keys:
///   RabbitMQ:Host         — broker hostname
///   RabbitMQ:Port         — 5672 (plain) or 5671 (TLS)
///   RabbitMQ:Username     — broker username
///   RabbitMQ:Password     — broker password
///   RabbitMQ:VirtualHost  — vhost (e.g. "/" or "evilufrt")
///   RabbitMQ:UseTls       — true → enable TLS/AMQPS
/// </summary>
public static class RabbitMqConnectionFactory
{
    /// <summary>
    /// Reads the RabbitMQ section and returns a configured <see cref="ConnectionFactory"/>.
    /// Throws <see cref="InvalidOperationException"/> if any required value is missing,
    /// so the service crashes loudly instead of silently falling back to a no-op.
    /// </summary>
    public static ConnectionFactory Create(IConfiguration config, string sectionKey = "RabbitMQ")
    {
        var section = config.GetSection(sectionKey);

        var host        = section["Host"];
        var username    = section["Username"];
        var password    = section["Password"];
        var vhost       = section["VirtualHost"];
        var portStr     = section["Port"];
        var useTlsStr   = section["UseTls"];

        // ── Guard: crash loudly if any credential is missing ───────────────────
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:Host' is missing or empty. " +
                "Set it to your CloudAMQP hostname (e.g. ostrich.lmq.cloudamqp.com).");

        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:Username' is missing. Check appsettings.json.");

        if (string.IsNullOrWhiteSpace(password) || password == "REPLACE_ME")
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:Password' is missing or still set to the placeholder. " +
                "Replace 'REPLACE_ME' with your real CloudAMQP password.");

        if (string.IsNullOrWhiteSpace(vhost))
            throw new InvalidOperationException(
                $"[RabbitMQ] '{sectionKey}:VirtualHost' is missing. " +
                "Set it to your CloudAMQP vhost (e.g. 'evilufrt').");

        var port  = int.TryParse(portStr, out var p) ? p : 5672;
        var useTls = bool.TryParse(useTlsStr, out var t) && t;

        // ── Build factory ───────────────────────────────────────────────────────
        var factory = new ConnectionFactory
        {
            HostName               = host,
            Port                   = useTls ? 5671 : port,
            UserName               = username,
            Password               = password,
            VirtualHost            = vhost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval  = TimeSpan.FromSeconds(10),
        };

        if (useTls)
        {
            factory.Ssl = new SslOption
            {
                Enabled    = true,
                ServerName = host,
                // Use the system certificate store (no custom certs needed for CloudAMQP)
                AcceptablePolicyErrors =
                    System.Net.Security.SslPolicyErrors.None,
            };
        }

        return factory;
    }
}
