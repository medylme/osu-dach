// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Editors.Components;
using osuTK;

namespace osu.Game.Tournament.Screens.Editors
{
    public partial class RoundEditorScreen : TournamentEditorScreen<RoundEditorScreen.RoundRow, TournamentRound>
    {
        protected override BindableList<TournamentRound> Storage => LadderInfo.Rounds;

        public partial class RoundRow : CompositeDrawable, IModelBacked<TournamentRound>
        {
            public TournamentRound Model { get; }

            [Resolved]
            private LadderInfo ladderInfo { get; set; } = null!;

            [Resolved]
            private IDialogOverlay? dialogOverlay { get; set; }

            public RoundRow(TournamentRound round)
            {
                Model = round;

                Masking = true;
                CornerRadius = 10;

                RoundBeatmapEditor beatmapEditor = new RoundBeatmapEditor(round)
                {
                    Width = 0.95f
                };

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        Colour = OsuColour.Gray(0.1f),
                        RelativeSizeAxes = Axes.Both,
                    },
                    new FillFlowContainer
                    {
                        Margin = new MarginPadding(5),
                        Padding = new MarginPadding { Right = 160 },
                        Spacing = new Vector2(5),
                        Direction = FillDirection.Full,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            new SettingsTextBox
                            {
                                LabelText = "Name",
                                Width = 0.33f,
                                Current = Model.Name
                            },
                            new SettingsTextBox
                            {
                                LabelText = "Description",
                                Width = 0.33f,
                                Current = Model.Description
                            },
                            new DateTextBox
                            {
                                LabelText = "Start Time",
                                Width = 0.33f,
                                Current = Model.StartDate
                            },
                            new SettingsSlider<int>
                            {
                                LabelText = "# of Bans",
                                Width = 0.33f,
                                Current = Model.BanCount
                            },
                            new SettingsSlider<int>
                            {
                                LabelText = "Best of",
                                Width = 0.33f,
                                Current = Model.BestOf
                            },
                            new SettingsButton
                            {
                                Width = 0.2f,
                                Margin = new MarginPadding(10),
                                Text = "Add beatmap",
                                Action = beatmapEditor.CreateNew
                            },
                            beatmapEditor
                        }
                    },
                    new DangerousSettingsButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        RelativeSizeAxes = Axes.None,
                        Width = 150,
                        Text = "Delete Round",
                        Action = () => dialogOverlay?.Push(new DeleteRoundDialog(Model, () =>
                        {
                            Expire();
                            ladderInfo.Rounds.Remove(Model);
                        }))
                    }
                };

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            public partial class RoundBeatmapEditor : CompositeDrawable
            {
                private readonly TournamentRound round;
                private readonly FillFlowContainer flow;

                public RoundBeatmapEditor(TournamentRound round)
                {
                    this.round = round;

                    RelativeSizeAxes = Axes.X;
                    AutoSizeAxes = Axes.Y;

                    InternalChild = flow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        ChildrenEnumerable = round.Beatmaps.Select(p => new RoundBeatmapRow(round, p))
                    };
                }

                public void CreateNew()
                {
                    var b = new RoundBeatmap();

                    round.Beatmaps.Add(b);

                    flow.Add(new RoundBeatmapRow(round, b));
                }

                public partial class RoundBeatmapRow : CompositeDrawable
                {
                    public RoundBeatmap Model { get; }

                    [Resolved]
                    protected IAPIProvider API { get; private set; } = null!;

                    private readonly Bindable<int?> beatmapId = new Bindable<int?>();

                    private readonly Bindable<string> mods = new Bindable<string>(string.Empty);

                    private readonly Bindable<bool> useCustomModMultipliers = new Bindable<bool>();
                    private readonly Bindable<string> ezMultiplierText = new Bindable<string>("1.0");
                    private readonly Bindable<string> ezhdMultiplierText = new Bindable<string>("1.0");

                    private readonly Container drawableContainer;
                    private FillFlowContainer multiplierRow = null!;

                    public RoundBeatmapRow(TournamentRound team, RoundBeatmap beatmap)
                    {
                        Model = beatmap;

                        Margin = new MarginPadding(10);

                        RelativeSizeAxes = Axes.X;
                        AutoSizeAxes = Axes.Y;

                        Masking = true;
                        CornerRadius = 5;

                        InternalChildren = new Drawable[]
                        {
                            new Box
                            {
                                Colour = OsuColour.Gray(0.2f),
                                RelativeSizeAxes = Axes.Both,
                            },
                            new FillFlowContainer
                            {
                                Margin = new MarginPadding(5),
                                Padding = new MarginPadding { Right = 160 },
                                Spacing = new Vector2(5),
                                Direction = FillDirection.Vertical,
                                AutoSizeAxes = Axes.Both,
                                Children = new Drawable[]
                                {
                                    new FillFlowContainer
                                    {
                                        Spacing = new Vector2(5),
                                        Direction = FillDirection.Horizontal,
                                        AutoSizeAxes = Axes.Both,
                                        Children = new Drawable[]
                                        {
                                            new SettingsNumberBox
                                            {
                                                LabelText = "Beatmap ID",
                                                RelativeSizeAxes = Axes.None,
                                                Width = 200,
                                                Current = beatmapId,
                                            },
                                            new SettingsTextBox
                                            {
                                                LabelText = "Mods",
                                                RelativeSizeAxes = Axes.None,
                                                Width = 200,
                                                Current = mods,
                                            },
                                            new Container
                                            {
                                                AutoSizeAxes = Axes.X,
                                                Height = 70,
                                                Child = new SettingsCheckbox
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    LabelText = "Custom Mod Multipliers",
                                                    RelativeSizeAxes = Axes.None,
                                                    Width = 170,
                                                    Current = useCustomModMultipliers,
                                                },
                                            },
                                            drawableContainer = new Container
                                            {
                                                Size = new Vector2(100, 70),
                                            },
                                        }
                                    },
                                    multiplierRow = new FillFlowContainer
                                    {
                                        Spacing = new Vector2(5),
                                        Direction = FillDirection.Horizontal,
                                        AutoSizeAxes = Axes.Both,
                                        Children = new Drawable[]
                                        {
                                            new SettingsTextBox
                                            {
                                                LabelText = "EZ Multiplier",
                                                RelativeSizeAxes = Axes.None,
                                                Width = 200,
                                                Current = ezMultiplierText,
                                            },
                                            new SettingsTextBox
                                            {
                                                LabelText = "EZHD Multiplier",
                                                RelativeSizeAxes = Axes.None,
                                                Width = 200,
                                                Current = ezhdMultiplierText,
                                            },
                                        }
                                    },
                                }
                            },
                            new DangerousSettingsButton
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                RelativeSizeAxes = Axes.None,
                                Width = 150,
                                Text = "Delete Beatmap",
                                Action = () =>
                                {
                                    Expire();
                                    team.Beatmaps.Remove(beatmap);
                                },
                            }
                        };
                    }

                    [BackgroundDependencyLoader]
                    private void load()
                    {
                        beatmapId.Value = Model.ID;
                        beatmapId.BindValueChanged(id =>
                        {
                            Model.ID = id.NewValue ?? 0;

                            if (id.NewValue != id.OldValue)
                                Model.Beatmap = null;

                            if (Model.Beatmap != null)
                            {
                                updatePanel();
                                return;
                            }

                            var req = new GetBeatmapRequest(new APIBeatmap { OnlineID = Model.ID });

                            req.Success += res =>
                            {
                                Model.Beatmap = new TournamentBeatmap(res);
                                updatePanel();
                            };

                            req.Failure += _ =>
                            {
                                Model.Beatmap = null;
                                updatePanel();
                            };

                            API.Queue(req);
                        }, true);

                        mods.Value = Model.Mods;
                        mods.BindValueChanged(modString => Model.Mods = modString.NewValue);

                        useCustomModMultipliers.Value = Model.CustomModMultipliers?.Enabled ?? false;
                        ezMultiplierText.Value = (Model.CustomModMultipliers?.EZ ?? 1.0).ToString(CultureInfo.InvariantCulture);
                        ezhdMultiplierText.Value = (Model.CustomModMultipliers?.EZHD ?? 1.0).ToString(CultureInfo.InvariantCulture);

                        useCustomModMultipliers.BindValueChanged(value =>
                        {
                            if (value.NewValue)
                            {
                                Model.CustomModMultipliers ??= new CustomModMultipliers();
                                Model.CustomModMultipliers.Enabled = true;

                                if (tryParseDouble(ezMultiplierText.Value, out double ez))
                                    Model.CustomModMultipliers.EZ = ez;

                                if (tryParseDouble(ezhdMultiplierText.Value, out double ezhd))
                                    Model.CustomModMultipliers.EZHD = ezhd;
                            }
                            else
                            {
                                Model.CustomModMultipliers = null;
                            }

                            updateMultiplierVisibility();
                        }, true);

                        ezMultiplierText.BindValueChanged(value =>
                        {
                            if (Model.CustomModMultipliers != null && tryParseDouble(value.NewValue, out double parsed))
                                Model.CustomModMultipliers.EZ = parsed;
                        });

                        ezhdMultiplierText.BindValueChanged(value =>
                        {
                            if (Model.CustomModMultipliers != null && tryParseDouble(value.NewValue, out double parsed))
                                Model.CustomModMultipliers.EZHD = parsed;
                        });
                    }

                    private void updateMultiplierVisibility()
                    {
                        multiplierRow.Alpha = useCustomModMultipliers.Value ? 1 : 0;
                    }

                    // Accepts both period and comma as decimal separator to handle locale differences.
                    private static bool tryParseDouble(string value, out double result)
                    {
                        string normalized = value.Replace(',', '.');
                        return double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
                    }

                    private void updatePanel() => Schedule(() =>
                    {
                        drawableContainer.Clear();

                        if (Model.Beatmap != null)
                        {
                            drawableContainer.Child = new TournamentBeatmapPanel(Model.Beatmap, Model.Mods)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Width = 300
                            };
                        }
                    });
                }
            }
        }

        protected override RoundRow CreateDrawable(TournamentRound model) => new RoundRow(model);
    }
}
