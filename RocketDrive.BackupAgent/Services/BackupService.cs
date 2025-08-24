using Google;                  // for GoogleApiException
using Google.Apis.Drive.v3;    // if not already there
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;              // for HttpStatusCode


namespace RocketDrive.BackupAgent.Services
{
    public class BackupService
    {
        private readonly IConfiguration _config;
        private readonly GoogleDriveService _googleDriveService;
        private readonly CompositeNotifier _notifier;

        public BackupService(IConfiguration config, GoogleDriveService googleDriveService, CompositeNotifier notifier)
        {
            _config = config;
            _googleDriveService = googleDriveService;
            _notifier = notifier;
        }

        public void RunBackup()
        {
            var started = DateTime.UtcNow;
            var status = new RunStatus { StartedUtc = started };

            // counters
            int filesScanned = 0, filesUploaded = 0, skippedExisting = 0, skippedUnstable = 0, errorCount = 0;

            try
            {
                var folders = _config.GetSection("BackupSettings:Folders").Get<string[]>() ?? Array.Empty<string>();

                // AllowedExtensions can be array or comma-separated string
                var extsArray = _config.GetSection("BackupSettings:AllowedExtensions").Get<string[]>();
                var extsCsv = _config["BackupSettings:AllowedExtensions"];
                var allowedExts = NormalizeExtensions(extsArray, extsCsv); // you already have this from previous step

                var lastUploadFile = _config["BackupSettings:LastUploadedFileTimePath"];
                if (string.IsNullOrWhiteSpace(lastUploadFile)) lastUploadFile = "last_uploaded.txt";
                var lastCutoff = ReadLastTimestamp(lastUploadFile,_notifier); // you already have this from previous step

                var targetFolderName = _config["BackupSettings:TargetDriveFolderName"];
                if (string.IsNullOrWhiteSpace(targetFolderName)) targetFolderName = "RocketDriveUploads";

                if (folders.Length == 0)
                {
                    Console.WriteLine("⚠️ No folders configured for backup.");
                    return;
                }

                // Ensure root destination folder on Drive once
                var driveRootId = _googleDriveService.EnsureRootFolder(targetFolderName);

                var candidates = new List<(FileInfo fi, string baseFolder)>();

                foreach (var baseFolder in folders)
                {
                    if (!Directory.Exists(baseFolder))
                    {
                        Console.WriteLine($"⚠️ Folder not found: {baseFolder}");
                        continue;
                    }

                    var files = Directory.EnumerateFiles(baseFolder, "*", SearchOption.AllDirectories)
                        .Select(p => new FileInfo(p))
                        .Where(fi => IsAllowed(fi, allowedExts))
                        .Where(fi => fi.LastWriteTimeUtc > lastCutoff)
                        .Select(fi => (fi, baseFolder));

                    candidates.AddRange(files);
                }

                if (candidates.Count == 0)
                {
                    Console.WriteLine("ℹ️ No new files to upload.");
                    return;
                }

                filesScanned = candidates.Count;
                // Sort by time so we can update the last timestamp correctly
                var ordered = candidates.OrderBy(t => t.fi.LastWriteTimeUtc).ToList();
                DateTime newestUploadedUtc = lastCutoff;

                foreach (var (fi, baseFolder) in ordered)
                {
                    try
                    {
                        // Build relative directory path from the baseFolder (preserve structure)
                        var relativeDir = GetRelativeDirectory(baseFolder, fi.DirectoryName ?? baseFolder);

                        // Ensure nested Drive folders
                        var pathParts = SplitRelativePath(relativeDir); // ["Sub1","Sub2",...]
                        var destinationId = _googleDriveService.EnsureNestedFolders(driveRootId, pathParts);



                        // Read overwrite flag from config
                        var overwrite = _config.GetValue<bool>("BackupSettings:OverwriteExisting");

                        // If a file with same name exists in that Drive folder, either overwrite or skip
                        var existingId = _googleDriveService.FindFileInFolder(fi.Name, destinationId);
                        if (existingId != null)
                        {
                            if (overwrite && fi.LastWriteTimeUtc > lastCutoff)
                            {
                                Console.WriteLine($"♻️ Overwriting existing: {fi.Name}");
                                Log.Information($"♻️ Overwriting existing: {fi.Name}");
                                _googleDriveService.DeleteFile(existingId);
                            }
                            else
                            {
                                skippedExisting++;
                                Console.WriteLine($"⏭️ Skipping (already exists): {fi.FullName}");
                                Log.Information($"⏭️ Skipping (already exists): {fi.FullName}");
                                continue; // go to next file
                            }
                        }

                        // Upload
                        Console.WriteLine($"⬆️ Uploading: {fi.FullName} -> /{targetFolderName}/{relativeDir}");
                        Log.Information($"⬆️ Uploading: {fi.FullName} -> /{targetFolderName}/{relativeDir}");
                        // var uploadedId = _googleDriveService.UploadFileToFolder(fi.FullName, destinationId);

                        var retryCount = _config.GetValue<int?>("BackupSettings:UploadRetryCount") ?? 3;
                        Retry(retryCount, () => _googleDriveService.UploadFileToFolder(fi.FullName, destinationId), fi.FullName);

                        filesUploaded++;
                        // Track newest timestamp
                        if (fi.LastWriteTimeUtc > newestUploadedUtc)
                            newestUploadedUtc = fi.LastWriteTimeUtc;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        Console.WriteLine($"❌ Failed to upload {fi.FullName}: {ex.Message}");
                        Log.Error($"❌ Failed to upload {fi.FullName}: {ex.Message}");
                        var bodyf = $"Failed to upload {fi.FullName}: {ex.Message}";
                        _notifier.Notify("Backup run failed", bodyf, false);
                    }
                }

                if (newestUploadedUtc > lastCutoff)
                {
                    WriteLastTimestamp(lastUploadFile, newestUploadedUtc); // you already have this method
                    Console.WriteLine($"✅ Updated last uploaded timestamp to {newestUploadedUtc:O}");
                }
                var finished = DateTime.UtcNow;
                var body = $"Started: {started:O}\nFinished: {finished:O}\nScanned: {filesScanned}\nUploaded: {filesUploaded}\nSkipped(existing): {skippedExisting}\nSkipped(unstable): {skippedUnstable}\nErrors: {errorCount}";
                _notifier.Notify("Backup run completed", body, isSuccess: true);
            }
            catch(Exception ex) {
                status.Notes = ex.Message;
                var finished = DateTime.UtcNow;
                var body = $"Started: {started:O}\nFinished: {finished:O}\nScanned: {filesScanned}\nUploaded: {filesUploaded}\nSkipped(existing): {skippedExisting}\nSkipped(unstable): {skippedUnstable}\nErrors: {errorCount}\n\nError: {ex.Message}";
                _notifier.Notify("Backup run failed", body, isSuccess: false);
                throw; // keep existing behavior/logs
            }
            finally
            {
                status.FinishedUtc = DateTime.UtcNow;
                status.FilesScanned = filesScanned;
                status.FilesUploaded = filesUploaded;
                status.SkippedExisting = skippedExisting;
                status.SkippedUnstable = skippedUnstable;
                status.Errors = errorCount;

                var statusPath = _config["BackupSettings:StatusFilePath"];
                if (string.IsNullOrWhiteSpace(statusPath))
                    statusPath = Path.Combine("logs", "status.json");

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statusPath))!);

