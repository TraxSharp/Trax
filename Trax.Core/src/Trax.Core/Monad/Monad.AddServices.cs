using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    public Monad<TInput, TReturn> AddServices<T1>(T1 service)
    {
        Type[] typeArray = [typeof(T1)];

        return AddServices([service], typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2>(T1 service1, T2 service2)
    {
        Type[] typeArray = [typeof(T1), typeof(T2)];

        object?[] services = [service1, service2];

        return AddServices(services, typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 service1, T2 service2, T3 service3)
    {
        Type[] typeArray = [typeof(T1), typeof(T2), typeof(T3)];

        object?[] services = [service1, service2, service3];

        return AddServices(services, typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4>(
        T1 service1,
        T2 service2,
        T3 service3,
        T4 service4
    )
    {
        Type[] typeArray = [typeof(T1), typeof(T2), typeof(T3), typeof(T4)];

        object?[] services = [service1, service2, service3, service4];

        return AddServices(services, typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(
        T1 service1,
        T2 service2,
        T3 service3,
        T4 service4,
        T5 service5
    )
    {
        Type[] typeArray = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5)];

        object?[] services = [service1, service2, service3, service4, service5];

        return AddServices(services, typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(
        T1 service1,
        T2 service2,
        T3 service3,
        T4 service4,
        T5 service5,
        T6 service6
    )
    {
        Type[] typeArray = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6)];

        object?[] services = [service1, service2, service3, service4, service5, service6];

        return AddServices(services, typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(
        T1 service1,
        T2 service2,
        T3 service3,
        T4 service4,
        T5 service5,
        T6 service6,
        T7 service7
    )
    {
        Type[] typeArray =
        [
            typeof(T1),
            typeof(T2),
            typeof(T3),
            typeof(T4),
            typeof(T5),
            typeof(T6),
            typeof(T7),
        ];

        object?[] services = [service1, service2, service3, service4, service5, service6, service7];

        return AddServices(services, typeArray);
    }

    /// <summary>
    /// Internal method that adds services to the chain's memory.
    /// </summary>
    /// <remarks>
    /// A null service is refused while a chain is recorded and throws when it runs. Either way
    /// the argument has to exist when <c>Junctions()</c> runs: a field assigned later, in a
    /// lifecycle hook say, is still null when the chain is read at startup.
    /// </remarks>
    internal Monad<TInput, TReturn> AddServices(object?[] services, Type[] typeArray)
    {
        if (Recorder is not null)
        {
            // Each service lands in Memory under the interface it was passed as, so that is the
            // type the rest of the chain can find.
            for (var i = 0; i < typeArray.Length; i++)
            {
                var serviceType = typeArray[i];

                if (services[i] is null)
                    Recorder.Refuse(NullServiceMessage(serviceType, whileRecording: true));

                // A value is stored under an interface it implements; the runtime refuses a
                // class on every run.
                if (!serviceType.IsInterface)
                    Recorder.Refuse(
                        $"AddServices<{serviceType.Name}> names a class; a service is stored "
                            + "under an interface it implements. Pass it as that interface."
                    );

                Recorder.Record(ChainStepKind.Seed, null, null, serviceType);
            }

            return this;
        }

        for (var i = 0; i < typeArray.Length; i++)
            if (services[i] is null)
                throw new Exception(NullServiceMessage(typeArray[i], whileRecording: false));

        foreach (var service in services.OfType<object>())
        {
            var serviceType = service.GetType();

            // Special handling for Moq mock objects
            if (serviceType.IsMoqProxy())
            {
                var mockedType = service.GetMockedTypeFromObject();
                if (mockedType is not null)
                    Memory[mockedType] = service;
                continue;
            }

            // Services must be classes
            if (!serviceType.IsClass)
            {
                Exception ??= new TrainException(
                    $"Params ({serviceType}) to AddServices must be Classes."
                );
                return this;
            }

            // Find the interface that matches the type parameter
            var interfaces = serviceType.GetInterfaces();
            var foundInterface = interfaces.FirstOrDefault(typeArray.Contains);

            if (foundInterface is null)
            {
                Exception ??= new TrainException(
                    $"Class ({serviceType}) does not have any interfaces."
                );
                return this;
            }

            // Store the service by its interface type
            Memory[foundInterface] = service;
        }

        return this;
    }

    private static string NullServiceMessage(Type serviceType, bool whileRecording) =>
        whileRecording
            ? $"AddServices<{serviceType.Name}> received null while the chain was being recorded. "
                + "A service cannot be null, and the argument must be available when Junctions() "
                + "runs; assigning it later, in OnStarted say, is not supported."
            : $"AddServices<{serviceType.Name}> received null. A service cannot be null.";
}
