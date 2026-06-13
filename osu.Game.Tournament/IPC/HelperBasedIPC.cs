// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    public partial class HelperBasedIPC : Component
    {
        private const string default_websocket_url = "ws://127.0.0.1:25050";
        private const int initial_retry_delay_ms = 1000;
        private const int max_retry_delay_ms = 10000;
        private const double retry_backoff_multiplier = 1.5;

        private ClientWebSocket? webSocket;
        private CancellationTokenSource? cancellationTokenSource;
        private Action? onHelperInfoSaved;
        private int currentRetryDelay = initial_retry_delay_ms;

        private int connectionGeneration;
        private string? connectedUrl;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladderInfo { get; set; } = null!;

        [Resolved]
        private HelperInfo helperInfo { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            if (helperInfo.WebsocketUrl == null)
            {
                SetWebsocketLocation(default_websocket_url);
            }
            else
            {
                connectWebSocket();
            }

            helperInfo.OnHelperInfoSaved += onHelperInfoSaved = () => Schedule(() =>
            {
                if (helperInfo.WebsocketUrl == connectedUrl)
                    return;

                currentRetryDelay = initial_retry_delay_ms;
                connectWebSocket();
            });
        }

        public void SetWebsocketLocation(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            helperInfo.WebsocketUrl = url.Trim();
            helperInfo.SaveChanges();
        }

        private void connectWebSocket()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            webSocket?.Dispose();

            connectedUrl = null;

            int generation = ++connectionGeneration;
            var ws = webSocket = new ClientWebSocket();
            var cts = cancellationTokenSource = new CancellationTokenSource();

            Task.Run(async () =>
            {
                string? url = helperInfo.WebsocketUrl;
                if (url == null) return;

                try
                {
                    await ws.ConnectAsync(new Uri(url), cts.Token).ConfigureAwait(false);

                    if (generation != connectionGeneration) return;

                    connectedUrl = url;
                    Schedule(() => ipc.HelperConnected.Value = true);
                    currentRetryDelay = initial_retry_delay_ms;

                    await receiveLoopAsync(ws, cts, generation).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (generation != connectionGeneration) return;

                    connectedUrl = null;
                    Schedule(() => ipc.HelperConnected.Value = false);
                    Logger.Error(e, $"Failed to connect to helper websocket: {url}");
                    resetScores();
                    scheduleReconnect(generation);
                }
            }, cts.Token);
        }

        private void scheduleReconnect(int generation)
        {
            if (generation != connectionGeneration)
                return;

            if (IsDisposed || (cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                return;

            Logger.Log($"Scheduling WebSocket reconnect in {currentRetryDelay}ms");

            var cts = cancellationTokenSource;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(currentRetryDelay, cts?.Token ?? CancellationToken.None).ConfigureAwait(false);

                    currentRetryDelay = Math.Min((int)(currentRetryDelay * retry_backoff_multiplier), max_retry_delay_ms);

                    if (!IsDisposed && generation == connectionGeneration && !(cts?.Token.IsCancellationRequested ?? true))
                        Schedule(connectWebSocket);
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        private async Task receiveLoopAsync(ClientWebSocket ws, CancellationTokenSource cts, int generation)
        {
            byte[] buffer = new byte[8192];

            while (!cts.Token.IsCancellationRequested)
            {
                if (ws.State != WebSocketState.Open)
                    break;

                try
                {
                    var messageBuilder = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            if (generation != connectionGeneration) return;

                            connectedUrl = null;
                            Schedule(() => ipc.HelperConnected.Value = false);
                            scheduleReconnect(generation);
                            return;
                        }

                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    string json = messageBuilder.ToString();
                    processWebSocketData(json);
                }
                catch (Exception e)
                {
                    if (generation != connectionGeneration) return;

                    connectedUrl = null;
                    Schedule(() => ipc.HelperConnected.Value = false);
                    Logger.Error(e, "Error receiving WebSocket data");
                    resetScores();
                    scheduleReconnect(generation);
                    return;
                }
            }

            if (!IsDisposed && generation == connectionGeneration && !cts.Token.IsCancellationRequested)
            {
                connectedUrl = null;
                resetScores();
                scheduleReconnect(generation);
            }
        }

        private static bool hasModAcronym(JsonElement modsElement, string acronym)
        {
            if (modsElement.ValueKind != JsonValueKind.Object ||
                !modsElement.TryGetProperty("mods", out var modsArray) ||
                modsArray.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var mod in modsArray.EnumerateArray())
            {
                if (mod.ValueKind == JsonValueKind.Object &&
                    mod.TryGetProperty("acronym", out var acronymElement) &&
                    acronymElement.ValueKind == JsonValueKind.String &&
                    acronymElement.GetString() == acronym)
                {
                    return true;
                }
            }

            return false;
        }

        private double getScoreMultiplier(int beatmapId, bool hasEZ, bool hasHD)
        {
            var round = ladderInfo.CurrentMatch.Value?.Round.Value;

            if (round == null)
                return 1.0;

            var customMultipliers = round.Beatmaps.FirstOrDefault(b => b.ID == beatmapId)?.CustomModMultipliers;

            if (customMultipliers == null || !customMultipliers.Enabled)
                return 1.0;

            if (hasEZ && hasHD)
                return customMultipliers.EZHD;

            if (hasEZ)
                return customMultipliers.EZ;

            return 1.0;
        }

        private void processWebSocketData(string json)
        {
            if (!helperInfo.HelperEnabled.Value) return;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    resetScores();
                    return;
                }

                int messageBeatmapId = 0;
                if (root.TryGetProperty("beatmap", out var beatmapElement) &&
                    beatmapElement.ValueKind == JsonValueKind.Object &&
                    beatmapElement.TryGetProperty("id", out var beatmapIdElement) &&
                    beatmapIdElement.TryGetInt32(out int parsedBeatmapId))
                {
                    messageBeatmapId = parsedBeatmapId;
                }

                if (tryGetClients(root, out var clients))
                {
                    var parsed = parseClients(clients);

                    Scheduler.Add(() =>
                    {
                        if (IsDisposed) return;

                        int beatmapId = ipc.Beatmap.Value?.OnlineID ?? messageBeatmapId;
                        long team0Total = 0;
                        long team1Total = 0;

                        foreach (var c in parsed)
                        {
                            long finalScore = (long)(c.Score * getScoreMultiplier(beatmapId, c.HasEZ, c.HasHD));

                            if (c.Team == 0)
                                team0Total += finalScore;
                            else if (c.Team == 1)
                                team1Total += finalScore;
                        }

                        ipc.Score1.Value = team0Total;
                        ipc.Score2.Value = team1Total;
                    });
                }
                else if (root.TryGetProperty("play", out var play) &&
                         play.ValueKind == JsonValueKind.Object &&
                         play.TryGetProperty("score", out var singleScoreElement) &&
                         singleScoreElement.TryGetInt64(out long singleScore))
                {
                    bool hasEZ = false;
                    bool hasHD = false;

                    if (play.TryGetProperty("mods", out var mods))
                    {
                        hasEZ = hasModAcronym(mods, "EZ");
                        hasHD = hasModAcronym(mods, "HD");
                    }

                    Scheduler.Add(() =>
                    {
                        if (IsDisposed) return;

                        int beatmapId = ipc.Beatmap.Value?.OnlineID ?? messageBeatmapId;
                        ipc.Score1.Value = (long)(singleScore * getScoreMultiplier(beatmapId, hasEZ, hasHD));
                        ipc.Score2.Value = 0;
                    });
                }
                else
                {
                    resetScores();
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Error parsing WebSocket data: {e.Message}");
            }
        }

        private static bool tryGetClients(JsonElement root, out JsonElement clients)
        {
            if (root.TryGetProperty("tourney", out var tourney) &&
                tourney.ValueKind == JsonValueKind.Object &&
                tourney.TryGetProperty("clients", out clients) &&
                clients.ValueKind == JsonValueKind.Array &&
                clients.GetArrayLength() > 0)
                return true;

            if (root.TryGetProperty("clients", out clients) &&
                clients.ValueKind == JsonValueKind.Array &&
                clients.GetArrayLength() > 0)
                return true;

            clients = default;
            return false;
        }

        private static List<ParsedClient> parseClients(JsonElement clients)
        {
            var result = new List<ParsedClient>();

            foreach (var client in clients.EnumerateArray())
            {
                if (client.ValueKind != JsonValueKind.Object)
                    continue;

                if (!client.TryGetProperty("score", out var scoreElement) || !scoreElement.TryGetInt64(out long score))
                    continue;

                if (!client.TryGetProperty("team", out var teamElement) || !teamElement.TryGetInt32(out int team))
                    continue;

                bool hasEZ = false;
                bool hasHD = false;

                if (client.TryGetProperty("mods", out var mods))
                {
                    hasEZ = hasModAcronym(mods, "EZ");
                    hasHD = hasModAcronym(mods, "HD");
                }

                result.Add(new ParsedClient(score, team, hasEZ, hasHD));
            }

            return result;
        }

        private readonly record struct ParsedClient(long Score, int Team, bool HasEZ, bool HasHD);

        private void resetScores()
        {
            if (!helperInfo.HelperEnabled.Value) return;

            Scheduler.Add(() =>
            {
                if (!IsDisposed)
                {
                    ipc.Score1.Value = 0;
                    ipc.Score2.Value = 0;
                }
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            helperInfo.OnHelperInfoSaved -= onHelperInfoSaved;
            cancellationTokenSource?.Cancel();

            if (webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    Schedule(() => ipc.HelperConnected.Value = false);
                    webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None).Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
            }

            webSocket?.Dispose();
            cancellationTokenSource?.Dispose();
        }
    }
}