                var json = JsonSerializer.Serialize(
                    status,
                    new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

                File.WriteAllText(statusPath, json);
            }
        }

        // --- Helpers for relative paths that work on .NET 6 AND net48 ---

        private static string GetRelativeDirectory(string baseFolder, string targetDir)
        {
            // Normalize separators and ensure trailing separator on base
            baseFolder = Path.GetFullPath(baseFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            targetDir = Path.GetFullPath(targetDir);

            try
            {
                var baseUri = new Uri(baseFolder, UriKind.Absolute);
                var targetUri = new Uri(targetDir, UriKind.Absolute);
                var rel = baseUri.MakeRelativeUri(targetUri).ToString();
                rel = Uri.UnescapeDataString(rel).Replace('/', Path.DirectorySeparatorChar);
                return rel; // may be "" (root)
            }
            catch
            {
                // Fallback: if on different drives etc., just use leaf name
                return new DirectoryInfo(targetDir).Name;
            }
        }

        private static IEnumerable<string> SplitRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) yield break;
            foreach (var part in relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }


        private static HashSet<string> NormalizeExtensions(string[]? arrayExts, string? csvExts)
        {
            var list = new List<string>();

            if (arrayExts != null && arrayExts.Length > 0)
                list.AddRange(arrayExts);

            if (!string.IsNullOrWhiteSpace(csvExts))
                list.AddRange(
      csvExts.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(s => s.Trim())
  );



            // Normalize to leading dot + lowercase (e.g., ".zip")
            return new HashSet<string>(list.Select(e =>
            {
                var s = e.Trim();
                if (!s.StartsWith(".")) s = "." + s;
                return s.ToLowerInvariant();
            }));
        }

        private static bool IsAllowed(FileInfo fi, HashSet<string> allowedExts)
        {
            if (allowedExts.Count == 0) return true; // if not configured, allow all
            return allowedExts.Contains(fi.Extension.ToLowerInvariant());
        }

        private static DateTime ReadLastTimestamp(string path, CompositeNotifier notifier)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path).Trim();
                    if (DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                        return dt.ToUniversalTime();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"issue while getting timestamp file", ex.Message);
                notifier.Notify("issue while getting timestamp file", ex.Message, isSuccess: false);
                throw new Exception($"issue while getting timestamp file", ex);
            }


            // Default far past so first run uploads everything
            return DateTime.MinValue.ToUniversalTime();
        }

        private static void WriteLastTimestamp(string path, DateTime utc)
        {
            try
            {
                File.WriteAllText(path, utc.ToString("O")); // ISO 8601
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not write last timestamp: {ex.Message}");
            }
        }


        private void Retry(int maxAttempts, Action action, string itemLabel)
        {
            int attempt = 0;
            Exception? last = null;

            while (attempt < Math.Max(1, maxAttempts))
            {
                try
                {
                    action();
                    return;
                }
                catch (GoogleApiException gex) when (IsTransient(gex))
                {
                    attempt++;
                    last = gex;
                    Log.Error($"⏳ Transient Google error on {itemLabel} (attempt {attempt}/{maxAttempts}): {gex.Message}");
                    Thread.Sleep(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)));
                }
                catch (IOException ioex)
                {
                    attempt++;
                    last = ioex;
                    Log.Error($"⏳ IO issue on {itemLabel} (attempt {attempt}/{maxAttempts}): {ioex.Message}");
                    Thread.Sleep(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)));
                }
                catch (UnauthorizedAccessException uaex)
                {
                    // Not transient: surface immediately
                    Log.Error($"Unauthorized access for {itemLabel}: {uaex.Message}", uaex);
                    throw new Exception($"Unauthorized access for {itemLabel}: {uaex.Message}", uaex);
                    
                }
                catch (Exception ex)
                {
                    attempt++;
                    last = ex;
                    Log.Error($"⏳ Unknown error on {itemLabel} (attempt {attempt}/{maxAttempts}): {ex.Message}");
                    Thread.Sleep(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)));
                }
            }
            Log.Error($"Failed after {maxAttempts} attempts for {itemLabel}", last);
            throw new Exception($"Failed after {maxAttempts} attempts for {itemLabel}", last);
        }

        private bool IsTransient(GoogleApiException ex)
        {
            int code = (int)ex.HttpStatusCode;

            // Check by numbers instead of enum names
            return code == 429 // Too Many Requests
                || code == 408 // Request Timeout
                || code == 502 // Bad Gateway
                || code == 503 // Service Unavailable
                || code == 504 // Gateway Timeout
                || (ex.Error?.Errors?.FirstOrDefault()?.Reason is string reason &&
                    (reason == "rateLimitExceeded" || reason == "userRateLimitExceeded" || reason == "backendError"));
        }


    }
}
