using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Intent;

/// <summary>
/// The intent store's plan-plane write/read surface (<c>plan</c> / <c>plan_interval</c>) — sibling
/// to <see cref="IntentWriter"/>, kept separate because the plan plane's access shapes differ:
/// by-night reads and whole-set interval replacement rather than provenance-keyed upserts.
/// <see cref="UpsertPlan"/> follows the intent-plane contract exactly: full-value keyed by the
/// caller-supplied id, <c>created_at</c> written on create only.
/// <see cref="ReplaceIntervals"/> is the authoring write — the plan's interval set is replaced
/// whole with the supplied ordered rows (one logical operation; group it with the plan upsert
/// under a caller transaction for an atomic save). Every operation composes with a caller-owned
/// <see cref="SqliteTransaction"/>; the writer never begins, commits, or rolls back one of its own.
/// </summary>
public sealed class PlanWriter
{
    private readonly IntentStore _store;

    /// <summary>Creates a writer over <paramref name="store"/> (which stays owned by the caller).</summary>
    public PlanWriter(IntentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Creates or fully updates a <c>plan</c> row keyed by <see cref="PlanIntent.Id"/>.</summary>
    public void UpsertPlan(PlanIntent row, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        using SqliteCommand command = _store.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO plan (id, profile_id, night_of, state_id, authored_by_id, switch_immediately,
                created_at, blessed_at)
            VALUES ($id, $profile, $night, $state, $authored, $switch, $created, $blessed)
            ON CONFLICT(id) DO UPDATE SET
                profile_id = excluded.profile_id, night_of = excluded.night_of,
                state_id = excluded.state_id, authored_by_id = excluded.authored_by_id,
                switch_immediately = excluded.switch_immediately, blessed_at = excluded.blessed_at;
            """;
        command.Parameters.AddWithValue("$id", GuidBlob.ToBlob(row.Id));
        command.Parameters.AddWithValue("$profile", GuidBlob.ToBlob(row.ProfileId));
        command.Parameters.AddWithValue("$night", row.NightOf);
        command.Parameters.AddWithValue("$state", row.StateId);
        command.Parameters.AddWithValue("$authored", row.AuthoredById);
        command.Parameters.AddWithValue("$switch", row.SwitchImmediately ? 1 : 0);
        command.Parameters.AddWithValue("$created", row.CreatedAt);
        command.Parameters.AddWithValue("$blessed", row.BlessedAt is long b ? b : DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Replaces the whole interval set of <paramref name="planId"/> with <paramref name="intervals"/>
    /// (ordered as supplied; <c>sequence_number</c> comes from each row). A supplied interval naming a
    /// different plan is a caller bug, thrown loudly before any write.
    /// </summary>
    public void ReplaceIntervals(
        Guid planId, IReadOnlyList<PlanIntervalIntent> intervals, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        foreach (PlanIntervalIntent interval in intervals)
        {
            if (interval.PlanId != planId)
            {
                throw new IntentStoreException(
                    $"Intent store: ReplaceIntervals for plan {planId} was handed an interval " +
                    $"(sequence {interval.SequenceNumber}) belonging to plan {interval.PlanId}.");
            }
        }

        using (SqliteCommand delete = _store.Connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM plan_interval WHERE plan_id = $plan;";
            delete.Parameters.AddWithValue("$plan", GuidBlob.ToBlob(planId));
            delete.ExecuteNonQuery();
        }

        foreach (PlanIntervalIntent interval in intervals)
        {
            using SqliteCommand insert = _store.Connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO plan_interval (id, plan_id, sequence_number, target_id, exposure_plan_id,
                    start_at, end_at, amended_by_user)
                VALUES ($id, $plan, $seq, $target, $eplan, $start, $end, $amended);
                """;
            insert.Parameters.AddWithValue("$id", GuidBlob.ToBlob(interval.Id));
            insert.Parameters.AddWithValue("$plan", GuidBlob.ToBlob(interval.PlanId));
            insert.Parameters.AddWithValue("$seq", interval.SequenceNumber);
            insert.Parameters.AddWithValue("$target", GuidBlob.ToBlob(interval.TargetId));
            insert.Parameters.AddWithValue("$eplan", GuidBlob.ToBlob(interval.ExposurePlanId));
            insert.Parameters.AddWithValue("$start", interval.StartAt);
            insert.Parameters.AddWithValue("$end", interval.EndAt);
            insert.Parameters.AddWithValue("$amended", interval.AmendedByUser ? 1 : 0);
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The profile+night's single non-superseded plan (draft or blessed), or <see langword="null"/>
    /// when none exists. Two non-superseded plans for one night is a data-integrity violation,
    /// thrown loudly — never disambiguated silently.
    /// </summary>
    public PlanIntent? FindCurrentPlan(Guid profileId, string nightOf, SqliteTransaction? transaction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nightOf);

        using SqliteCommand command = _store.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, profile_id, night_of, state_id, authored_by_id, switch_immediately, created_at, blessed_at
            FROM plan WHERE profile_id = $profile AND night_of = $night AND state_id != 2;
            """;
        command.Parameters.AddWithValue("$profile", GuidBlob.ToBlob(profileId));
        command.Parameters.AddWithValue("$night", nightOf);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        PlanIntent plan = ReadPlan(reader);
        if (reader.Read())
        {
            throw new IntentStoreException(
                $"Intent store: profile {profileId} night {nightOf} has more than one non-superseded plan — " +
                "a data-integrity violation, not something to disambiguate silently.");
        }

        return plan;
    }

    /// <summary>The plan's intervals ordered by <c>sequence_number</c> (empty when none).</summary>
    public IReadOnlyList<PlanIntervalIntent> ReadIntervals(Guid planId, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = _store.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, plan_id, sequence_number, target_id, exposure_plan_id, start_at, end_at, amended_by_user
            FROM plan_interval WHERE plan_id = $plan ORDER BY sequence_number;
            """;
        command.Parameters.AddWithValue("$plan", GuidBlob.ToBlob(planId));

        List<PlanIntervalIntent> intervals = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            intervals.Add(new PlanIntervalIntent
            {
                Id = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(0)),
                PlanId = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(1)),
                SequenceNumber = reader.GetInt64(2),
                TargetId = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(3)),
                ExposurePlanId = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(4)),
                StartAt = reader.GetInt64(5),
                EndAt = reader.GetInt64(6),
                AmendedByUser = reader.GetInt64(7) != 0,
            });
        }

        return intervals;
    }

    private static PlanIntent ReadPlan(SqliteDataReader reader) => new()
    {
        Id = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(0)),
        ProfileId = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(1)),
        NightOf = reader.GetString(2),
        StateId = reader.GetInt32(3),
        AuthoredById = reader.GetInt32(4),
        SwitchImmediately = reader.GetInt64(5) != 0,
        CreatedAt = reader.GetInt64(6),
        BlessedAt = reader.IsDBNull(7) ? null : reader.GetInt64(7),
    };
}
