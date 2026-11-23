using Reasonix.Studio.State;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

public sealed partial class SessionEngine
{
    /// <summary>The latest canonical task-list request.</summary>
    private long _planRead;

    /// <summary>Reads the host's plan without inferring advances from the transcript.</summary>
    /// <param name="token">The initiating operation's lifetime.</param><returns>The optional refresh.</returns>
    private async Task RefreshPlanAsync(CancellationToken token = default)
    {
        var ticket = Interlocked.Increment(ref _planRead);
        var generation = Generation;
        PlanStep[] plan;
        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token, _closing.Token);
            var values = await _client.Pages.TodosAsync(request.Token).ConfigureAwait(false);
            plan = values.OfType<JsonObject>().Select(row => new PlanStep
            {
                Text = (string?)row["content"] ?? string.Empty,
                Status = (string?)row["status"] switch
                {
                    "completed" => TodoStatus.Completed,
                    "in_progress" => TodoStatus.InProgress,
                    _ => TodoStatus.Pending,
                },
                ActiveForm = (string?)row["activeForm"],
                Level = (int?)row["level"],
            }).ToArray();
        }
        catch (Exception)
        {
            // An optional read cannot make an acknowledged plan disappear.
            return;
        }

        if (plan.All(step => step.Status == TodoStatus.Completed))
        {
            plan = [];
        }

        SessionState next;
        lock (_sync)
        {
            if (token.IsCancellationRequested || Volatile.Read(ref _disposed) != 0 || ticket != Interlocked.Read(ref _planRead)
                || !ReferenceEquals(generation, Generation) || _state.Plan.SequenceEqual(plan))
            {
                return;
            }

            next = _state with
            {
                Plan = plan,
                Revision = _state.Revision + 1,
            };
            _state = next;
        }

        Changed?.Invoke(next);
    }
}
