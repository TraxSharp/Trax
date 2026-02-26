using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using LanguageExt;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    private static readonly ConcurrentDictionary<Type, Type[]> InterfaceCache = new();

    /// <summary>
    /// Activates the monad by storing the input and additional objects in the Memory dictionary.
    /// This is typically the first method called after constructing a Monad.
    /// </summary>
    /// <param name="input">The primary input for the chain</param>
    /// <param name="otherInputs">Additional objects to store in Memory</param>
    /// <returns>The Monad instance for method chaining</returns>
    public Monad<TInput, TReturn> Activate(TInput input, params object[] otherInputs)
    {
        // Validate input is not null
        if (input is null)
        {
            Exception ??= new WorkflowException($"Input ({typeof(TInput)}) is null.");
            return this;
        }

        var inputType = input.GetType();

        // Handle tuple inputs differently by extracting each element
        if (inputType.IsTuple())
            this.AddTupleToMemory(input);
        else
        {
            // Store by concrete type
            Memory[inputType] = input;

            // Also store by all interfaces for interface-based retrieval
            var interfaces = InterfaceCache.GetOrAdd(inputType, t => t.GetInterfaces());
            foreach (var foundInterface in interfaces)
                Memory[foundInterface] = input;
        }

        // Process additional inputs
        foreach (var otherInput in otherInputs)
        {
            var otherType = otherInput.GetType();

            if (otherType.IsTuple())
                this.AddTupleToMemory((ITuple)otherInput);
            else
            {
                Memory[otherType] = otherInput;

                var interfaces = InterfaceCache.GetOrAdd(otherType, t => t.GetInterfaces());
                foreach (var foundInterface in interfaces)
                    Memory[foundInterface] = otherInput;
            }
        }

        return this;
    }
}
