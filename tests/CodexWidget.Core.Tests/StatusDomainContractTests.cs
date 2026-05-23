using System.Reflection;

namespace CodexWidget.Core.Tests;

public sealed class StatusDomainContractTests
{
    [Fact]
    public void StatusAvailability_FactoryMethods_PreserveExpectedState()
    {
        var available = StatusAvailability.Available();
        var unavailable = StatusAvailability.Unavailable(StatusAvailabilityCode.MissingWindow, "window payload absent");

        Assert.True(available.IsAvailable);
        Assert.Equal(StatusAvailabilityState.Available, available.State);
        Assert.Equal(StatusAvailabilityCode.Available, available.Code);
        Assert.Null(available.Detail);

        Assert.False(unavailable.IsAvailable);
        Assert.Equal(StatusAvailabilityState.Unavailable, unavailable.State);
        Assert.Equal(StatusAvailabilityCode.MissingWindow, unavailable.Code);
        Assert.Equal("window payload absent", unavailable.Detail);
    }

    [Fact]
    public void UsageWindow_DefaultShape_ModelsUnavailableWithoutNumericSentinels()
    {
        var window = new UsageWindowSnapshot();

        Assert.Equal(StatusAvailabilityState.Unavailable, window.Availability.State);
        Assert.Equal(StatusAvailabilityCode.Unavailable, window.Availability.Code);
        Assert.Null(window.UsedPercent);
        Assert.Null(window.QuotaLeftPercent);
        Assert.Null(window.TimeLeftPercent);
        Assert.Null(window.DurationSeconds);
        Assert.Null(window.ResetAtUnixSeconds);
    }

    [Fact]
    public void StatusSnapshot_Defaults_AreUtcAndCollectionSafe()
    {
        var snapshot = new StatusSnapshot();

        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.CapturedAtUtc);
        Assert.Equal(TimeSpan.Zero, snapshot.CapturedAtUtc.Offset);
        Assert.Empty(snapshot.Profiles);
        Assert.Empty(snapshot.Sources);
        Assert.Equal(StatusRefreshOutcome.Idle, snapshot.RefreshState.Outcome);
    }

    [Fact]
    public void ProfileDescriptor_UsesValueEquality()
    {
        var sourceStatus = new SourceStatus
        {
            Source = StatusSourceKind.SavedProfileAuth,
            State = SourceStatusState.Available,
            Availability = StatusAvailability.Available(),
        };

        var left = new ProfileDescriptor
        {
            ProfileId = "abc",
            DisplayName = "Work",
            LoginName = "person@example.com",
            SubscriptionTier = SubscriptionTier.Pro,
            IsCurrent = true,
            AuthKind = ProfileAuthKind.Login,
            UsageEligibility = ProfileUsageEligibility.Eligible,
            SourceStatus = sourceStatus,
        };

        var right = left with { };

        Assert.Equal(left, right);
    }

    [Fact]
    public void Contracts_ExposeUnixSecondsAndUtcInstants()
    {
        Assert.Equal(typeof(long?), typeof(UsageWindowSnapshot).GetProperty(nameof(UsageWindowSnapshot.ResetAtUnixSeconds), BindingFlags.Public | BindingFlags.Instance)!.PropertyType);
        Assert.Equal(typeof(DateTimeOffset), typeof(StatusSnapshot).GetProperty(nameof(StatusSnapshot.CapturedAtUtc), BindingFlags.Public | BindingFlags.Instance)!.PropertyType);
        Assert.Equal(typeof(DateTimeOffset?), typeof(StatusRefreshState).GetProperty(nameof(StatusRefreshState.RequestedAtUtc), BindingFlags.Public | BindingFlags.Instance)!.PropertyType);
    }

    [Fact]
    public void SourceStatusAndRefreshReason_Enumerations_CoverRequiredStates()
    {
        Assert.Contains(SourceStatusState.Missing, Enum.GetValues<SourceStatusState>());
        Assert.Contains(SourceStatusState.Malformed, Enum.GetValues<SourceStatusState>());
        Assert.Contains(SourceStatusState.Stale, Enum.GetValues<SourceStatusState>());
        Assert.Contains(SourceStatusState.Unavailable, Enum.GetValues<SourceStatusState>());
        Assert.Contains(SourceStatusState.Error, Enum.GetValues<SourceStatusState>());

        Assert.Contains(StatusRefreshReason.Startup, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshReason.StaleWidgetOpen, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshReason.ProfileChanged, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshReason.ConfigChanged, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshReason.Periodic, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshReason.ResetTimeElapsed, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshReason.Manual, Enum.GetValues<StatusRefreshReason>());
        Assert.Contains(StatusRefreshOutcome.Failed, Enum.GetValues<StatusRefreshOutcome>());
    }
}
