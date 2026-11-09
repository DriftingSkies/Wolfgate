#nullable enable
using Content.Server._WF.Photography;
using Content.Shared._WF.Photography;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Photography;

/// <summary>Duplicate saved albums are removed, and blanking a photo hash clears every copy in the round.</summary>
[TestFixture]
[TestOf(typeof(PhotoStoreSystem))]
public sealed class PhotographyTest
{
    private const string AlbumProto = "WFPhotoAlbumPersonal";
    private const string PhotoProto = "WFPhoto";
    private static readonly string Hash = new('A', 64);

    [Test]
    public async Task SecondCopyOfAlbumIsDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid first = default;
        EntityUid second = default;
        await server.WaitPost(() =>
        {
            first = SpawnOwnedAlbum(entMan, map.GridCoords, "album-a");
            second = SpawnOwnedAlbum(entMan, map.GridCoords, "album-a");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(first), Is.False, "The album already in the world was deleted.");
            Assert.That(entMan.Deleted(second), Is.True, "The second copy of the album was kept.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlankClearsPhotosAndGalleries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var store = entMan.System<PhotoStoreSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var photoUid = entMan.SpawnEntity(PhotoProto, map.GridCoords);
            var photo = entMan.GetComponent<PhotoComponent>(photoUid);
            photo.Hash = Hash;
            photo.Thumbnail = "thumbnail";

            var holder = entMan.SpawnEntity(null, map.GridCoords);
            var gallery = entMan.AddComponent<PhotoGalleryComponent>(holder);
            gallery.Photos.Add(new PhotoGalleryEntry { Hash = Hash });
            gallery.Photos.Add(new PhotoGalleryEntry { Hash = new string('B', 64) });

            Assert.That(store.Blank(Hash), Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(photo.Hash, Is.Empty);
                Assert.That(photo.Thumbnail, Is.Empty);
                Assert.That(gallery.Photos, Has.Count.EqualTo(1));
                Assert.That(gallery.Photos[0].Hash, Is.Not.EqualTo(Hash));
            });
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnOwnedAlbum(IEntityManager entMan, Robust.Shared.Map.EntityCoordinates coords,
        string id)
    {
        var album = entMan.SpawnEntity(AlbumProto, coords);
        var owner = entMan.ComponentFactory.GetComponent<PhotoAlbumOwnerComponent>();
        owner.AlbumId = id;
        entMan.AddComponent(album, owner);
        return album;
    }
}
