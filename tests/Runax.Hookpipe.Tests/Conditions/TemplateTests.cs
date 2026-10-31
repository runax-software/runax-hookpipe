using Runax.Hookpipe.Conditions;
using Shouldly;

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
        var placeholder = Template.Parse(text).Placeholders.ShouldHaveSingleItem();

        placeholder.Source.ShouldBe(source);
        placeholder.Name.ShouldBe(name);
        placeholder.Default.ShouldBeNull();
        placeholder.Text.ShouldBe(text);
    }

    [Fact]
    public void Parse_Default_IsSplitAtTheFirstSeparatorOutsideBrackets()
    {
        var withDefault = Template.Parse("{$.type:-other}").Placeholders.Single();
        var slice = Template.Parse("{$.items[0:-1]}").Placeholders.Single();
        var empty = Template.Parse("{header.X-Tenant:-}").Placeholders.Single();

        withDefault.Name.ShouldBe("$.type");
        withDefault.Default.ShouldBe("other");
        slice.Name.ShouldBe("$.items[0:-1]");
        slice.Default.ShouldBeNull();
        empty.Default.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_MixedText_KeepsLiteralsAroundPlaceholders()
    {
        var template = Template.Parse("payments.{source}.{$.type}");

        template.HasPlaceholders.ShouldBeTrue();
        template.Literals.ShouldBe(["payments.", ".", ""]);
        template.Placeholders.Select(placeholder => placeholder.Source)
            .ShouldBe([TemplateSource.PathParameter, TemplateSource.JsonPath]);
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

        template.HasPlaceholders.ShouldBeFalse();
        template.Literals.ShouldBe([literal]);
    }

    [Fact]
    public void Parse_JsonPathWithBracesInQuotes_FindsTheClosingBrace()
    {
        var template = Template.Parse("x.{$.items[?@.sku == '}']}.y");

        template.Placeholders.Single().Name.ShouldBe("$.items[?@.sku == '}']");
        template.Literals.ShouldBe(["x.", ".y"]);
    }

    [Theory]
    [InlineData("a.{source", "has no closing '}'")]
    [InlineData("{bad name}", "isn't valid")]
    [InlineData("{metadata.}", "isn't valid")]
    [InlineData("{$.a b}", "invalid JSONPath")]
    public void Parse_InvalidPlaceholder_Throws(string text, string message)
    {
        var act = () => Template.Parse(text);

        var exception = Should.Throw<ArgumentException>(act);
        exception.Message.ShouldContain($"{message}");
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

        template.TryRender(context, out var rendered, out var unresolved).ShouldBeTrue();
        rendered.ShouldBe("hooks/github/acme/eu/invoice.paid/100.0/true");
        unresolved.ShouldBeNull();
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

        template.TryRender(Context(body), out _, out var unresolved).ShouldBeFalse();
        unresolved!.Text.ShouldBe(text);
    }

    [Fact]
    public void TryRender_Default_IsUsedWhenNothingResolves()
    {
        var template = Template.Parse("payments.{$.type:-other}");

        template.TryRender(Context("""{}"""), out var rendered, out _).ShouldBeTrue();
        rendered.ShouldBe("payments.other");
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
