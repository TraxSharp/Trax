using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    public Monad<TInput, TReturn> AddServices<T1>(T1 service)
    {
        Type[] typeArray = [typeof(T1)];

        if (service is null)
            throw new Exception($"({service}) cannot be null");

        return AddServices([service], typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2>(T1 service1, T2 service2)
    {
        Type[] typeArray = [typeof(T1), typeof(T2)];

        if (service1 is null)
            throw new Exception($"({service1}) cannot be null");

        if (service2 is null)
            throw new Exception($"({service2}) cannot be null");

        object[] services = [service1, service2];

        return AddServices(services, typeArray);
    }

    public Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 service1, T2 service2, T3 service3)
    {
        Type[] typeArray = [typeof(T1), typeof(T2), typeof(T3)];
        if (service1 is null)
            throw new Exception($"({service1}) cannot be null");

        if (service2 is null)
            throw new Exception($"({service2}) cannot be null");

        if (service3 is null)
            throw new Exception($"({service3}) cannot be null");

        object[] services = [service1, service2, service3];

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
        if (service1 is null)
            throw new Exception($"({service1}) cannot be null");

        if (service2 is null)
            throw new Exception($"({service2}) cannot be null");

        if (service3 is null)
            throw new Exception($"({service3}) cannot be null");

        if (service4 is null)
            throw new Exception($"({service4}) cannot be null");

        object[] services = [service1, service2, service3, service4];

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
        if (service1 is null)
            throw new Exception($"({service1}) cannot be null");

        if (service2 is null)
            throw new Exception($"({service2}) cannot be null");

        if (service3 is null)
            throw new Exception($"({service3}) cannot be null");

        if (service4 is null)
            throw new Exception($"({service4}) cannot be null");

        if (service5 is null)
            throw new Exception($"({service5}) cannot be null");

        object[] services = [service1, service2, service3, service4, service5];

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
        if (service1 is null)
            throw new Exception($"({service1}) cannot be null");

        if (service2 is null)
            throw new Exception($"({service2}) cannot be null");

        if (service3 is null)
            throw new Exception($"({service3}) cannot be null");

        if (service4 is null)
            throw new Exception($"({service4}) cannot be null");

        if (service5 is null)
            throw new Exception($"({service5}) cannot be null");

        if (service6 is null)
            throw new Exception($"({service6}) cannot be null");

        object[] services = [service1, service2, service3, service4, service5, service6];

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
        if (service1 is null)
            throw new Exception($"({service1}) cannot be null");

        if (service2 is null)
            throw new Exception($"({service2}) cannot be null");

        if (service3 is null)
            throw new Exception($"({service3}) cannot be null");

        if (service4 is null)
            throw new Exception($"({service4}) cannot be null");

        if (service5 is null)
            throw new Exception($"({service5}) cannot be null");

        if (service6 is null)
            throw new Exception($"({service6}) cannot be null");

        if (service7 is null)
            throw new Exception($"({service7}) cannot be null");

        object[] services = [service1, service2, service3, service4, service5, service6, service7];

        return AddServices(services, typeArray);
    }

    /// <summary>
    /// Internal method that adds services to the chain's memory.
    /// </summary>
    internal Monad<TInput, TReturn> AddServices(object[] services, Type[] typeArray)
    {
        foreach (var service in services)
        {
            var serviceType = service.GetType();

            // Special handling for Moq mock objects
            if (serviceType.IsMoqProxy())
            {
                var mockedType = service.GetMockedTypeFromObject();
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
}
