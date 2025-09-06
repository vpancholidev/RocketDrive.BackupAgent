using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using File = Google.Apis.Drive.v3.Data.File;

namespace RocketDrive.BackupAgent.Services
{
    public class GoogleDriveService
    {
        private readonly string[] Scopes = { DriveService.Scope.DriveFile };
        private readonly string ApplicationName = "RocketDrive Backup Agent";

        private DriveService _service;

        public GoogleDriveService()
        {
            InitializeService();
        }

        private void InitializeService()
        {
            try
            {
                using var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read);

                string credPath = "token.json"; // stores per-user access token

                var credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",  // <- you can later make this dynamic per user
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;

                _service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Google Drive service: {ex.Message}");
                throw;
            }
        }

        public void UploadFile(string filePath, string folderId = null)
        {
            if (_service == null) throw new InvalidOperationException("Google Drive service not initialized.");

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = folderId != null ? new[] { folderId } : null
            };

            FilesResource.CreateMediaUpload request;
            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                request = _service.Files.Create(fileMetadata, stream, "application/octet-stream");
                request.Fields = "id";
                request.Upload();
            }

            var file = request.ResponseBody;
            Console.WriteLine($"File uploaded successfully. File ID: {file.Id}");
        }

        public string EnsureRootFolder(string folderName)
        {
            // try find
            var listReq = _service.Files.List();
            listReq.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{Escape(folderName)}' and 'root' in parents and trashed = false";
            listReq.Fields = "files(id,name)";
            var list = listReq.Execute();
            if (list.Files != null && list.Files.Count > 0) return list.Files[0].Id;

            // create
            var meta = new File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { "root" }
            };
            var createReq = _service.Files.Create(meta);
            createReq.Fields = "id";
            var created = createReq.Execute();
            return created.Id;
        }

        // Ensures nested subfolders exist under a given parent. pathParts are folders like ["2025","08","23"]
        public string EnsureNestedFolders(string parentId, IEnumerable<string> pathParts)
        {
            var currentParent = parentId;
            foreach (var part in pathParts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                // find under currentParent
                var listReq = _service.Files.List();
                listReq.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{Escape(part)}' and '{currentParent}' in parents and trashed = false";
                listReq.Fields = "files(id,name)";
                var list = listReq.Execute();
                if (list.Files != null && list.Files.Count > 0)
                {
                    currentParent = list.Files[0].Id;
                }
                else
                {
                    // create
                    var meta = new File
                    {
                        Name = part,
                        MimeType = "application/vnd.google-apps.folder",
                        Parents = new List<string> { currentParent }
                    };
                    var createReq = _service.Files.Create(meta);
                    createReq.Fields = "id";
                    var folder = createReq.Execute();
                    currentParent = folder.Id;
                }
            }
            return currentParent; // id of deepest folder
        }

        // Upload a file into a specific Drive folder ID
        public string UploadFileToFolder(string filePath, string destinationFolderId)
        {
            var fileMeta = new File
            {
                Name = Path.GetFileName(filePath),
                Parents = new List<string> { destinationFolderId }
            };

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var create = _service.Files.Create(fileMeta, fs, "application/octet-stream");
            create.Fields = "id";
            var result = create.Upload();
            if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
                throw new Exception($"Upload failed: {result.Exception?.Message}");

            return create.ResponseBody.Id;
        }

        public string? FindFileInFolder(string fileName, string parentFolderId)
        {
            var listReq = _service.Files.List();
            listReq.Q = $"name = '{Escape(fileName)}' and '{parentFolderId}' in parents and trashed = false";
            listReq.Fields = "files(id,name)";
            var list = listReq.Execute();
            return list.Files != null && list.Files.Count > 0 ? list.Files[0].Id : null;
        }
        public void DeleteFile(string fileId)
        {
            _service.Files.Delete(fileId).Execute();
        }

        private static string Escape(string name) => name.Replace("'", "\\'");
    }
}
public class RunStatus
{
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public int FilesScanned { get; set; }
    public int FilesUploaded { get; set; }
    public int SkippedExisting { get; set; }
    public int SkippedUnstable { get; set; }
    public int Errors { get; set; }
    public string? Notes { get; set; }
}