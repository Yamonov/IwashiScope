using System.Text.Json.Nodes;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.History;
using IwashiScope.Core.Models;
using IwashiScope.Core.Workspace;
using IwashiScope.Infrastructure.Windows.Storage;

namespace IwashiScope.Tests;

public sealed class UserIlluminantHistoryTests
{
    [Fact]
    public void AmbientAndEmissiveHistoriesCanFillAndMoveSlots()
    {
        var history = new MeasurementHistory();
        var ambient = history.Add(TestMeasurementFactory.Create(MeasurementMode.Ambient), "Ambient");
        var emissive = history.Add(TestMeasurementFactory.Create(MeasurementMode.Emissive), "Emitter");
        var reflectance = history.Add(TestMeasurementFactory.Create(MeasurementMode.Reflectance));

        Assert.True(history.RegisterUserIlluminant(ambient.Id, UserIlluminantSlot.User1));
        Assert.True(history.RegisterUserIlluminant(ambient.Id, UserIlluminantSlot.User3));
        Assert.Equal(
            [UserIlluminantSlot.User1, UserIlluminantSlot.User3],
            history.UserIlluminantSlotsFor(ambient.Id));

        Assert.True(history.RegisterUserIlluminant(emissive.Id, UserIlluminantSlot.User1));
        Assert.Equal([UserIlluminantSlot.User3], history.UserIlluminantSlotsFor(ambient.Id));
        Assert.Equal([UserIlluminantSlot.User1], history.UserIlluminantSlotsFor(emissive.Id));
        Assert.False(history.RegisterUserIlluminant(reflectance.Id, UserIlluminantSlot.User2));

        var source = IlluminantSpectrumDefinition.User(
            UserIlluminantSlot.User1,
            history.UserIlluminantEntry(UserIlluminantSlot.User1)!,
            japanese: true);
        Assert.NotNull(source);
        Assert.Equal("User定義１", source.DisplayName);
        Assert.Equal("Emitter", source.UserName);
        Assert.Equal(emissive.Measurement.CapturedAt, source.MeasuredAt);
    }

    [Fact]
    public void RegisteredHistoriesAreExcludedFromEveryDeletionPath()
    {
        var history = new MeasurementHistory();
        var protectedEntry = history.Add(TestMeasurementFactory.Create(MeasurementMode.Ambient, 1));
        var ordinary = history.Add(TestMeasurementFactory.Create(MeasurementMode.Ambient, 2));
        Assert.True(history.RegisterUserIlluminant(protectedEntry.Id, UserIlluminantSlot.User1));

        history.SelectAll(MeasurementMode.Ambient);
        Assert.Single(history.DeleteSelected(MeasurementMode.Ambient));
        Assert.Null(history.Entry(ordinary.Id));
        Assert.NotNull(history.Entry(protectedEntry.Id));
        Assert.False(history.Delete(protectedEntry.Id));
        Assert.Empty(history.DeleteAll(MeasurementMode.Ambient));
        Assert.Equal(0, history.DeletableCount(MeasurementMode.Ambient));

        Assert.Equal(1, history.RemoveUserIlluminantRegistrations(protectedEntry.Id));
        Assert.Single(history.DeleteAll(MeasurementMode.Ambient));
        Assert.Empty(history.Ordered(MeasurementMode.Ambient));
    }

    [Fact]
    public void WorkspaceRoundTripsRegistrationsAndReadsLegacyFiles()
    {
        var history = new MeasurementHistory();
        var ambient = history.Add(TestMeasurementFactory.Create(MeasurementMode.Ambient));
        Assert.True(history.RegisterUserIlluminant(ambient.Id, UserIlluminantSlot.User2));
        var document = WorkspaceDocument.Create(
            history,
            MeasurementMode.Ambient,
            MeasurementSidebarTab.MeasurementValues);

        var json = WorkspaceSerializer.Serialize(document);
        var restored = new MeasurementHistory();
        WorkspaceSerializer.Deserialize(json).RestoreInto(restored);
        Assert.Equal(ambient.Id, restored.UserIlluminantEntryId(UserIlluminantSlot.User2));
        Assert.True(restored.IsDeletionProtected(ambient.Id));

        var legacyNode = JsonNode.Parse(json)!.AsObject();
        legacyNode["workspace"]!["history"]!.AsObject()
            .Remove("userIlluminantRegistrations");
        var legacy = WorkspaceSerializer.Deserialize(legacyNode.ToJsonString());
        var legacyHistory = new MeasurementHistory();
        legacy.RestoreInto(legacyHistory);
        Assert.Empty(legacyHistory.AvailableUserIlluminantSlots);
    }

    [Fact]
    public async Task PersistentHistoryRoundTripsThroughLocalStorageFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"iwashiscope-history-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "MeasurementHistory.json");
        try
        {
            var history = new MeasurementHistory();
            var ambient = history.Add(TestMeasurementFactory.Create(MeasurementMode.Ambient));
            history.RegisterUserIlluminant(ambient.Id, UserIlluminantSlot.User3);
            var store = new MeasurementHistoryPersistenceStore(path);
            await store.SaveAsync(WorkspaceDocument.Create(
                history,
                null,
                MeasurementSidebarTab.MeasurementValues));

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            var restored = new MeasurementHistory();
            loaded.RestoreInto(restored);
            Assert.Equal(ambient.Id, restored.UserIlluminantEntryId(UserIlluminantSlot.User3));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
