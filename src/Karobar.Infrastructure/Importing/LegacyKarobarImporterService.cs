using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Constants;
using Karobar.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Karobar.Infrastructure.Importing;

/// <summary>
/// One-Click Legacy Karobar Importer Service.
/// Imports Full Backups (.zip), Database Backups (.sql.gz, .sql, .sqlite, .db), Settings (.json),
/// and Live Local MySQL/MariaDB databases directly into the native Windows SQLite database.
/// </summary>
public sealed class LegacyKarobarImporterService : ILegacyKarobarImporterService
{
    private static readonly byte[] SqliteMagicHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    private readonly IDatabaseInitializerService _dbInitializer;
    private readonly ILicenseService _licenseService;
    private readonly ILedgerRebuildService _ledgerRebuildService;
    private readonly IDbContextFactory<KarobarDbContext> _dbFactory;
    private readonly ILogger<LegacyKarobarImporterService> _logger;

    public LegacyKarobarImporterService(
        IDatabaseInitializerService dbInitializer,
        ILicenseService licenseService,
        ILedgerRebuildService ledgerRebuildService,
        IDbContextFactory<KarobarDbContext> dbFactory,
        ILogger<LegacyKarobarImporterService> logger)
    {
        _dbInitializer = dbInitializer;
        _licenseService = licenseService;
        _ledgerRebuildService = ledgerRebuildService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<ImportVerificationReport> ImportFromFileAsync(
        string filePath,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new ImportVerificationReport
        {
            SourceName = Path.GetFileName(filePath)
        };

        if (!File.Exists(filePath))
        {
            report.Success = false;
            report.Message = $"File not found: {filePath}";
            return report;
        }

        await _dbInitializer.InitializeAndSeedAsync(cancellationToken);

        // Snapshot local machine's valid license before importing settings
        var preservedLicense = await SnapshotLocalLicenseAsync(cancellationToken);

        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            progress?.Report(new ImportProgressUpdate
            {
                Percentage = 5,
                Stage = "Inspecting backup file",
                Detail = $"Analyzing {report.SourceName}..."
            });

            if (ext == ".zip" || IsZipFile(filePath))
            {
                report.SourceType = "Full Backup Archive (.zip)";
                await ImportFullZipArchiveAsync(filePath, report, progress, cancellationToken);
            }
            else if (ext == ".json")
            {
                report.SourceType = "Settings Backup (.json)";
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                await ApplySettingsJsonAsync(json, report, cancellationToken);
            }
            else
            {
                report.SourceType = "Database Backup (.sql.gz / .sql / .sqlite)";
                await ImportDatabasePayloadFileAsync(filePath, report, progress, cancellationToken);
            }

            // Restore local Windows machine's license if valid, or clear foreign machine license
            await RestoreOrSanitizeLicenseAsync(preservedLicense, cancellationToken);

            // Re-seed any missing permissions/COA codes introduced in newer versions
            progress?.Report(new ImportProgressUpdate
            {
                Percentage = 90,
                Stage = "Synchronizing RBAC & Ledgers",
                Detail = "Ensuring all 145 permissions, Chart of Accounts, and Ledgers are synchronized..."
            });

            await _dbInitializer.InitializeAndSeedAsync(cancellationToken);
            await _ledgerRebuildService.RebuildAllCustomerLedgersAsync(cancellationToken);
            await _ledgerRebuildService.RebuildAllSupplierLedgersAsync(cancellationToken);

            report.PostImportStats = await _dbInitializer.GetOverviewStatsAsync(cancellationToken);
            sw.Stop();
            report.Elapsed = sw.Elapsed;
            report.Success = true;
            report.Message = $"Successfully imported {report.TotalRowsImported:N0} rows across {report.ImportedTableRowCounts.Count} tables in {report.Elapsed.TotalSeconds:F2}s.";

            progress?.Report(new ImportProgressUpdate
            {
                Percentage = 100,
                Stage = "Import Complete",
                Detail = report.Message
            });

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Legacy Karobar import failed for {FilePath}", filePath);
            sw.Stop();
            report.Elapsed = sw.Elapsed;
            report.Success = false;
            report.Message = $"Import failed: {ex.Message}";
            return report;
        }
    }

