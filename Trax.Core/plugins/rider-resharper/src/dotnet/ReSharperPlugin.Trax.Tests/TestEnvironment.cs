using System.Threading;
using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Feature.Services;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.TestFramework;
using JetBrains.TestFramework;
using JetBrains.TestFramework.Application.Zones;
using NUnit.Framework;

[assembly: Apartment(ApartmentState.STA)]

namespace ReSharperPlugin.Trax.Core.Tests
{
    [ZoneDefinition]
    public class Trax.CoreTestEnvironmentZone
        : ITestsEnvZone,
            IRequire<PsiFeatureTestZone>,
            IRequire<ITrax.CoreZone> { }

    [ZoneMarker]
    public class ZoneMarker
        : IRequire<ICodeEditingZone>,
            IRequire<ILanguageCSharpZone>,
            IRequire<Trax.CoreTestEnvironmentZone> { }

    [SetUpFixture]
    public class Trax.CoreTestsAssembly
        : ExtensionTestEnvironmentAssembly<Trax.CoreTestEnvironmentZone> { }
}
