namespace BodyLife.Crm.Modules.Payments;

public sealed record DailyPaymentSourceRow(
    string ClientDisplayName,
    ClientPaymentRow Payment);
