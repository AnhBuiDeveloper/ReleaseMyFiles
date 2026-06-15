using System.Runtime.InteropServices;

namespace ReleaseMyFiles.ShellExtension;

public enum ExplorerCommandState
{
    Enabled = 0
}

[Flags]
public enum ExplorerCommandFlags
{
    Default = 0
}

public enum ShellItemDisplayName : uint
{
    FileSystemPath = 0x80058000
}

[ComImport]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    void GetTitle(IShellItemArray? items, out IntPtr title);
    void GetIcon(IShellItemArray? items, out IntPtr icon);
    void GetToolTip(IShellItemArray? items, out IntPtr toolTip);
    void GetCanonicalName(out Guid canonicalName);
    void GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow,
        out ExplorerCommandState state);
    void Invoke(IShellItemArray items, IntPtr bindContext);
    void GetFlags(out ExplorerCommandFlags flags);
    void EnumSubCommands(out IntPtr enumCommands);
}

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId,
        out IntPtr result);
    void GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr propertyStore);
    void GetPropertyDescriptionList(ref IntPtr propertyKey, ref Guid interfaceId,
        out IntPtr propertyDescriptionList);
    void GetAttributes(uint attributeFlags, uint attributes, out uint result);
    void GetCount(out uint count);
    void GetItemAt(uint index, out IShellItem shellItem);
    void EnumItems(out IntPtr enumShellItems);
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId,
        out IntPtr result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(ShellItemDisplayName displayName, out IntPtr name);
    void GetAttributes(uint attributes, out uint result);
    void Compare(IShellItem shellItem, uint hint, out int order);
}
