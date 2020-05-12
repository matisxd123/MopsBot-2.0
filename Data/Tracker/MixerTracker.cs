using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MopsBot.Data.Tracker.APIResults.Mixer;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;
//https://dev.mixer.com/reference/constellation/events/live
//https://dev.mixer.com/reference/webhooks
namespace MopsBot.Data.Tracker
{
    [BsonIgnoreExtraElements]
    public class MixerTracker : BaseUpdatingTracker
    {
        public event HostingEventHandler OnHosting;
        public event StatusEventHandler OnLive;
        public event StatusEventHandler OnOffline;
        public delegate Task HostingEventHandler(string hostName, string targetName, int viewers);
        public delegate Task StatusEventHandler(BaseTracker sender);
        public DatePlot ViewerGraph;
        public List<Tuple<string, DateTime>> GameChanges = new List<Tuple<string, DateTime>>();
        private MixerResult StreamerStatus;
        public Boolean IsOnline;
        public string CurGame;
        public int TimeoutCount;
        public ulong MixerId;
        //public DateTime WebhookExpire = DateTime.Now;
        public static readonly string GAMECHANGE = "NotifyOnGameChange", ONLINE = "NotifyOnOnline", OFFLINE = "NotifyOnOffline", SHOWEMBED = "ShowEmbed", SHOWGRAPH = "ShowGraph", SHOWTIMESTAMPS = "ShowTimestamps", THUMBNAIL = "LargeThumbnail";

        public MixerTracker() : base()
        {
        }

        public MixerTracker(string streamerName) : base()
        {
            Name = streamerName;
            IsOnline = false;

            //Check if person exists by forcing Exceptions if not.
            try
            {
                MixerId = GetIdFromUsername(streamerName).Result;
            }
            catch (Exception e)
            {
                Dispose();
                throw new Exception($"Streamer {TrackerUrl()} could not be found on Mixer!", e);
            }
        }

        public async override void PostChannelAdded(ulong channelId)
        {
            base.PostChannelAdded(channelId);

            var config = ChannelConfig[channelId];
            config[SHOWEMBED] = true;
            config[SHOWGRAPH] = false;
            config[THUMBNAIL] = false;
            config[GAMECHANGE] = true;
            config[OFFLINE] = true;
            config[ONLINE] = true;
            config[SHOWTIMESTAMPS] = true;
        }

        public override async void Conversion(object obj = null)
        {
            bool save = false;
            foreach (var channel in ChannelConfig.Keys.ToList())
            {
                if (!ChannelConfig[channel].ContainsKey(SHOWGRAPH))
                {
                    ChannelConfig[channel][SHOWGRAPH] = false;
                    save = true;
                }
            }
            if (save)
                await UpdateTracker();
        }

        public async override void PostInitialisation(object info = null)
        {
            //if (IsOnline) SetTimer(60000, StaticBase.ran.Next(5000, 60000));

            if (ViewerGraph != null)
                ViewerGraph.InitPlot();

            /*if((WebhookExpire - DateTime.Now).TotalMinutes < 10){
                await SubscribeWebhookAsync();
            }*/
        }

