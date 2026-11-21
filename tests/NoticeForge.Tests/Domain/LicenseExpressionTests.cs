using NoticeForge.Domain;

namespace NoticeForge.Tests.Domain;

public class LicenseExpressionTests
{
    static readonly string[] Allowed = ["MIT", "Apache-2.0", "BSD-3-Clause"];

    [Theory]
    [InlineData("MIT")]
    [InlineData("mit")]                                  // SPDX ids are case-insensitive
    [InlineData("MIT OR GPL-3.0-only")]                  // OR needs any one
    [InlineData("GPL-3.0-only OR Apache-2.0")]
    [InlineData("MIT AND BSD-3-Clause")]                 // AND needs all
    [InlineData("(MIT OR GPL-2.0-only) AND Apache-2.0")]
    [InlineData("Apache-2.0 WITH LLVM-exception")]       // an exception only adds permissions
    [InlineData("Apache-2.0+")]
    public void Allowed_expressions(string expression) =>
        Assert.True(LicenseExpression.IsAllowed(expression, Allowed));

    [Theory]
    [InlineData("GPL-3.0-only")]
    [InlineData("MIT AND GPL-3.0-only")]
    [InlineData("(MIT OR Apache-2.0) AND MPL-2.0")]
    [InlineData("GPL-2.0-only WITH Classpath-exception-2.0")]
    public void Disallowed_expressions(string expression) =>
        Assert.False(LicenseExpression.IsAllowed(expression, Allowed));

    [Fact]
    public void And_binds_tighter_than_or()
    {
        // Read as MIT OR (GPL AND MPL), so allowed. Reading left to right as (MIT OR GPL) AND MPL would disallow it
        Assert.True(LicenseExpression.IsAllowed("MIT OR GPL-3.0-only AND MPL-2.0", Allowed));
    }

    [Fact]
    public void Whole_with_expression_can_be_allowed() =>
        Assert.True(LicenseExpression.IsAllowed("GPL-2.0-only WITH Classpath-exception-2.0", ["GPL-2.0-only WITH Classpath-exception-2.0"]));

    [Theory]
    [InlineData("")]
    [InlineData("MIT AND")]
    [InlineData("(MIT")]
    [InlineData("MIT)")]
    [InlineData("OR MIT")]
    [InlineData("MIT WITH")]
    public void Malformed_expressions_throw(string expression) =>
        Assert.Throws<FormatException>(() => LicenseExpression.IsAllowed(expression, Allowed));
}
