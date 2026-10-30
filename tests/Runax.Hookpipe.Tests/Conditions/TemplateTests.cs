using FluentAssertions;
using Runax.Hookpipe.Conditions;

namespace Runax.Hookpipe.Tests.Conditions;

public sealed class TemplateTests
{
    [Theory]
    [InlineData("{source}", TemplateSource.PathParameter, "source")]
    [InlineData("{webhook_id}", TemplateSource.WebhookId, "")]
    [InlineData("{topic}", TemplateSource.Topic, "")]
    [InlineData("{metadata.tenant}", TemplateSource.Metadata, "tenant")]
    [InlineData("{header.X-Tenant}", TemplateSource.Header, "X-Tenant")]
    [InlineData("{$.customer.id}", TemplateSource.JsonPath, "$.customer.id")]
    public void Parse_Placeholder_ReadsItsSource(string text, TemplateSource source, string name)
    {
        var placeholder = Template.Parse(text).Placeholders.Should().ContainSingle().Subject;

        placeholder.Source.Should().Be(source);
        placeholder.Name.Should().Be(name);
        placeholder.Default.Should().BeNull();
        placeholder.Text.Should().Be(text);
    }

    [Fact]
    public void Parse_Default_IsSplitAtTheFirstSeparatorOutsideBrackets()
    {
        var withDefault = Template.Parse("{$.type:-other}").Placeholders.Single();
        var slice = Template.Parse("{$.items[0:-1]}").Placeholders.Single();
        var empty = Template.Parse("{header.X-Tenant:-}").Placeholders.Single();

        withDefault.Name.Should().Be("$.type");
        withDefault.Default.Should().Be("other");
        slice.Name.Should().Be("$.items[0:-1]");
        slice.Default.Should().BeNull();
        empty.Default.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MixedText_KeepsLiteralsAroundPlaceholders()
    {
        var template = Template.Parse("payments.{source}.{$.type}");

        template.HasPlaceholders.Should().BeTrue();
        template.Literals.Should().Equal("payments.", ".", "");
        template.Placeholders.Select(placeholder => placeholder.Source)
            .Should().Equal(TemplateSource.PathParameter, TemplateSource.JsonPath);
    }

    [Theory]
    [InlineData("a{{b}}c", "a{b}c")]
    [InlineData("{}", "{}")]
    [InlineData("{ \"a\": 1 }", "{ \"a\": 1 }")]
    [InlineData("${NOT_A_VARIABLE}", "${NOT_A_VARIABLE}")]
    [InlineData("plain", "plain")]
    public void Parse_LiteralBraces_AreNotPlaceholders(string text, string literal)
    {
        var template = Template.Parse(text);

        template.HasPlaceholders.Should().BeFalse();
        template.Literals.Should().Equal(literal);
    }

    [Fact]
    public void Parse_JsonPathWithBracesInQuotes_FindsTheClosingBrace()
    {
        var template = Template.Parse("x.{$.items[?@.sku == '}']}.y");

        template.Placeholders.Single().Name.Should().Be("$.items[?@.sku == '}']");
        template.Literals.Should().Equal("x.", ".y");
    }

    [Theory]
    [InlineData("a.{source", "has no closing '}'")]
    [InlineData("{bad name}", "isn't valid")]
    [InlineData("{metadata.}", "isn't valid")]
    [InlineData("{$.a b}", "invalid JSONPath")]
    public void Parse_InvalidPlaceholder_Throws(string text, string message)
    {
        var act = () => Template.Parse(text);

        act.Should().Throw<ArgumentException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void TryRender_FillsInEverySource()
    {
        var context = Context(
            """{"type":"invoice.paid","amount":100.0,"live":true}""",
            headers: new() { ["X-Tenant"] = "acme" },
            parameters: new() { ["source"] = "github" });
        context.Metadata = new Dictionary<string, string> { ["region"] = "eu" };

        var template = Template.Parse(
            "{webhook_id}/{source}/{header.X-Tenant}/{metadata.region}/{$.type}/{$.amount}/{$.live}");

        template.TryRender(context, out var rendered, out var unresolved).Should().BeTrue();
        rendered.Should().Be("hooks/github/acme/eu/invoice.paid/100.0/true");
        unresolved.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"items":[1,2]}""", "{$.items[*]}")]
    [InlineData("""{"customer":{"id":1}}""", "{$.customer}")]
    [InlineData("""{"type":null}""", "{$.type}")]
    [InlineData("""{"type":""}""", "{$.type}")]
    [InlineData("""{}""", "{$.type}")]
    [InlineData("not json", "{$.type}")]
    [InlineData("""{}""", "{header.X-Missing}")]
    public void TryRender_NoSingleScalar_DoesNotResolve(string body, string text)
    {
        var template = Template.Parse(text);

        template.TryRender(Context(body), out _, out var unresolved).Should().BeFalse();
        unresolved!.Text.Should().Be(text);
    }

    [Fact]
    public void TryRender_Default_IsUsedWhenNothingResolves()
    {
        var template = Template.Parse("payments.{$.type:-other}");

        template.TryRender(Context("""{}"""), out var rendered, out _).Should().BeTrue();
        rendered.Should().Be("payments.other");
    }

    private static EvaluationContext Context(
        string? body,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? parameters = null)
    {
        var headerValues = new Dictionary<string, string>(headers ?? [], StringComparer.OrdinalIgnoreCase);
        return new EvaluationContext("hooks", name => headerValues.GetValueOrDefault(name), parameters ?? [], body);
    }
}
