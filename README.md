# RocketDrive.BackupAgent
A lightweight Windows backup agent that mirrors your local folders to Google Drive on a schedule. Designed for low-configuration PCs (Win7+) and clean installs Supports per-user Google auth, multi-folder uploads, subfolder mirroring, extension filters, resumable scheduling via Task Schedule logging, status snapshots, and Telegram/Email notification

# Prepare release use following command for net48 version 
dotnet publish -c Release -f net48 -p:Platform=x86
# Prepare release use following command for net6.0 version 
dotnet publish -c Release -f net6.0 -r win-x64 --self-contained true -p:PublishSingleFile=true


this will generate single exe file that include all the dlls 

