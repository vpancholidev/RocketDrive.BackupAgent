namespace RocketDrive.BackupAgent.Config
{
    public class BackupSettings
    {
        public List<string> Folders { get; set; } = new();
        public string TargetDriveFolder { get; set; } = "RocketDriveUploads"; // default folder
        public List<string> AllowedExtensions { get; set; } = new(); // e.g., [".txt", ".pdf"]
        public string LastRunFile { get; set; } = "last_run.txt"; // stores last run timestamp
    }
}
