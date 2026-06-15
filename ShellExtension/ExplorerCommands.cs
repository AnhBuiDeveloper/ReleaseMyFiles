using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ReleaseMyFiles.ShellExtension;

public abstract class ExplorerCommandBase : IExplorerCommand
{
    protected abstract string Title { get; }
    protected abstract string CommandArgument { get; }
    protected abstract Guid CanonicalName { get; }

    public void GetTitle(IShellItemArray? items, out IntPtr title) =>
        title = Marshal.StringToCoTaskMemUni(Title);

    public void GetIcon(IShellItemArray? items, out IntPtr icon) =>
        icon = Marshal.StringToCoTaskMemUni(GetApplicationPath());

    public void GetToolTip(IShellItemArray? items, out IntPtr toolTip) =>
        toolTip = Marshal.StringToCoTaskMemUni(Title);

    public void GetCanonicalName(out Guid canonicalName) => canonicalName = CanonicalName;

    public void GetState(IShellItemArray? items, bool okToBeSlow, out ExplorerCommandState state) =>
        state = ExplorerCommandState.Enabled;

    public void Invoke(IShellItemArray items, IntPtr bindContext)
    {
        string? path = GetFirstPath(items);
        if (string.IsNullOrEmpty(path))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = GetApplicationPath(),
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(CommandArgument);
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo);
    }

    public void GetFlags(out ExplorerCommandFlags flags) => flags = ExplorerCommandFlags.Default;

    public void EnumSubCommands(out IntPtr enumCommands) => enumCommands = IntPtr.Zero;

    private static string? GetFirstPath(IShellItemArray items)
    {
        items.GetCount(out uint count);
        if (count == 0)
            return null;

        items.GetItemAt(0, out IShellItem item);
        item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out IntPtr pathPointer);
        try
        {
            return Marshal.PtrToStringUni(pathPointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    private static string GetApplicationPath()
    {
        string extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate shell extension directory.");
        return Path.Combine(extensionDirectory, "ReleaseMyFiles.exe");
    }
}

[ComVisible(true)]
[Guid("287107F8-274A-48AD-82BB-9B41D97A5F81")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ReleaseCommand : ExplorerCommandBase
{
    protected override string Title => "Release me";
    protected override string CommandArgument => "--release";
    protected override Guid CanonicalName => new("287107F8-274A-48AD-82BB-9B41D97A5F81");
}

[ComVisible(true)]
[Guid("BC852822-647D-46D4-BF6D-A7B1BBE2259E")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class WhoHoldingCommand : ExplorerCommandBase
{
    protected override string Title => "Who is holding this?";
    protected override string CommandArgument => "--whohold";
    protected override Guid CanonicalName => new("BC852822-647D-46D4-BF6D-A7B1BBE2259E");
}
