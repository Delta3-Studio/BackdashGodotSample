using Backdash;
using Backdash.Core;
using SpaceWar.Logic;
using SpaceWar.Models;
using SpaceWar.Services;

public partial class BattleScene : Node2D
{
    GlobalConfig config;
    INetcodeSession<GameInputs> netcode;
    GameSessionHandler handler;
    GameState gs;
    NonGameState ngs;

    [Export] Label lblPing;
    [Export] Label lblRollbackFrames;
    [Export] Label lblDelayFrames;
    [Export] Label lblStatusMessage;
    [Export] PanelContainer messageBox;

    public override void _Ready()
    {
        config = GlobalConfig.Instance;
        lblStatusMessage.Text = "";
        messageBox.Visible = false;
        InitializeSession();
    }

    void InitializeSession()
    {
        gs = new();
        ngs = new();

        netcode = CreateRollbackSession();
        gs.Init(netcode.NumberOfPlayers, (Rect2I)GetViewportRect());
        ngs.Init(gs.Ships, netcode.GetPlayers(), this);
        Engine.SetMaxFps(netcode.FixedFrameRate);

        handler = new(gs, ngs, netcode);
        netcode.SetHandler(handler);
        netcode.Start();
    }

    INetcodeSession<GameInputs> CreateRollbackSession()
    {
        var builder =
            RollbackNetcode
                .WithInputType<GameInputs>()
                .WithPort(config.LocalPort)
                .WithInputDelayFrames(2)
                .WithLogLevel(LogLevel.Warning)
                .WithLogWriter<GDBackdashLogger>();

        switch (config.Mode)
        {
            case PeerMode.Player:
                if (!config.MatchPlayers.Any(x => x.IsLocal()))
                    throw new InvalidOperationException("No local player defined");

                return builder
                    .WithPlayers(config.MatchPlayers)
                    .ForRemote()
                    .Build();

            case PeerMode.Spectator:
                return builder
                    .WithPlayerCount(config.LobbyInfo.Players.Length)
                    .ForSpectator(config.SpectateHost)
                    .Build();

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (handler is null) return;
        handler.Update(delta);
        UpdateUi();
    }

    void UpdateUi()
    {
        lblPing.Text = $"{netcode.MaxPing:f2}";
        lblRollbackFrames.Text = netcode.RollbackFrames.Frames.ToString();
        lblDelayFrames.Text = netcode.DynamicFrameDelay.Frames.ToString();

        if (!messageBox.Visible && ngs.StatusText.Length > 0)
        {
            messageBox.Visible = true;
            lblStatusMessage.Text = ngs.StatusText.ToString();
        }
        else if (messageBox.Visible && ngs.StatusText.Length is 0)
        {
            messageBox.Visible = false;
            lblStatusMessage.Text = "";
        }
    }

    public override void _Input(InputEvent input)
    {
        if (input.IsActionPressed(ActionNames.Cancel))
            GetTree().Quit();
    }

    protected override void Dispose(bool disposing)
    {
        netcode.Dispose();
        base.Dispose(disposing);
    }
}
