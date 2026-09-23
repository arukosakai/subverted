using Microsoft.Data.Sqlite;
using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Reads SVN's working-copy metadata straight out of <c>.svn/wc.db</c> instead of shelling out
/// to <c>svn status</c>. The schema is private to Subversion, so every read is version-gated and
/// any surprise is reported as <see cref="WcDbException"/> for the caller to fall back on.
/// </summary>
public sealed class WcDbReader : IDisposable
{
    // Format 29 shipped with SVN 1.7, 31 with 1.8 and every release through 1.14.
    private const int MinSupportedFormat = 29;
    private const int MaxSupportedFormat = 31;

    private readonly SqliteConnection _connection;
    private readonly long _wcId;

    public WorkingCopyInfo Info { get; }

    private WcDbReader(SqliteConnection connection, long wcId, WorkingCopyInfo info)
    {
        _connection = connection;
        _wcId = wcId;
        Info = info;
    }

    /// <summary>
    /// Walks up from <paramref name="startPath"/> to the working-copy root and opens its wc.db.
    /// </summary>
    /// <exception cref="WcDbException">No working copy found, or its schema is not understood.</exception>
    public static WcDbReader Open(string startPath)
    {
        var dbPath =
            LocateDatabase(startPath)
            ?? throw new WcDbException(
                WcDbFailure.NotAWorkingCopy,
                $"No .svn/wc.db found at or above '{startPath}'."
            );

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString()
        );