        public async Task<string> SubscribeWebhookAsync(bool subscribe = true)
        {
            try
            {
                var url = "https://mixer.com/api/v1/hooks";
                var data = "{ \"kind\": \"web\", \"events\":[\"channel:" + MixerId + ":broadcast\"], \"url\":\"https://dev.mixer.com/onHook\" }";

                var test = await MopsBot.Module.Information.PostURLAsync(url, data,
                    KeyValuePair.Create("Authorization", "Secret " + Program.Config["MixerSecret"]),
                    KeyValuePair.Create("Client-ID", Program.Config["MixerKey"])
                );

                //WebhookExpire = DateTime.Now.AddDays(90);
                await UpdateTracker();

                return test;
            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $" error by {Name}", e));
                return "Failed";
            }
        }

        public async override void CheckForChange_Elapsed(object stateinfo)
        {
            try
            {
                /*if((WebhookExpire - DateTime.Now).TotalMinutes < 10){
                    await SubscribeWebhookAsync();
                }*/

                await CheckStreamerInfoAsync();
            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $" error by {Name}", e));
            }
        }

        public async Task CheckStreamerInfoAsync()
        {
            try
            {
                StreamerStatus = await streamerInformation();
                bool isStreaming = StreamerStatus.online;

                if (IsOnline != isStreaming)
                {
                    if (IsOnline)
                    {
                        if (++TimeoutCount >= 3)
                        {
                            TimeoutCount = 0;
                            IsOnline = false;

                            ViewerGraph?.Dispose();
                            ViewerGraph = null;
                            
                            ToUpdate = new Dictionary<ulong, ulong>();
                            GameChanges = new List<Tuple<string, DateTime>>();

                            if (OnOffline != null) await OnOffline.Invoke(this);
                            foreach (ulong channel in ChannelConfig.Keys.Where(x => (bool)ChannelConfig[x][OFFLINE]).ToList())
                                await OnMinorChangeTracked(channel, $"{Name} went Offline!");

                            //SetTimer(600000, 600000);

                        }
                    }
                    else
                    {
                        ViewerGraph = new DatePlot(Name + "Mixer", "Time since start", "Viewers");
                        IsOnline = true;
                        CurGame = StreamerStatus.type?.name ?? "Nothing";
                        GameChanges.Add(Tuple.Create(CurGame, DateTime.UtcNow));
                        ViewerGraph.AddValue(CurGame, 0, (await GetBroadcastStartTime()).AddHours(-2));

                        if (OnLive != null) await OnLive.Invoke(this);
                        foreach (ulong channel in ChannelConfig.Keys.Where(x => (bool)ChannelConfig[x][ONLINE]).ToList())
                            await OnMinorChangeTracked(channel, (string)ChannelConfig[channel]["Notification"]);

                        //SetTimer(60000, 60000);
                    }
                    await UpdateTracker();
                }
                else
                    TimeoutCount = 0;

                if (isStreaming)
                {
                    if (CurGame.CompareTo(StreamerStatus.type?.name ?? "Nothing") != 0)
                    {
                        CurGame = StreamerStatus.type?.name ?? "Nothing";
                        GameChanges.Add(Tuple.Create(CurGame, DateTime.UtcNow));
                        await UpdateTracker();

                        foreach (ulong channel in ChannelConfig.Keys.Where(x => (bool)ChannelConfig[x][GAMECHANGE]).ToList())
                            await OnMinorChangeTracked(channel, $"{Name} switched games to **{CurGame}**");
                    }

                    if (ChannelConfig.Any(x => (bool)x.Value[SHOWGRAPH]))
                        await ModifyAsync(x => x.ViewerGraph.AddValue(CurGame, StreamerStatus.viewersCurrent));

                    foreach (ulong channel in ChannelConfig.Keys.Where(x => (bool)ChannelConfig[x][SHOWEMBED]).ToList())
                        await OnMajorChangeTracked(channel, createEmbed(StreamerStatus, (bool)ChannelConfig[channel][THUMBNAIL], (bool)ChannelConfig[channel][SHOWTIMESTAMPS], (bool)ChannelConfig[channel][SHOWGRAPH]));
                }
            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $" error by {Name}", e));
            }
        }

        private async Task<MixerResult> streamerInformation()
        {
            var tmpResult = await FetchJSONDataAsync<MixerResult>($"https://mixer.com/api/v1/channels/{Name}");

            return tmpResult;
        }

        public static async Task<ulong> GetIdFromUsername(string name)
        {
            var tmpResult = await FetchJSONDataAsync<dynamic>($"https://mixer.com/api/v1/channels/{name}?fields=id");

            return tmpResult["id"];
        }

        public async Task<DateTime> GetBroadcastStartTime()
        {
            var tmpResult = await FetchJSONDataAsync<dynamic>($"https://mixer.com/api/v1/channels/{MixerId}/broadcast");

            return DateTime.Parse(tmpResult["startedAt"].ToString());
        }

        public Embed createEmbed(MixerResult StreamerStatus, bool largeThumbnail = false, bool showTimestamps = false, bool showGraph = false)
        {
            if (showGraph)
                ViewerGraph.SetMaximumLine();

            EmbedBuilder e = new EmbedBuilder();
            e.Color = new Color(0, 163, 243);
            e.Title = StreamerStatus.name;
            e.Url = TrackerUrl();
            e.WithCurrentTimestamp();

            if (showTimestamps)
            {
                string vods = "";
                for (int i = Math.Max(0, GameChanges.Count - 6); i < GameChanges.Count; i++)
                {
                    TimeSpan duration = i != GameChanges.Count - 1 ? GameChanges[i + 1].Item2 - GameChanges[i].Item2
                                                             : DateTime.UtcNow - GameChanges[i].Item2;
                    TimeSpan timestamp = GameChanges[i].Item2 - GameChanges[0].Item2;
                    vods += $"\n{GameChanges[i].Item1} ({duration.ToString("hh")}h {duration.ToString("mm")}m)";
                }
                e.AddField("VOD Segments", String.IsNullOrEmpty(vods) ? "/" : vods);
            }


            EmbedAuthorBuilder author = new EmbedAuthorBuilder();
            author.Name = Name;
            author.Url = TrackerUrl();
            author.IconUrl = StreamerStatus.user.avatarUrl;
            e.Author = author;

            EmbedFooterBuilder footer = new EmbedFooterBuilder();
            footer.IconUrl = "https://img.pngio.com/mixer-logo-png-images-png-cliparts-free-download-on-seekpng-mixer-logo-png-300_265.png";
            footer.Text = "Mixer";
            e.Footer = footer;

            if (largeThumbnail)
            {
                e.ImageUrl = $"https://thumbs.mixer.com/channel/{MixerId}.small.jpg?rand={StaticBase.ran.Next(0, 99999999)}";
                if (showGraph)
                    e.ThumbnailUrl = ViewerGraph.DrawPlot();
            }
            else
            {
                e.ThumbnailUrl = $"https://thumbs.mixer.com/channel/{MixerId}.small.jpg?rand={StaticBase.ran.Next(0, 99999999)}";
                if (showGraph)
                    e.ImageUrl = ViewerGraph.DrawPlot();
            }

            if (!showGraph)
            {
                e.AddField("Viewers", StreamerStatus.viewersCurrent, true);
                e.AddField("Game", StreamerStatus.type?.name ?? "Nothing", true);
            }

            return e.Build();
        }

        public async Task ModifyAsync(Action<MixerTracker> action)
        {
            action(this);
            await UpdateTracker();
        }

        public override void Dispose()
        {
            base.Dispose(true);
            GC.SuppressFinalize(this);
            ViewerGraph?.Dispose();
            ViewerGraph = null;
            //SubscribeWebhookAsync(false).Wait();
        }

        public override string TrackerUrl()
        {
            return "https://mixer.com/" + Name;
        }

        public override async Task UpdateTracker()
        {
            await StaticBase.Trackers[TrackerType.Mixer].UpdateDBAsync(this);
        }
    }
}
