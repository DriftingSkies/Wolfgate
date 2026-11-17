using Robust.Shared.Input;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.MappingMode.Modes;

/// <summary>A mode handler that claims nothing unless it overrides a method.</summary>
public abstract partial class WGMappingModeHandlerBase : IWGMappingModeHandler
{
    protected readonly WGMappingBuildModeSystem Modes;

    protected WGMappingModeHandlerBase(WGMappingBuildModeSystem modes)
    {
        Modes = modes;
        IoCManager.InjectDependencies(this);
    }

    public abstract WGMappingBuildMode Mode { get; }

    public virtual void Enter()
    {
    }

    public virtual void Exit()
    {
    }

    public virtual bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args) => false;

    public virtual bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args) => false;

    public virtual bool Middle(in PointerInputCmdHandler.PointerInputCmdArgs args) => false;

    public virtual bool Wheel(float delta) => false;

    public virtual bool Cancel() => false;

    protected static bool IsDown(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return args.State == BoundKeyState.Down;
    }
}
