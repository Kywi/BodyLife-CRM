using System.Text;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayPreviewInput
{
    public const int ReasonCodeMaxLength = 64;
    public const int ReasonCommentMaxLength = 1000;

    public NonWorkingDayPreviewInput(
        DateRange period,
        string reasonCode,
        string? reasonComment = null)
    {
        if (period.StartDate == default || period.EndDate == default)
        {
            throw new ArgumentException(
                "NonWorkingDay period dates are required.",
                nameof(period));
        }

        ArgumentNullException.ThrowIfNull(reasonCode);
        var normalizedReasonCode = reasonCode.Trim().Normalize(NormalizationForm.FormC);
        if (normalizedReasonCode.Length == 0)
        {
            throw new ArgumentException(
                "NonWorkingDay reason code is required.",
                nameof(reasonCode));
        }

        if (normalizedReasonCode.Length > ReasonCodeMaxLength)
        {
            throw new ArgumentException(
                $"NonWorkingDay reason code must be {ReasonCodeMaxLength} characters or fewer.",
                nameof(reasonCode));
        }

        var normalizedReasonComment = NormalizeOptional(reasonComment);
        if (normalizedReasonComment?.Length > ReasonCommentMaxLength)
        {
            throw new ArgumentException(
                $"NonWorkingDay reason comment must be {ReasonCommentMaxLength} characters or fewer.",
                nameof(reasonComment));
        }

        Period = period;
        ReasonCode = normalizedReasonCode;
        ReasonComment = normalizedReasonComment;
    }

    public DateRange Period { get; }

    public string ReasonCode { get; }

    public string? ReasonComment { get; }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim().Normalize(NormalizationForm.FormC);
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
