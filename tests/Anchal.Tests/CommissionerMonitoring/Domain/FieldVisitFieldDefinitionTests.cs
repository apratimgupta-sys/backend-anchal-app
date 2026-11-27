using System.Text.Json;
using Anchal.Domain.CommissionerMonitoring.Rules;
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
                Required: f.GetProperty("required").GetBoolean(),
                Integer: f.TryGetProperty("integer", out var i) && i.GetBoolean(),
                Options: f.TryGetProperty("options", out var o) ? o.EnumerateArray().Select(x => x.GetString()!).ToArray() : null)).ToArray();
            var actual = MonitoringFieldDefinitions.For(id).Select(f => (f.Key, f.Type, f.Required, f.Integer, Options: f.Options?.ToArray())).ToArray();
            Assert.Equal(expected.Select(e => (e.Key, e.Type, e.Required, e.Integer)), actual.Select(a => (a.Key, a.Type, a.Required, a.Integer)));
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
}
