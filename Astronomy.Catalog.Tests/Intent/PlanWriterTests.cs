using Astronomy.Catalog.Intent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The plan-plane surface's contract (spec intent-store, add-plan-write-surface): full-value plan
// upsert with created_at create-only, whole-set interval replacement (plan-id mismatch loud),
// by-night current-plan resolution (superseded excluded; duplicates loud), sequence-ordered reads,
// and caller-owned transaction composition across plan + intervals.
public sealed class PlanWriterTests
{
    [Fact]
    public void DraftRoundTrips_PlanAndOrderedIntervals()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        (Guid profileId, Guid targetId, Guid exposurePlanId) = SeedIntentChain(store);
        PlanWriter writer = new(store);
        Guid planId = Guid.NewGuid();

        using (SqliteTransaction tx = store.Connection.BeginTransaction())
        {
            writer.UpsertPlan(new PlanIntent
            {
                Id = planId, ProfileId = profileId, NightOf = "2026-08-13",
                AuthoredById = 0, CreatedAt = 1_755_000_000,
            }, tx);
            writer.ReplaceIntervals(planId,
            [
                Interval(planId, 1, targetId, exposurePlanId, 1_755_100_000, 1_755_103_600),
                Interval(planId, 2, targetId, exposurePlanId, 1_755_103_600, 1_755_107_200, amended: true),
            ], tx);
            tx.Commit();
        }

        PlanIntent? current = new PlanWriter(store).FindCurrentPlan(profileId, "2026-08-13");
        Assert.NotNull(current);
        Assert.Equal(planId, current.Id);
        Assert.Equal(0, current.StateId);                               // draft, the DDL default
        Assert.Equal(0, current.AuthoredById);                          // manual
        Assert.False(current.SwitchImmediately);
        Assert.Null(current.BlessedAt);

