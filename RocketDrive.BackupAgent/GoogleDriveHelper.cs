using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class GoogleDriveHelper
{
    static string[] Scopes = { DriveService.Scope.DriveFile }; // only upload files
    static string ApplicationName = "RocketDrive Backup";

    public static DriveService GetDriveService()
    {
        UserCredential credential;

        using (var stream =
               new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
        {
            // token.json stores the user's access and refresh tokens.
            string credPath = "token.json";
            credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(credPath, true)).Result;
        }

        // Create Drive API service.
        var service = new DriveService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });

        return service;
    }
}
