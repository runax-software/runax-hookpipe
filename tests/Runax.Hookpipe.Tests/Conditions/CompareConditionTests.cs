using FluentAssertions;
using Runax.Hookpipe.Conditions;

namespace Runax.Hookpipe.Tests.Conditions;

public sealed class CompareConditionTests
{
    [Theory]
    [InlineData("""{"amount":100}""", true)]
    [InlineData("""{"amount":100.0}""", true)]
    [InlineData("""{"amount":"100"}""", false)]
    [InlineData("""{"amount":101}""", false)]
    public void Eq_NumberOperand_ComparesByValue(string body, bool expected)
    {
        var condition = JsonPath("$.amount", ConditionOperator.Eq, new ConditionValue("100", ConditionValueKind.Number));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"code":100}""", true)]
    [InlineData("""{"code":"100"}""", true)]
    [InlineData("""{"code":100.0}""", false)]
    public void Eq_TextOperand_ComparesTheText(string body, bool expected)
    {
        var condition = JsonPath("$.code", ConditionOperator.Eq, ConditionValue.Of("100"));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"live":true}""", true)]
    [InlineData("""{"live":"true"}""", false)]
    [InlineData("""{"live":false}""", false)]
    public void Eq_BooleanOperand_MatchesJsonBooleans(string body, bool expected)
    {
        var condition = JsonPath("$.live", ConditionOperator.Eq, ConditionValue.Of(true));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"value":100}""", true)]
    [InlineData("""{"value":"100"}""", true)]
    [InlineData("""{"value":100.0}""", true)]
    [InlineData("""{"value":"abc"}""", false)]
    public void Eq_UntypedOperand_MatchesTextOrSameNumber(string body, bool expected)
    {
        var condition = JsonPath("$.value", ConditionOperator.Eq, new ConditionValue("100", ConditionValueKind.Untyped));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Theory]
    [InlineData("push", true)]
    [InlineData("Push", false)]
    public void Eq_Header_IsCaseSensitiveOnTheValue(string header, bool expected)
    {
        var condition = new CompareCondition(ConditionSource.Header, "X-Event", ConditionOperator.Eq, [ConditionValue.Of("push")]);

        condition.Evaluate(Headers(("x-event", header))).Should().Be(expected);
    }

    [Fact]
    public void Eq_HeaderAndNumberOperand_ComparesByValue()
    {
        var condition = new CompareCondition(ConditionSource.Header, "X-Priority", ConditionOperator.Eq,
            [new ConditionValue("5", ConditionValueKind.Number)]);

        condition.Evaluate(Headers(("X-Priority", "5.0"))).Should().BeTrue();
    }

    [Fact]
    public void Eq_SeveralSelectedValues_MatchesWhenAnyDoes()
    {
        var condition = JsonPath("$.items[*].sku", ConditionOperator.Eq, ConditionValue.Of("A1"));

        condition.Evaluate(Body("""{"items":[{"sku":"B2"},{"sku":"A1"}]}""")).Should().BeTrue();
        condition.Evaluate(Body("""{"items":[{"sku":"B2"}]}""")).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"status":"pending"}""", true)]
    [InlineData("""{"status":"paid"}""", false)]
    [InlineData("""{}""", false)]
    public void Ne_IsFalseWhenNothingIsSelected(string body, bool expected)
    {
        var condition = JsonPath("$.status", ConditionOperator.Ne, ConditionValue.Of("paid"));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"status":"pending"}""", false, true)]
    [InlineData("""{"status":"paid"}""", true, false)]
    [InlineData("""{}""", false, false)]
    public void InAndNotIn_CompareWithEveryOperand(string body, bool expectedIn, bool expectedNotIn)
    {
        ConditionValue[] operands = [ConditionValue.Of("paid"), ConditionValue.Of("refunded")];

        JsonPath("$.status", ConditionOperator.In, operands).Evaluate(Body(body)).Should().Be(expectedIn);
        JsonPath("$.status", ConditionOperator.NotIn, operands).Evaluate(Body(body)).Should().Be(expectedNotIn);
    }

    [Theory]
    [InlineData(ConditionOperator.Gt, """{"amount":150}""", true)]
    [InlineData(ConditionOperator.Gt, """{"amount":100}""", false)]
    [InlineData(ConditionOperator.Gte, """{"amount":100}""", true)]
    [InlineData(ConditionOperator.Lt, """{"amount":99.5}""", true)]
    [InlineData(ConditionOperator.Lte, """{"amount":101}""", false)]
    [InlineData(ConditionOperator.Gt, """{"amount":"150"}""", false)]
    [InlineData(ConditionOperator.Gt, """{}""", false)]
    public void NumericOperators_CompareJsonNumbersOnly(ConditionOperator @operator, string body, bool expected)
    {
        var condition = JsonPath("$.amount", @operator, new ConditionValue("100", ConditionValueKind.Number));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Theory]
    [InlineData(ConditionOperator.Contains, "REQUEST", true)]
    [InlineData(ConditionOperator.StartsWith, "PULL", true)]
    [InlineData(ConditionOperator.EndsWith, "_Request", true)]
    [InlineData(ConditionOperator.StartsWith, "request", false)]
    public void TextOperators_IgnoreCase(ConditionOperator @operator, string operand, bool expected)
    {
        var condition = new CompareCondition(ConditionSource.Header, "X-Event", @operator, [ConditionValue.Of(operand)]);

        condition.Evaluate(Headers(("X-Event", "pull_request"))).Should().Be(expected);
    }

    [Fact]
    public void Regex_IsCaseSensitive()
    {
        var condition = new CompareCondition(ConditionSource.Header, "X-Event", ConditionOperator.Regex,
            [ConditionValue.Of("^pull_.*$")]);

        condition.Evaluate(Headers(("X-Event", "pull_request"))).Should().BeTrue();
        condition.Evaluate(Headers(("X-Event", "Pull_request"))).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"id":"a"}""", true, true)]
    [InlineData("""{"id":null}""", true, false)]
    [InlineData("""{}""", true, false)]
    [InlineData("""{}""", false, true)]
    [InlineData("""{"id":"a"}""", false, false)]
    public void Exists_CountsOnlyNonNullValues(string body, bool operand, bool expected)
    {
        var condition = JsonPath("$.id", ConditionOperator.Exists, ConditionValue.Of(operand));

        condition.Evaluate(Body(body)).Should().Be(expected);
    }

    [Fact]
    public void JsonPath_BodyNotJson_SelectsNothing()
    {
        var context = new EvaluationContext("test", _ => null, new Dictionary<string, string>(), rawBody: "not json");

        JsonPath("$.id", ConditionOperator.Eq, ConditionValue.Of("a")).Evaluate(context).Should().BeFalse();
        JsonPath("$.id", ConditionOperator.Exists, ConditionValue.Of(false)).Evaluate(context).Should().BeTrue();
    }

    [Fact]
    public void JsonPath_FilterSelector_Works()
    {
        var condition = JsonPath("$.items[?@.qty > 10 && @.sku == 'A1']", ConditionOperator.Exists, ConditionValue.Of(true));

        condition.Evaluate(Body("""{"items":[{"sku":"A1","qty":12}]}""")).Should().BeTrue();
        condition.Evaluate(Body("""{"items":[{"sku":"A1","qty":2}]}""")).Should().BeFalse();
    }

    [Fact]
    public void Path_ReadsThePathParameter()
    {
        var condition = new CompareCondition(ConditionSource.Path, "source", ConditionOperator.Ne, [ConditionValue.Of("test")]);
        var context = PathParameters(("source", "github"));

        condition.Evaluate(context).Should().BeTrue();
    }

    [Fact]
    public void CombinedConditions_EvaluateAllAnyAndNot()
    {
        var paid = JsonPath("$.status", ConditionOperator.Eq, ConditionValue.Of("paid"));
        var large = JsonPath("$.amount", ConditionOperator.Gte, new ConditionValue("1000", ConditionValueKind.Number));
        var context = Body("""{"status":"paid","amount":10}""");

        new AllCondition([paid, large]).Evaluate(context).Should().BeFalse();
        new AnyCondition([paid, large]).Evaluate(context).Should().BeTrue();
        new NotCondition(large).Evaluate(context).Should().BeTrue();
        new AllCondition([paid, new NotCondition(large)]).UsesJsonPath.Should().BeTrue();
    }

    [Theory]
    [InlineData(ConditionSource.JsonPath, "status", ConditionOperator.Eq, "x", "must start with '$'")]
    [InlineData(ConditionSource.JsonPath, "$.a[", ConditionOperator.Eq, "x", "invalid JSONPath")]
    [InlineData(ConditionSource.Header, "X-A", ConditionOperator.Regex, "([a-z", "invalid regex")]
    [InlineData(ConditionSource.Header, "X-A", ConditionOperator.Gt, "many", "'gt' needs a number")]
    [InlineData(ConditionSource.Header, " ", ConditionOperator.Eq, "x", "the selector is empty")]
    public void Constructor_InvalidInput_Throws(
        ConditionSource source,
        string selector,
        ConditionOperator @operator,
        string operand,
        string expected)
    {
        var act = () => new CompareCondition(source, selector, @operator, [ConditionValue.Of(operand)]);

        act.Should().Throw<ArgumentException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Constructor_InWithoutOperands_Throws()
    {
        var act = () => new CompareCondition(ConditionSource.Header, "X-A", ConditionOperator.In, []);

        act.Should().Throw<ArgumentException>().WithMessage("*'in' needs a list of values*");
    }

    [Fact]
    public void Operand_PathParameterPlaceholder_IsFilledInPerRequest()
    {
        var condition = new CompareCondition(ConditionSource.Header, "X-Tenant", ConditionOperator.Eq,
            [ConditionValue.Of("{tenant}")]);
        var context = new EvaluationContext("test", name => name == "X-Tenant" ? "acme" : null,
            new Dictionary<string, string> { ["tenant"] = "acme" }, null);

        condition.Evaluate(context).Should().BeTrue();
        condition.PathParameters.Should().Equal("tenant");
    }

    [Fact]
    public void Operand_PlaceholderInList_IsFilledIn()
    {
        var condition = new CompareCondition(ConditionSource.Path, "source", ConditionOperator.In,
            [ConditionValue.Of("internal"), ConditionValue.Of("{tenant}-app")]);

        condition.Evaluate(PathParameters(("source", "acme-app"), ("tenant", "acme"))).Should().BeTrue();
        condition.Evaluate(PathParameters(("source", "other-app"), ("tenant", "acme"))).Should().BeFalse();
    }

    [Fact]
    public void Operand_BracesWithoutPlaceholder_StayLiteral()
    {
        var condition = JsonPath("$.text", ConditionOperator.Eq, ConditionValue.Of("{}"));

        condition.Evaluate(Body("""{"text":"{}"}""")).Should().BeTrue();
    }

    [Fact]
    public void Operand_RegexQuantifier_IsNotAPlaceholder()
    {
        var condition = JsonPath("$.code", ConditionOperator.Regex, ConditionValue.Of("^a{2}$"));

        condition.Evaluate(Body("""{"code":"aa"}""")).Should().BeTrue();
        condition.PathParameters.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{header.X-Tenant}", "condition values can only use path parameters")]
    [InlineData("{$.tenant}", "condition values can only use path parameters")]
    [InlineData("{tenant:-acme}", "take no default")]
    public void Operand_UnsupportedPlaceholder_Throws(string value, string message)
    {
        var act = () => new CompareCondition(ConditionSource.Header, "X-Tenant", ConditionOperator.Eq,
            [ConditionValue.Of(value)]);

        act.Should().Throw<ArgumentException>().WithMessage($"*{message}*");
    }

    private static CompareCondition JsonPath(string path, ConditionOperator @operator, params ConditionValue[] operands) =>
        new(ConditionSource.JsonPath, path, @operator, operands);

    private static EvaluationContext Body(string json) =>
        new("test", _ => null, new Dictionary<string, string>(), json);

    private static EvaluationContext Headers(params (string Name, string Value)[] headers)
    {
        var values = headers.ToDictionary(header => header.Name, header => header.Value, StringComparer.OrdinalIgnoreCase);
        return new EvaluationContext("test", name => values.GetValueOrDefault(name), new Dictionary<string, string>(), null);
    }

    private static EvaluationContext PathParameters(params (string Name, string Value)[] parameters) =>
        new("test", _ => null,
            parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase),
            null);
}
