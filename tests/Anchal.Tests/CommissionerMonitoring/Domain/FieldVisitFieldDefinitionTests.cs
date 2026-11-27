using System.Text.Json;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Officers;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// The per-service field visit definitions are a hand-kept twin of the frontend's
/// src/shared/field-visit-fields.json. The golden fixture (shape-twin.fixture.json,
/// "fieldVisitFields") is committed identically in both repos; the frontend asserts
/// its own file against it (shape-twin.test.ts), this asserts the table here, so a
/// one-sided edit goes red in the repo where it was made. Labels and Hindi are
/// presentation and stay in the frontend.
public sealed class FieldVisitFieldDefinitionTests
{
    private static JsonElement Golden()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shape-twin.fixture.json")));
        return doc.RootElement.GetProperty("fieldVisitFields").Clone();
    }

    [Fact]
    public void TheDefinitionsMatchTheGoldenTwinFixture()
    {
        var golden = Golden();
        Assert.Equal(8, golden.EnumerateObject().Count());

        foreach (var entry in golden.EnumerateObject())
        {
            var id = int.Parse(entry.Name);
            var expected = entry.Value.EnumerateArray().Select(f => (
                Key: f.GetProperty("key").GetString()!,
                Type: f.GetProperty("type").GetString()!,
                Integer: f.TryGetProperty("integer", out var i) && i.GetBoolean(),
                Options: f.TryGetProperty("options", out var o) ? o.EnumerateArray().Select(x => x.GetString()!).ToArray() : null)).ToArray();
            var actual = MonitoringFieldDefinitions.For(id).Select(f => (f.Key, f.Type, f.Integer, Options: f.Options?.ToArray())).ToArray();
            Assert.Equal(expected.Select(e => (e.Key, e.Type, e.Integer)), actual.Select(a => (a.Key, a.Type, a.Integer)));
            Assert.Equal(expected.Select(e => e.Options), actual.Select(a => a.Options));
        }
    }

    [Fact]
    public void NoServiceOutsideTheFixtureHasDefinitions()
    {
        var golden = Golden().EnumerateObject().Select(p => int.Parse(p.Name)).ToHashSet();
        for (var id = -1; id <= 30; id++)
            if (!golden.Contains(id)) Assert.Empty(MonitoringFieldDefinitions.For(id));
    }

    // The limits a details value is held to are held twice: here as constants, and in the frontend as
    // field-visit-bounds.json, which its inputs' maxLength/max and the key of its Hindi length messages
    // read. The fixture is the one place both are asserted, so moving either alone goes red.
    [Fact]
    public void TheDetailsBoundsMatchTheGoldenTwinFixture()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shape-twin.fixture.json")));
        var bounds = doc.RootElement.GetProperty("fieldVisitBounds");
        Assert.Equal(MonitoringFieldVisitService.DetailsTextMaximumLength, bounds.GetProperty("textMax").GetInt32());
        Assert.Equal(MonitoringFieldVisitService.DetailsTextAreaMaximumLength, bounds.GetProperty("textAreaMax").GetInt32());
        Assert.Equal(MonitoringFieldVisitService.DetailsNumberMaximum, bounds.GetProperty("numberMax").GetInt64());
        Assert.Equal(3, bounds.EnumerateObject().Count());
    }

    // M3: a Required flag was carried through the table, the fixture and both twin tests and read by
    // nothing. Every details field is optional, by the client's ruling, and nothing says otherwise.
    [Fact]
    public void NoFieldInTheFixtureCarriesARequiredFlag()
    {
        foreach (var service in Golden().EnumerateObject())
            foreach (var field in service.Value.EnumerateArray())
                Assert.False(field.TryGetProperty("required", out _), $"{service.Name}.{field.GetProperty("key").GetString()}");
        Assert.DoesNotContain(typeof(VisitFieldDefinition).GetProperties(), p => p.Name == "Required");
    }

    // M4's other half: the table itself. A select with no options would be refused on every value, and
    // an option list on anything but a select would be ignored, so neither may be in the table.
    [Fact]
    public void EverySelectHasTwoOrMoreDistinctOptionsAndNothingElseHasAny()
    {
        for (var id = 1; id <= 25; id++)
            foreach (var field in MonitoringFieldDefinitions.For(id))
            {
                if (field.Type == "select")
                {
                    Assert.NotNull(field.Options);
                    Assert.True(field.Options!.Count >= 2, $"{id}.{field.Key}");
                    Assert.Equal(field.Options.Count, field.Options.Distinct().Count());
                }
                else Assert.Null(field.Options);
            }
    }
}
