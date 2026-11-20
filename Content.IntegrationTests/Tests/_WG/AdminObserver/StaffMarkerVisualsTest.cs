#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.AdminObserver;
using Content.Client.Ghost;
using Content.Server._WG.AdminObserver;
using Content.Shared._WG.AdminObserver;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.AdminObserver;

/// <summary>The staff marker's visuals: what the server lets through, and what a client makes of it.</summary>
[TestFixture]
[TestOf(typeof(WGStaffMarkerSystem))]
public sealed class StaffMarkerVisualsTest
{
    private static readonly WGMarkerVisuals[] WithVariants =
    {
        WGMarkerVisuals.Ring, WGMarkerVisuals.Glyph, WGMarkerVisuals.Shield, WGMarkerVisuals.Aura,
    };

    [Test]
    public async Task ServerDecidesColourSigilAndVariants()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var prototypes = server.ProtoMan;
        var marker = server.System<WGStaffMarkerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var marked = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // Every combination with at least one visual goes through as asked.
            for (var flags = 1; flags <= (int) WGStaffMarkerSystem.Supported; flags++)
            {
                var asked = (WGMarkerVisuals) flags;
                var got = marker.Mark(marked, new WGStaffMarkerSettings { Visuals = asked }, null, "moon", false).Visuals;
                Assert.That(got, Is.EqualTo(asked));
            }

            // Each visual with several looks has exactly one default, and a sprite look has its sprite.
            var variants = prototypes.EnumeratePrototypes<WGStaffMarkerVariantPrototype>().ToList();
            Assert.Multiple(() =>
            {
                foreach (var visual in WithVariants)
                {
                    Assert.That(variants.Count(v => v.Visual == visual && v.Default), Is.EqualTo(1), $"{visual} needs one default look.");
                }

                foreach (var variant in variants)
                {
                    Assert.That(WithVariants, Does.Contain(variant.Visual), $"{variant.ID} is a look for no visual.");
                    Assert.That(variant.Sprite != null, Is.EqualTo(variant.Kind == WGMarkerVariantKind.Sprite), $"{variant.ID}: sprite and kind disagree.");
                }
            });

            // Without the Host flag: the fixed colour and the staff sigil, whatever was asked for.
            var settings = new WGStaffMarkerSettings
            {
                Visuals = WGStaffMarkerSystem.Supported,
                Color = "#00ff00",
                Ring = "WGStaffRingPulseIn",
                Glyph = "WGStaffRingPulseIn",
                Shield = "NoSuchLook",
                Aura = "WGStaffAuraOutline",
            };
            var staff = marker.Mark(marked, settings, "Admin", "moon", host: false);
            Assert.Multiple(() =>
            {
                Assert.That(staff.Color, Is.EqualTo(WGStaffMarkerComponent.StaffColor));
                Assert.That(staff.Host, Is.False);
                Assert.That(staff.Ring, Is.EqualTo("WGStaffRingPulseIn"), "A chosen look is kept.");
                Assert.That(staff.Aura, Is.EqualTo("WGStaffAuraOutline"));
                Assert.That(staff.Glyph, Is.EqualTo("WGStaffGlyphStar"), "A look for another visual falls back to the default.");
                Assert.That(staff.Shield, Is.EqualTo("WGStaffShieldBubble"), "An unknown look falls back to the default.");
                Assert.That(prototypes.HasIndex(staff.AdminSigil) && prototypes.HasIndex(staff.HostSigil));
            });

