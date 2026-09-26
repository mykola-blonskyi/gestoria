using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine;

public sealed record Tranche(Money? UpTo, Rate Rate);
