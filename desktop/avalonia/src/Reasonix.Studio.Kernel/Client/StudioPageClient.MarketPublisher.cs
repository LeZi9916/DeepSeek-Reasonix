using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads marketplace packages belonging to the current account.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>Publisher inventory.</returns>
    public Task<JsonObject> MarketMineAsync(CancellationToken token) => ReadAsync<JsonObject>("/market/mine", token);

    /// <summary>Sends the publisher's explicit marketplace submission.</summary>
    /// <param name="submission">Fields from the current publish form.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The publisher's receipt.</returns>
    public Task<JsonObject> PublishMarketAsync(JsonObject submission, CancellationToken token) =>
        DataPostAsync("/market/publish", submission, token);

    /// <summary>Submits an existing private package for public review.</summary>
    /// <param name="slug">The publisher's package.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The updated marketplace entry.</returns>
    public Task<JsonObject> SubmitMarketAsync(string slug, CancellationToken token) =>
        DataPostAsync("/market/mine/" + MarketPackageId(slug) + "/submit", new JsonObject(), token);

    /// <summary>Previews or applies the publisher's own package using the account's installation route.</summary>
    /// <param name="apply">Whether the reviewed plan has been accepted.</param>
    /// <param name="request">Package identity and the exact reviewed plan when applying.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The installation plan or result.</returns>
    public Task<JsonObject> MarketOwnPlanAsync(bool apply, JsonObject request, CancellationToken token) =>
        DataPostAsync(apply ? "/market/mine/install" : "/market/mine/plan", request, token);
}
