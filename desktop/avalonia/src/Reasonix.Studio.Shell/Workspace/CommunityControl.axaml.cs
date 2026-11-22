using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>The existing repository community assets and contributor list in native controls.</summary>
public sealed partial class CommunityControl : UserControl
{
    /// <summary>The release's existing contributor list, without network discovery.</summary>
    private readonly string[] _contributors;

    /// <summary>The host's existing browser operation.</summary>
    private Func<string, Task>? _open;

    /// <summary>The currently selected interface language.</summary>
    private InterfaceLanguage _language;

    /// <summary>Whether the complete bundled contributor list is expanded.</summary>
    private bool _all;

    /// <summary>Initializes a new instance of the <see cref="CommunityControl"/> class.</summary>
    public CommunityControl()
    {
        InitializeComponent();
        using var contributors = AssetLoader.Open(new Uri("avares://Reasonix.Studio.Shell/Assets/Community/contributors.json"));
        _contributors = (JsonSerializer.Deserialize<string[]>(contributors) ?? []).Where(login => Regex.IsMatch(login, "^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$", RegexOptions.CultureInvariant)).ToArray();
        QqQr.Source = new Bitmap(AssetLoader.Open(new Uri("avares://Reasonix.Studio.Shell/Assets/Community/qq-group-qr.png")));
        DouyinQr.Source = new Bitmap(AssetLoader.Open(new Uri("avares://Reasonix.Studio.Shell/Assets/Community/douyin-qr.jpg")));
        Connect(JoinQq, "https://qm.qq.com/q/i59b0z2R8s");
        Connect(Discord, "https://discord.gg/XF78rEME2D");
        Connect(Issues, "https://github.com/esengine/DeepSeek-Reasonix/issues");
        Connect(AllContributors, "https://github.com/esengine/DeepSeek-Reasonix/graphs/contributors");
        CopyGroup.Click += async (_, _) => await CopyAsync("1093562660").ConfigureAwait(true);
        CopyDouyin.Click += async (_, _) => await CopyAsync("22703872788").ConfigureAwait(true);
        ToggleDouyin.Click += (_, _) =>
        {
            DouyinQr.IsVisible = !DouyinQr.IsVisible;
            LabelDouyin();
        };
        MorePeople.Click += (_, _) =>
        {
            _all = !_all;
            ShowPeople();
        };
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 425;
            Join.RowSpacing = narrow ? 20 : 0;
            Join.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "185,*");
            Grid.SetColumn(QqDetails, narrow ? 0 : 1);
            Grid.SetRow(QqDetails, narrow ? 1 : 0);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            (QqQr.Source as IDisposable)?.Dispose();
            (DouyinQr.Source as IDisposable)?.Dispose();
        };
    }

    /// <summary>Connects the account's community destination to the same platform browser operation.</summary>
    /// <param name="language">Interface language.</param><param name="open">Existing native browser operation.</param>
    public void Configure(InterfaceLanguage language, Func<string, Task> open)
    {
        _language = language;
        _open = open;
        GroupLabel.Text = T("feedback.group_number");
        JoinHint.Text = T("feedback.scan_the_qr_code_with");
        JoinQq.Content = T("feedback.join_the_qq_group");
        DouyinLabel.Text = T("feedback.douyin");
        DouyinIdLabel.Text = T("feedback.douyin_id");
        ContributorsLabel.Text = T("feedback.contributors");
        ContributorsHint.Text = T("feedback.sorted_by_commits_the_list");
        AllContributors.Content = T("feedback.view_all_contributors");
        AutomationProperties.SetName(QqQr, T("feedback.qq_group_qr_code"));
        AutomationProperties.SetName(DouyinQr, T("feedback.douyin_qr_code"));
        AutomationProperties.SetName(CopyGroup, T("feedback.copy_group_number"));
        AutomationProperties.SetName(CopyDouyin, T("feedback.copy_douyin_id"));
        LabelDouyin();
        ShowPeople();
    }

    /// <summary>Reads the existing community message table.</summary>
    /// <param name="key">Canonical message key.</param><returns>Localized copy.</returns>
    private string T(string key) => MessageCatalog.Get(key, _language);

    /// <summary>Changes only the explicitly requested Douyin image visibility.</summary>
    private void LabelDouyin() => ToggleDouyin.Content = T(DouyinQr.IsVisible ? "feedback.hide_qr_code" : "feedback.show_qr_code");

    /// <summary>Offers exactly the release's first 24 contributors until expanded.</summary>
    private void ShowPeople()
    {
        People.Children.Clear();
        foreach (var login in _contributors.Take(_all ? _contributors.Length : 24))
        {
            var button = new Button { Content = login, Classes = { "community-person" } };
            AutomationProperties.SetAutomationId(button, "action:community.profile");
            AutomationProperties.SetName(button, login);
            Connect(button, "https://github.com/" + login);
            People.Children.Add(button);
        }

        MorePeople.IsVisible = _contributors.Length > 24;
        MorePeople.Content = _all ? T("common.collapse") : MessageCatalog.Get("feedback.show_all_n", _language, ("n", _contributors.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Opens a concrete release-owned community destination only on user activation.</summary>
    /// <param name="button">The native command.</param><param name="url">Existing repository destination.</param>
    private void Connect(Button button, string url)
    {
        button.Click += async (_, _) =>
        {
            try
            {
                if (_open is not null)
                {
                    await _open(url).ConfigureAwait(true);
                    Failure.IsVisible = false;
                }
            }
            catch (Exception)
            {
                Failure.Text = MessageCatalog.Get("common.could_not_open_a_browser", _language, ("at", url));
                Failure.IsVisible = true;
            }
        };
    }

    /// <summary>Copies only the number the reader explicitly selected.</summary>
    /// <param name="text">Repository-owned public group or profile number.</param><returns>The clipboard acknowledgement.</returns>
    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }
}
