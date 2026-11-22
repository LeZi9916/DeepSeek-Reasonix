using Avalonia.Controls;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Adds the reference sidebar's concrete settings destinations to native command search.</summary>
    /// <param name="entries">The currently available canonical command list.</param>
    private void AddWorkspaceSearchDestinations(ICollection<WorkspaceSearchHit> entries)
    {
        foreach (var (key, section, aliases, focus) in new[]
        {
            ("common.storage", "storage", "storage disk data 存储 磁盘 数据", string.Empty),
            ("session.tools_and_integrations", "ext", "tools mcp skills 工具 集成", string.Empty),
            ("session.where_it_runs", "network", "ssh local remote 运行环境", "remote.add_a_machine"),
            ("common.model_services", "model", "model provider 模型 来源 服务", string.Empty),
            ("common.community_and_contributors", "account", "community discord qq group contributors 社区 交流群 加群 贡献者", "common.community_and_contributors"),
        })
        {
            entries.Add(new WorkspaceSearchHit(
                "actions",
                MessageCatalog.Get(key, Language),
                MessageCatalog.Get("app.action", Language),
                async () =>
                {
                    OpenPreferences();
                    await ShowSectionAsync(section).ConfigureAwait(true);
                    SelectSettingsNav(section);
                    if (focus.Length > 0)
                    {
                        SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Equals(control.Tag, focus))?.BringIntoView();
                    }
                },
                aliases,
                "c:" + key));
        }
    }
}
