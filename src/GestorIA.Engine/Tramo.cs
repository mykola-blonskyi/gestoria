using GestorIA.Domain.ValueObjects;

namespace GestorIA.Engine;

public sealed record Tramo(string Name, Money NetFrom, Money? NetUpTo, Money BaseMin, Money BaseMax, Money CuotaMin);