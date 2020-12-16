using System;
using System.Threading.Tasks;

using Impostor.Api.Events;
using Impostor.Api.Innersloth;
using Impostor.Api.Events.Player;

using Microsoft.Extensions.Logging;
using Impostor.Api.Net.Inner.Objects;

namespace LobbyCommands
{
    public class GameEventListener : IEventListener
    {
        readonly ILogger<LobbyCommandsPlugin> _logger;
        private const string commandprefix = "/lc";

        static readonly string[] _mapNames = Enum.GetNames(typeof(MapTypes));
        static readonly int _minImposters = 1;
        static int _maxImposters => Math.DivRem(_curPlayers, 3, out _);
        static readonly int _minPlayers = 4;
        static int _curPlayers = 10;
        static readonly int _maxPlayers = 128;

        private static string HelpText => $"- Lobby Commands Plugin -" +
                    $"\n/lc help - Lists all available commands" +
                    $"\n" +
                    $"\n/lc impostors {{limit}} - Set the impostor limit ({_minImposters} - {_maxImposters})" +
                    $"\n" +
                    $"\n/lc players {{limit}} - Set the player limit ({_minPlayers} - {_maxPlayers})" +
                    $"\n" +
                    $"\n/lc map {{mapname}} - Set the map ({string.Join(", ", _mapNames)})";

        public GameEventListener(ILogger<LobbyCommandsPlugin> logger) => _logger = logger;

        [EventListener]
        public void OnPlayerChat(IPlayerChatEvent e)
        {
            if (e.Game.GameState != GameStates.NotStarted || !e.Message.StartsWith(commandprefix) || !e.ClientPlayer.IsHost)
                return;

            Task.Run(async () => await EvalCommand(e));
        }

        [EventListener]
        public void OnGameCreated(IGameCreatedEvent e)
        {
            if (e.Game.GameState != GameStates.NotStarted)
                return;

            Task.Run(async () => await ShowMOTD(e));
        }

        private async Task ShowMOTD(IGameCreatedEvent e)
        {
            await Task.Delay(1000);
            await SendChatAsServer(e.Game.Host.Character, HelpText);
        }

        private async Task SendChatAsServer(IInnerPlayerControl p, string msg)
        {
            var currentColor = p.PlayerInfo.ColorId;
            var currentName = p.PlayerInfo.PlayerName;
            await p.SetColorAsync(Impostor.Api.Innersloth.Customization.ColorType.White);
            await p.SetNameAsync("LobbyCommands");
            await p.SendChatAsync(msg);
            await p.SetColorAsync(currentColor);
            await p.SetNameAsync(currentName);
        }

        private async Task EvalCommand(IPlayerChatEvent e)
        {
            _logger.LogDebug($"Attempting to evaluate command from {e.PlayerControl.PlayerInfo.PlayerName} on {e.Game.Code.Code}. Message was: {e.Message}");

            string[] args = e.Message.ToLowerInvariant()[($"{commandprefix} ".Length)..].Split(" ");

            switch (args[0])
            {
                case "help":
                    await SendChatAsServer(e.PlayerControl, HelpText);
                    break;
                case "map":
                    if (args.Length != 2)
                    {
                        await SendChatAsServer(e.PlayerControl, $"/lc map {{mapname}}\nSet the current map. Options are: {string.Join(", ", _mapNames)}");
                        return;
                    }

                    var tryMap = Enum.TryParse<MapTypes>(args[1], ignoreCase: true, out var map);

                    if (tryMap == false)
                    {
                        await SendChatAsServer(e.PlayerControl, $"[FF0000FF]Error: Unknown map.");
                        break;
                    }

                    await SendChatAsServer(e.PlayerControl, $"[00FF00FF]Map has been set to {map}");

                    e.Game.Options.Map = map;
                    await e.Game.SyncSettingsAsync();
                    break;
                case "impostors":
                    if (args.Length != 2)
                    {
                        await SendChatAsServer(e.PlayerControl, $"/lc impostors {{limit}} - Set the impostor limit. Value must be within {_minImposters} and {_maxImposters}");
                        break;
                    }

                    var tryImpostorLimit = int.TryParse(args[1], out var impostorLimit);

                    if (tryImpostorLimit == false)
                    {
                        await SendChatAsServer(e.PlayerControl, "[FF0000FF]Error: Please enter a valid whole number!");
                        break;
                    }

                    if (impostorLimit > _maxImposters || impostorLimit < _minImposters)
                    {
                        await SendChatAsServer(e.PlayerControl, $"[FF0000FF]Error: Impostor limit can only be within {_maxImposters} and {_minImposters}!");
                        await e.Game.SyncSettingsAsync();
                        break;
                    }

                    impostorLimit = Math.Clamp(impostorLimit, _minImposters, _maxImposters);

                    await SendChatAsServer(e.PlayerControl, $"[00FF00FF]Impostor limit has been set to {impostorLimit}");

                    e.Game.Options.NumImpostors = impostorLimit;
                    await e.Game.SyncSettingsAsync();
                    break;
                case "players":
                    if (args.Length != 2)
                    {
                        await SendChatAsServer(e.PlayerControl, $"/lc players {{limit}} - Set the player limit. Value must be within {_minPlayers} and {_maxPlayers}");
                        break;
                    }

                    bool tryPlayerLimit = int.TryParse(args[1], out var playerLimit);
                    if (tryPlayerLimit == false)
                    {
                        await SendChatAsServer(e.PlayerControl, "[FF0000FF]Error: Please enter a valid whole number!");
                        break;
                    }

                    if (playerLimit > _maxPlayers || playerLimit < _minPlayers)
                    {
                        await SendChatAsServer(e.PlayerControl, $"[FF0000FF]Error: Player limit can only be within {_minPlayers} and {_maxPlayers}!");
                        await e.Game.SyncSettingsAsync();
                        break;
                    }

                    _curPlayers = playerLimit;
                    e.Game.Options.MaxPlayers = (byte)playerLimit;
                    if (e.Game.Options.NumImpostors > _maxImposters)
                    {
                        e.Game.Options.NumImpostors = _maxImposters;
                        await SendChatAsServer(e.PlayerControl, $"[00FF00FF]Player limit has been set to {playerLimit}!\nImposter limit was too high for the player limit and has been set to {_maxImposters}!\nNote: The counter will not change until someone joins/leaves!");
                    }
                    else
                    {
                        await SendChatAsServer(e.PlayerControl, $"[00FF00FF]Player limit has been set to {args[1]}!\nNote: The counter will not change until someone joins/leaves!");
                    }

                    await e.Game.SyncSettingsAsync();
                    break;
                default:
                    await SendChatAsServer(e.PlayerControl, $"[FF0000FF]unknown command: \"{args[0]}\"");
                    _logger.LogInformation($"Unknown command {args[0]} from {e.PlayerControl.PlayerInfo.PlayerName} on {e.Game.Code.Code}.");
                    break;
            }
        }
    }
}
