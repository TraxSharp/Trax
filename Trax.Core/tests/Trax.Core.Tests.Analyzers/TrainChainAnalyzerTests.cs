using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using Trax.Core.Analyzers;

namespace Trax.Core.Tests.Analyzers;

/// <summary>
/// Tests for the TrainChainAnalyzer covering Phase 1 (basic chain validation)
/// and Phase 2 (tuple decomposition, interface resolution).
/// </summary>
[TestFixture]
public class TrainChainAnalyzerTests
{
    /// <summary>
    /// Minimal stub types that mirror Trax.Core's type structure.
    /// The analyzer matches by name + namespace, so these stubs are sufficient.
    /// </summary>
    private const string StubTypes =
        @"
namespace LanguageExt
{
    public struct Unit
    {
        public static readonly Unit Default = new Unit();
    }
}

namespace Trax.Core.Junction
{
    public interface IJunction<TIn, TOut> { }
}

namespace Trax.Core.Train
{
    public class Train<TInput, TReturn>
    {
        public Trax.Core.Monad.Monad<TInput, TReturn> Activate(TInput input, params object[] otherInputs)
            => new Trax.Core.Monad.Monad<TInput, TReturn>();
    }
}

namespace Trax.Core.Monad
{
    public class Monad<TInput, TReturn>
    {
        public Monad<TInput, TReturn> Chain<TJunction>() where TJunction : class => this;
        public Monad<TInput, TReturn> Chain<TJunction, TIn, TOut>() where TJunction : Trax.Core.Junction.IJunction<TIn, TOut> => this;
        public Monad<TInput, TReturn> AddServices<T1>() => this;
        public Monad<TInput, TReturn> AddServices<T1, T2>() => this;
        public Monad<TInput, TReturn> IChain<TJunction>() where TJunction : class => this;
        public Monad<TInput, TReturn> ShortCircuit<TJunction>() where TJunction : class => this;
        public Monad<TInput, TReturn> Extract<TIn, TOut>() => this;
        public TReturn Resolve() => default!;
    }
}
";

