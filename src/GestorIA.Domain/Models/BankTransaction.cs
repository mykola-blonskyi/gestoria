using GestorIA.Domain.ValueObjects;

namespace GestorIA.Domain.Models;

// One line of a bank statement as the bank printed it (SPEC-001 §4). Amount is signed: money in is positive, money out
// negative. Classification, the account and the linked document arrive with the classifier (SPEC-004).
public sealed record BankTransaction(DateOnly BookingDate, DateOnly ValueDate, string Description, Money Amount, Money? Balance);
