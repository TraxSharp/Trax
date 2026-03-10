using FluentAssertions;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class DashboardSettingsServiceTests
{
    private FakeLocalStorageService _storage;
    private DashboardSettingsService _service;

    [SetUp]
    public void SetUp()
    {
        _storage = new FakeLocalStorageService();
        _service = new DashboardSettingsService(_storage);
    }

    #region Default Values

    [Test]
    public void DefaultValues_AreCorrect()
    {
        _service.PollingInterval.TotalSeconds.Should().Be(5);
        _service.HideAdminTrains.Should().BeTrue();
        _service.ShowSummaryCards.Should().BeTrue();
        _service.ShowExecutionsChart.Should().BeTrue();
        _service.ShowFailures.Should().BeTrue();
        _service.ShowAvgDuration.Should().BeTrue();
        _service.ShowServerHealth.Should().BeTrue();
    }

    #endregion

    #region SetPollingIntervalAsync

    [Test]
    public async Task SetPollingIntervalAsync_UpdatesProperty()
    {
        // Act
        await _service.SetPollingIntervalAsync(10);

        // Assert
        _service.PollingInterval.TotalSeconds.Should().Be(10);
    }

    [Test]
    public async Task SetPollingIntervalAsync_ClampsToMinimum1()
    {
        // Act
        await _service.SetPollingIntervalAsync(0);

        // Assert
        _service.PollingInterval.TotalSeconds.Should().Be(1);
    }

    #endregion

    #region SetHideAdminTrainsAsync

    [Test]
    public async Task SetHideAdminTrainsAsync_UpdatesProperty()
    {
        // Act
        await _service.SetHideAdminTrainsAsync(false);

        // Assert
        _service.HideAdminTrains.Should().BeFalse();
    }

    #endregion

    #region SetComponentVisibilityAsync

    [Test]
    public async Task SetComponentVisibilityAsync_UpdatesCorrectProperty()
    {
        // Act
        await _service.SetComponentVisibilityAsync(StorageKeys.ShowSummaryCards, false);

        // Assert
        _service.ShowSummaryCards.Should().BeFalse();
        // Other properties remain true
        _service.ShowExecutionsChart.Should().BeTrue();
    }

    #endregion

    #region NotifyPolled

    [Test]
    public void NotifyPolled_UpdatesLastPollTime()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        _service.NotifyPolled();

        // Assert
        _service.LastPollTime.Should().BeOnOrAfter(before);
    }

    #endregion

    #region InitializeAsync

    [Test]
    public async Task InitializeAsync_LoadsStoredValues()
    {
        // Arrange — pre-populate storage
        _storage.Store[StorageKeys.PollingInterval] = 15;
        _storage.Store[StorageKeys.HideAdminTrains] = false;

        // Act
        await _service.InitializeAsync();

        // Assert
        _service.PollingInterval.TotalSeconds.Should().Be(15);
        _service.HideAdminTrains.Should().BeFalse();
    }

    #endregion

    #region Fake ILocalStorageService

    private class FakeLocalStorageService : ILocalStorageService
    {
        public Dictionary<string, object> Store { get; } = new();

        public Task<T?> GetAsync<T>(string key)
        {
            if (Store.TryGetValue(key, out var value) && value is T typed)
                return Task.FromResult<T?>(typed);
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value)
        {
            Store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            Store.Remove(key);
            return Task.CompletedTask;
        }
    }

    #endregion
}
