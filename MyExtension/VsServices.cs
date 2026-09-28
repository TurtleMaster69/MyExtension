using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.CompilerServices;

namespace MyExtension
{
    /// <summary>
    /// One MEF/DTE resolver: the single place that turns an <see cref="IServiceProvider"/> into
    /// the VS component model, a MEF export, or the DTE automation object. DTE is cached per
    /// package (keyed by the <see cref="IServiceProvider"/>) so repeated navigation lookups don't
    /// re-query the service.
    /// </summary>
    internal static class VsServices
    {
        private static readonly ConditionalWeakTable<object, object> _dteCache = new ConditionalWeakTable<object, object>();

        public static IComponentModel? ComponentModel(IServiceProvider sp)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return sp.GetService(typeof(SComponentModel)) as IComponentModel;
        }

        public static T? Mef<T>(IServiceProvider sp) where T : class
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ComponentModel(sp)?.DefaultExportProvider.GetExportedValue<T>();
        }

        public static DTE Dte(IServiceProvider sp)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dteCache.TryGetValue(sp, out var cached))
            {
                return (DTE)cached;
            }
            DTE? dte = (DTE)sp.GetService(typeof(DTE));
            if (dte != null)
            {
                _dteCache.Add(sp, dte);
            }
            return dte!;
        }

        public static IComponentModel? GlobalComponentModel()
        {
            return Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
        }
    }
}
