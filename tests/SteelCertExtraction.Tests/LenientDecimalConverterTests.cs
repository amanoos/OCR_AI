using System.Text.Json;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class LenientDecimalConverterTests
{
    private sealed class Holder
    {
        public decimal? V { get; set; }
        public int W { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LenientDecimalConverter() },
    };

    private static Holder Parse(string json) => JsonSerializer.Deserialize<Holder>(json, Options)!;

    [Theory]
    [InlineData("""{"V":0.074}""", 0.074)]
    [InlineData("""{"V":18}""", 18)]
    [InlineData("""{"V":1E3}""", 1000)]
    [InlineData("""{"V":"0.074"}""", 0.074)]
    [InlineData("""{"V":" 0.074 "}""", 0.074)]
    [InlineData("""{"V":"0.074%"}""", 0.074)]
    [InlineData("""{"V":"-0.5"}""", -0.5)]
    public void Numbers_and_plain_numeric_strings_are_read(string json, double expected)
    {
        Assert.Equal((decimal)expected, Parse(json).V);
    }

    [Theory]
    [InlineData("""{"V":"<0.001"}""")]
    [InlineData("""{"V":"ND"}""")]
    [InlineData("""{"V":"-"}""")]
    [InlineData("""{"V":""}""")]
    [InlineData("""{"V":"1,5"}""")]          // comma decimal
    [InlineData("""{"V":"1e3"}""")]          // no exponents in text
    [InlineData("""{"V":"about 5"}""")]
    [InlineData("""{"V":null}""")]
    [InlineData("""{"V":true}""")]
    [InlineData("""{"V":false}""")]
    [InlineData("""{"V":1e400}""")]          // beyond decimal range
    public void Anything_that_is_not_a_plain_number_becomes_null_without_throwing(string json)
    {
        Assert.Null(Parse(json).V);
    }

    [Theory]
    [InlineData("""{"V":{"a":1,"b":[1,2,3]},"W":7}""")]
    [InlineData("""{"V":[1,2,{"x":"y"}],"W":7}""")]
    public void Objects_and_arrays_are_skipped_and_the_reader_stays_in_sync(string json)
    {
        var holder = Parse(json);

        Assert.Null(holder.V);
        Assert.Equal(7, holder.W);   // the property AFTER the odd value still parses
    }

    [Fact]
    public void Missing_property_stays_null()
    {
        Assert.Null(Parse("""{"W":1}""").V);
    }

    [Fact]
    public void Writes_numbers_and_null()
    {
        Assert.Equal("""{"V":0.5,"W":0}""", JsonSerializer.Serialize(new Holder { V = 0.5m }, Options));
        Assert.Equal("""{"V":null,"W":0}""", JsonSerializer.Serialize(new Holder { V = null }, Options));
    }
}
