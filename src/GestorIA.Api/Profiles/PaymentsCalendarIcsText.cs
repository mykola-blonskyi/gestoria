using System.Diagnostics;
using System.Globalization;
using GestorIA.Domain.ValueObjects;
using GestorIA.Engine;
using static System.FormattableString;

namespace GestorIA.Api.Profiles;

// The four locales' text for the ICS export (#70): SUMMARY and DESCRIPTION reach a calendar app directly, with no web UI to
// translate them the way next-intl translates the JSON API's English reference text (TraceStep titles, NoticeView.text). A
// small, separate surface, kept here rather than shared with web/src/i18n/messages because the two run in different
// languages and processes.
public static class PaymentsCalendarIcsText
{
    public const string DefaultLocale = "en";

    public static readonly IReadOnlyList<string> SupportedLocales = ["uk", "es", "en", "ru"];

    public static string Title(ObligationKind kind, string locale) => Locales[locale].Titles[kind];

    public static string DueWindow(DateOnly from, DateOnly to, string locale) =>
        Invariant($"{Locales[locale].DueWindowLabel}: {Iso(from)} – {Iso(to)}.");

    public static string NotYetKnownReason(ObligationKind kind, string locale) => kind switch
    {
        ObligationKind.Modelo303 => Locales[locale].Reason303,
        ObligationKind.Modelo349 => Locales[locale].Reason349,
        _ => throw new UnreachableException("Only Modelo 303 and 349 are ever NotYetKnown (GestorIA.Engine.PaymentCalendar)."),
    };

    // RD 1415/2004 art. 56.1.b).1.º and 8.b) move the TGSS cuota's own deadline backward, the opposite direction from every filing
    // deadline here (FilingDeadline.LocalHolidaysNotApplied vs LocalHolidaysNotAppliedBackward): the caveat reads differently.
    public static string LocalHolidaysNote(ObligationKind kind, string locale) =>
        kind == ObligationKind.SeguridadSocial ? Locales[locale].LocalHolidaysBackward : Locales[locale].LocalHolidaysForward;

    // In the locale's own way of writing euros, as the payments page does: "80,00 €" in es, uk and ru, "€80.00" in en. Money
    // is a decimal, so the figure is formatted exactly, never through a binary float.
    public static string Euros(Money money, string locale) => money.Round2().Amount.ToString("C2", Locales[locale].Money);

    private static string Iso(DateOnly day) => Invariant($"{day:yyyy-MM-dd}");

    private sealed record LocaleText(
        IReadOnlyDictionary<ObligationKind, string> Titles,
        string DueWindowLabel,
        string Reason303,
        string Reason349,
        string LocalHolidaysForward,
        string LocalHolidaysBackward,
        NumberFormatInfo Money);

    // The locale's number format with the euro as its currency, whatever currency the culture itself uses (uk-UA's is the
    // hryvnia).
    private static NumberFormatInfo EurosIn(string culture)
    {
        var format = (NumberFormatInfo)CultureInfo.GetCultureInfo(culture).NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return NumberFormatInfo.ReadOnly(format);
    }

    private static readonly IReadOnlyDictionary<string, LocaleText> Locales = new Dictionary<string, LocaleText>
    {
        ["en"] = new(
            new Dictionary<ObligationKind, string>
            {
                [ObligationKind.Modelo130] = "Modelo 130",
                [ObligationKind.Modelo303] = "Modelo 303",
                [ObligationKind.Modelo349] = "Modelo 349",
                [ObligationKind.SeguridadSocial] = "TGSS cuota",
                [ObligationKind.RentaTrueUp] = "Renta true-up",
            },
            "Due window",
            "The amount is not yet known: GestorIA has no Modelo 303 calculator yet.",
            "The amount is not yet known: GestorIA has no Modelo 349 calculator yet, and this filing is only due for a quarter with intra-EU operations.",
            "Municipal holidays where you live are not applied, so this date can be early but never late.",
            "Municipal holidays where you live are not applied, so this date may already be a working day late: pay a day earlier to be safe.",
            EurosIn("en-IE")),

        ["es"] = new(
            new Dictionary<ObligationKind, string>
            {
                [ObligationKind.Modelo130] = "Modelo 130",
                [ObligationKind.Modelo303] = "Modelo 303",
                [ObligationKind.Modelo349] = "Modelo 349",
                [ObligationKind.SeguridadSocial] = "Cuota TGSS",
                [ObligationKind.RentaTrueUp] = "Ajuste de la Renta",
            },
            "Plazo",
            "El importe todavía no se conoce: GestorIA no tiene calculadora del Modelo 303.",
            "El importe todavía no se conoce: GestorIA no tiene calculadora del Modelo 349, y esta declaración solo corresponde si tuviste operaciones intracomunitarias este trimestre.",
            "No se aplican los festivos locales de tu municipio, así que esta fecha puede ser anterior, nunca posterior.",
            "No se aplican los festivos locales de tu municipio, así que esta fecha puede ser ya un día hábil tarde: paga un día antes para ir sobre seguro.",
            EurosIn("es-ES")),

        ["uk"] = new(
            new Dictionary<ObligationKind, string>
            {
                [ObligationKind.Modelo130] = "Modelo 130",
                [ObligationKind.Modelo303] = "Modelo 303",
                [ObligationKind.Modelo349] = "Modelo 349",
                [ObligationKind.SeguridadSocial] = "Cuota до TGSS",
                [ObligationKind.RentaTrueUp] = "Доплата за Renta",
            },
            "Строк сплати",
            "Суму ще не відомо: у GestorIA ще немає калькулятора Modelo 303.",
            "Суму ще не відомо: у GestorIA ще немає калькулятора Modelo 349, і ця декларація належить лише за квартал із внутрішньоєвропейськими операціями.",
            "Місцеві свята за вашим місцем проживання не враховано, тож ця дата може бути ранішою, але ніколи пізнішою.",
            "Місцеві свята за вашим місцем проживання не враховано, тож ця дата може бути вже на робочий день пізньою: сплатіть на день раніше для певності.",
            EurosIn("uk-UA")),

        ["ru"] = new(
            new Dictionary<ObligationKind, string>
            {
                [ObligationKind.Modelo130] = "Modelo 130",
                [ObligationKind.Modelo303] = "Modelo 303",
                [ObligationKind.Modelo349] = "Modelo 349",
                [ObligationKind.SeguridadSocial] = "Cuota в TGSS",
                [ObligationKind.RentaTrueUp] = "Доплата по Renta",
            },
            "Срок оплаты",
            "Сумма пока неизвестна: у GestorIA ещё нет калькулятора Modelo 303.",
            "Сумма пока неизвестна: у GestorIA ещё нет калькулятора Modelo 349, и эта декларация нужна только за квартал с внутриевропейскими операциями.",
            "Местные праздники по вашему месту жительства не учтены, поэтому эта дата может быть раньше, но никогда не позже.",
            "Местные праздники по вашему месту жительства не учтены, поэтому эта дата может уже быть на рабочий день поздней: заплатите на день раньше для надёжности.",
            EurosIn("ru-RU")),
    };
}
