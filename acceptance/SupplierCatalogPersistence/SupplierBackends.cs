using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace SupplierCatalogPersistence.Acceptance;

// No Docker CLI child process. Only the explicitly admitted local hosted socket is used.
internal sealed class SupplierBackends
{
    private readonly SupplierResourceScope owner;
    private readonly DockerClient docker;
    private readonly string run = Guid.NewGuid().ToString("N");
    private readonly string expires = DateTimeOffset.UtcNow.AddMinutes(15).ToString("O");
    private readonly List<Resource> resources = [];
    private string? daemon;
    private bool clientReleased;
    internal PostgreSqlContainer Postgres { get; private set; } = null!;
    internal RedisContainer Redis { get; private set; } = null!;
    internal SupplierBackends(SupplierResourceScope owner)
    {
        if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")))
            throw new InvalidOperationException("Only the admitted disposable hosted local Docker socket is supported.");
        this.owner = owner;
        owner.BindBackend("postgres-and-redis", run, () => resources.Where(resource => resource.Id is not null).Select(resource => resource.Id!).Order().ToArray());
        docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
    }

    internal async Task StartAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        daemon = (await docker.System.GetSystemInfoAsync(deadline.Token)).ID;
        if (string.IsNullOrWhiteSpace(daemon)) throw new InvalidOperationException("Daemon identity unavailable.");
        Resource Plan(string kind, string image, string mount, long memory, long cpu, string command)
        {
            var resource = new Resource($"supplier-proof-{run}-{kind}", image, mount, memory, cpu, command);
            resources.Add(resource); // Intent recorded before Build or actual container dispatch.
            return resource;
        }
        void Configure(CreateContainerParameters parameters, Resource resource)
        {
            parameters.HostConfig.Memory = resource.Memory;
            parameters.HostConfig.NanoCPUs = resource.Cpu;
            parameters.HostConfig.Tmpfs = new Dictionary<string, string> { [resource.Mount] = "rw,size=268435456" };
            foreach (var bindings in parameters.HostConfig.PortBindings.Values)
                foreach (var binding in bindings) binding.HostIP = "127.0.0.1";
        }
        var pg = Plan("postgres", "postgres:18-alpine", "/var/lib/postgresql", 512L * 1024 * 1024, 1_000_000_000,
            "(sleep 900; kill -TERM 1; sleep 5; kill -KILL 1) & exec docker-entrypoint.sh postgres");
        Postgres = new PostgreSqlBuilder(pg.Image).WithDockerEndpoint(new Uri("unix:///var/run/docker.sock"))
            .WithCleanUp(false).WithName(pg.Name).WithLabel("maliev.owner", "intranet-supplier-proof").WithLabel("maliev.run", run)
            .WithLabel("maliev.expires-utc", expires).WithEntrypoint("/bin/sh", "-c").WithCommand(pg.Command)
            .WithCreateParameterModifier(parameters => Configure(parameters, pg)).Build();
        pg.Container = Postgres;
        await StartResourceAsync(pg, deadline.Token);
        var redis = Plan("redis", "redis:7-alpine", "/data", 128L * 1024 * 1024, 500_000_000,
            "(sleep 900; kill -TERM 1; sleep 5; kill -KILL 1) & exec docker-entrypoint.sh redis-server --save '' --appendonly no");
        Redis = new RedisBuilder(redis.Image).WithDockerEndpoint(new Uri("unix:///var/run/docker.sock"))
            .WithCleanUp(false).WithName(redis.Name).WithLabel("maliev.owner", "intranet-supplier-proof").WithLabel("maliev.run", run)
            .WithLabel("maliev.expires-utc", expires).WithEntrypoint("/bin/sh", "-c").WithCommand(redis.Command)
            .WithCreateParameterModifier(parameters => Configure(parameters, redis)).Build();
        redis.Container = Redis;
        await StartResourceAsync(redis, deadline.Token);
    }

    private async Task StartResourceAsync(Resource resource, CancellationToken token)
    {
        if (await InspectAsync(resource.Name, token) is not null) throw new InvalidOperationException("Owned name collision; preserve pre-existing resource.");
        resource.BaselineAbsent = true;
        Exception? startup = null;
        try { await resource.Container!.StartAsync(token); }
        catch (Exception error) { startup = error; }
        using var capture = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var actual = await InspectAsync(resource.Name, capture.Token);
            if (actual is not null)
            {
                Validate(resource, actual);
                string? sdkId;
                try { sdkId = resource.Container!.Id; } catch (InvalidOperationException) { sdkId = null; }
                if (startup is null && sdkId is null || sdkId is not null && sdkId != actual.ID)
                    throw new InvalidOperationException("SDK and inspected creation IDs do not match.");
                resource.Id = actual.ID;
                resource.Signature = Signature(actual);
                await ReceiptAsync(new { run, state = "created", daemon, resource.Name, resource.Id,
                    actual.Created, actual.Image, resource.Memory, resource.Cpu, expires,
                    persistentData = false, localEndpoint = "unix:///var/run/docker.sock", ownershipSignature = resource.Signature }, capture.Token);
            }
            else if (startup is null) throw new InvalidOperationException("Successful startup lacks creation evidence.");
        }
        catch (Exception evidence) when (startup is not null) { throw new AggregateException(startup, evidence); }
        if (startup is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startup).Throw();
    }

    private async Task<ContainerInspectResponse?> InspectAsync(string id, CancellationToken token)
    {
        if ((await docker.System.GetSystemInfoAsync(token)).ID != daemon) throw new InvalidOperationException("Daemon changed; preserve resources.");
        try { return await docker.Containers.InspectContainerAsync(id, token); }
        catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private void Validate(Resource resource, ContainerInspectResponse actual)
    {
        var config = actual.Config;
        var host = actual.HostConfig;
        if (!resource.BaselineAbsent || actual.Name != "/" + resource.Name
            || !Regex.IsMatch(actual.ID ?? "", "\\A[a-f0-9]{64}\\z")
            || actual.Created == default || actual.Created.Kind != DateTimeKind.Utc || actual.Created < resource.Intent || actual.Created > DateTime.UtcNow
            || !Regex.IsMatch(actual.Image ?? "", "\\Asha256:[a-f0-9]{64}\\z")
            || config.Image != resource.Image || config.Labels["maliev.owner"] != "intranet-supplier-proof"
            || config.Labels["maliev.run"] != run || config.Labels["maliev.expires-utc"] != expires
            || host.Memory != resource.Memory || host.NanoCPUs != resource.Cpu
            || !config.Entrypoint.SequenceEqual(new[] { "/bin/sh", "-c" }) || !config.Cmd.SequenceEqual(new[] { resource.Command })
            || host.Tmpfs.Count != 1 || host.Tmpfs[resource.Mount] != "rw,size=268435456"
            || host.Binds is { Count: > 0 } || host.Mounts is { Count: > 0 }
            || actual.Mounts.Any(mount => mount.Type != "tmpfs" || mount.Destination != resource.Mount || !mount.RW)
            || host.PortBindings.Values.SelectMany(bindings => bindings).Any(binding => binding.HostIP != "127.0.0.1"))
            throw new InvalidOperationException("Owned resource envelope mismatch; preserve exact resource.");
    }

    private static string Signature(ContainerInspectResponse actual) => JsonSerializer.Serialize(new
    {
        actual.ID, actual.Created, actual.Image, actual.Name,
        labels = actual.Config.Labels.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        mounts = actual.Mounts.Select(mount => new { mount.Type, mount.Source, mount.Destination, mount.RW }).OrderBy(mount => mount.Destination).ToArray(),
        tmpfs = actual.HostConfig.Tmpfs.OrderBy(pair => pair.Key).ToArray(),
        ports = actual.HostConfig.PortBindings.OrderBy(pair => pair.Key).ToArray(),
        actual.HostConfig.Memory, actual.HostConfig.NanoCPUs, actual.Config.Cmd, actual.Config.Entrypoint
    });

    private async Task<ContainerInspectResponse?> ReobserveAsync(Resource resource, CancellationToken token)
    {
        var byName = await InspectAsync(resource.Name, token);
        if (resource.Id is null)
        {
            if (byName is not null) throw new InvalidOperationException("Uncaptured resource preserved.");
            return null;
        }
        var byId = await InspectAsync(resource.Id, token);
        if (byId is null && byName is null) return null;
        if (byId is null || byName is null || byName.ID != resource.Id || byId.ID != resource.Id)
            throw new InvalidOperationException("Resource generation changed; preserve ownership.");
        Validate(resource, byId);
        Validate(resource, byName);
        if (Signature(byId) != resource.Signature || Signature(byName) != resource.Signature)
            throw new InvalidOperationException("Resource identity changed; preserve ownership.");
        return byId;
    }

    internal async Task ReleaseAsync(CancellationToken token)
    {
        if (clientReleased) return;
        var failures = new List<Exception>();
        var releases = resources.Where(resource => !resource.Absent).Reverse().Select(async resource =>
        {
            try
            {
                if (!resource.BaselineAbsent)
                {
                    // No start dispatch occurred for this resource; never touch a name collision.
                    if (resource.Container is not null) await resource.Container.DisposeAsync();
                    resource.Absent = true;
                    return;
                }
                var actual = await ReobserveAsync(resource, token);
                if (actual is not null)
                {
                    await docker.Containers.StopContainerAsync(actual.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, token);
                    actual = await ReobserveAsync(resource, token);
                    if (actual is not null)
                    {
                        if (actual.State.Running) throw new InvalidOperationException("Container did not stop; removal refused.");
                        await docker.Containers.RemoveContainerAsync(actual.ID, new ContainerRemoveParameters { Force = false, RemoveVolumes = false }, token);
                    }
                }
                if (await ReobserveAsync(resource, token) is not null) throw new InvalidOperationException("Absence is not verified.");
                await ReceiptAsync(new { run, daemon, resource.Name, resource.Id, state = "verified-absent", expires, persistentData = false }, token);
                if (resource.Container is not null) await resource.Container.DisposeAsync();
                resource.Absent = true;
            }
            catch (Exception error) { lock (failures) failures.Add(error); }
        }).ToArray();
        await Task.WhenAll(releases);
        if (resources.All(resource => resource.Absent)) { docker.Dispose(); clientReleased = true; }
        if (failures.Count != 0) throw new AggregateException("Exact backend ownership retained.", failures);
    }

    private Task ReceiptAsync<T>(T receipt, CancellationToken token) => owner.WriteReceiptAsync("resources.jsonl", receipt, token);

    private sealed class Resource(string name, string image, string mount, long memory, long cpu, string command)
    {
        internal DateTime Intent { get; } = DateTime.UtcNow;
        internal string Name { get; } = name;
        internal string Image { get; } = image;
        internal string Mount { get; } = mount;
        internal long Memory { get; } = memory;
        internal long Cpu { get; } = cpu;
        internal string Command { get; } = command;
        internal IContainer? Container { get; set; }
        internal bool BaselineAbsent { get; set; }
        internal string? Id { get; set; }
        internal string? Signature { get; set; }
        internal bool Absent { get; set; }
    }
}
