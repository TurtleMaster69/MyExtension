using System;

namespace MyExtension.Package
{
    /// <summary>
    /// Command set GUID and command IDs for the extension's VS commands (the Telescope finders +
    /// the Gap 6 goto commands). The <c>Telescope.Show</c> command is registered in
    /// <see cref="MyExtensionPackage"/> and also reachable via the <c>telescope</c> keybinding
    /// action.
    /// </summary>
    internal static class GuidList
    {
        public static readonly Guid CommandSet = new Guid("3f0c5a2d-4f7e-4a2b-9c3e-8d1b7f0e6a2c");
    }

    internal static class CommandList
    {
        public const int TelescopeShow = 0x0100;

        // Gap 6: the goto commands (VSCT-declared in MyExtensionPackage.vsct — the IDs MUST
        // match the IDSymbols there). Canonical DTE names: MyExtension.GotoDefinition /
        // GotoReferences / GotoImplementation (the user maps gd/gr/gI to them in VsVim).
        public const int GotoDefinition = 0x0101;
        public const int GotoReferences = 0x0102;
        public const int GotoImplementation = 0x0103;
    }
}
