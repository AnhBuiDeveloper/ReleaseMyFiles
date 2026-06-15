## ReleaseMyFiles

A Windows desktop application that resides in the system tray and offers context menu options in Windows Explorer to identify and terminate processes locking specific files or folders.

### Features

* System tray integration
* Windows Explorer context menu ("Who is holding this file/folder?")
* Native Windows 11 context menu integration
* Displays a list of processes currently locking the selected file/folder
* Allows terminating the locking processes
* Options dialog for configuration

### Requirements

* Windows OS
* .NET Framework / .NET Core / .NET (depending on build target)

### Building

Open the solution in Visual Studio or use the .NET CLI:

```bat
dotnet build
```

### Usage

Run the application. It will sit in your system tray. Right-click any file or folder in Windows Explorer and select the associated context menu item to see which processes are holding it.
