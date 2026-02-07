using Backdash;
using SpaceWar;
using SpaceWar.Models;
using SpaceWar.Services;

public partial class LobbyScene : Node
{
    GlobalConfig config;
    Label lblStatus;
    Label lblUserName;
    Label lblLobbyName;
    ItemList lstPlayers;
    ItemList lstSpectators;
    LobbyHttpClient httpClient;
    LobbyUdpClient udpClient;
    readonly CancellationTokenSource cts = new();

    Lobby lobbyInfo;
    User user;
    bool readyToStart;
    bool connected;

    static readonly Texture2D blankTexture = GD.Load<Texture2D>("res://textures/blank.tres");

    public override async void _Ready()
    {
        config = GlobalConfig.Instance;
        LoadControls();
        ResetControls();
        UpdateTitle();
        FillHelperLabels();
        UpdateStatus();

        httpClient = new(config.LocalPort, config.ServerUrl, config.LobbyGameId);
        udpClient = new(config.LocalPort, config.ServerUrl, config.ServerUdpPort);

        await RequestLobby();
    }

    void UpdateTitle() =>
        DisplayServer.WindowSetTitle($"Space War {config.LocalPort}: {config.Username}@{config.LobbyName}");

    protected override void Dispose(bool disposing)
    {
        if (!cts.IsCancellationRequested) cts.Cancel();
        cts.Dispose();
        udpClient.Dispose();
        httpClient.Dispose();
        base.Dispose(disposing);
    }

    public override void _Process(double delta)
    {
        if (user is null) return;
        CheckPlayersReady();
    }

    public override async void _Input(InputEvent input)
    {
        if (input.IsActionPressed(ActionNames.Cancel))
            GetTree().Quit();

        if (input.IsActionPressed(ActionNames.Start))
            await ToggleReady();
    }

    public override async void _ExitTree() => await TryLeave();
    async void _OnRefreshLobby() => await RefreshLobby();
    async void _OnPingTimer() => await HandShakePeers();

    async Task TryLeave()
    {
        try
        {
            if (lobbyInfo is null) return;
            lobbyInfo = null;
            Log.Info("Leaving Lobby...");
            await httpClient.LeaveLobby(cts.Token);
            Log.Info("Lobby Leaved.");
        }
        catch (TaskCanceledException)
        {
            // skip
        }
        catch (Exception e)
        {
            Log.Warning($"Unable to leave lobby: {e}");
        }
    }

    void LoadControls()
    {
        lblLobbyName = GetNode<Label>("%lblLobbyName");
        lblUserName = GetNode<Label>("%lblUserName");
        lstPlayers = GetNode<ItemList>("%lstPlayers");
        lstSpectators = GetNode<ItemList>("%lstSpectators");
        lblStatus = GetNode<Label>("%lblStatus");
    }

    void ResetControls()
    {
        lblLobbyName.Text = config.LobbyName;
        lblUserName.Text = config.Username;
        lstPlayers.Clear();
        lstSpectators.Clear();
        UpdateStatus();
    }

    void Status(string text = null) => lblStatus.Text = text ?? "";