    public async Task<ImportVerificationReport> ImportFromMySqlAsync(
        MySqlConnectionOptions options,
        IProgress<ImportProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new ImportVerificationReport
        {
            SourceType = "Live Local MySQL / MariaDB Server",
            SourceName = $"{options.Host}:{options.Port}/{options.Database}"
        };

        await _dbInitializer.InitializeAndSeedAsync(cancellationToken);
        var preservedLicense = await SnapshotLocalLicenseAsync(cancellationToken);

        try
        {
            var connBuilder = new MySqlConnectionStringBuilder
            {
                Server = options.Host,
                Port = (uint)options.Port,
                Database = options.Database,
                UserID = options.Username,
                Password = options.Password,
                ConvertZeroDateTime = true,
                AllowZeroDateTime = true
            };

            progress?.Report(new ImportProgressUpdate
            {
                Percentage = 5,
                Stage = "Connecting to MySQL",
                Detail = $"Connecting to {report.SourceName}..."
            });

            await using var mysqlConn = new MySqlConnection(connBuilder.ConnectionString);
            await mysqlConn.OpenAsync(cancellationToken);

            // Discover available tables in MySQL
            var mysqlTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var showCmd = new MySqlCommand("SHOW TABLES;", mysqlConn))
            await using (var reader = await showCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    mysqlTables.Add(reader.GetString(0));
                }
            }

            await using var sqliteConn = new SqliteConnection($"Data Source={_dbInitializer.DatabaseFilePath}");
            await sqliteConn.OpenAsync(cancellationToken);

            await ExecuteSqliteNonQueryAsync(sqliteConn, "PRAGMA foreign_keys = OFF;", cancellationToken);
            await using var tx = (SqliteTransaction)await sqliteConn.BeginTransactionAsync(cancellationToken);

            try
            {
                // Clear target tables in reverse dependency order
                foreach (var table in DomainSeedConstants.OrderedTablesForImport.Reverse())
                {
                    if (mysqlTables.Contains(table))
                    {
                        await ExecuteSqliteNonQueryAsync(sqliteConn, $"DELETE FROM \"{table}\";", cancellationToken, tx);
                    }
                }

                var totalTables = DomainSeedConstants.OrderedTablesForImport.Length;
                for (var i = 0; i < totalTables; i++)
                {
                    var table = DomainSeedConstants.OrderedTablesForImport[i];
                    if (!mysqlTables.Contains(table))
                        continue;

                    var pct = 10 + (int)((i / (double)totalTables) * 75);
                    progress?.Report(new ImportProgressUpdate
                    {
                        Percentage = pct,
                        Stage = $"Importing MySQL table: {table}",
                        Detail = $"Streaming {table} ({i + 1}/{totalTables})..."
                    });

                    var sqliteCols = await GetSqliteTableColumnsAsync(sqliteConn, "main", table, tx, cancellationToken);
                    if (sqliteCols.Count == 0)
                        continue;

                    await using var selectCmd = new MySqlCommand($"SELECT * FROM `{table}`;", mysqlConn);
                    await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);

                    var sharedCols = new List<(int Ordinal, string Name)>();
                    for (var colIdx = 0; colIdx < reader.FieldCount; colIdx++)
                    {
                        var colName = reader.GetName(colIdx);
                        if (sqliteCols.Contains(colName))
                        {
                            sharedCols.Add((colIdx, colName));
                        }
                    }

                    if (sharedCols.Count == 0)
                        continue;

                    var colListSql = string.Join(", ", sharedCols.Select(c => $"\"{c.Name}\""));
                    var paramListSql = string.Join(", ", sharedCols.Select((_, idx) => $"@p{idx}"));
                    var insertSql = $"INSERT INTO \"{table}\" ({colListSql}) VALUES ({paramListSql});";

                    await using var insertCmd = new SqliteCommand(insertSql, sqliteConn, tx);
                    var parameters = new SqliteParameter[sharedCols.Count];
                    for (var pIdx = 0; pIdx < sharedCols.Count; pIdx++)
                    {
                        parameters[pIdx] = insertCmd.Parameters.Add($"@p{pIdx}", SqliteType.Text);
                    }

                    var rowCount = 0;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        for (var pIdx = 0; pIdx < sharedCols.Count; pIdx++)
                        {
                            var val = reader.GetValue(sharedCols[pIdx].Ordinal);
                            parameters[pIdx].Value = val is DBNull ? DBNull.Value : val;
                        }
                        await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                        rowCount++;
                    }

                    report.ImportedTableRowCounts[table] = rowCount;
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            finally
            {
                await ExecuteSqliteNonQueryAsync(sqliteConn, "PRAGMA foreign_keys = ON;", cancellationToken);
            }

