using Trax.Core.Exceptions;

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

        // Try to get the source object from Memory
        var typeFromMemory = (TIn?)Memory.GetValueOrDefault(typeof(TIn));

        if (typeFromMemory is null)
        {
            Exception ??= new TrainException($"Could not find type: ({typeof(TIn)}).");

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

    private object? GetPropertyValue<TIn, TOut>(TIn input)
    {
        var propertyInfo = input!
            .GetType()
            .GetProperties()
            .FirstOrDefault(x => x.PropertyType == typeof(TOut));

        return propertyInfo?.GetValue(input);
    }

    private object? GetFieldValue<TIn, TOut>(TIn input)
    {
        var fieldInfo = input!
            .GetType()
            .GetFields()
            .FirstOrDefault(x => x.FieldType == typeof(TOut));

        return fieldInfo?.GetValue(input);
    }
}
