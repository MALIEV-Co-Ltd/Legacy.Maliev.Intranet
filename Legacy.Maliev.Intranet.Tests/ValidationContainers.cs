using Testcontainers.Redis;

namespace Legacy.Maliev.Intranet.Tests;

internal static class ValidationContainers
{
    internal static RedisContainer Redis() => new RedisBuilder("redis:7.4.5-alpine")
        .WithLabel("maliev.validation.intranet", Environment.GetEnvironmentVariable("MALIEV_INTRANET_TEST_RUN")
            ?? Guid.NewGuid().ToString("N"))
        .WithCreateParameterModifier(parameters =>
        {
            var host = parameters.HostConfig ?? throw new InvalidOperationException("Disposable Redis host configuration is required.");
            host.Memory = 128L * 1024 * 1024;
            host.NanoCPUs = 500_000_000;
            foreach (var bindings in host.PortBindings.Values)
                foreach (var binding in bindings) binding.HostIP = "127.0.0.1";
        }).Build();
}
