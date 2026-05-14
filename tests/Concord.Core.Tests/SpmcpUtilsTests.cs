namespace Concord.Core.Tests;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Interop;
using Terminal.Spmcp.Utils;
using Xunit;

public class SpmcpUtilsTests
{
    [Fact]
    public void GetArrayParam_ReturnsArray_WhenValueIsRealJsonArray()
    {
        var p = new JsonObject
        {
            ["values"] = new JsonArray("Draft", "Submitted", "Approved"),
        };
        var result = Utils.GetArrayParam(p, "values");
        result.Should().NotBeNull();
        result!.Count.Should().Be(3);
        result[0]!.ToString().Should().Be("Draft");
    }

    [Fact]
    public void GetArrayParam_ReturnsArray_WhenValueIsStringEncodedJsonArray()
    {
        // The Claude-Code-v2.1-without-schema scenario: array gets stringified.
        // This is the exact shape captured in terminal.log on the user's repro.
        var p = new JsonObject
        {
            ["values"] = "[\"Draft\",\"Submitted\",\"Approved\"]",
        };
        var result = Utils.GetArrayParam(p, "values");
        result.Should().NotBeNull();
        result!.Count.Should().Be(3);
        result[0]!.ToString().Should().Be("Draft");
    }

    [Fact]
    public void GetArrayParam_FallsBackToAlias_WhenCanonicalAbsent()
    {
        var p = new JsonObject
        {
            ["enumeration_values"] = new JsonArray("New", "Confirmed"),
        };
        var result = Utils.GetArrayParam(p, "values", "enumeration_values");
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void GetArrayParam_FallsBackToAliasWithStringEncoded()
    {
        var p = new JsonObject
        {
            ["enum_values"] = "[\"a\",\"b\"]",
        };
        var result = Utils.GetArrayParam(p, "values", "enumeration_values", "enum_values");
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void GetArrayParam_ReturnsNull_WhenAllKeysMissing()
    {
        var p = new JsonObject { ["other"] = "x" };
        Utils.GetArrayParam(p, "values", "enumeration_values").Should().BeNull();
    }

    [Fact]
    public void GetArrayParam_ReturnsNull_WhenStringIsNotArrayShape()
    {
        var p = new JsonObject { ["values"] = "just a string, not JSON" };
        Utils.GetArrayParam(p, "values").Should().BeNull();
    }

    [Fact]
    public void GetArrayParam_ReturnsNull_WhenStringIsUnparseableJson()
    {
        var p = new JsonObject { ["values"] = "[unclosed" };
        Utils.GetArrayParam(p, "values").Should().BeNull();
    }

    [Fact]
    public void GetArrayParam_ReturnsNull_OnNullInput()
    {
        Utils.GetArrayParam(null, "values").Should().BeNull();
    }

    [Fact]
    public void GetArrayParam_AcceptsArrayOfObjects()
    {
        var p = new JsonObject
        {
            ["values"] = new JsonArray(
                new JsonObject { ["name"] = "Draft", ["caption"] = "Draft" },
                new JsonObject { ["name"] = "Done",  ["caption"] = "Completed" }
            ),
        };
        var result = Utils.GetArrayParam(p, "values");
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[0]!["name"]!.ToString().Should().Be("Draft");
    }

    [Fact]
    public void GetArrayParam_AcceptsStringEncodedArrayOfObjects()
    {
        var p = new JsonObject
        {
            ["values"] = "[{\"name\":\"Draft\",\"caption\":\"Draft\"},{\"name\":\"Done\",\"caption\":\"Completed\"}]",
        };
        var result = Utils.GetArrayParam(p, "values");
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[0]!["name"]!.ToString().Should().Be("Draft");
    }

    [Fact]
    public void GetArrayParam_PrefersCanonicalOverAlias_WhenBothPresent()
    {
        var p = new JsonObject
        {
            ["values"] = new JsonArray("canonical"),
            ["enumeration_values"] = new JsonArray("alias"),
        };
        var result = Utils.GetArrayParam(p, "values", "enumeration_values");
        result![0]!.ToString().Should().Be("canonical");
    }

    // -----------------------------------------------------------------------
    // TryPerModule<T> — Phase 2b Task 5 failing tests
    // -----------------------------------------------------------------------

    [Fact]
    public void TryPerModule_HappyPath_ReturnsValueAndLeavesSkippedEmpty()
    {
        var moduleId = new ModuleId(Guid.Empty, "MyFirstModule");
        var skipped = new List<object>();
        var result = Utils.TryPerModule(
            moduleId,
            () => "the-value",
            skipped,
            "TestOp",
            NullLogger.Instance);
        result.Should().Be("the-value");
        skipped.Should().BeEmpty();
    }

    [Fact]
    public void TryPerModule_KeyNotFound_ReturnsDefaultAndRecordsSkip()
    {
        var moduleId = new ModuleId(Guid.Empty, "SystemModule");
        var skipped = new List<object>();
        var result = Utils.TryPerModule<string>(
            moduleId,
            () => throw new KeyNotFoundException("Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy"),
            skipped,
            "ListEnumerations",
            NullLogger.Instance);
        result.Should().BeNull();
        skipped.Should().HaveCount(1);

        // The skip record is an anonymous object — pull the "module" property by reflection.
        var entry = skipped[0]!.GetType().GetProperty("module")!.GetValue(skipped[0]);
        entry!.ToString().Should().Be("SystemModule");
    }

    [Fact]
    public void TryPerModule_OtherException_RethrowsRatherThanSwallowing()
    {
        var moduleId = new ModuleId(Guid.Empty, "MyFirstModule");
        var skipped = new List<object>();
        Action act = () => Utils.TryPerModule<string>(
            moduleId,
            () => throw new InvalidOperationException("unrelated bug"),
            skipped,
            "TestOp",
            NullLogger.Instance);
        act.Should().Throw<InvalidOperationException>().WithMessage("unrelated bug");
        skipped.Should().BeEmpty();
    }
}