            // With it: the host sigil and the saved colour.
            var host = marker.Mark(marked, settings, "Host", "moon", host: true);
            Assert.Multiple(() =>
            {
                Assert.That(host.Host);
                Assert.That(host.Color, Is.EqualTo(Color.FromHex("#00ff00")));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientDrawsWhatItIsSent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var manifest = server.System<WGManifestSystem>();
        var marker = server.System<WGStaffMarkerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await AdminKitFixture.Living(pair, map.GridCoords);
        var observer = await AdminKitFixture.Unmanned(pair, map.GridCoords);
        var netObserver = server.EntMan.GetNetEntity(observer);

        // A player back in a body after ghosting has ghosts switched off.
        await client.WaitPost(() => client.System<GhostSystem>().ToggleGhostVisibility(false));

        var settings = new WGStaffMarkerSettings
        {
            Visuals = WGStaffMarkerSystem.Supported,
            Aura = "WGStaffAuraOutline",
            Shield = "WGStaffShieldSkin",
        };
        await server.WaitPost(() =>
        {
            manifest.Manifest(observer, new WGAvatarPreset { EntryStyle = WGEntryStyle.Silent }, null);
            marker.Mark(observer, settings, "Admin", "moon", host: true);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var uid = client.EntMan.GetEntity(netObserver);
            var sent = client.EntMan.GetComponent<WGStaffMarkerComponent>(uid);
            var sprite = client.EntMan.GetComponent<SpriteComponent>(uid);
            var passes = client.System<SpriteSystem>().GetPostShaders(sprite).Select(p => p.Id).ToList();
            var icons = new GetStatusIconsEvent(new List<StatusIconData>());
            client.EntMan.EventBus.RaiseLocalEvent(uid, ref icons);

            Assert.Multiple(() =>
            {
                Assert.That(sent.Visuals, Is.EqualTo(WGStaffMarkerSystem.Supported));
                Assert.That(sent.Color, Is.EqualTo(WGStaffMarkerComponent.HostColor));
                Assert.That(sent.Host);
                Assert.That(sent.Ring, Is.EqualTo("WGStaffRingBoiler"));
                Assert.That(sent.Shield, Is.EqualTo("WGStaffShieldSkin"));
                Assert.That(sprite.Visible, "A manifested observer is drawn even with ghosts switched off.");
                Assert.That(passes, Does.Contain(WGStaffMarkerVisualsSystem.HologramPass));
                Assert.That(passes, Does.Contain(WGStaffMarkerVisualsSystem.OutlinePass));
                Assert.That(icons.StatusIcons.Select(i => (i as SsdIconPrototype)?.ID), Does.Contain(sent.HostSigil.Id));
            });

            var visuals = client.EntMan.GetComponent<WGStaffMarkerVisualsComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(visuals.Skin, Is.Not.Null, "The shield skin has its shader.");
                Assert.That(visuals.Light, Is.Null, "An outline aura makes no light.");
            });
        });

        // The aura as a light, and nothing else.
        settings.Visuals = WGMarkerVisuals.Aura;
        settings.Aura = "WGStaffAuraLight";
        await server.WaitPost(() => marker.Mark(observer, settings, "Admin", "moon", host: false));
        await pair.RunTicksSync(10);

        EntityUid? light = null;
        await client.WaitAssertion(() =>
        {
            var uid = client.EntMan.GetEntity(netObserver);
            var visuals = client.EntMan.GetComponent<WGStaffMarkerVisualsComponent>(uid);
            var sprite = client.EntMan.GetComponent<SpriteComponent>(uid);
            var icons = new GetStatusIconsEvent(new List<StatusIconData>());
            client.EntMan.EventBus.RaiseLocalEvent(uid, ref icons);
            light = visuals.Light;

            Assert.Multiple(() =>
            {
                Assert.That(client.System<SpriteSystem>().GetPostShaders(sprite), Is.Empty, "Switched-off passes are taken off the sprite.");
                Assert.That(icons.StatusIcons, Is.Empty);
                Assert.That(visuals.Skin, Is.Null);
                Assert.That(light, Is.Not.Null);
            });

            Assert.Multiple(() =>
            {
                Assert.That(client.EntMan.GetComponent<TransformComponent>(light!.Value).ParentUid, Is.EqualTo(uid));
                Assert.That(client.EntMan.GetComponent<PointLightComponent>(light.Value).Color, Is.EqualTo(WGStaffMarkerComponent.StaffColor));
            });
        });

        await server.WaitPost(() => manifest.Unmanifest(observer));
        await pair.RunTicksSync(10);
        // A living player's client stops hearing about the observer, so the light leaves the map with it.
        await client.WaitAssertion(() =>
        {
            var gone = !client.EntMan.EntityExists(light)
                       || client.EntMan.GetComponent<TransformComponent>(light!.Value).MapID == MapId.Nullspace;
            Assert.That(gone, "Hiding takes the aura's light out of the world.");
        });

        await pair.CleanReturnAsync();
    }
}