        try
        {
            connection.Open();

            var format = (int)ExecuteScalar<long>(connection, "PRAGMA user_version;");
            if (format is < MinSupportedFormat or > MaxSupportedFormat)
            {
                throw new WcDbException(
                    WcDbFailure.Unreadable,
                    $"wc.db format {format} is outside the supported range "
                        + $"{MinSupportedFormat}-{MaxSupportedFormat}."
                );
            }

            var (wcId, rootPath) = ReadWcRoot(connection, dbPath);
            var (repoRoot, repoUuid) = ReadRepository(connection, wcId);

            return new WcDbReader(
                connection,
                wcId,
                new WorkingCopyInfo(rootPath, repoRoot, repoUuid, format)
            );
        }
        catch (SqliteException ex)
        {
            connection.Dispose();
            throw new WcDbException(
                WcDbFailure.Unreadable,
                $"Could not read '{dbPath}': {ex.Message}",
                ex
            );
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every versioned node, collapsed to its highest op_depth so local tree changes win over BASE.
    /// The working-copy root is included, under the empty relative path — <c>svn status</c> reports
    /// it as <c>.</c> when its own properties change, and it is a directory like any other.
    ///
    /// The revision is the one exception to that collapse: it comes from BASE, because that is what
    /// <c>svn status -v</c> prints. See <see cref="WcDbRow.BaseRevision"/>.
    /// </summary>
    internal IEnumerable<WcDbRow> ReadNodes() => Read(wholeTree: true, relPath: null);

    /// <summary>
    /// The same projection for a single path, so a change to one file can be re-resolved without
    /// reading a hundred thousand rows to find it.
    /// </summary>
    /// <returns><see langword="null"/> when wc.db has no node there — the path is unversioned.</returns>
    internal WcDbRow? ReadNode(string relPath) => Read(wholeTree: false, relPath).FirstOrDefault();

    private IEnumerable<WcDbRow> Read(bool wholeTree, string? relPath)
    {
        const string projection = """
            SELECT
                n.local_relpath,
                n.op_depth,
                n.presence,
                n.kind,
                -- The working revision is BASE's, never the projected row's. At op_depth > 0 that
                -- column holds the copyfrom revision for a copy and nothing at all for a delete,
                -- so reading it prints a copy's source where `svn status -v` prints `-`, and
                -- prints nothing for a delete where svn prints the revision being deleted.
                b.revision AS base_revision,
                n.translated_size,
                n.last_mod_time,
                -- Gated because BASE has nothing beneath it by definition: ungated, this subquery
                -- ran once per row and cost ~100 ms of a 101,001-node read; gated, ~10.
                CASE WHEN n.op_depth > 0 THEN (
                    SELECT MAX(n3.op_depth) FROM nodes n3
                    WHERE n3.wc_id = n.wc_id AND n3.local_relpath = n.local_relpath
                      AND n3.op_depth < n.op_depth
                ) END AS lower_op_depth,
                a.conflict_data IS NOT NULL AS has_conflict,
                a.changelist,
                l.lock_token IS NOT NULL AS has_lock,
                n.checksum,
                -- Locally changed properties live in actual_node and win over the pristine set;
                -- a NULL there means "unchanged", not "none". Both are needed: the effective set
                -- drives the checksum fast path, the pair of them drives property status.
                a.properties AS working_properties,
                n.properties AS pristine_properties,
                n.repos_id IS NOT NULL AS has_copy_source
            FROM nodes n
            LEFT JOIN nodes b
                ON b.wc_id = n.wc_id AND b.local_relpath = n.local_relpath AND b.op_depth = 0
            LEFT JOIN actual_node a
                ON a.wc_id = n.wc_id AND a.local_relpath = n.local_relpath
            LEFT JOIN lock l
                ON l.repos_id = n.repos_id AND l.repos_relpath = n.repos_path
            WHERE n.wc_id = $wcId
              AND n.op_depth = (
                    SELECT MAX(n2.op_depth) FROM nodes n2
                    WHERE n2.wc_id = n.wc_id AND n2.local_relpath = n.local_relpath
              )
            """;

        using var command = _connection.CreateCommand();
        command.CommandText = wholeTree
            ? $"{projection} ORDER BY n.local_relpath;"
            : $"{projection} AND n.local_relpath = $relPath;";
        command.Parameters.AddWithValue("$wcId", _wcId);
        if (!wholeTree)
        {
            command.Parameters.AddWithValue("$relPath", relPath);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var nodeRelPath = reader.GetString(0);
            var opDepth = reader.GetInt32(1);
            var presence = reader.IsDBNull(2) ? "normal" : reader.GetString(2);
            var workingProperties = reader.IsDBNull(12) ? null : reader.GetFieldValue<byte[]>(12);
            var pristineProperties = reader.IsDBNull(13) ? null : reader.GetFieldValue<byte[]>(13);

            yield return new WcDbRow(
                RelPath: nodeRelPath,
                OpDepth: opDepth,
                Presence: presence,
                Kind: ParseKind(reader.IsDBNull(3) ? null : reader.GetString(3)),
                BaseRevision: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                RawTranslatedSize: reader.IsDBNull(5) ? null : reader.GetInt64(5),
                RecordedModTime: reader.IsDBNull(6) ? null : reader.GetInt64(6),
                LowerOpDepth: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                HasCopySource: reader.GetBoolean(14),
                HasConflict: reader.GetBoolean(8),
                Changelist: reader.IsDBNull(9) ? null : reader.GetString(9),
                HasLockToken: reader.GetBoolean(10),
                Checksum: reader.IsDBNull(11) ? null : reader.GetString(11),
                IsTranslated: SvnProperties.IsTranslated(workingProperties ?? pristineProperties),
                PropertyStatus: PropertyStatusResolver.Resolve(
                    opDepth,
                    WcDbRow.IsWithinCopyAt(nodeRelPath, opDepth, presence),
                    workingProperties,
                    pristineProperties
                )
            );
        }
    }

    /// <summary>
    /// Where this working copy's <c>svn:externals</c> are checked out, as relative paths.
    /// </summary>
    /// <remarks>
    /// Externals have no NODES row at all — they are a separate working copy with its own
    /// <c>.svn</c>, registered only here. Without this they read as unversioned directories, which
    /// tells the user to add something they must not.
    /// </remarks>
    internal IReadOnlyList<ExternalNode> ReadExternals()
    {
        const string sql = """
            SELECT local_relpath, kind FROM externals
            WHERE wc_id = $wcId
            ORDER BY local_relpath;
            """;

        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$wcId", _wcId);

        var externals = new List<ExternalNode>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            externals.Add(
                new ExternalNode(
                    reader.GetString(0).Replace('\\', '/'),
                    ParseKind(reader.IsDBNull(1) ? null : reader.GetString(1))
                )
            );
        }

        return externals;
    }

    /// <summary>
    /// Every versioned directory's ignore globs, keyed by relative path with the root as the empty
    /// string. Directories declaring nothing are included, because the inheritance chain that
    /// <see cref="WorkingCopyIgnoreRules"/> builds needs an unbroken path back to the root.
    /// </summary>
    internal IReadOnlyDictionary<string, DirectoryIgnorePatterns> ReadDirectoryIgnorePatterns()
    {
        const string sql = """
            SELECT
                n.local_relpath,
                COALESCE(a.properties, n.properties)
            FROM nodes n
            LEFT JOIN actual_node a
                ON a.wc_id = n.wc_id AND a.local_relpath = n.local_relpath
            WHERE n.wc_id = $wcId
              AND n.kind = 'dir'
              AND n.op_depth = (
                    SELECT MAX(n2.op_depth) FROM nodes n2
                    WHERE n2.wc_id = n.wc_id AND n2.local_relpath = n.local_relpath
              );
            """;

        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$wcId", _wcId);

        var patterns = new Dictionary<string, DirectoryIgnorePatterns>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            patterns[reader.GetString(0)] = DirectoryIgnorePatterns.FromProperties(
                reader.IsDBNull(1) ? null : reader.GetFieldValue<byte[]>(1)
            );
        }

