using System.Reflection;
using Trax.Core.Exceptions;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Extracts a value of type TOut from an object of type TIn in Memory.
    /// </summary>
    public Monad<TInput, TReturn> Extract<TIn, TOut>()
    {
        if (Recorder is not null)
        {
            Recorder.Record(ChainStepKind.Extract, null, typeof(TIn), typeof(TOut));
            return this;
        }

        // A resumed run skips an extraction before its resume point with the step it reads.
        if (Resuming is not null)
            return this;

        // Try to get the source object from Memory
        if (!Memory.TryGetValue(typeof(TIn), out var stored) || stored is not TIn typeFromMemory)
        {
            Exception ??= new TrainException(
                $"Extract<{typeof(TIn).ReadableName()}, {typeof(TOut).ReadableName()}> (train "
                    + $"'{Train.GetType().ReadableName()}') found no '{typeof(TIn).ReadableName()}' "
                    + $"in Memory. Chain a junction that outputs '{typeof(TIn).ReadableName()}' "
                    + "before the Extract."
            );

            return this;
        }

        return Extract<TIn, TOut>(typeFromMemory);
    }

    /// <summary>
    /// Extracts a value of type TOut from the provided TIn object.
    /// </summary>
    public Monad<TInput, TReturn> Extract<TIn, TOut>(TIn input)
    {
        if (Recorder is not null)
        {
            Recorder.Record(ChainStepKind.Seed, null, null, typeof(TOut));
            return this;
        }

        if (input is null)
        {
            Exception ??= new TrainException(
                $"Null value for type: ({typeof(TIn)}) passed to Extract function."
            );
            return this;
        }

        // Try to get a property or field of type TOut
        var value = GetPropertyValue<TIn, TOut>(input) ?? GetFieldValue<TIn, TOut>(input);

        if (value is null)
        {
            Exception ??= new TrainException(
                $"Could not find non-null value of type: ({typeof(TOut)}) in properties or fields for ({typeof(TIn)}). Is it public?"
            );
            return this;
        }

        // Store the extracted value in Memory
        Memory[typeof(TOut)] = value;
        return this;
    }

    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.Instance;

    private object? GetPropertyValue<TIn, TOut>(TIn input)
    {
        // Instance properties only: a static member of the same type is not this object's value,
        // and an indexer cannot be read without arguments.
        var propertyInfo = input!
            .GetType()
            .GetProperties(InstanceMembers)
            .FirstOrDefault(x =>
                x.PropertyType == typeof(TOut) && x.GetIndexParameters().Length == 0
            );

        return propertyInfo?.GetValue(input);
    }

    private object? GetFieldValue<TIn, TOut>(TIn input)
    {
        var fieldInfo = input!
            .GetType()
            .GetFields(InstanceMembers)
            .FirstOrDefault(x => x.FieldType == typeof(TOut));

        return fieldInfo?.GetValue(input);
    }
}
