using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Samples.ChatService.Hooks;

public class ChatLifecycleHookFactory(IServiceProvider serviceProvider) : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() =>
        ActivatorUtilities.CreateInstance<ChatLifecycleHook>(serviceProvider);
}
