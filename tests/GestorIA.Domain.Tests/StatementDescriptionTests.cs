using GestorIA.Domain.Interfaces;
using Xunit;

namespace GestorIA.Domain.Tests;

// The one rule a stored description is held to, by the statement import and by a restore (SPEC-009 §2.2).
public class StatementDescriptionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("PAGO EN HEROKU")]
    [InlineData("TRANSFERENCIA  A\tCUENTA")]
    [InlineData("CAFÉ ☕ 😀")]
    public void WhatAStatementLineCanHoldIsValid(string description)
    {
        Assert.True(StatementDescription.IsValid(description));
    }

    [Theory]
    [InlineData("PAGO\0X")]
    [InlineData("PAGO\nX")]
    [InlineData("PAGO\rX")]
    [InlineData("PAGO\u2028X")]
    [InlineData(" PAGO")]
    [InlineData("PAGO ")]
    public void WhatNoStatementLineHoldsIsRefused(string description)
    {
        Assert.False(StatementDescription.IsValid(description));
    }

    // Not as InlineData: xUnit serializes a theory's data and turns a lone surrogate into U+FFFD, which is a whole character.
    [Fact]
    public void HalfACharacterIsRefused()
    {
        Assert.All(["\ud800", "PAGO\udc00", "PAGO\ud83d", "\udc00\ud83d"], description => Assert.False(StatementDescription.IsValid(description)));
    }
}
