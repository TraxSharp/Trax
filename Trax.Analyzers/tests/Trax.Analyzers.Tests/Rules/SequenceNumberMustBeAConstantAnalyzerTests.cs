using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.SequenceNumberMustBeAConstantAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class SequenceNumberMustBeAConstantAnalyzerTests
{
    private static AnalyzerTest CreateTest()
    {
        var test = new AnalyzerTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddAspNetCore();
        return test;
    }

    [Theory]
    [InlineData("builder.AddAttribute(0, frame: default)")]
    [InlineData("builder.AddAttribute(0, name: default(string))")]
    [InlineData("builder.AddAttribute(0, name: default(string), value: default(Microsoft.AspNetCore.Components.EventCallback))")]
    [InlineData("builder.AddAttribute(0, name: default(string), value: false)")]
    [InlineData("builder.AddAttribute(0, name: default(string), value: default(MulticastDelegate))")]
    [InlineData("builder.AddAttribute(0, name: default(string), value: default(object))")]
    [InlineData("builder.AddAttribute(0, name: default(string), value: default(string))")]
    [InlineData("builder.AddAttribute<int>(0, name: default(string), value: default(Microsoft.AspNetCore.Components.EventCallback<int>))")]
    [InlineData("builder.AddComponentReferenceCapture(0, componentReferenceCaptureAction: null)")]
    [InlineData("builder.AddContent(0, markupContent: default(Microsoft.AspNetCore.Components.MarkupString))")]
    [InlineData("builder.AddContent(0, markupContent: default(Microsoft.AspNetCore.Components.MarkupString?))")]
    [InlineData("builder.AddContent(0, fragment: default(Microsoft.AspNetCore.Components.RenderFragment))")]
    [InlineData("builder.AddContent<int>(0, fragment: default(Microsoft.AspNetCore.Components.RenderFragment<int>), value: default(int))")]
    [InlineData("builder.AddContent(0, textContent: default(object))")]
    [InlineData("builder.AddContent(0, textContent: default(string))")]
    [InlineData("builder.AddElementReferenceCapture(0, elementReferenceCaptureAction: default(Action<Microsoft.AspNetCore.Components.ElementReference>))")]
    [InlineData("builder.AddMarkupContent(0, markupContent: default(string))")]
    [InlineData("builder.AddMultipleAttributes(0, default(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,object>>))")]
    [InlineData("builder.OpenComponent(0, componentType: default(Type))")]
    [InlineData("builder.OpenComponent<Microsoft.AspNetCore.Components.IComponent>(0)")]
    [InlineData("builder.OpenElement(0, elementName: default(string))")]
    [InlineData("builder.OpenRegion(0)")]
    [InlineData("builder.CloseRegion()")]
    [InlineData("builder.AddEventStopPropagationAttribute(0, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventPreventDefaultAttribute(0, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventPreventDefaultAttribute(param, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventPreventDefaultAttribute((int)(long)param, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventPreventDefaultAttribute((int)longparam, eventName: default(string), value: false)")]
    [InlineData("builder.AddContent(textContent: \"a\" + param, sequence: 0)")]
    [InlineData("builder.AddEventPreventDefaultAttribute(eventName: \"a\" + param, sequence: 0, value: false)")]
    public Task Valid(string code)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            using System;
            using Microsoft.AspNetCore.Components.Rendering;
            using Microsoft.AspNetCore.Components.Web;
            class Test
            {
                void BuildRenderTree(RenderTreeBuilder builder, int param, long longparam)
                {
                    {{code}};
                }
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, frame: default)")]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, name: default(string))")]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, name: default(string), value: default(Microsoft.AspNetCore.Components.EventCallback))")]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, name: default(string), value: false)")]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, name: default(string), value: default(MulticastDelegate))")]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, name: default(string), value: default(object))")]
    [InlineData("builder.AddAttribute({|TRX0123:value++|}, name: default(string), value: default(string))")]
    [InlineData("builder.AddAttribute<int>({|TRX0123:value++|}, name: default(string), value: default(Microsoft.AspNetCore.Components.EventCallback<int>))")]
    [InlineData("builder.AddComponentReferenceCapture({|TRX0123:value++|}, componentReferenceCaptureAction: null)")]
    [InlineData("builder.AddContent({|TRX0123:value++|}, markupContent: default(Microsoft.AspNetCore.Components.MarkupString))")]
    [InlineData("builder.AddContent({|TRX0123:value++|}, markupContent: default(Microsoft.AspNetCore.Components.MarkupString?))")]
    [InlineData("builder.AddContent({|TRX0123:value++|}, fragment: default(Microsoft.AspNetCore.Components.RenderFragment))")]
    [InlineData("builder.AddContent<int>({|TRX0123:value++|}, fragment: default(Microsoft.AspNetCore.Components.RenderFragment<int>), value: 0)")]
    [InlineData("builder.AddContent({|TRX0123:value++|}, textContent: default(object))")]
    [InlineData("builder.AddContent({|TRX0123:value++|}, textContent: default(string))")]
    [InlineData("builder.AddElementReferenceCapture({|TRX0123:value++|}, elementReferenceCaptureAction: default(Action<Microsoft.AspNetCore.Components.ElementReference>))")]
    [InlineData("builder.AddMarkupContent({|TRX0123:value++|}, markupContent: default(string))")]
    [InlineData("builder.AddMultipleAttributes({|TRX0123:value++|}, attributes: default(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,object>>))")]
    [InlineData("builder.OpenComponent({|TRX0123:value++|}, componentType: default(Type))")]
    [InlineData("builder.OpenComponent<Microsoft.AspNetCore.Components.IComponent>({|TRX0123:value++|})")]
    [InlineData("builder.OpenElement({|TRX0123:value++|}, elementName: default(string))")]
    [InlineData("builder.OpenRegion({|TRX0123:value++|})")]
    [InlineData("builder.AddEventPreventDefaultAttribute({|TRX0123:value++|}, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventStopPropagationAttribute({|TRX0123:value++|}, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventStopPropagationAttribute({|TRX0123:param++|}, eventName: default(string), value: false)")]
    [InlineData("builder.AddEventStopPropagationAttribute({|TRX0123:(int)longparam++|}, eventName: default(string), value: false)")]
    [InlineData("builder.AddContent(textContent: default(string), sequence: {|TRX0123:value++|})")]
    [InlineData("builder.AddEventStopPropagationAttribute(eventName: default(string), sequence: {|TRX0123:value++|}, value: false)")]
    public Task Variable(string code)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            using System;
            using Microsoft.AspNetCore.Components.Rendering;
            using Microsoft.AspNetCore.Components.Web;
            class Test
            {
                void BuildRenderTree(RenderTreeBuilder builder, int param, long longparam)
                {
                    int value = 0;
                    {{code}};
                }
            }
            """;

        return test.RunAsync();
    }
}
