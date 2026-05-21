// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Setup
{
    internal partial class EndpointSelector : ActionableInfo
    {
        public readonly Bindable<string> CurrentEndpoint = new Bindable<string>();

        public new Action<string>? Action;

        public string DefaultEndpoint { get; set; } = "";

        private OsuTextBox? endpointBox;
        private SpriteText? statusText;

        public readonly BindableBool IsConnected = new BindableBool(false);
        public readonly BindableBool SelectorEnabled = new BindableBool(true);

        protected override Drawable CreateComponent()
        {
            var drawable = base.CreateComponent();

            FlowContainer.Insert(-1, endpointBox = new OsuTextBox
            {
                PlaceholderText = "ws://127.0.0.1:25050 or 127.0.0.1:25050",
                Width = 350
            });

            var statusContainer = new Container
            {
                Width = 150,
                Height = 40
            };

            statusText = new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = "Disconnected",
                Colour = Color4.Red
            };

            statusContainer.Add(statusText);

            FlowContainer.Insert(-1, statusContainer);

            IsConnected.BindValueChanged(updateStatus, true);
            SelectorEnabled.BindValueChanged(updateEnabledState, true);

            base.Action = () =>
            {
                if (endpointBox == null || !SelectorEnabled.Value)
                    return;

                if (!parseAndNormalizeUrl(endpointBox.Text, out string? displayText, out string? wsUrl))
                    return;

                endpointBox.Text = displayText!;
                Action?.Invoke(wsUrl!);
            };

            return drawable;
        }

        private void updateEnabledState(ValueChangedEvent<bool> e)
        {
            if (endpointBox != null)
            {
                endpointBox.Alpha = e.NewValue ? 1f : 0.5f;
                endpointBox.Current.Disabled = !e.NewValue;
            }

            if (statusText != null)
                statusText.Alpha = e.NewValue ? 1f : 0.5f;
        }

        public bool UpdateTextbox(string? url)
        {
            if (url != null && endpointBox != null && parseAndNormalizeUrl(url, out string? displayText, out _))
            {
                endpointBox.Text = displayText!;
                return true;
            }

            return false;
        }

        private void updateStatus(ValueChangedEvent<bool> connected)
        {
            if (statusText == null)
                return;

            if (connected.NewValue)
            {
                statusText.Text = "Connected";
                statusText.Colour = Color4.Green;
            }
            else
            {
                statusText.Text = "Disconnected";
                statusText.Colour = Color4.Red;
            }
        }

        // Accepts ws://, wss://, http://, https:// schemes as well as bare host:port.
        private bool parseAndNormalizeUrl(string? raw, out string? displayText, out string? wsUrl)
        {
            displayText = null;
            wsUrl = null;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim();

            string host;
            int port;

            if (raw.Contains("://"))
            {
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                    return false;

                string scheme = uri.Scheme.ToLowerInvariant();
                if (scheme != "ws" && scheme != "wss" && scheme != "http" && scheme != "https")
                    return false;

                host = uri.Host;
                port = uri.Port;

                // Uri.Port returns -1 when no port is specified
                if (port <= 0 || port > 65535)
                    return false;
            }
            else
            {
                if (!raw.Contains(':'))
                    return false;

                string[] parts = raw.Split(':');
                if (parts.Length != 2)
                    return false;

                host = parts[0];

                if (!int.TryParse(parts[1], out port) || port <= 0 || port > 65535)
                    return false;
            }

            bool hostValid = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                             System.Net.IPAddress.TryParse(host, out _);

            if (!hostValid)
                return false;

            displayText = $"{host}:{port}";
            wsUrl = $"ws://{host}:{port}";

            return true;
        }
    }
}
