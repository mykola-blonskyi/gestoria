using System.Globalization;
using System.Text;
using GestorIA.Domain.Interfaces;
using GestorIA.Domain.Models;
using GestorIA.Domain.ValueObjects;

namespace GestorIA.Infrastructure.Parsers;

// BBVA's CSV export: a header line, then one movement per line, fields separated by ";" and optionally double-quoted.
//   Fecha;Fecha Valor;Concepto;Importe;Saldo
//   15/01/2025;15/01/2025;PAGO EN HEROKU;-15,50;1.250,00
// Dates are dd/MM/yyyy, amounts Spanish-style (thousands ".", decimals ","), Saldo may be empty. XLSX is not read yet.
public sealed class BbvaCsvStatementParser : IStatementParser
{
    private const int MaxLineErrors = 20;

    private static readonly string[] Header = ["Fecha", "Fecha Valor", "Concepto", "Importe", "Saldo"];

    // Spelled out rather than CultureInfo("es-ES"), so the parse does not depend on the ICU data of the machine it runs on.
    private static readonly NumberFormatInfo Spanish = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NegativeSign = "-" };

    // A numeric(18,6) column holds twelve digits before the point.
    private const decimal Limit = 1_000_000_000_000m;

    public string Bank => "bbva";

    public IReadOnlyList<StatementLine> Parse(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var errors = new Dictionary<string, string[]>();
        var movements = new List<StatementLine>();
        var headerSeen = false;
        var failed = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!headerSeen)
            {
                if (Fields(line) is not { } names || !names.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidStatementException(new Dictionary<string, string[]>
                    {
                        ["file"] = [$"The first line is not the header of a BBVA CSV statement, {string.Join(';', Header)}."],
                    });
                }

                headerSeen = true;
                continue;
            }

            var number = index + 1;
            var reasons = Movement(line, out var movement);
            if (reasons.Count == 0)
            {
                movements.Add(new StatementLine(number, movement!));
            }
            else if (++failed <= MaxLineErrors)
            {
                errors[$"line {number}"] = [.. reasons.Select(reason => $"Line {number}: {reason}")];
            }
        }

        if (!headerSeen)
        {
            errors["file"] = ["The file is empty; a BBVA CSV statement starts with its header line."];
        }
        else if (failed > MaxLineErrors)
        {
            errors["file"] = [$"{failed} lines cannot be read; the first {MaxLineErrors} are listed."];
        }

        return errors.Count == 0 ? movements : throw new InvalidStatementException(errors);
    }

    private static List<string> Movement(string line, out BankTransaction? movement)
    {
        movement = null;
        if (Fields(line) is not { Count: 5 } fields)
        {
            return ["it must have five fields separated by \";\": Fecha, Fecha Valor, Concepto, Importe, Saldo."];
        }

        var reasons = new List<string>();
        var booking = Date(fields[0], "Fecha", reasons);
        var value = Date(fields[1], "Fecha Valor", reasons);
        var amount = Amount(fields[3], "Importe", reasons);
        var balance = fields[4].Length == 0 ? null : Amount(fields[4], "Saldo", reasons);

        if (reasons.Count == 0)
        {
            movement = new BankTransaction(booking, value, fields[2], amount!.Value, balance);
        }

        return reasons;
    }

    private static DateOnly Date(string text, string column, List<string> reasons)
    {
        if (DateOnly.TryParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        reasons.Add($"{column} must be a date written dd/MM/yyyy.");
        return default;
    }

    private static Money? Amount(string text, string column, List<string> reasons)
    {
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint;
        if (decimal.TryParse(text, style, Spanish, out var amount) && amount.Scale <= 2 && Math.Abs(amount) < Limit)
        {
            return new Money(amount);
        }

        reasons.Add($"{column} must be an amount in euros with at most two decimals, written like -1.234,56.");
        return null;
    }

    // Splits on ";" outside double quotes; a quoted field may hold ";" and a doubled quote. Null when a quote is left open.
    private static List<string>? Fields(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ';')
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString().Trim());
        return quoted ? null : fields;
    }
}
