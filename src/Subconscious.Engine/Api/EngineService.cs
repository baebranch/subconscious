using Microsoft.Extensions.DependencyInjection;
using Subconscious.Engine.Agents;
using Subconscious.Engine.Configuration;
using Subconscious.Engine.Api.Events;
using Subconscious.Engine.Api.Services;
using Subconscious.Engine.Api.WebSocket;
using Subconscious.Engine.Dispatch;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Api;

/// <summary>
/// DI registration for every service the local API needs. Kestrel hosting/lifetime itself
/// lives in <see cref="EngineHost"/> — this only wires the service graph.
/// </summary>
public static class EngineServiceExtensions
{
    public static IServiceCollection AddEngineServices(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IFernetKeyProvider, WindowsCredentialManagerFernetKeyProvider>();
        services.AddSingleton<IModelConfigurationStore, EncryptedModelConfigurationStore>();
        services.AddSingleton<ProviderTable>();
        services.AddSingleton<ToolDispatcher>();
        services.AddSingleton<HandshakeService>();
        services.AddSingleton<ConnectionClassifier>();
        services.AddSingleton<PeerApprovalGate>();
        services.AddSingleton<BaseToolRegistry>();
        services.AddSingleton<AgentManager>();

        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IWorkspaceFileService, WorkspaceFileService>();
        services.AddScoped<IThreadService, ThreadService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IToolRegistryService, ToolRegistryService>();

        services.AddSingleton<WebSocketHandlerFactory>();

        return services;
    }
}