        return patterns;
    }

    /// <summary>
    /// The write locks held over this working copy — normally none. A row left behind by a client
    /// that crashed is what makes every later <c>svn</c> operation fail with <c>E155004</c>, and
    /// what <c>sv cleanup</c> releases.
    /// </summary>
    internal IReadOnlyList<WorkingCopyWriteLock> ReadWriteLocks()
    {
        const string sql = """
            SELECT local_dir_relpath, locked_levels FROM wc_lock
            WHERE wc_id = $wcId
            ORDER BY local_dir_relpath;
            """;

        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$wcId", _wcId);

        var locks = new List<WorkingCopyWriteLock>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            locks.Add(
                new WorkingCopyWriteLock(reader.GetString(0).Replace('\\', '/'), reader.GetInt32(1))
            );
        }

        return locks;
    }

    /// <summary>
    /// How many steps of an interrupted operation are still queued. Any at all and <c>svn</c> itself
    /// refuses to read the working copy — <c>E155037</c>, from <c>svn status</c> included — until
    /// <c>svn cleanup</c> runs them.
    /// </summary>
    /// <remarks>
    /// <c>WORK_QUEUE</c> carries no <c>wc_id</c>, so this counts the database rather than the root;
    /// for the single-root layout every working copy has, those are the same thing.
    /// </remarks>
    internal int CountUnfinishedWork() =>
        (int)ExecuteScalar<long>(_connection, "SELECT COUNT(*) FROM work_queue;");

    /// <summary>
    /// The lowest and highest BASE revision at and below a node — what <c>svnversion</c> prints as
    /// <c>3:5</c>. Null when nothing there has a BASE, as for a node that is only added.
    /// </summary>
    /// <param name="relPath">Slash-separated, relative to <see cref="WorkingCopyInfo.RootPath"/>; empty for the root.</param>
    /// <remarks>
    /// Checked against <c>svnversion</c> on 1.8.15 for a mixed, a switched, a sparse and an excluded
    /// tree: only <c>normal</c> and <c>incomplete</c> BASE rows count — a <c>not-present</c> row
    /// left by updating a file to before it existed would otherwise widen the range — and a file
    /// external's own revision does not count either.
    /// </remarks>
    internal BaseRevisionRange? ReadBaseRevisionRange(string relPath)
    {
        // The descendant test is a range rather than a LIKE so it can use the (wc_id,
        // local_relpath) index: every strict descendant sorts between "p/" and "p0".
        const string sql = """
            SELECT MIN(revision), MAX(revision) FROM nodes
            WHERE wc_id = $wcId
              AND op_depth = 0
              AND presence IN ('normal', 'incomplete')
              AND file_external IS NULL
              AND ($relPath = ''
                   OR local_relpath = $relPath
                   OR (local_relpath > $relPath || '/' AND local_relpath < $relPath || '0'));
            """;

        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$wcId", _wcId);
        command.Parameters.AddWithValue("$relPath", relPath);

        using var reader = command.ExecuteReader();
        reader.Read();
        return reader.IsDBNull(0)
            ? null
            : new BaseRevisionRange(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static string? LocateDatabase(string startPath)
    {
        var dir = Directory.Exists(startPath)
            ? new DirectoryInfo(startPath)
            : new FileInfo(startPath).Directory;

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".svn", "wc.db");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static (long WcId, string RootPath) ReadWcRoot(
        SqliteConnection connection,
        string dbPath
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, local_abspath FROM wcroot ORDER BY id LIMIT 1;";

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new WcDbException(WcDbFailure.Unreadable, $"'{dbPath}' has no wcroot row.");
        }

        // local_abspath is NULL for the common single-root layout; the root is then wc.db's grandparent.
        var rootPath = reader.IsDBNull(1)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dbPath)!, ".."))
            : reader.GetString(1);

        return (reader.GetInt64(0), rootPath);
    }

    private static (string Root, string Uuid) ReadRepository(SqliteConnection connection, long wcId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.root, r.uuid
            FROM repository r
            JOIN nodes n ON n.repos_id = r.id
            WHERE n.wc_id = $wcId AND n.local_relpath = '' AND n.op_depth = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$wcId", wcId);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetString(0), reader.GetString(1))
            : (string.Empty, string.Empty);
    }

    private static T ExecuteScalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }

    private static NodeKind ParseKind(string? kind) =>
        kind switch
        {
            "file" => NodeKind.File,
            "dir" => NodeKind.Directory,
            "symlink" => NodeKind.Symlink,
            _ => NodeKind.Unknown,
        };

    public void Dispose() => _connection.Dispose();
}
