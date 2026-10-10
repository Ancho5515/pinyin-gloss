using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PinyinGloss.Core;

namespace PinyinGloss.Providers;
public record DictionaryManifest(string Name, string Version, string Source, string License, string Sha256, string[] SupportedTargets, int Entries);
public static class DictionaryStore
{
    public static string Normalize(string text) => text.Trim().Normalize(NormalizationForm.FormC);
    public static async Task<DictionaryManifest> ImportAsync(string tsv, string db, string name, string license, string source, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(license) || string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("词典必须注明许可与出处");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(db))!);
        var temp = db + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var languages = new HashSet<string>(); var entries = 0; var line = 0;
            await using (var connection = Connection(temp, false))
            {
                await connection.OpenAsync(token);
                using var setup = connection.CreateCommand();
                setup.CommandText = "CREATE TABLE entries(word TEXT NOT NULL, language TEXT NOT NULL, gloss TEXT NOT NULL, PRIMARY KEY(word, language, gloss)); CREATE TABLE metadata(json TEXT NOT NULL);";
                await setup.ExecuteNonQueryAsync(token);
                using var transaction = connection.BeginTransaction();
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO entries(word,language,gloss) VALUES($w,$l,$g)";
                var w = insert.Parameters.Add("$w", SqliteType.Text); var l = insert.Parameters.Add("$l", SqliteType.Text); var g = insert.Parameters.Add("$g", SqliteType.Text);
                using var reader = new StreamReader(tsv, new UTF8Encoding(false, true));
                while (await reader.ReadLineAsync(token) is { } row)
                {
                    line++; if (string.IsNullOrWhiteSpace(row) || row.StartsWith('#')) continue;
                    var fields = row.Split('\t');
                    if (fields.Length is < 3 or > 5 || fields[0].Length > 128 || fields[2].Length > 512 || fields.Take(3).Any(string.IsNullOrWhiteSpace))
                        throw new InvalidDataException($"TSV 第 {line} 行格式错误（word / language / gloss）");
                    var language = fields[1].Trim().ToLowerInvariant();
                    try { _ = LanguageCatalog.Name(language); } catch { throw new InvalidDataException($"第 {line} 行语言代码无效"); }
                    var words = new[] { fields[0] }.Concat(fields.Length == 5 ? fields[4].Split('|', StringSplitOptions.RemoveEmptyEntries) : []);
                    foreach (var word in words)
                    {
                        if (word.Length > 128) throw new InvalidDataException($"第 {line} 行别名过长");
                        w.Value = Normalize(word); l.Value = language; g.Value = fields[2].Trim();
                        entries += await insert.ExecuteNonQueryAsync(token);
                    }
                    languages.Add(language);
                }
                if (entries == 0) throw new InvalidDataException("空词典");
                using var data = File.OpenRead(tsv); var hash = Convert.ToHexString(await SHA256.HashDataAsync(data, token)).ToLowerInvariant();
                var manifest = new DictionaryManifest(name, hash[..12], source, license, hash, languages.Order().ToArray(), entries);
                using var metadata = connection.CreateCommand(); metadata.Transaction = transaction;
                metadata.CommandText = "INSERT INTO metadata(json) VALUES($json)";
                metadata.Parameters.AddWithValue("$json", JsonSerializer.Serialize(manifest, SettingsJson.Options));
                await metadata.ExecuteNonQueryAsync(token); transaction.Commit();
            }
            token.ThrowIfCancellationRequested();
            // Database includes its manifest, so one atomic replacement swaps both data and provenance.
            File.Move(temp, db, true);
            return ReadManifest(db);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static DictionaryManifest ReadManifest(string db)
    {
        using var connection = Connection(db, true); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM metadata LIMIT 1";
        return JsonSerializer.Deserialize<DictionaryManifest>((string)command.ExecuteScalar()!, SettingsJson.Options) ?? throw new InvalidDataException("词典缺少 manifest");
    }
    public static async Task<string?> LookupAsync(string db, string word, string language, CancellationToken token)
    {
        await using var connection = Connection(db, true); await connection.OpenAsync(token);
        using var command = connection.CreateCommand(); command.CommandText = "SELECT gloss FROM entries WHERE word=$w AND language=$l ORDER BY gloss LIMIT 2";
        command.Parameters.AddWithValue("$w", Normalize(word)); command.Parameters.AddWithValue("$l", language);
        using var reader = await command.ExecuteReaderAsync(token); var glosses = new List<string>();
        while (await reader.ReadAsync(token)) glosses.Add(reader.GetString(0));
        return glosses.Count == 0 ? null : string.Join(" / ", glosses);
    }
    private static SqliteConnection Connection(string path, bool readOnly) => new(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(path), Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 1 }.ToString());
}
public sealed class DictionaryProvider : ITranslationProvider
{
    private readonly ProviderSettings _config; private readonly DictionaryManifest _manifest;
    public DictionaryProvider(ProviderSettings config) { _config = config; _manifest = DictionaryStore.ReadManifest(config.DictionaryPath); }
    public ProviderDescriptor Describe() => new(_config.Id, _manifest.Version, ProviderKind.LocalDictionary, _manifest.SupportedTargets, false, true);
    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    public async Task<ProviderResult> QueryAsync(ProviderQuery q, CancellationToken token)
    {
        var gloss = await Task.Run(() => DictionaryStore.LookupAsync(_config.DictionaryPath, q.Candidate.Text, q.TargetLanguage, token), token);
        return new(q.Candidate.Id, q.Candidate.Text, q.TargetLanguage, gloss == null ? ResultStatus.NotFound : ResultStatus.Ready, gloss);
    }
}