    private static CSharpAnalyzerTest<TrainChainAnalyzer, DefaultVerifier> CreateTest(
        string testSource,
        params DiagnosticResult[] expected
    )
    {
        var test = new CSharpAnalyzerTest<TrainChainAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    // ──────────────────────────────────────────────
    // Phase 1: Basic chain validation
    // ──────────────────────────────────────────────

    [Test]
    public async Task BasicChain_TypesFlowCorrectly_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class OrderRequest { }
    public class OrderResult { }

    public class ProcessOrderJunction : Trax.Core.Junction.IJunction<OrderRequest, OrderResult> { }

    public class TestTrain : Trax.Core.Train.Train<OrderRequest, OrderResult>
    {
        public void Run(OrderRequest input)
        {
            Activate(input)
                .Chain<ProcessOrderJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task MissingInputType_Reports_CHAIN001()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class Intermediate { }
    public class NeedsSpecial { }

    public class JunctionA : Trax.Core.Junction.IJunction<MyInput, Intermediate> { }
    public class JunctionB : Trax.Core.Junction.IJunction<NeedsSpecial, LanguageExt.Unit> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, LanguageExt.Unit>
    {
        public void Run(MyInput input)
        {
            Activate(input)
                .Chain<JunctionA>()
                .{|#0:Chain<JunctionB>()|}
                .Resolve();
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN001", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("JunctionB", "NeedsSpecial", "Unit, MyInput, Intermediate");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    [Test]
    public async Task MissingReturnType_Reports_CHAIN002()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class OrderRequest { }
    public class Receipt { }
    public class Validated { }

    public class ValidateJunction : Trax.Core.Junction.IJunction<OrderRequest, Validated> { }

    public class TestTrain : Trax.Core.Train.Train<OrderRequest, Receipt>
    {
        public void Run(OrderRequest input)
        {
            Activate(input)
                .Chain<ValidateJunction>()
                .{|#0:Resolve()|};
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN002", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("Receipt", "Unit, OrderRequest, Validated");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    // ──────────────────────────────────────────────
    // Phase 2: Tuple validation
    // ──────────────────────────────────────────────

    [Test]
    public async Task TupleInput_AllComponentsPresent_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class User { }
    public class Order { }
    public class Combined { }

    public class ProduceUserJunction : Trax.Core.Junction.IJunction<string, User> { }
    public class ProduceOrderJunction : Trax.Core.Junction.IJunction<User, Order> { }
    public class CombineJunction : Trax.Core.Junction.IJunction<(User, Order), Combined> { }

    public class TestTrain : Trax.Core.Train.Train<string, Combined>
    {
        public void Run(string input)
        {
            Activate(input)
                .Chain<ProduceUserJunction>()
                .Chain<ProduceOrderJunction>()
                .Chain<CombineJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task TupleInput_ComponentMissing_Reports_CHAIN001()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class User { }
    public class Order { }
    public class Combined { }

    public class ProduceUserJunction : Trax.Core.Junction.IJunction<MyInput, User> { }
    // Note: no junction produces Order
    public class CombineJunction : Trax.Core.Junction.IJunction<(User, Order), Combined> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, Combined>
    {
        public void Run(MyInput input)
        {
            Activate(input)
                .Chain<ProduceUserJunction>()
                .{|#0:Chain<CombineJunction>()|}
                .Resolve();
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN001", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("CombineJunction", "(User, Order)", "Unit, MyInput, User");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    [Test]
    public async Task TupleOutput_DecomposesComponents_AvailableDownstream()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class User { }
    public class Order { }

    public class ProducePairJunction : Trax.Core.Junction.IJunction<string, (User, Order)> { }
    public class ConsumeUserJunction : Trax.Core.Junction.IJunction<User, LanguageExt.Unit> { }

    public class TestTrain : Trax.Core.Train.Train<string, LanguageExt.Unit>
    {
        public void Run(string input)
        {
            Activate(input)
                .Chain<ProducePairJunction>()
                .Chain<ConsumeUserJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    // ──────────────────────────────────────────────
    // Phase 2: Interface resolution
    // ──────────────────────────────────────────────

    [Test]
    public async Task InterfaceInput_ConcreteImplementsInterface_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public interface IUser { }
    public class ConcreteUser : IUser { }
    public class Result { }

    public class ProduceUserJunction : Trax.Core.Junction.IJunction<string, ConcreteUser> { }
    public class ConsumeInterfaceJunction : Trax.Core.Junction.IJunction<IUser, Result> { }

    public class TestTrain : Trax.Core.Train.Train<string, Result>
    {
        public void Run(string input)
        {
            Activate(input)
                .Chain<ProduceUserJunction>()
                .Chain<ConsumeInterfaceJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task InterfaceInput_NoImplementor_Reports_CHAIN001()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public interface IUser { }
    public class MyInput { }
    public class UnrelatedType { }
    public class Result { }

    public class ProduceUnrelatedJunction : Trax.Core.Junction.IJunction<MyInput, UnrelatedType> { }
    public class ConsumeInterfaceJunction : Trax.Core.Junction.IJunction<IUser, Result> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, Result>
    {
        public void Run(MyInput input)
        {
            Activate(input)
                .Chain<ProduceUnrelatedJunction>()
                .{|#0:Chain<ConsumeInterfaceJunction>()|}
                .Resolve();
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN001", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("ConsumeInterfaceJunction", "IUser", "Unit, MyInput, UnrelatedType");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    // ──────────────────────────────────────────────
    // Phase 2: Tuple return type validation
    // ──────────────────────────────────────────────

    [Test]
    public async Task TupleReturnType_AllComponentsPresent_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class User { }
    public class Order { }

    public class ProduceUserJunction : Trax.Core.Junction.IJunction<string, User> { }
    public class ProduceOrderJunction : Trax.Core.Junction.IJunction<User, Order> { }

    public class TestTrain : Trax.Core.Train.Train<string, (User, Order)>
    {
        public void Run(string input)
        {
            Activate(input)
                .Chain<ProduceUserJunction>()
                .Chain<ProduceOrderJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task TupleReturnType_ComponentMissing_Reports_CHAIN002()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class User { }
    public class Order { }

    public class ProduceUserJunction : Trax.Core.Junction.IJunction<MyInput, User> { }
    // Note: no junction produces Order

    public class TestTrain : Trax.Core.Train.Train<MyInput, (User, Order)>
    {
        public void Run(MyInput input)
        {
            Activate(input)
                .Chain<ProduceUserJunction>()
                .{|#0:Resolve()|};
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN002", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("(User, Order)", "Unit, MyInput, User");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    // ──────────────────────────────────────────────
    // Additional method tracking
    // ──────────────────────────────────────────────

    [Test]
    public async Task AddServices_TypesAvailableDownstream_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public interface IRepository { }
    public class Result { }

    public class ConsumeRepoJunction : Trax.Core.Junction.IJunction<IRepository, Result> { }

    public class TestTrain : Trax.Core.Train.Train<string, Result>
    {
        public void Run(string input)
        {
            Activate(input)
                .AddServices<IRepository>()
                .Chain<ConsumeRepoJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task IChain_TracksTOut_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public interface IProduceResult : Trax.Core.Junction.IJunction<string, Result> { }
    public class Result { }

    public class ConsumeResultJunction : Trax.Core.Junction.IJunction<Result, LanguageExt.Unit> { }

    public class TestTrain : Trax.Core.Train.Train<string, LanguageExt.Unit>
    {
        public void Run(string input)
        {
            Activate(input)
                .IChain<IProduceResult>()
                .Chain<ConsumeResultJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task Extract_TracksTOut_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class Container { }
    public class Inner { }

    public class ProduceContainerJunction : Trax.Core.Junction.IJunction<string, Container> { }
    public class ConsumeInnerJunction : Trax.Core.Junction.IJunction<Inner, LanguageExt.Unit> { }

    public class TestTrain : Trax.Core.Train.Train<string, LanguageExt.Unit>
    {
        public void Run(string input)
        {
            Activate(input)
                .Chain<ProduceContainerJunction>()
                .Extract<Container, Inner>()
                .Chain<ConsumeInnerJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    // ──────────────────────────────────────────────
    // Phase 3: ShortCircuit tracking
    // ──────────────────────────────────────────────

    [Test]
    public async Task ShortCircuit_TypesFlowCorrectly_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class OrderRequest { }
    public class OrderResult { }

    public class ProcessOrderJunction : Trax.Core.Junction.IJunction<OrderRequest, OrderResult> { }

    public class TestTrain : Trax.Core.Train.Train<OrderRequest, OrderResult>
    {
        public void Run(OrderRequest input)
        {
            Activate(input)
                .ShortCircuit<ProcessOrderJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task ShortCircuit_MissingInputType_Reports_CHAIN001()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class NeedsSpecial { }
    public class Result { }

    public class BadJunction : Trax.Core.Junction.IJunction<NeedsSpecial, Result> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, Result>
    {
        public void Run(MyInput input)
        {
            Activate(input)
                .{|#0:ShortCircuit<BadJunction>()|}
                .Resolve();
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN001", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("BadJunction", "NeedsSpecial", "Unit, MyInput");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    [Test]
    public async Task ShortCircuit_ProvidesTReturn_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class OrderRequest { }
    public class Validated { }
    public class Receipt { }

    public class ValidateJunction : Trax.Core.Junction.IJunction<OrderRequest, Validated> { }
    public class CacheCheckJunction : Trax.Core.Junction.IJunction<OrderRequest, Receipt> { }

    public class TestTrain : Trax.Core.Train.Train<OrderRequest, Receipt>
    {
        public void Run(OrderRequest input)
        {
            Activate(input)
                .ShortCircuit<CacheCheckJunction>()
                .Chain<ValidateJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task ShortCircuit_DoesNotProvideTReturn_Reports_CHAIN002()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class OrderRequest { }
    public class Intermediate { }
    public class Receipt { }

    public class MissJunction : Trax.Core.Junction.IJunction<OrderRequest, Intermediate> { }

    public class TestTrain : Trax.Core.Train.Train<OrderRequest, Receipt>
    {
        public void Run(OrderRequest input)
        {
            Activate(input)
                .ShortCircuit<MissJunction>()
                .{|#0:Resolve()|};
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN002", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("Receipt", "Unit, OrderRequest, Intermediate");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }

    [Test]
    public async Task ShortCircuit_TOutAvailableForDownstreamChain_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class OrderRequest { }
    public class CachedData { }
    public class FinalResult { }

    public class CacheJunction : Trax.Core.Junction.IJunction<OrderRequest, CachedData> { }
    public class ProcessJunction : Trax.Core.Junction.IJunction<CachedData, FinalResult> { }

    public class TestTrain : Trax.Core.Train.Train<OrderRequest, FinalResult>
    {
        public void Run(OrderRequest input)
        {
            Activate(input)
                .ShortCircuit<CacheJunction>()
                .Chain<ProcessJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task ShortCircuit_WithTupleOutput_DecomposesComponents()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class User { }
    public class Order { }

    public class ProducePairJunction : Trax.Core.Junction.IJunction<string, (User, Order)> { }
    public class ConsumeUserJunction : Trax.Core.Junction.IJunction<User, LanguageExt.Unit> { }

    public class TestTrain : Trax.Core.Train.Train<string, LanguageExt.Unit>
    {
        public void Run(string input)
        {
            Activate(input)
                .ShortCircuit<ProducePairJunction>()
                .Chain<ConsumeUserJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    // ──────────────────────────────────────────────
    // Activate other inputs
    // ──────────────────────────────────────────────

    [Test]
    public async Task Activate_OtherInputs_TypeAvailableForResolve_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, TestTrain>
    {
        public void Run(MyInput input)
        {
            Activate(input, this)
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task Activate_OtherInputs_SatisfiesJunctionInput_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class ExtraService { }
    public class Result { }

    public class NeedsServiceJunction : Trax.Core.Junction.IJunction<ExtraService, Result> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, Result>
    {
        public void Run(MyInput input, ExtraService svc)
        {
            Activate(input, svc)
                .Chain<NeedsServiceJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task Activate_OtherInputs_InterfacesTracked_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public interface IService { }
    public class ConcreteService : IService { }
    public class Result { }

    public class NeedsInterfaceJunction : Trax.Core.Junction.IJunction<IService, Result> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, Result>
    {
        public void Run(MyInput input, ConcreteService svc)
        {
            Activate(input, svc)
                .Chain<NeedsInterfaceJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task Activate_MultipleOtherInputs_AllTracked_NoDiagnostics()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class ServiceA { }
    public class ServiceB { }
    public class Result { }

    public class NeedsBothJunction : Trax.Core.Junction.IJunction<(ServiceA, ServiceB), Result> { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, Result>
    {
        public void Run(MyInput input, ServiceA a, ServiceB b)
        {
            Activate(input, a, b)
                .Chain<NeedsBothJunction>()
                .Resolve();
        }
    }
}";

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Test]
    public async Task Activate_NoOtherInputs_MissingType_StillReports_CHAIN002()
    {
        var source =
            StubTypes
            + @"
namespace TestApp
{
    public class MyInput { }
    public class MissingType { }

    public class TestTrain : Trax.Core.Train.Train<MyInput, MissingType>
    {
        public void Run(MyInput input)
        {
            Activate(input)
                .{|#0:Resolve()|};
        }
    }
}";

        var expected = new DiagnosticResult("CHAIN002", DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("MissingType", "Unit, MyInput");

        var test = CreateTest(source, expected);
        await test.RunAsync();
    }
}
