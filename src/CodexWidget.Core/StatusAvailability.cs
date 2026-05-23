namespace CodexWidget.Core;

public enum StatusAvailabilityState
{
    Unknown = 0,
    Available = 1,
    Unavailable = 2,
}

public enum StatusAvailabilityCode
{
    None = 0,
    Available = 1,
    Missing = 2,
    Malformed = 3,
    Stale = 4,
    Unavailable = 5,
    Error = 6,
    Unauthorized = 7,
    NetworkError = 8,
    ApiKeyProfile = 9,
    MissingProfile = 10,
    MissingBucket = 11,
    MissingWindow = 12,
    MissingTimestampOrDuration = 13,
    MissingRequiredField = 14,
    TokenRefreshFailed = 15,
}

public readonly record struct StatusAvailability(
    StatusAvailabilityState State,
    StatusAvailabilityCode Code = StatusAvailabilityCode.None,
    string? Detail = null)
{
    public bool IsAvailable => State == StatusAvailabilityState.Available;

    public static StatusAvailability Available(StatusAvailabilityCode code = StatusAvailabilityCode.Available)
    {
        return new(StatusAvailabilityState.Available, code);
    }

    public static StatusAvailability Unavailable(StatusAvailabilityCode code, string? detail = null)
    {
        return new(StatusAvailabilityState.Unavailable, code, detail);
    }
}
