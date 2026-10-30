using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Triggers;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Core.Triggers;

/// <summary>Webhooks of all active workflows, by path. Each path belongs to exactly one trigger.</summary>
internal sealed class PipeWebhookRegistry : IPipeWebhookRouter
{
    private readonly Dictionary<string, PipeWebhookRegistration> _webhooks = new();
    private readonly ILogger<PipeWebhookRegistry> _logger;

    public PipeWebhookRegistry(ILogger<PipeWebhookRegistry> logger)
    {
        _logger = logger;
    }

    public IDisposable Register(PipeWebhookRegistration registration)
    {
        var path = PipeWebhookPaths.Normalize(registration.Path);
        registration = registration with { Path = path, Method = registration.Method.ToUpperInvariant() };

        lock (_webhooks)
        {
            if (_webhooks.TryGetValue(path, out var existing))
            {
                throw new PipeException(
                    $"Webhook path '/{path}' is already used by node '{existing.NodeName}' of workflow '{existing.WorkflowName}'");
            }
            _webhooks[path] = registration;
        }

        _logger.LogInformation("Webhook /{Path} ({Method}) registered for workflow {WorkflowName}", path, registration.Method, registration.WorkflowName);
        return new Registration(this, registration);
    }

    public async Task<PipeWebhookResponse> HandleAsync(PipeWebhookRequest request, CancellationToken cancellationToken)
    {
        PipeWebhookRegistration? registration;
        lock (_webhooks)
        {
            _webhooks.TryGetValue(request.Path, out registration);
        }

        if (registration is null)
        {
            return PipeWebhookResponse.Error(404, $"No active webhook at '/{request.Path}'");
        }
        if (registration.Method != "*" && !registration.Method.Equals(request.Method, StringComparison.OrdinalIgnoreCase))
        {
            var response = PipeWebhookResponse.Error(405, $"Method {request.Method} is not allowed, use {registration.Method}");
            response.Headers["Allow"] = registration.Method;
            return response;
        }

        try
        {
            return await registration.Handler(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Webhook /{Path} of workflow {WorkflowName} failed", request.Path, registration.WorkflowName);
            return PipeWebhookResponse.Error(500, ex.Message);
        }
    }

    private void Unregister(PipeWebhookRegistration registration)
    {
        lock (_webhooks)
        {
            // only remove our own entry; the path may have been taken over after a re-activation
            if (_webhooks.TryGetValue(registration.Path, out var current) && ReferenceEquals(current, registration))
            {
                _webhooks.Remove(registration.Path);
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly PipeWebhookRegistry _registry;
        private readonly PipeWebhookRegistration _registration;

        public Registration(PipeWebhookRegistry registry, PipeWebhookRegistration registration)
        {
            _registry = registry;
            _registration = registration;
        }

        public void Dispose() => _registry.Unregister(_registration);
    }
}

/// <summary>The services triggers get: schedules and webhooks.</summary>
internal sealed class PipeTriggerHost : IPipeTriggerHost
{
    private readonly PipeScheduler _scheduler;
    private readonly PipeWebhookRegistry _webhooks;

    public PipeTriggerHost(PipeScheduler scheduler, PipeWebhookRegistry webhooks)
    {
        _scheduler = scheduler;
        _webhooks = webhooks;
    }

    public IDisposable RegisterSchedule(string cronExpression, TimeZoneInfo timeZone, Func<CancellationToken, Task> callback)
        => _scheduler.RegisterSchedule(cronExpression, timeZone, callback);

    public IDisposable RegisterWebhook(PipeWebhookRegistration registration)
        => _webhooks.Register(registration);
}