            await RestoreOrSanitizeLicenseAsync(preservedLicense, cancellationToken);
            await _dbInitializer.InitializeAndSeedAsync(cancellationToken);
            await _ledgerRebuildService.RebuildAllCustomerLedgersAsync(cancellationToken);
            await _ledgerRebuildService.RebuildAllSupplierLedgersAsync(cancellationToken);

            report.PostImportStats = await _dbInitializer.GetOverviewStatsAsync(cancellationToken);
            sw.Stop();
            report.Elapsed = sw.Elapsed;
            report.Success = true;
            report.Message = $"Imported {report.TotalRowsImported:N0} rows from MySQL ({report.SourceName}) in {report.Elapsed.TotalSeconds:F2}s.";
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL import failed for {Source}", report.SourceName);
            sw.Stop();
            report.Elapsed = sw.Elapsed;
            report.Success = false;
            report.Message = $"MySQL import failed: {ex.Message}";
            return report;
        }
    }

    private async Task ImportFullZipArchiveAsync(
        string zipPath,
        ImportVerificationReport report,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var tempExtractDir = Path.Combine(Path.GetTempPath(), "karobar_import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempExtractDir);

        try
        {
            progress?.Report(new ImportProgressUpdate
            {
                Percentage = 15,
                Stage = "Extracting Full Backup (.zip)",
                Detail = "Unpacking database snapshot, settings, and store logos..."
            });

            ZipFile.ExtractToDirectory(zipPath, tempExtractDir, overwriteFiles: true);

            // 1. Copy store logo / files if present
            var filesDir = Path.Combine(tempExtractDir, "files");
            if (Directory.Exists(filesDir))
            {
                CopyDirectoryRecursive(filesDir, _dbInitializer.StorageDirectoryPath);
            }

            // 2. Locate database dump inside extracted archive
            var dbCandidates = new[]
            {
                Path.Combine(tempExtractDir, "database", "database.sql.gz"),
                Path.Combine(tempExtractDir, "database", "database.sql"),
                Path.Combine(tempExtractDir, "database", "database.sqlite"),
                Path.Combine(tempExtractDir, "database.sql.gz"),
                Path.Combine(tempExtractDir, "database.sql"),
                Path.Combine(tempExtractDir, "database.sqlite")
            };

            var dbFile = dbCandidates.FirstOrDefault(File.Exists)
                ?? Directory.EnumerateFiles(tempExtractDir, "*.*", SearchOption.AllDirectories)
                    .FirstOrDefault(f =>
                        f.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".db", StringComparison.OrdinalIgnoreCase));

            if (dbFile != null)
            {
                await ImportDatabasePayloadFileAsync(dbFile, report, progress, cancellationToken);
            }
            else
            {
                report.Warnings.Add("No database file found inside ZIP archive; checked settings.json only.");
            }

            // 3. Apply settings.json if present
            var settingsJsonPath = Path.Combine(tempExtractDir, "settings.json");
            if (File.Exists(settingsJsonPath))
            {
                var json = await File.ReadAllTextAsync(settingsJsonPath, cancellationToken);
                await ApplySettingsJsonAsync(json, report, cancellationToken);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempExtractDir))
                    Directory.Delete(tempExtractDir, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup locks
            }
        }
    }

    private async Task ImportDatabasePayloadFileAsync(
        string payloadFilePath,
        ImportVerificationReport report,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        string workingFile = payloadFilePath;
        string? tempDecompressedFile = null;

        try
        {
            if (IsGzipFile(payloadFilePath))
            {
                progress?.Report(new ImportProgressUpdate
                {
                    Percentage = 25,
                    Stage = "Decompressing GZIP Backup",
                    Detail = "Decompressing .sql.gz database stream..."
                });

                tempDecompressedFile = Path.Combine(Path.GetTempPath(), $"karobar_raw_{Guid.NewGuid():N}.dat");
                await using (var input = File.OpenRead(payloadFilePath))
                await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                await using (var output = File.Create(tempDecompressedFile))
                {
                    await gzip.CopyToAsync(output, cancellationToken);
                }
                workingFile = tempDecompressedFile;
            }

            if (IsBinarySqliteFile(workingFile))
            {
                report.SourceType = "Binary SQLite Database Snapshot";
                await ImportBinarySqliteSnapshotAsync(workingFile, report, progress, cancellationToken);
            }
            else
            {
                report.SourceType = "SQL Dump (MySQL / SQLite)";
                var sqlContent = await File.ReadAllTextAsync(workingFile, cancellationToken);
                await ImportSqlDumpTextAsync(sqlContent, report, progress, cancellationToken);
            }
        }
        finally
        {
            if (tempDecompressedFile != null && File.Exists(tempDecompressedFile))
            {
                try { File.Delete(tempDecompressedFile); } catch { }
            }
        }
    }

    /// <summary>
    /// Attaches a binary SQLite snapshot (created via VACUUM INTO or raw database.sqlite)
    /// and copies all 56 tables by intersecting column schemas.
    /// </summary>
    private async Task ImportBinarySqliteSnapshotAsync(
        string sourceSqlitePath,
        ImportVerificationReport report,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbInitializer.DatabaseFilePath}");
        await conn.OpenAsync(cancellationToken);

        await ExecuteSqliteNonQueryAsync(conn, "PRAGMA foreign_keys = OFF;", cancellationToken);

        await using (var attachCmd = conn.CreateCommand())
        {
            attachCmd.CommandText = "ATTACH DATABASE @src AS legacy_src;";
            attachCmd.Parameters.AddWithValue("@src", sourceSqlitePath);
            await attachCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            // Discover tables in legacy_src
            var srcTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var tblCmd = new SqliteCommand(
                "SELECT name FROM legacy_src.sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';",
                conn, tx))
            await using (var reader = await tblCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    srcTables.Add(reader.GetString(0));
                }
            }

            // Clear target tables in reverse order
            foreach (var table in DomainSeedConstants.OrderedTablesForImport.Reverse())
            {
                if (srcTables.Contains(table))
                {
                    await ExecuteSqliteNonQueryAsync(conn, $"DELETE FROM main.\"{table}\";", cancellationToken, tx);
                }
            }

            var totalTables = DomainSeedConstants.OrderedTablesForImport.Length;
            for (var i = 0; i < totalTables; i++)
            {
                var table = DomainSeedConstants.OrderedTablesForImport[i];
                if (!srcTables.Contains(table))
                    continue;

                var pct = 35 + (int)((i / (double)totalTables) * 50);
                progress?.Report(new ImportProgressUpdate
                {
                    Percentage = pct,
                    Stage = $"Copying SQLite table: {table}",
                    Detail = $"Transferring {table} ({i + 1}/{totalTables})..."
                });

                var targetCols = await GetSqliteTableColumnsAsync(conn, "main", table, tx, cancellationToken);
                var sourceCols = await GetSqliteTableColumnsAsync(conn, "legacy_src", table, tx, cancellationToken);
                var commonCols = targetCols.Intersect(sourceCols, StringComparer.OrdinalIgnoreCase).ToList();

                if (commonCols.Count == 0)
                    continue;

                var quotedCols = string.Join(", ", commonCols.Select(c => $"\"{c}\""));
                var copySql = $"INSERT INTO main.\"{table}\" ({quotedCols}) SELECT {quotedCols} FROM legacy_src.\"{table}\";";
                var rowsAffected = await ExecuteSqliteNonQueryAsync(conn, copySql, cancellationToken, tx);
                report.ImportedTableRowCounts[table] = rowsAffected;
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            try
            {
                await ExecuteSqliteNonQueryAsync(conn, "DETACH DATABASE legacy_src;", cancellationToken);
            }
            catch { }

            await ExecuteSqliteNonQueryAsync(conn, "PRAGMA foreign_keys = ON;", cancellationToken);
        }
    }

    /// <summary>
    /// Parses MySQL/SQLite SQL dump files using the exact quote/escape/comment-aware state machine
    /// from BackupService.php::splitSqlStatements().
    /// </summary>
    private async Task ImportSqlDumpTextAsync(
        string sqlContent,
        ImportVerificationReport report,
        IProgress<ImportProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ImportProgressUpdate
        {
            Percentage = 35,
            Stage = "Parsing SQL Statements",
            Detail = "Splitting SQL dump statements..."
        });

        var statements = SplitSqlStatements(sqlContent);
        var validTableSet = new HashSet<string>(DomainSeedConstants.OrderedTablesForImport, StringComparer.OrdinalIgnoreCase);

        var insertStatements = new List<(string TableName, string Sql)>();
        var touchedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawStmt in statements)
        {
            var stmt = rawStmt.Trim();
            if (string.IsNullOrEmpty(stmt))
                continue;

            var match = Regex.Match(
                stmt,
                @"^(?:INSERT(?:\s+OR\s+REPLACE|\s+IGNORE)?|REPLACE)\s+INTO\s+[`""']?([a-zA-Z0-9_]+)[`""']?",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            var tableName = match.Groups[1].Value;
            if (!validTableSet.Contains(tableName))
                continue;

            var normalizedSql = NormalizeMySqlInsertForSqlite(stmt);
            insertStatements.Add((tableName, normalizedSql));
            touchedTables.Add(tableName);
        }

        await using var conn = new SqliteConnection($"Data Source={_dbInitializer.DatabaseFilePath}");
        await conn.OpenAsync(cancellationToken);
        await ExecuteSqliteNonQueryAsync(conn, "PRAGMA foreign_keys = OFF;", cancellationToken);

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            // Clear touched tables in reverse dependency order
            foreach (var table in DomainSeedConstants.OrderedTablesForImport.Reverse())
            {
                if (touchedTables.Contains(table))
                {
                    await ExecuteSqliteNonQueryAsync(conn, $"DELETE FROM \"{table}\";", cancellationToken, tx);
                }
            }

            var total = insertStatements.Count;
            for (var i = 0; i < total; i++)
            {
                var (table, sql) = insertStatements[i];
                if (i % 50 == 0 || i == total - 1)
                {
                    var pct = 45 + (int)((i / (double)Math.Max(1, total)) * 40);
                    progress?.Report(new ImportProgressUpdate
                    {
                        Percentage = pct,
                        Stage = $"Executing SQL Inserts ({i + 1}/{total})",
                        Detail = $"Importing into {table}..."
                    });
                }

                var affected = await ExecuteSqliteNonQueryAsync(conn, sql, cancellationToken, tx);
                report.ImportedTableRowCounts[table] = report.ImportedTableRowCounts.GetValueOrDefault(table) + Math.Max(1, affected);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await ExecuteSqliteNonQueryAsync(conn, "PRAGMA foreign_keys = ON;", cancellationToken);
        }
    }

    private async Task ApplySettingsJsonAsync(string jsonContent, ImportVerificationReport report, CancellationToken cancellationToken)
    {
        var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        var dataElem = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;

        if (dataElem.ValueKind != JsonValueKind.Object)
            return;

        await using var conn = new SqliteConnection($"Data Source={_dbInitializer.DatabaseFilePath}");
        await conn.OpenAsync(cancellationToken);

        var validCols = await GetSqliteTableColumnsAsync(conn, "main", "settings", null, cancellationToken);
        var updates = new List<string>();
        await using var cmd = conn.CreateCommand();

        var pIdx = 0;
        foreach (var prop in dataElem.EnumerateObject())
        {
            if (prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!validCols.Contains(prop.Name))
                continue;

            var paramName = $"@p{pIdx++}";
            updates.Add($"\"{prop.Name}\" = {paramName}");
            object? val = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDecimal(),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Null => DBNull.Value,
                _ => prop.Value.GetRawText()
            };
            cmd.Parameters.AddWithValue(paramName, val ?? DBNull.Value);
        }

        if (updates.Count > 0)
        {
            cmd.CommandText = $"UPDATE \"settings\" SET {string.Join(", ", updates)} WHERE \"id\" = 1;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            report.ImportedTableRowCounts["settings"] = 1;
        }
    }

    /// <summary>
    /// Exact port of BackupService.php::splitSqlStatements()
    /// Splits SQL dump text on semicolons only when outside single/double/backtick quotes and SQL comments.
    /// </summary>
    public static List<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        var buffer = new StringBuilder();
        var len = sql.Length;

        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < len; i++)
        {
            var ch = sql[i];
            var next = i + 1 < len ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingle && !inDouble && !inBacktick)
            {
                if (ch == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (ch == '#')
                {
                    inLineComment = true;
                    continue;
                }
                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (ch == '\\' && (inSingle || inDouble))
            {
                buffer.Append(ch);
                if (i + 1 < len)
                {
                    buffer.Append(sql[++i]);
                }
                continue;
            }

            if (ch == '\'' && !inDouble && !inBacktick)
            {
                if (inSingle && next == '\'')
                {
                    buffer.Append("''");
                    i++;
                    continue;
                }
                inSingle = !inSingle;
            }
            else if (ch == '"' && !inSingle && !inBacktick)
            {
                inDouble = !inDouble;
            }
            else if (ch == '`' && !inSingle && !inDouble)
            {
                inBacktick = !inBacktick;
            }

            if (ch == ';' && !inSingle && !inDouble && !inBacktick)
            {
                var trimmed = buffer.ToString().Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    statements.Add(trimmed);
                }
                buffer.Clear();
                continue;
            }

            buffer.Append(ch);
        }

        var tail = buffer.ToString().Trim();
        if (!string.IsNullOrEmpty(tail))
        {
            statements.Add(tail);
        }

        return statements;
    }

    /// <summary>
    /// Converts MySQL backslash escapes inside single-quoted literals (\', \", \r, \n, \\) into standard SQLite syntax.
    /// </summary>
    public static string NormalizeMySqlInsertForSqlite(string stmt)
    {
        var headerNormalized = Regex.Replace(
            stmt,
            @"^INSERT\s+IGNORE\s+INTO\b",
            "INSERT OR IGNORE INTO",
            RegexOptions.IgnoreCase);

        if (!headerNormalized.Contains('\\'))
            return headerNormalized;

        var sb = new StringBuilder(headerNormalized.Length);
        var inSingle = false;

        for (var i = 0; i < headerNormalized.Length; i++)
        {
            var c = headerNormalized[i];
            if (inSingle && c == '\\' && i + 1 < headerNormalized.Length)
            {
                var next = headerNormalized[++i];
                switch (next)
                {
                    case '\'': sb.Append("''"); break;
                    case '"': sb.Append('"'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '0': sb.Append('\0'); break;
                    default: sb.Append(next); break;
                }
                continue;
            }

            if (c == '\'')
            {
                if (inSingle && i + 1 < headerNormalized.Length && headerNormalized[i + 1] == '\'')
                {
                    sb.Append("''");
                    i++;
                    continue;
                }
                inSingle = !inSingle;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private async Task<(string? Key, string? Status, string? Payload, DateTime? VerifiedAt, DateTime? ExpiresAt, string? MachineId)> SnapshotLocalLicenseAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (s == null || string.IsNullOrWhiteSpace(s.LicenseKey))
            return default;

        var check = _licenseService.VerifyKey(s.LicenseKey);
        if (!check.Valid)
            return default;

        return (s.LicenseKey, s.LicenseStatus, s.LicensePayload, s.LicenseVerifiedAt, s.LicenseExpiresAt, s.LicenseMachineId);
    }

    private async Task RestoreOrSanitizeLicenseAsync(
        (string? Key, string? Status, string? Payload, DateTime? VerifiedAt, DateTime? ExpiresAt, string? MachineId) preserved,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preserved.Key))
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var s = await db.Settings.FirstOrDefaultAsync(cancellationToken);
            if (s != null)
            {
                s.LicenseKey = preserved.Key;
                s.LicenseStatus = preserved.Status;
                s.LicensePayload = preserved.Payload;
                s.LicenseVerifiedAt = preserved.VerifiedAt;
                s.LicenseExpiresAt = preserved.ExpiresAt;
                s.LicenseMachineId = preserved.MachineId;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        await _licenseService.ClearLicenseIfMachineChangedAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> GetSqliteTableColumnsAsync(
        SqliteConnection conn,
        string schemaName,
        string tableName,
        SqliteTransaction? tx,
        CancellationToken cancellationToken)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqliteCommand($"PRAGMA {schemaName}.table_info(\"{tableName}\");", conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cols.Add(reader.GetString(1));
        }
        return cols;
    }

    private static async Task<int> ExecuteSqliteNonQueryAsync(
        SqliteConnection conn,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? tx = null)
    {
        await using var cmd = new SqliteCommand(sql, conn, tx);
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsZipFile(string path)
    {
        try
        {
            Span<byte> buf = stackalloc byte[4];
            using var fs = File.OpenRead(path);
            if (fs.Read(buf) < 4) return false;
            return buf[0] == 0x50 && buf[1] == 0x4B && buf[2] == 0x03 && buf[3] == 0x04;
        }
        catch { return false; }
    }

    private static bool IsGzipFile(string path)
    {
        try
        {
            Span<byte> buf = stackalloc byte[2];
            using var fs = File.OpenRead(path);
            if (fs.Read(buf) < 2) return false;
            return buf[0] == 0x1F && buf[1] == 0x8B;
        }
        catch { return false; }
    }

    private static bool IsBinarySqliteFile(string path)
    {
        try
        {
            Span<byte> buf = stackalloc byte[16];
            using var fs = File.OpenRead(path);
            if (fs.Read(buf) < 16) return false;
            return buf.SequenceEqual(SqliteMagicHeader);
        }
        catch { return false; }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSub);
        }
    }
}
