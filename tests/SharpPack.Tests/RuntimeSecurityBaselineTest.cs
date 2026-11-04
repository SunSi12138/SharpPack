using System;
using System.Runtime.InteropServices;

namespace SharpPack.Tests;

public class RuntimeSecurityBaselineTest
{
    [Fact]
    public void TestProcessUsesPatchedNet10Runtime()
    {
        var version = Environment.Version;
        Assert.True(version.Major == 10 && version.Minor == 0 && version.Build >= 14,
            $"Security validation requires .NET 10.0.14 or later 10.0 patch; actual: {version}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}.");
    }
}
