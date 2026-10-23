using System.Text.Json;
using WITS.Api.Common;
using Xunit;

namespace WITS.Api.EndpointTests;

public sealed class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new UtcDateTimeJsonConverter() }
    };

    [Fact]
    public void Unspecified_values_are_serialized_as_utc()
    {
        var value = new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal("\"2026-10-23T09:30:00Z\"", JsonSerializer.Serialize(value, Options));
    }

    [Fact]
    public void Offsetless_input_is_treated_as_utc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-10-23T09:30:00\"", Options);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Utc), value);
    }

    [Fact]
    public void Offset_input_is_converted_to_utc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-10-23T11:30:00+02:00\"", Options);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Utc), value);
    }
}
