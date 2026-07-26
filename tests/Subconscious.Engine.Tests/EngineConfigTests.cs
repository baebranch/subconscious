using FluentAssertions;
using Subconscious.Engine;

namespace Subconscious.Engine.Tests;

public class EngineConfigTests
{
    [Fact]
    public void DataDirectory_DevMode_HasDevSuffix()
    {
        var prod = new EngineConfig(Dev: false);
        var dev = new EngineConfig(Dev: true);

        dev.DataDirectory.Should().Be(prod.DataDirectory + "-dev");
    }

    [Fact]
    public void Version_IsPrefixedWithV()
    {
        Constants.Version.Should().StartWith("v");
    }
}