        IReadOnlyList<PlanIntervalIntent> intervals = writer.ReadIntervals(planId);
        Assert.Equal([1L, 2L], intervals.Select(i => i.SequenceNumber));
        Assert.Equal(1_755_100_000, intervals[0].StartAt);
        Assert.Equal(1_755_107_200, intervals[1].EndAt);
        Assert.False(intervals[0].AmendedByUser);
        Assert.True(intervals[1].AmendedByUser);
        Assert.All(intervals, i => Assert.Equal(targetId, i.TargetId));
    }

    [Fact]
    public void UpsertPlan_IsFullValue_AndPreservesCreatedAt()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        (Guid profileId, _, _) = SeedIntentChain(store);
        PlanWriter writer = new(store);
        Guid planId = Guid.NewGuid();

        PlanIntent draft = new()
        {
            Id = planId, ProfileId = profileId, NightOf = "2026-08-13",
            AuthoredById = 0, CreatedAt = 1_755_000_000,
        };
        writer.UpsertPlan(draft);
        writer.UpsertPlan(draft with
        {
            StateId = 1, BlessedAt = 1_755_200_000, SwitchImmediately = true, CreatedAt = 9_999_999_999,
        });

        Assert.Equal(1L, Scalar(store, "SELECT count(*) FROM plan;"));
        Assert.Equal(1L, Scalar(store, "SELECT state_id FROM plan;"));                 // blessed via upsert
        Assert.Equal(1_755_200_000L, Scalar(store, "SELECT blessed_at FROM plan;"));
        Assert.Equal(1L, Scalar(store, "SELECT switch_immediately FROM plan;"));
        Assert.Equal(1_755_000_000L, Scalar(store, "SELECT created_at FROM plan;"));   // creation instant immutable
    }

    [Fact]
    public void ReplaceIntervals_ReplacesWholeSet_GrowShrinkReorder()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        (Guid profileId, Guid targetId, Guid exposurePlanId) = SeedIntentChain(store);
        PlanWriter writer = new(store);
        Guid planId = Guid.NewGuid();
        writer.UpsertPlan(new PlanIntent
        {
            Id = planId, ProfileId = profileId, NightOf = "2026-08-13",
            AuthoredById = 0, CreatedAt = 1_755_000_000,
        });

        writer.ReplaceIntervals(planId,
        [
            Interval(planId, 1, targetId, exposurePlanId, 1_755_100_000, 1_755_103_600),
            Interval(planId, 2, targetId, exposurePlanId, 1_755_103_600, 1_755_107_200),
            Interval(planId, 3, targetId, exposurePlanId, 1_755_107_200, 1_755_110_800),
        ]);
        // The re-authored set: shrunk to two, times moved — the old rows must be gone whole.
        writer.ReplaceIntervals(planId,
        [
            Interval(planId, 1, targetId, exposurePlanId, 1_755_101_000, 1_755_105_000),
            Interval(planId, 2, targetId, exposurePlanId, 1_755_105_000, 1_755_109_000),
        ]);

        IReadOnlyList<PlanIntervalIntent> intervals = writer.ReadIntervals(planId);
        Assert.Equal(2, intervals.Count);
        Assert.Equal(1_755_101_000, intervals[0].StartAt);
        Assert.Equal(1_755_109_000, intervals[1].EndAt);
    }

    [Fact]
    public void ReplaceIntervals_ForeignPlanInterval_ThrowsBeforeAnyWrite()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        (Guid profileId, Guid targetId, Guid exposurePlanId) = SeedIntentChain(store);
        PlanWriter writer = new(store);
        Guid planId = Guid.NewGuid();
        writer.UpsertPlan(new PlanIntent
        {
            Id = planId, ProfileId = profileId, NightOf = "2026-08-13",
            AuthoredById = 0, CreatedAt = 1_755_000_000,
        });
        writer.ReplaceIntervals(planId,
            [Interval(planId, 1, targetId, exposurePlanId, 1_755_100_000, 1_755_103_600)]);

        Assert.Throws<IntentStoreException>(() => writer.ReplaceIntervals(planId,
            [Interval(Guid.NewGuid(), 1, targetId, exposurePlanId, 1_755_100_000, 1_755_103_600)]));

        Assert.Single(writer.ReadIntervals(planId));   // the existing set survived the refused call
    }

    [Fact]
    public void CallerRollback_DiscardsPlanAndIntervalsTogether()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        (Guid profileId, Guid targetId, Guid exposurePlanId) = SeedIntentChain(store);
        PlanWriter writer = new(store);
        Guid planId = Guid.NewGuid();

        using (SqliteTransaction tx = store.Connection.BeginTransaction())
        {
            writer.UpsertPlan(new PlanIntent
            {
                Id = planId, ProfileId = profileId, NightOf = "2026-08-13",
                AuthoredById = 0, CreatedAt = 1_755_000_000,
            }, tx);
            writer.ReplaceIntervals(planId,
                [Interval(planId, 1, targetId, exposurePlanId, 1_755_100_000, 1_755_103_600)], tx);
            tx.Rollback();
        }

        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM plan;"));
        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM plan_interval;"));
    }

    [Fact]
    public void FindCurrentPlan_ExcludesSuperseded_NullWhenNone_DuplicateLoud()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        (Guid profileId, _, _) = SeedIntentChain(store);
        PlanWriter writer = new(store);

        Assert.Null(writer.FindCurrentPlan(profileId, "2026-08-13"));   // none yet

        PlanIntent first = new()
        {
            Id = Guid.NewGuid(), ProfileId = profileId, NightOf = "2026-08-13",
            AuthoredById = 0, CreatedAt = 1_755_000_000,
        };
        writer.UpsertPlan(first);
        writer.UpsertPlan(first with { StateId = 2 });                  // superseded (start-over)
        PlanIntent second = first with { Id = Guid.NewGuid(), CreatedAt = 1_755_000_100 };
        writer.UpsertPlan(second);

        Assert.Equal(second.Id, writer.FindCurrentPlan(profileId, "2026-08-13")!.Id);

        // A second live plan for the night (integrity violation) resolves loudly, never silently.
        writer.UpsertPlan(first with { StateId = 0 });
        Assert.Throws<IntentStoreException>(() => writer.FindCurrentPlan(profileId, "2026-08-13"));
    }

    // ---- fixtures -----------------------------------------------------------------------------

    private static PlanIntervalIntent Interval(
        Guid planId, long sequence, Guid targetId, Guid exposurePlanId, long start, long end,
        bool amended = false) => new()
    {
        Id = Guid.NewGuid(), PlanId = planId, SequenceNumber = sequence,
        TargetId = targetId, ExposurePlanId = exposurePlanId,
        StartAt = start, EndAt = end, AmendedByUser = amended,
    };

    /// <summary>Seeds the minimal intent chain plan intervals reference: profile → project →
    /// target → template → exposure plan.</summary>
    private static (Guid ProfileId, Guid TargetId, Guid ExposurePlanId) SeedIntentChain(IntentStore store)
    {
        Guid profileId = Guid.NewGuid();
        using (SqliteCommand command = store.Connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO profile (id, name, created_at) VALUES ($id, 'Test', 0);";
            command.Parameters.AddWithValue("$id", GuidBlob.ToBlob(profileId));
            command.ExecuteNonQuery();
        }

        IntentWriter writer = new(store);
        Guid projectId = Guid.NewGuid(), targetId = Guid.NewGuid(), templateId = Guid.NewGuid(), planId = Guid.NewGuid();
        writer.UpsertProject(new ProjectIntent
        {
            Id = projectId, ProfileId = profileId, Name = "P", StateId = 1, PriorityId = 1,
            HorizonOffsetDeg = 0, CreatedAt = 0,
        });
        writer.UpsertTarget(new TargetIntent
        {
            Id = targetId, ProjectId = projectId, Name = "T", RaHours = 20.0, DecDegreesSigned = 35.0,
            CreatedAt = 0,
        });
        writer.UpsertExposureTemplate(new ExposureTemplateIntent
        {
            Id = templateId, ProfileId = profileId, Name = "Ha", FilterName = "Ha", Binning = 1,
            DefaultExposureSeconds = 300.0, TwilightLevelId = 1,
        });
        writer.UpsertExposurePlan(new ExposurePlanIntent
        {
            Id = planId, TargetId = targetId, ExposureTemplateId = templateId, DesiredCount = 40,
        });
        return (profileId, targetId, planId);
    }

    private static string NewStorePath() =>
        Path.Combine(Directory.CreateTempSubdirectory("plan-writer-").FullName, "intent.db");

    private static object? Scalar(IntentStore store, string sql)
    {
        using SqliteCommand cmd = store.Connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
