using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MotionDesk.Services
{
    /// <summary>
    /// Compatibility of a plugin DLL with the contract of this Opsis, read from the metadata of the DLL (nothing of the plugin is loaded or run).
    /// Rules (Opsis 2.6.0, contract 1.0.0.0):
    ///  - built against an OLDER or EQUAL contract (for example 0.8.0.0 = every plugin of Opsis 2.4.x): it loads. PluginHost.PluginLoadContext always returns the contract assembly of the host,
    ///    and the runtime accepts it whatever version the plugin asked for (proved by PluginContractLoadTests with a real plugin DLL built against 0.8.0.0).
    ///  - built against a NEWER contract than the host has: refused with a clear message ("needs an update of Opsis"); a newer plugin may use types this Opsis does not have.
    ///  - no reference to the contract at all: ok (it will simply not find a plugin class).
    /// </summary>
    internal static class PluginContractCheck
    {
        internal const string ContractAssemblyName = "OpsisNewsCrawler.Plugin.Abstractions";

        internal sealed record Result(bool Ok, string Message, Version? Required);

        public static Result Check(string pluginDllPath, Version hostContract)
        {
            Version? required;
            try { required = ReadRequiredContract(pluginDllPath); }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new Result(false, "it is not a readable .NET plugin (" + ex.Message + ")", null);
            }
            if (required is null) return new Result(true, "", null);
            // compare the first three parts: 1.0.0.0 vs 1.0.0.0, and a patch-level contract difference is still additive
            if (Normalize(required) > Normalize(hostContract))
                return new Result(false, $"it needs a newer Opsis: it was built for plugin contract {required}, this Opsis has {hostContract} - update Opsis (or install an older version of the plugin)", required);
            return new Result(true, "", required);
        }

        private static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        /// <summary>The version of OpsisNewsCrawler.Plugin.Abstractions the DLL references, or null.</summary>
        public static Version? ReadRequiredContract(string dll)
        {
            using var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) throw new BadImageFormatException("no metadata");
            var md = pe.GetMetadataReader();
            foreach (var h in md.AssemblyReferences)
            {
                var r = md.GetAssemblyReference(h);
                if (string.Equals(md.GetString(r.Name), ContractAssemblyName, StringComparison.OrdinalIgnoreCase)) return r.Version;
            }
            return null;
        }
    }
}
