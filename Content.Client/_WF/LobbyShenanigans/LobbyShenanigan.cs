using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.LobbyShenanigans;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Client._WF.LobbyShenanigans;

/// <summary>
/// One bit of lobby fun, set off for every client from the SSymphony panel. A subclass is found by reflection and
/// moves and draws on a layer over the whole lobby, which takes a click only on something one of them shows.
/// </summary>
public abstract class LobbyShenanigan
{
    /// <summary>One of <see cref="LobbyShenaniganIds"/>, which also names its Fluent strings.</summary>
    public abstract string Id { get; }

    /// <summary>Lower is drawn first, under the rest.</summary>
    public virtual int DrawOrder => 0;

    /// <summary>Every shenanigan on the layer, this one too, so one can play with what another shows.</summary>
    public IReadOnlyList<LobbyShenanigan> All { get; internal set; } = Array.Empty<LobbyShenanigan>();

    /// <summary>The server set it off.</summary>
    public abstract void Trigger(Vector2 bounds, float scale);

    /// <summary>Moves on by <paramref name="dt"/> seconds in a layer of <paramref name="bounds"/> pixels.</summary>
    public abstract void Update(float dt, Vector2 bounds, float scale);

    /// <summary>Draws in the layer's pixels.</summary>
    public abstract void Draw(DrawingHandleScreen handle, float scale);

    /// <summary>Staff cleared the lobby: everything it shows goes.</summary>
    public abstract void Clear();

    /// <summary>Whether something it shows is under <paramref name="pos"/>, in the layer's pixels; the layer takes clicks only there.</summary>
    public virtual bool HitTest(Vector2 pos) => false;

    /// <summary>Picks up, or otherwise answers a click on, what is under <paramref name="pos"/>; false when nothing is there.</summary>
    public virtual bool TryGrab(Vector2 pos) => false;

    /// <summary>The mouse moved to <paramref name="pos"/> while holding what was picked up.</summary>
    public virtual void Drag(Vector2 pos, float dt)
    {
    }

    /// <summary>The mouse button came up.</summary>
    public virtual void Release()
    {
    }

    /// <summary>Plays a sound to this player only; quietly nothing while the game's systems are down.</summary>
    protected static void PlaySound(ResolvedSoundSpecifier sound, float volume)
    {
        if (IoCManager.Resolve<IEntitySystemManager>().TryGetEntitySystem<SharedAudioSystem>(out var audio))
            audio.PlayGlobal(sound, Filter.Local(), false, AudioParams.Default.WithVolume(volume).WithVariation(0.1f));
    }
}

/// <summary>A shenanigan whose pieces get knocked aside by something passing through, such as a carp.</summary>
public interface ILobbyShovable
{
    /// <summary>Pushes whatever is within <paramref name="radius"/> of <paramref name="at"/> out of the way, along with <paramref name="velocity"/>.</summary>
    void Shove(Vector2 at, float radius, Vector2 velocity);
}

/// <summary>A shenanigan whose pieces can be pulled in and swallowed, by a singularity.</summary>
public interface ILobbyPullable
{
    /// <summary>
    /// Draws whatever is within <paramref name="reach"/> of <paramref name="at"/> towards it, harder the closer it is,
    /// and removes what comes within <paramref name="swallow"/>. Returns how many it swallowed.
    /// </summary>
    int Pull(Vector2 at, float reach, float swallow, float strength, float dt);
}