    static Color MemberStatusColor(PeerStatus status) => status switch
    {
        PeerStatus.None => Colors.White,
        PeerStatus.Connecting => Colors.Red,
        PeerStatus.Connected => Colors.Orange,
        PeerStatus.Reachable => Colors.SkyBlue,
        PeerStatus.Ready => Colors.Lime,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    void UpdateStatus()
    {
        UpdateStatusText();
        UpdateStatusColor();

        void UpdateStatusText()
        {
            if (config.Mode is PeerMode.Spectator)
                Status("waiting players start...");
            else if (user is null || !connected)
                Status("joining lobby...");
            else if (!AllReachable())
                Status("connecting to players...");
            else if (readyToStart)
                Status("waiting other players...");
            else
                Status("press enter to start.");
        }

        void UpdateStatusColor()
        {
            if (!connected)
                lblUserName.LabelSettings.FontColor = MemberStatusColor(PeerStatus.Connecting);
            else if (config.Mode is PeerMode.Spectator)
                lblUserName.LabelSettings.FontColor = MemberStatusColor(PeerStatus.None);
            else if (readyToStart)
                lblUserName.LabelSettings.FontColor = MemberStatusColor(PeerStatus.Ready);
            else
                lblUserName.LabelSettings.FontColor = MemberStatusColor(PeerStatus.Reachable);
        }
    }

    async Task ToggleReady()
    {
        if (readyToStart || config.Mode is PeerMode.Spectator)
            return;

        if (!AllReachable()) return;

        await httpClient.ToggleReady(cts.Token);

        readyToStart = true;
        UpdateStatus();
    }

    async Task RequestLobby()
    {
        user = null;
        try
        {
            user = await httpClient.EnterLobby(config.LobbyName, config.Username, config.Mode, cts.Token);
            await RefreshLobby();

            config.LobbyName = lobbyInfo.Name;
            config.Username = user.Username;
            lblLobbyName.Text = config.LobbyName;
            lblUserName.Text = config.Username;
            UpdateTitle();

            if (Array.Exists(lobbyInfo.Spectators, s => s.PeerId == user.PeerId))
                config.Mode = PeerMode.Spectator;

            Status();
        }
        catch (OperationCanceledException)
        {
            // skip
        }
        catch (Exception ex)
        {
            Status($"Unable to join: {ex.Message}");
            Log.Error(ex, "Request Lobby Failure");
        }
    }

    bool AllReachable()
    {
        if (lobbyInfo is null || lobbyInfo.Players.Length <= 1)
            return false;

        foreach (var peer in lobbyInfo.Players)
        {
            if (peer.PeerId == user.PeerId)
                continue;

            if (!peer.Connected || !udpClient.IsKnown(peer.PeerId))
                return false;
        }

        return true;
    }

    bool refreshing;

    async Task RefreshLobby()
    {
        if (user is null || refreshing) return;
        refreshing = true;
        try
        {
            if (await httpClient.GetLobby(cts.Token) is not { } lobby)
            {
                Log.Warning("Can't find Lobby...");
                lobbyInfo = null;
                SceneManager.Instance.ChangeTo(SceneName.Login);
                return;
            }

            lobbyInfo = lobby;
            // Log.Info($"On lobby: {lobbyInfo.Name} at {lobbyInfo.CreatedAt}");

            connected = lobbyInfo
                .GetPeers(config.Mode)
                .SingleOrDefault(x => x.PeerId == user.PeerId) is { Connected: true };

            FillMemberList(lstPlayers, lobbyInfo.Players);
            FillMemberList(lstSpectators, lobbyInfo.Spectators);

            await udpClient.HandShake(user);
        }
        catch (OperationCanceledException)
        {
            // skip
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh Lobby Failure");
        }
        finally
        {
            refreshing = false;
            UpdateStatus();
        }
    }

    bool pinging;

    async Task HandShakePeers()
    {
        if (user is null || pinging || lobbyInfo is null || lobbyInfo.Ready) return;

        pinging = true;
        try
        {
            await Task.WhenAll(
                udpClient.HandShake(user, lobbyInfo.Players, cts.Token),
                udpClient.HandShake(user, lobbyInfo.Spectators, cts.Token)
            );
        }
        catch (OperationCanceledException)
        {
            // skip
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Upd Ping Failure");
        }
        finally
        {
            pinging = false;
        }
    }

    void FillMemberList(ItemList list, IEnumerable<Peer> peers)
    {
        list.Clear();
        foreach (var player in peers)
        {
            var status = GetStatus(player);
            var index = list.AddItem(player.Username, blankTexture, false);
            list.SetItemIconModulate(index, MemberStatusColor(status));
            list.SetItemTooltip(index, status.ToString());
        }

        return;

        PeerStatus GetStatus(Peer player)
        {
            if (player.PeerId == user.PeerId)
            {
                if (!connected) return PeerStatus.Connecting;
                return readyToStart ? PeerStatus.Ready : PeerStatus.Reachable;
            }

            if (!player.Connected)
                return PeerStatus.Connecting;

            if (!udpClient.IsKnown(player.PeerId))
                return PeerStatus.Connected;

            return player.Ready ? PeerStatus.Ready : PeerStatus.Reachable;
        }
    }

    void FillHelperLabels()
    {
        var labels = new[]
        {
            (Status: PeerStatus.Connecting, Text: "Not connected to the server yet."),
            (Status: PeerStatus.Connected, Text: "Valid UDP connection with server."),
            (Status: PeerStatus.Reachable, Text: "Valid UDP connection with peers."),
            (Status: PeerStatus.Ready, Text: "Ready to start the game."),
        };

        var list = GetNode<ItemList>("%lstStatusLabels");
        list.Clear();
        list.MaxColumns = labels.Length;
        foreach (var (status, tooltip) in labels)
        {
            var index = list.AddItem(status.ToString(), blankTexture, false);
            list.SetItemIconModulate(index, MemberStatusColor(status));
            list.SetItemTooltip(index, tooltip);
        }
    }

    static void LoadBattleScene() => SceneManager.Instance.ChangeTo(SceneName.Battle);

    void CheckPlayersReady()
    {
        if (lobbyInfo is null or { Ready: false }) return;

        cts.Cancel();
        udpClient.Stop();

        Log.Info($"STARTING {config.Username} AS '{config.Mode}' ON {config.LobbyName}");
        config.LobbyInfo = lobbyInfo;

        switch (config.Mode)
        {
            case PeerMode.Player:
                ConfigureBattleScene();
                break;
            case PeerMode.Spectator:
                ConfigureSpectatorScene();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        LoadBattleScene();

        return;

        void ConfigureBattleScene()
        {
            var players = lobbyInfo.Players
                .Select(player =>
                    player.PeerId == user.PeerId
                        ? NetcodePlayer.CreateLocal()
                        : NetcodePlayer.CreateRemote(player.GetEndpointForUser(user)))
                .ToList();

            if (lobbyInfo.SpectatorMapping.SingleOrDefault(m => m.Host == user.PeerId)
                is { Watchers: { Length: > 0 } spectatorIds })
            {
                var spectators = lobbyInfo.Spectators
                    .Where(s => spectatorIds.Contains(s.PeerId))
                    .Select(s => NetcodePlayer.CreateSpectator(s.GetEndpointForUser(user)));

                players.AddRange(spectators);
            }

            config.MatchPlayers = players.AsReadOnly();
        }

        void ConfigureSpectatorScene()
        {
            var hostId = lobbyInfo.SpectatorMapping.SingleOrDefault(x => x.Watchers.Contains(user.PeerId))?.Host;
            var host = lobbyInfo.Players.Single(x => x.PeerId == hostId);
            config.SpectateHost = host.GetEndpointForUser(user);
            DisplayServer.WindowSetTitle($"Space War {config.LocalPort}: watching {host.Username}@{lobbyInfo.Name}");
        }
    }

    public enum PeerStatus
    {
        None,
        Connecting,
        Connected,
        Reachable,
        Ready,
    }
}
