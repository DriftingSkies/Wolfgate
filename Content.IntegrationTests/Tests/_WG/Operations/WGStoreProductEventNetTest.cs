using System.Collections.Generic;
using System.IO;
using Content.Shared.Store;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._WG.Operations;

/// <summary>
/// A store sends each listing's purchase event to the client with the listing, so every one must be net-serializable;
/// one that isn't leaves everyone who can see the store without a game state.
/// </summary>
[TestFixture]
public sealed class WGStoreProductEventNetTest
{
    [Test]
    public async Task EveryListingEventCanBeSent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var serializer = server.ResolveDependency<IRobustSerializer>();

        await server.WaitAssertion(() =>
        {
            var failures = new List<string>();
            foreach (var listing in protos.EnumeratePrototypes<ListingPrototype>())
            {
                if (listing.ProductEvent is not { } ev)
                    continue;

                try
                {
                    using var stream = new MemoryStream();
                    serializer.Serialize(stream, ev);
                }
                catch (Exception e)
                {
                    failures.Add($"{listing.ID} ({ev.GetType().Name}): {e.Message}");
                }
            }

            Assert.That(failures, Is.Empty, "Listings whose purchase event can't be sent to a client:\n" + string.Join("\n", failures));
        });

        await pair.CleanReturnAsync();
    }
}
