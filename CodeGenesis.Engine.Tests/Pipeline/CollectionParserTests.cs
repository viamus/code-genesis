using CodeGenesis.Engine.Pipeline;
using FluentAssertions;

namespace CodeGenesis.Engine.Tests.Pipeline;

public class CollectionParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        CollectionParser.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmptyList()
    {
        CollectionParser.Parse("   ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_JsonArray_ReturnsItems()
    {
        var result = CollectionParser.Parse("""["a", "b", "c"]""");

        result.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Parse_JsonArrayWithNumbers_ReturnsRawText()
    {
        var result = CollectionParser.Parse("[1, 2, 3]");

        result.Should().Equal("1", "2", "3");
    }

    [Fact]
    public void Parse_CommaSeparated_ReturnsItems()
    {
        var result = CollectionParser.Parse("alpha, beta, gamma");

        result.Should().Equal("alpha", "beta", "gamma");
    }

    [Fact]
    public void Parse_CommaSeparated_TrimsWhitespace()
    {
        var result = CollectionParser.Parse("  x , y , z  ");

        result.Should().Equal("x", "y", "z");
    }

    [Fact]
    public void Parse_CommaSeparated_RemovesEmptyEntries()
    {
        var result = CollectionParser.Parse("a,,b,");

        result.Should().Equal("a", "b");
    }

    [Fact]
    public void Parse_NewlineSeparated_ReturnsItems()
    {
        var result = CollectionParser.Parse("line1\nline2\nline3");

        result.Should().Equal("line1", "line2", "line3");
    }

    [Fact]
    public void Parse_NewlineSeparated_RemovesEmptyLines()
    {
        var result = CollectionParser.Parse("line1\n\nline2\n");

        result.Should().Equal("line1", "line2");
    }

    [Fact]
    public void Parse_SingleItem_ReturnsSingleElementList()
    {
        var result = CollectionParser.Parse("just-one");

        result.Should().Equal("just-one");
    }

    [Fact]
    public void Parse_InvalidJsonArray_FallsBackToCommaSeparated()
    {
        var result = CollectionParser.Parse("[broken, json");

        result.Should().Equal("[broken", "json");
    }

    [Fact]
    public void Parse_JsonArrayPrioritizedOverComma()
    {
        // A valid JSON array with commas should parse as JSON, not comma-separated
        var result = CollectionParser.Parse("""["a,b", "c,d"]""");

        result.Should().Equal("a,b", "c,d");
    }
}
