using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Stores services in Memory, each under the type argument it is passed as, so later
    /// junctions and <see cref="IChain{TJunction}()"/> can find them. Each type argument must be an
    /// interface the service implements (a class, or a struct value, fails the chain), and one
    /// object may be passed under several interfaces. A Moq mock is stored under the type it mocks.
    /// </summary>
    /// <remarks>
    /// A null service fails the chain with a <see cref="Exceptions.TrainException"/> naming its
    /// type argument and position, like every other invalid argument, and nothing from that call is
    /// stored. While the chain is read at startup, null, a struct or a class type argument is
    /// recorded as a refusal instead, so the argument must already be assigned when
    /// <c>Junctions()</c> runs.
    /// </remarks>
    /// <param name="service">The service to store under <typeparamref name="T1"/>.</param>
    public Monad<TInput, TReturn> AddServices<T1>(T1 service)
    {
        Type[] typeArray = [typeof(T1)];

        return AddServices([service], typeArray);
    }

    /// <summary>
    /// Stores two services in Memory, each under its type argument; see
    /// <see cref="AddServices{T1}(T1)"/> for the rules each one follows.
    /// </summary>
    /// <param name="service1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="service2">The service to store under <typeparamref name="T2"/>.</param>
    public Monad<TInput, TReturn> AddServices<T1, T2>(T1 service1, T2 service2)
    {
        Type[] typeArray = [typeof(T1), typeof(T2)];

        object?[] services = [service1, service2];

        return AddServices(services, typeArray);
    }

    /// <summary>
    /// Stores three services in Memory, each under its type argument; see
    /// <see cref="AddServices{T1}(T1)"/> for the rules each one follows.
    /// </summary>
    /// <param name="service1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="service2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="service3">The service to store under <typeparamref name="T3"/>.</param>
    public Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 service1, T2 service2, T3 service3)
    {
        Type[] typeArray = [typeof(T1), typeof(T2), typeof(T3)];

        object?[] services = [service1, service2, service3];

        return AddServices(services, typeArray);
    }

    /// <summary>
    /// Stores four services in Memory, each under its type argument; see
    /// <see cref="AddServices{T1}(T1)"/> for the rules each one follows.
    /// </summary>
    /// <param name="service1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="service2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="service3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="service4">The service to store under <typeparamref name="T4"/>.</param>
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

    /// <summary>
    /// Stores five services in Memory, each under its type argument; see
    /// <see cref="AddServices{T1}(T1)"/> for the rules each one follows.
    /// </summary>
    /// <param name="service1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="service2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="service3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="service4">The service to store under <typeparamref name="T4"/>.</param>
    /// <param name="service5">The service to store under <typeparamref name="T5"/>.</param>
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

    /// <summary>
    /// Stores six services in Memory, each under its type argument; see
    /// <see cref="AddServices{T1}(T1)"/> for the rules each one follows.
    /// </summary>
    /// <param name="service1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="service2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="service3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="service4">The service to store under <typeparamref name="T4"/>.</param>
    /// <param name="service5">The service to store under <typeparamref name="T5"/>.</param>
    /// <param name="service6">The service to store under <typeparamref name="T6"/>.</param>
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

    /// <summary>
    /// Stores seven services in Memory, each under its type argument; see
    /// <see cref="AddServices{T1}(T1)"/> for the rules each one follows.
    /// </summary>
    /// <param name="service1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="service2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="service3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="service4">The service to store under <typeparamref name="T4"/>.</param>
    /// <param name="service5">The service to store under <typeparamref name="T5"/>.</param>
    /// <param name="service6">The service to store under <typeparamref name="T6"/>.</param>
    /// <param name="service7">The service to store under <typeparamref name="T7"/>.</param>
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
    /// A null service is refused while a chain is recorded and fails the chain when it runs. Either way
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
                    Recorder.RefuseStep(
                        ChainStepKind.Seed,
                        null,
                        NullServiceMessage(i, typeArray, whileRecording: true)
                    );
                else if (services[i]!.GetType().IsValueType)
                    // A struct passed as an interface is boxed, and the runtime refuses it.
                    Recorder.RefuseStep(
                        ChainStepKind.Seed,
                        null,
                        $"AddServices<{serviceType.Name}> received a value of the struct type "
                            + $"{services[i]!.GetType().Name}; a service must be a class."
                    );

                // A value is stored under an interface it implements; the runtime refuses a
                // class on every run.
                if (!serviceType.IsInterface)
                    Recorder.RefuseStep(
                        ChainStepKind.Seed,
                        null,
                        $"AddServices<{serviceType.Name}> names a class; a service is stored "
                            + "under an interface it implements. Pass it as that interface."
                    );

                Recorder.Record(ChainStepKind.Seed, null, null, serviceType);
            }

            return this;
        }

        // Every service is checked before any is stored, so a failed call leaves Memory as it
        // found it.
        for (var i = 0; i < typeArray.Length; i++)
        {
            if (ServiceProblem(services[i], typeArray, i) is { } problem)
            {
                Exception ??= new TrainException(problem);
                return this;
            }
        }

        // Each service goes under the type argument it was passed as, in the same position:
        // one object may be passed under several interfaces, and each is a separate slot the
        // chain can find.
        for (var i = 0; i < typeArray.Length; i++)
        {
            var service = services[i]!;

            // A Moq mock goes under the type it mocks.
            if (service.GetType().IsMoqProxy())
            {
                if (service.GetMockedTypeFromObject() is { } mockedType)
                    Memory[mockedType] = service;

                continue;
            }

            Memory[typeArray[i]] = service;
        }

        return this;
    }

    /// <summary>
    /// Why the service at <paramref name="position"/> cannot be stored, or null when it can.
    /// </summary>
    private static string? ServiceProblem(object? service, Type[] typeArray, int position)
    {
        if (service is null)
            return NullServiceMessage(position, typeArray, whileRecording: false);

        var serviceType = service.GetType();

        if (serviceType.IsMoqProxy())
            return null;

        if (!serviceType.IsClass)
            return $"Params ({serviceType}) to AddServices must be Classes.";

        var slot = typeArray[position];

        if (!slot.IsInterface || !slot.IsInstanceOfType(service))
            return $"Class ({serviceType}) passed to AddServices as ({slot}) must be passed as an "
                + "interface it implements.";

        return null;
    }

    private static string NullServiceMessage(int position, Type[] typeArray, bool whileRecording)
    {
        var received =
            $"AddServices<{typeArray[position].Name}> received null"
            + (
                typeArray.Length > 1
                    ? $" for the service at position {position + 1} of {typeArray.Length}"
                    : ""
            );

        return whileRecording
            ? $"{received} while the chain was being recorded. A service cannot be null, and the "
                + "argument must be available when Junctions() runs; assigning it later, in "
                + "OnStarted say, is not supported."
            : $"{received}. A service cannot be null.";
    }
}
