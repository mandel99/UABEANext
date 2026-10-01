using System.Collections.Generic;
using System.Threading.Tasks;
using UABEANext4.AssetWorkspace;

namespace UABEANext4.Plugins;

/// <summary>Optional format choices displayed directly in asset action menus.</summary>
public interface IUavPluginFormatOption : IUavPluginOption
{
    IReadOnlyList<string> Extensions { get; }
    Task<bool> ExecuteFormat(Workspace workspace, IUavPluginFunctions funcs,
        UavPluginMode mode, IList<AssetInst> selection, string extension);
}
