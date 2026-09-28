using System.Globalization;
using GestorIA.Engine;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using static System.FormattableString;
using IcsCalendar = Ical.Net.Calendar;

namespace GestorIA.Api.Profiles;

// An RFC 5545 export of the payments calendar (#70), for the taxpayer's own calendar app. Ical.Net (Directory.Packages.props)
// owns line folding, CRLF and escaping; this class only has to get the calendar semantics right: a Uid stable across exports
// of the same obligation but unique across profiles and tax years, a one-day DATE (not DATE-TIME) event on the actual due
// day, and the due window, the reason and the local-holidays caveat spelt out in the DESCRIPTION rather than a banner that
// spans the whole window.
public static class PaymentsCalendarIcs
{
    public static string Write(IReadOnlyList<PaymentObligation> obligations, int taxYear, Guid profileId, bool includeAmounts, string locale)
    {
        var calendar = new IcsCalendar();

        foreach (var obligation in obligations)
        {
            calendar.Events.Add(new CalendarEvent
            {
                // {kind}-{period} alone repeats across tax years (Modelo130-Q1 in 2025 and in 2026); the tax year and the
                // profile make it unique while staying the same on every export of this profile's same obligation.
                Uid = Invariant($"{obligation.Kind}-{obligation.Period}-{taxYear}@{profileId:N}.gestoria.local"),
                Start = AllDay(obligation.DueWindow.End),
                // DTEND of a DATE-valued event is exclusive (RFC 5545 §3.6.1): a one-day event still needs a DTEND the day after.
                End = AllDay(obligation.DueWindow.End.AddDays(1)),
                Summary = Summary(obligation, includeAmounts, locale),
                Description = Description(obligation, locale),
            });
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? throw new InvalidOperationException("Ical.Net serialized a calendar to null.");
    }

    private static CalDateTime AllDay(DateOnly day) => new(day);

    // No amount or profile-identifying detail in the title unless the caller opts in (SPEC-013): a calendar app can sync a
    // title to other people before the taxpayer chooses to share it.
    private static string Summary(PaymentObligation obligation, bool includeAmounts, string locale)
    {
        var title = Invariant($"{PaymentsCalendarIcsText.Title(obligation.Kind, locale)} {obligation.Period}");
        return includeAmounts && obligation.Amount is ObligationAmount.Known known
            ? Invariant($"{title} — {Euros(known)}")
            : title;
    }

    private static string Description(PaymentObligation obligation, string locale)
    {
        var window = PaymentsCalendarIcsText.DueWindow(obligation.DueWindow.Start, obligation.DueWindow.End, locale);
        var reason = obligation.Amount is ObligationAmount.NotYetKnown ? PaymentsCalendarIcsText.NotYetKnownReason(obligation.Kind, locale) + " " : "";
        return window + " " + reason + PaymentsCalendarIcsText.LocalHolidaysNote(obligation.Kind, locale);
    }

    private static string Euros(ObligationAmount.Known known) => known.Value.Round2().Amount.ToString("0.00", CultureInfo.InvariantCulture) + " €";
}
