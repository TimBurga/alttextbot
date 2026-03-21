namespace AltTextBot.Domain.Enums;

public enum AuditEventType
{
    SubscriberAdded,
    SubscriberDeactivated,
    SubscriberReactivated,
    LabelApplied,
    LabelNegated,
    LabelChanged,
    ImagePostReceived,
    ImagePostMissingAlt,
    ManualRescore,
    StartupSync,
    RescoringRun
}
