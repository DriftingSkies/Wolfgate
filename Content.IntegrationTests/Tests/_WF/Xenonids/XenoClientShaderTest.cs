#nullable enable
using Robust.Client.Graphics;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Every shader the ported client code asks for by name exists and its source ships with the client.</summary>
[TestFixture]
public sealed class XenoClientShaderTest
{
    [TestCase("RMCAuraOutline", "/Textures/Shaders/outline.swsl")]
    [TestCase("RMCInvisible", "/Textures/_WF/Xenonids/Shaders/invisible.swsl")]
    [TestCase("RMCShockWave", "/Textures/_WF/Xenonids/Shaders/shock_wave.swsl")]
    [TestCase("RMCXenoScreechShockWave", "/Textures/_WF/Xenonids/Shaders/screech_shock_wave.swsl")]
    [TestCase("RMCDoomVision", "/Textures/_WF/Xenonids/Shaders/night_vision.swsl")]
    public async Task ShaderExistsOnClient(string id, string source)
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var resources = client.ResolveDependency<IResourceManager>();

        await client.WaitAssertion(() =>
        {
            Assert.That(protoMan.HasIndex<ShaderPrototype>(id), $"{id} is a known shader on the client.");
            Assert.That(resources.ContentFileExists(new ResPath(source)), $"{source} ships with the client.");
        });

        await pair.CleanReturnAsync();
    }
}
