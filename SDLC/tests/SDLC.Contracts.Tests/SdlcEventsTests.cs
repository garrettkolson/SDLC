using FluentAssertions;
using SDLC.Contracts;
using NUnit.Framework;

namespace SDLC.Contracts.Tests;

public class SdlcEventsTests
{
    [Test]
    public void AllEventConstants_AreNonNullOrWhitespace()
    {
        var constants = typeof(SdlcEvents)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        constants.Should().NotBeEmpty();
        constants.Should().AllSatisfy(c => c.Should().NotBeNullOrWhiteSpace());
    }

    [Test]
    public void AllEventConstants_AreUnique()
    {
        var constants = typeof(SdlcEvents)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        constants.Should().OnlyHaveUniqueItems();
    }

    [TestCase(nameof(SdlcEvents.RunStarted))]
    [TestCase(nameof(SdlcEvents.RunComplete))]
    [TestCase(nameof(SdlcEvents.GatePending))]
    [TestCase(nameof(SdlcEvents.GateApproved))]
    [TestCase(nameof(SdlcEvents.GateRejected))]
    [TestCase(nameof(SdlcEvents.ResearchComplete))]
    [TestCase(nameof(SdlcEvents.RequirementsComplete))]
    [TestCase(nameof(SdlcEvents.DesignComplete))]
    [TestCase(nameof(SdlcEvents.BuildComplete))]
    [TestCase(nameof(SdlcEvents.LearnComplete))]
    public void SdlcEvents_RequiredConstant_Exists(string constantName)
    {
        var field = typeof(SdlcEvents).GetField(constantName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        field.Should().NotBeNull($"{constantName} must exist on SdlcEvents");
    }
}
