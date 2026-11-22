using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Lifetime of the active draft rewrite.</summary>
    private CancellationTokenSource? _refineCancellation;

    /// <summary>The proposal awaiting an explicit adoption.</summary>
    private string _refinedText = string.Empty;

    /// <summary>The runtime owning the side conversation.</summary>
    private StudioPageClient? _sideApi;

    /// <summary>The side conversation identity on its owning runtime.</summary>
    private string? _sideId;

    /// <summary>Lifetime of an isolated side answer.</summary>
    private CancellationTokenSource? _sideCancellation;

    /// <summary>Requests a proposal while preserving the current input.</summary>
    /// <returns>The completed proposal or visible refusal.</returns>
    private async Task RefineDraftAsync()
    {
        if (_engine is not { } engine || string.IsNullOrWhiteSpace(Composer.Text))
        {
            return;
        }

        DismissRefinement();
        var source = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _refineCancellation = source;
        var draft = _draftKey;
        var text = Composer.Text;
        RefineCard.IsVisible = true;
        RefineText.Text = MessageCatalog.Get("composer.refining", Language);
        AdoptRefine.IsVisible = false;
        RetryRefine.IsVisible = false;
        RefinePrompt.IsEnabled = false;
        try
        {
            var result = await engine.Pages.RefinePromptAsync(text, source.Token).ConfigureAwait(true);
            if (source.IsCancellationRequested || engine != _engine || draft != _draftKey)
            {
                return;
            }

            _refinedText = result;
            RefineText.Text = result;
            AdoptRefine.IsVisible = true;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (engine == _engine && draft == _draftKey)
            {
                RefineText.Text = DescribeError(error);
            }
        }
        finally
        {
            if (ReferenceEquals(_refineCancellation, source))
            {
                RefinePrompt.IsEnabled = true;
                RetryRefine.IsVisible = true;
            }
        }
    }

    /// <summary>Discards a proposal and cancels any response still in flight.</summary>
    private void DismissRefinement()
    {
        _refineCancellation?.Cancel();
        _refineCancellation?.Dispose();
        _refineCancellation = null;
        _refinedText = string.Empty;
        RefineCard.IsVisible = false;
        RefinePrompt.IsEnabled = true;
    }

    /// <summary>Opens a separate read-only question without submitting the main draft.</summary>
    /// <returns>The side conversation creation.</returns>
    private async Task ToggleSideAsync()
    {
        if (SideCard.IsVisible)
        {
            SideCard.IsVisible = false;
            SideQuestion.Classes.Remove("open");
            return;
        }

        if (_engine is not { } engine)
        {
            return;
        }

        SideCard.IsVisible = true;
        SideQuestion.Classes.Add("open");
        if (_sideApi != engine.Pages || _sideId is null)
        {
            var draft = _draftKey;
            SideQuestion.IsEnabled = false;
            try
            {
                await TryAnswerAsync(async () =>
                {
                    var id = await engine.Pages.OpenSideAsync(_closing.Token).ConfigureAwait(true);
                    if (engine != _engine || draft != _draftKey || !SideCard.IsVisible)
                    {
                        await engine.Pages.CloseSideAsync(id, _closing.Token).ConfigureAwait(true);
                        return;
                    }

                    _sideApi = engine.Pages;
                    _sideId = id;
                }).ConfigureAwait(true);
            }
            finally
            {
                SideQuestion.IsEnabled = true;
            }
        }

        SideInput.Focus();
    }

    /// <summary>Reads a side answer without writing to the main conversation.</summary>
    /// <returns>The completed answer.</returns>
    private async Task AskSideQuestionAsync()
    {
        if (_sideApi is not { } api || _sideId is not { } id || string.IsNullOrWhiteSpace(SideInput.Text) || !AskSide.IsEnabled)
        {
            return;
        }

        _sideCancellation?.Dispose();
        var source = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _sideCancellation = source;
        AskSide.IsEnabled = false;
        SideAnswer.Text = string.Empty;
        QuoteSide.IsEnabled = false;
        var draft = _draftKey;
        var completed = false;
        try
        {
            var progress = new Progress<string>(text =>
            {
                if (!completed && !source.IsCancellationRequested && draft == _draftKey && _sideId == id)
                {
                    SideAnswer.Text += text;
                }
            });
            var answer = await api.AskSideAsync(id, SideInput.Text, progress, source.Token).ConfigureAwait(true);
            completed = true;
            if (!source.IsCancellationRequested && draft == _draftKey && _sideId == id)
            {
                SideAnswer.Text = answer;
                SideInput.Text = string.Empty;
                QuoteSide.IsEnabled = true;
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (draft == _draftKey && _sideId == id)
            {
                SideAnswer.Text = DescribeError(error);
            }
        }
        finally
        {
            AskSide.IsEnabled = true;
        }
    }

    /// <summary>Closes the side question on the runtime that created it.</summary>
    /// <returns>The host acknowledgement.</returns>
    private async Task CloseSideQuestionAsync()
    {
        var api = _sideApi;
        var id = _sideId;
        _sideCancellation?.Cancel();
        _sideApi = null;
        _sideId = null;
        SideCard.IsVisible = false;
        SideQuestion.Classes.Remove("open");
        SideInput.Text = string.Empty;
        SideAnswer.Text = string.Empty;
        if (api is not null && id is not null)
        {
            await TryAnswerAsync(() => api.CloseSideAsync(id, _closing.Token)).ConfigureAwait(true);
        }

        SideQuestion.Focus();
    }
}
