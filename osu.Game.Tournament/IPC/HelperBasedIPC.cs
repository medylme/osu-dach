// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource();

            Task.Run(async () =>
            {
                if (helperInfo.WebsocketUrl == null) return;

                try
                {
                    await webSocket.ConnectAsync(new Uri(helperInfo.WebsocketUrl), cancellationTokenSource.Token).ConfigureAwait(false);

                    Schedule(() => ipc.HelperConnected.Value = true);
                    currentRetryDelay = initial_retry_delay_ms;

                    await receiveLoopAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Schedule(() => ipc.HelperConnected.Value = false);
                    Logger.Error(e, $"Failed to connect to helper websocket: {helperInfo.WebsocketUrl}");
                    resetScores();
                    scheduleReconnect();
                }
            }, cancellationTokenSource.Token);
        }

        private void scheduleReconnect()
        {
            if (IsDisposed || (cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                return;

            Logger.Log($"Scheduling WebSocket reconnect in {currentRetryDelay}ms");

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(currentRetryDelay, cancellationTokenSource?.Token ?? CancellationToken.None).ConfigureAwait(false);

                    currentRetryDelay = Math.Min((int)(currentRetryDelay * retry_backoff_multiplier), max_retry_delay_ms);

                    if (!IsDisposed && !(cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                        Schedule(connectWebSocket);
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        private async Task receiveLoopAsync()
        {
            byte[] buffer = new byte[8192];

            while (!cancellationTokenSource?.Token.IsCancellationRequested ?? false)
            {
                if (webSocket == null || webSocket.State != WebSocketState.Open)
                    break;

                try
                {
                    var messageBuilder = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource?.Token ?? CancellationToken.None).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Schedule(() => ipc.HelperConnected.Value = false);
                            scheduleReconnect();
                            return;
                        }

                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    string json = messageBuilder.ToString();
                    processWebSocketData(json);
                }
                catch (Exception e)
                {
                    Schedule(() => ipc.HelperConnected.Value = false);
                    Logger.Error(e, "Error receiving WebSocket data");
                    resetScores();
                    scheduleReconnect();
                    return;
                }
            }

            if (!IsDisposed && !(cancellationTokenSource?.Token.IsCancellationRequested ?? true))
            {
                resetScores();
                scheduleReconnect();
            }
        }

        private bool hasModAcronym(JsonElement modsElement, string acronym)
        {
            if (!modsElement.TryGetProperty("mods", out var modsArray) || modsArray.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var mod in modsArray.EnumerateArray())
            {
                if (mod.TryGetProperty("acronym", out var acronymElement) &&
                    acronymElement.GetString() == acronym)
                {
                    return true;
                }
            }

            return false;
        }

        private double getScoreMultiplier(int beatmapId, JsonElement modsElement)
        {
            var currentMatch = ladderInfo.CurrentMatch.Value;
            var round = currentMatch?.Round.Value;

            if (round == null)
                return 1.0;

            var roundBeatmap = round.Beatmaps.FirstOrDefault(b => b.ID == beatmapId);
            var customMultipliers = roundBeatmap?.CustomModMultipliers;

            if (customMultipliers == null || !customMultipliers.Enabled)
                return 1.0;

            bool hasEZ = hasModAcronym(modsElement, "EZ");
            bool hasHD = hasModAcronym(modsElement, "HD");

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

                int currentBeatmapId = 0;
                if (ipc.Beatmap.Value != null)
                {
                    currentBeatmapId = ipc.Beatmap.Value.OnlineID;
                }
                else if (root.TryGetProperty("beatmap", out var beatmapElement) &&
                    beatmapElement.TryGetProperty("id", out var onlineIdElement))
                {
                    currentBeatmapId = onlineIdElement.GetInt32();
                }

                if (root.TryGetProperty("tourney", out var tourney) &&
                    tourney.TryGetProperty("clients", out var clients) &&
                    clients.ValueKind == JsonValueKind.Array &&
                    clients.GetArrayLength() > 0)
                {
                    processClients(clients, currentBeatmapId);
                }
                else if (root.TryGetProperty("clients", out var rootClients) &&
                    rootClients.ValueKind == JsonValueKind.Array &&
                    rootClients.GetArrayLength() > 0)
                {
                    processClients(rootClients, currentBeatmapId);
                }

                // Fallback to single player score for testing
                else if (root.TryGetProperty("play", out var play) && play.TryGetProperty("score", out var singleScoreElement))
                {
                    int score = singleScoreElement.GetInt32();

                    JsonElement modsElement = default;
                    if (play.TryGetProperty("mods", out var mods))
                        modsElement = mods;

                    double multiplier = getScoreMultiplier(currentBeatmapId, modsElement);
                    long finalScore = (long)(score * multiplier);

                    Scheduler.Add(() =>
                    {
                        if (!IsDisposed)
                        {
                            ipc.Score1.Value = finalScore;
                            ipc.Score2.Value = 0;
                        }
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

        private void processClients(JsonElement clients, int currentBeatmapId)
        {
            long team0Total = 0;
            long team1Total = 0;

            foreach (var client in clients.EnumerateArray())
            {
                if (!client.TryGetProperty("score", out var scoreElement))
                    continue;

                int score = scoreElement.GetInt32();

                JsonElement modsElement = default;
                if (client.TryGetProperty("mods", out var mods))
                    modsElement = mods;

                double multiplier = getScoreMultiplier(currentBeatmapId, modsElement);
                long finalScore = (long)(score * multiplier);

                if (client.TryGetProperty("team", out var teamElement))
                {
                    int team = teamElement.GetInt32();
                    if (team == 0)
                        team0Total += finalScore;
                    else if (team == 1)
                        team1Total += finalScore;
                }
            }

            Scheduler.Add(() =>
            {
                if (!IsDisposed)
                {
                    ipc.Score1.Value = team0Total;
                    ipc.Score2.Value = team1Total;
                }
            });
        }

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
