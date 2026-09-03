using System;

namespace MyExtension
{
    /// <summary>
    /// Command set GUID and command IDs for the extension's VS commands. The <c>Telescope.Show</c>
    /// command is registered in <see cref="MyExtensionPackage"/> and also reachable via the
    /// <c>telescope</c> keybinding action.
    /// </summary>
    internal static class GuidList
    {
        public static readonly Guid CommandSet = new Guid("3f0c5a2d-4f7e-4a2b-9c3e-8d1b7f0e6a2c");
    }

    internal static class CommandList
    {
        public const int TelescopeShow = 0x0100;
    }
}
